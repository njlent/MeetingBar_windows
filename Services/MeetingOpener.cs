using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MeetingBar.Windows.Models;

namespace MeetingBar.Windows.Services
{
    public sealed class MeetingOpener
    {
        private const string GoogleCalendarUrl = "https://calendar.google.com/calendar/u/0/r";
        private const string OutlookWebCalendarUrl = "https://outlook.office.com/calendar/";

        // Links read from calendars come from other people, so only web and meeting-app protocols are
        // opened. This keeps an invite or ICS feed from launching file:, ms-msdt: or similar handlers.
        private static readonly HashSet<string> CalendarLinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "http", "https", "zoommtg", "zoomus", "msteams", "discord", "slack", "webex", "tel", "sip", "sips"
        };

        private readonly SettingsService _settings;
        public MeetingOpener(SettingsService settings) { _settings = settings; }

        public void Open(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var s = _settings.Current;
            if (!string.IsNullOrWhiteSpace(s.PreferredBrowserPath))
            {
                var arguments = string.IsNullOrWhiteSpace(s.PreferredBrowserArguments) ? Quote(url) : s.PreferredBrowserArguments.Replace("{url}", Quote(url));
                Process.Start(new ProcessStartInfo(s.PreferredBrowserPath, arguments) { UseShellExecute = true });
            }
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        public void Open(CalendarEvent item)
        {
            if (IsSafeCalendarLink(item?.MeetingUrl)) Open(item.MeetingUrl);
        }

        internal static bool IsSafeCalendarLink(string url) =>
            !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && CalendarLinkSchemes.Contains(uri.Scheme);

        /// <summary>The provider of the event's own source, or of the first enabled source for the general calendar button.</summary>
        internal static CalendarProviderKind CalendarProviderFor(AppSettings settings, CalendarEvent item)
        {
            var sources = settings?.CalendarSources ?? new List<CalendarSourceSettings>();
            var source = sources.FirstOrDefault(x => x != null && item != null && string.Equals(x.Id, item.SourceId, StringComparison.OrdinalIgnoreCase))
                ?? sources.FirstOrDefault(x => x != null && x.Enabled);
            return source?.Provider ?? settings?.Provider ?? CalendarProviderKind.Outlook;
        }

        private static string CalendarLink(CalendarEvent item, bool openEvent) =>
            (openEvent ? new[] { item?.CalendarOpenUrl, item?.EventUrl } : new[] { item?.CalendarOpenUrl }).FirstOrDefault(IsSafeCalendarLink);

        public bool CanOpenCalendar(CalendarEvent item, bool openEvent) =>
            CalendarProviderFor(_settings.Current, item) != CalendarProviderKind.Ics || CalendarLink(item, openEvent) != null;

        /// <summary>
        /// Opens the event's calendar. With <paramref name="openEvent"/>, an event that has its own link opens that link instead.
        /// </summary>
        public void OpenCalendar(CalendarEvent item, bool openEvent)
        {
            var link = CalendarLink(item, openEvent);
            if (openEvent && link != null) { Open(link); return; }
            switch (CalendarProviderFor(_settings.Current, item))
            {
                case CalendarProviderKind.Google: Open(GoogleCalendarUrl); break;
                case CalendarProviderKind.Outlook: OpenOutlookCalendar(item); break;
                default: Open(link); break;
            }
        }

        private void OpenOutlookCalendar(CalendarEvent item)
        {
            // Events from an Outlook source carry their folder's EntryID as CalendarId.
            var fromOutlookSource = item != null && _settings.Current.CalendarSources.Any(x => x != null
                && x.Provider == CalendarProviderKind.Outlook && string.Equals(x.Id, item.SourceId, StringComparison.OrdinalIgnoreCase));
            var calendarId = fromOutlookSource ? item.CalendarId : null;
            // Starting Outlook can take seconds; keep the UI responsive.
            Task.Run(() =>
            {
                try { OutlookCalendarProvider.OpenCalendarFolder(calendarId); }
                catch (Exception error)
                {
                    AppLog.Error("Open Outlook calendar", error);
                    try { Open("outlookcal:"); }
                    catch { Open(OutlookWebCalendarUrl); }
                }
            });
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
