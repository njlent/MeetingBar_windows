using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MeetingBar.Windows.Models;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public sealed class OutlookCalendarProvider
    {
        public Task<Tuple<List<CalendarInfo>, List<CalendarEvent>>> FetchAsync(DateTime from, DateTime to)
        {
            return Task.Run(() => Fetch(from, to));
        }

        private Tuple<List<CalendarInfo>, List<CalendarEvent>> Fetch(DateTime from, DateTime to)
        {
            var calendars = new List<CalendarInfo>();
            var events = new List<CalendarEvent>();
            var outlookType = Type.GetTypeFromProgID("Outlook.Application");
            if (outlookType == null) throw new InvalidOperationException(T("Classic Outlook desktop is not installed. Choose ICS or Google Calendar in Settings."));
            object app = null, session = null, stores = null;
            try
            {
                app = Activator.CreateInstance(outlookType);
                session = Invoke(app, "GetNamespace", "MAPI");
                stores = Get(session, "Stores");
                var storeCount = Convert.ToInt32(Get(stores, "Count"), CultureInfo.InvariantCulture);
                for (var s = 1; s <= storeCount; s++)
                {
                    object store = null, root = null;
                    try
                    {
                        store = GetIndexed(stores, s);
                        root = Invoke(store, "GetRootFolder");
                        ReadFolderTree(root, from, to, calendars, events);
                    }
                    catch { }
                    finally { Release(root); Release(store); }
                }
            }
            finally { Release(stores); Release(session); Release(app); }
            return Tuple.Create(calendars.GroupBy(x => x.Id).Select(x => x.First()).ToList(), events.OrderBy(x => x.Start).ToList());
        }

        private void ReadFolderTree(object folder, DateTime from, DateTime to, List<CalendarInfo> calendars, List<CalendarEvent> output)
        {
            if (folder == null) return;
            var defaultItemType = SafeInt(() => Get(folder, "DefaultItemType"), -1);
            if (defaultItemType == 1) ReadCalendarFolder(folder, from, to, calendars, output); // olAppointmentItem
            object folders = null;
            try
            {
                folders = Get(folder, "Folders");
                var count = SafeInt(() => Get(folders, "Count"), 0);
                for (var i = 1; i <= count; i++)
                {
                    object child = null;
                    try { child = GetIndexed(folders, i); ReadFolderTree(child, from, to, calendars, output); }
                    catch { }
                    finally { Release(child); }
                }
            }
            finally { Release(folders); }
        }

        private void ReadCalendarFolder(object folder, DateTime from, DateTime to, List<CalendarInfo> calendars, List<CalendarEvent> output)
        {
            var id = SafeString(() => Get(folder, "EntryID")) ?? Guid.NewGuid().ToString("N");
            var name = SafeString(() => Get(folder, "Name")) ?? "Outlook Calendar";
            var color = ColorFor(id);
            calendars.Add(new CalendarInfo { Id = id, Name = name, Source = "Outlook", Color = color });
            object items = null, restricted = null;
            try
            {
                items = Get(folder, "Items");
                Set(items, "IncludeRecurrences", true);
                Invoke(items, "Sort", "[Start]");
                var filter = "[Start] < '" + to.ToString("g", CultureInfo.CurrentCulture) + "' AND [End] > '" + from.ToString("g", CultureInfo.CurrentCulture) + "'";
                restricted = Invoke(items, "Restrict", filter);
                var count = Math.Min(SafeInt(() => Get(restricted, "Count"), 0), 5000);
                for (var i = 1; i <= count; i++)
                {
                    object item = null;
                    try
                    {
                        item = GetIndexed(restricted, i);
                        var start = SafeDate(() => Get(item, "Start"));
                        var end = SafeDate(() => Get(item, "End"));
                        if (start == DateTime.MinValue || end <= from || start >= to) continue;
                        var response = SafeInt(() => Get(item, "ResponseStatus"), 0);
                        var busy = SafeInt(() => Get(item, "BusyStatus"), 2);
                        var body = SafeString(() => Get(item, "Body"));
                        var location = SafeString(() => Get(item, "Location"));
                        var itemUrl = TryGetOnlineMeetingUrl(item);
                        output.Add(new CalendarEvent
                        {
                            Id = SafeString(() => Get(item, "GlobalAppointmentID")) ?? SafeString(() => Get(item, "EntryID")) ?? Guid.NewGuid().ToString("N"),
                            Title = SafeString(() => Get(item, "Subject")) ?? T("Untitled event"), Notes = body, Location = location,
                            ConferenceUrl = itemUrl, CalendarId = id, CalendarName = name, CalendarColor = color,
                            Organizer = SafeString(() => Get(item, "Organizer")), Start = start, End = end,
                            IsAllDay = SafeBool(() => Get(item, "AllDayEvent")), IsDeclined = response == 4,
                            IsTentative = response == 2, IsCancelled = SafeInt(() => Get(item, "MeetingStatus"), 0) == 5,
                            IsBusy = busy != 0
                        });
                    }
                    catch { }
                    finally { Release(item); }
                }
            }
            finally { Release(restricted); Release(items); }
        }

        private static string TryGetOnlineMeetingUrl(object item)
        {
            try
            {
                var accessor = Get(item, "PropertyAccessor");
                try { return Convert.ToString(Invoke(accessor, "GetProperty", "http://schemas.microsoft.com/mapi/string/{00020329-0000-0000-C000-000000000046}/OnlineMeetingExternalLink")); }
                finally { Release(accessor); }
            }
            catch { return null; }
        }

        private static object Get(object o, string name) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, o, null);
        private static object GetIndexed(object o, int i) => o.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, o, new object[] { i });
        private static object Invoke(object o, string name, params object[] args) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, o, args);
        private static void Set(object o, string name, object value) => o.GetType().InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, o, new[] { value });
        private static void Release(object o) { if (o != null && Marshal.IsComObject(o)) try { Marshal.FinalReleaseComObject(o); } catch { } }
        private static int SafeInt(Func<object> f, int fallback) { try { return Convert.ToInt32(f(), CultureInfo.InvariantCulture); } catch { return fallback; } }
        private static string SafeString(Func<object> f) { try { return Convert.ToString(f(), CultureInfo.InvariantCulture); } catch { return null; } }
        private static DateTime SafeDate(Func<object> f) { try { return Convert.ToDateTime(f(), CultureInfo.CurrentCulture); } catch { return DateTime.MinValue; } }
        private static bool SafeBool(Func<object> f) { try { return Convert.ToBoolean(f(), CultureInfo.InvariantCulture); } catch { return false; } }
        private static string ColorFor(string id) { var colors = new[] { "#6D7CFF", "#49C5B6", "#FC8C6A", "#C66BFF", "#F5C451" }; return colors[Math.Abs(id.GetHashCode()) % colors.Length]; }
    }
}
