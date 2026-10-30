using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MeetingBar.Windows.Models;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public static class DebugEventFactory
    {
        private static readonly Dictionary<string, string> Links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Google Meet"] = "https://meet.google.com/debug-test",
            ["Microsoft Teams"] = "https://teams.microsoft.com/l/meetup-join/debug-test",
            ["Zoom"] = "https://zoom.us/j/123456789",
            ["No meeting link"] = null
        };

        public static CalendarEvent Create(string title, int startMinutes, int durationMinutes, string service, string color, DateTime now)
        {
            string link;
            Links.TryGetValue(service ?? "", out link);
            var start = now.AddMinutes(Math.Max(-1440, Math.Min(1440, startMinutes)));
            return new CalendarEvent
            {
                Id = "debug-" + Guid.NewGuid().ToString("N"),
                Title = string.IsNullOrWhiteSpace(title) ? T("Test meeting") : title.Trim(),
                Start = start,
                End = start.AddMinutes(Math.Max(1, Math.Min(1440, durationMinutes))),
                CalendarId = "meetingbar-debug",
                CalendarName = "MeetingBar Debug",
                CalendarColor = Regex.IsMatch(color ?? "", "^#[0-9A-Fa-f]{6}$") ? color : "#0078D4",
                Organizer = "debug@meetingbar.local",
                MeetingService = string.Equals(service, "No meeting link", StringComparison.OrdinalIgnoreCase) ? null : service,
                MeetingUrl = link,
                ConferenceUrl = link,
                Notes = T("Injected locally from MeetingBar Settings. Not stored in your calendar.")
            };
        }
    }
}
