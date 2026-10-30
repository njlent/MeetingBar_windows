using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using MeetingBar.Windows.Models;
using MeetingBar.Localization;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public static class WinWWidgetStateService
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingBar");
        public static readonly string StatePath = Path.Combine(Folder, "winw-widget-state.json");

        public static void Write(IEnumerable<CalendarEvent> events, AppSettings settings)
        {
            if (!settings.EnableWinWWidget) { try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { } return; }
            Directory.CreateDirectory(Folder);
            var state = new Dictionary<string, object>
            {
                ["ApplicationPath"] = Process.GetCurrentProcess().MainModule.FileName,
                ["Use24HourClock"] = settings.Use24HourClock,
                ["Language"] = Localizer.Language,
                ["Events"] = events.OrderBy(x => x.Start).Select(x => new Dictionary<string, object>
                {
                    ["Title"] = x.Title ?? T("Untitled event"), ["Start"] = x.Start.ToString("o"), ["End"] = x.End.ToString("o"),
                    ["IsAllDay"] = x.IsAllDay, ["CanJoin"] = x.CanJoin, ["IsDismissed"] = x.IsDismissed, ["Color"] = x.CalendarColor ?? "#6D7CFF"
                }).ToList()
            };
            var temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(state), new UTF8Encoding(false));
            if (File.Exists(StatePath)) File.Replace(temporary, StatePath, null); else File.Move(temporary, StatePath);
        }
    }
}
