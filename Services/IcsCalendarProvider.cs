using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MeetingBar.Windows.Models;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public sealed class IcsCalendarProvider
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public async Task<Tuple<List<CalendarInfo>, List<CalendarEvent>>> FetchAsync(string sourceList, DateTime from, DateTime to)
        {
            var calendars = new List<CalendarInfo>();
            var events = new List<CalendarEvent>();
            foreach (var source in (sourceList ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                var remoteUri = NormalizeRemoteUri(source);
                var text = remoteUri != null ? await _http.GetStringAsync(remoteUri) : File.ReadAllText(Environment.ExpandEnvironmentVariables(source));
                var id = StableId(source);
                var name = CalendarName(text, source);
                calendars.Add(new CalendarInfo { Id = id, Name = name, Source = "ICS", Color = ColorFor(id) });
                events.AddRange(Parse(text, id, name, ColorFor(id), from, to));
            }
            return Tuple.Create(calendars, events);
        }

        internal static Uri NormalizeRemoteUri(string source)
        {
            Uri uri;
            if (!Uri.TryCreate(source, UriKind.Absolute, out uri)) return null;
            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) return uri;
            if (!string.Equals(uri.Scheme, "webcal", StringComparison.OrdinalIgnoreCase) && !string.Equals(uri.Scheme, "webcals", StringComparison.OrdinalIgnoreCase)) return null;
            var builder = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps };
            if (uri.IsDefaultPort) builder.Port = -1;
            return builder.Uri;
        }

        internal static List<CalendarEvent> Parse(string input, string calendarId, string calendarName, string color, DateTime from, DateTime to)
        {
            var unfolded = (input ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
            while (unfolded.Contains("\n ") || unfolded.Contains("\n\t")) unfolded = unfolded.Replace("\n ", "").Replace("\n\t", "");
            var result = new List<CalendarEvent>();
            Dictionary<string, string> fields = null;
            foreach (var raw in unfolded.Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line == "BEGIN:VEVENT") { fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); continue; }
                if (line == "END:VEVENT")
                {
                    var item = CreateEvent(fields, calendarId, calendarName, color);
                    if (item != null) result.AddRange(ExpandRecurrence(item, fields, from, to));
                    fields = null;
                    continue;
                }
                if (fields == null) continue;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon);
                var value = line.Substring(colon + 1);
                fields[key] = value;
                var baseKey = key.Split(';')[0];
                if (!fields.ContainsKey(baseKey)) fields[baseKey] = value;
            }
            return result.Where(x => x.End > from && x.Start < to).OrderBy(x => x.Start).ToList();
        }

        private static CalendarEvent CreateEvent(Dictionary<string, string> f, string calendarId, string calendarName, string color)
        {
            if (f == null || !f.ContainsKey("DTSTART")) return null;
            var startKey = f.Keys.First(x => x.StartsWith("DTSTART", StringComparison.OrdinalIgnoreCase));
            var endKey = f.Keys.FirstOrDefault(x => x.StartsWith("DTEND", StringComparison.OrdinalIgnoreCase));
            var start = ParseDate(f[startKey], startKey);
            var end = endKey == null ? start.AddHours(1) : ParseDate(f[endKey], endKey);
            var allDay = startKey.IndexOf("VALUE=DATE", StringComparison.OrdinalIgnoreCase) >= 0 || f[startKey].Length == 8;
            string Get(string key) => f.ContainsKey(key) ? Unescape(f[key]) : null;
            return new CalendarEvent
            {
                Id = Get("UID") ?? Guid.NewGuid().ToString("N"), Title = Get("SUMMARY") ?? T("Untitled event"), Notes = Get("DESCRIPTION"),
                Location = Get("LOCATION"), EventUrl = Get("URL"), ConferenceUrl = Get("CONFERENCE"), Organizer = Get("ORGANIZER"),
                CalendarId = calendarId, CalendarName = calendarName, CalendarColor = color, Start = start, End = end, IsAllDay = allDay,
                IsCancelled = string.Equals(Get("STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase),
                IsTentative = string.Equals(Get("STATUS"), "TENTATIVE", StringComparison.OrdinalIgnoreCase),
                IsBusy = !string.Equals(Get("TRANSP"), "TRANSPARENT", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static IEnumerable<CalendarEvent> ExpandRecurrence(CalendarEvent original, Dictionary<string, string> fields, DateTime from, DateTime to)
        {
            if (!fields.ContainsKey("RRULE")) return new[] { original };
            var parts = fields["RRULE"].Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1], StringComparer.OrdinalIgnoreCase);
            if (!parts.ContainsKey("FREQ")) return new[] { original };
            var interval = parts.ContainsKey("INTERVAL") ? Math.Max(1, int.Parse(parts["INTERVAL"], CultureInfo.InvariantCulture)) : 1;
            var count = parts.ContainsKey("COUNT") ? Math.Min(1000, int.Parse(parts["COUNT"], CultureInfo.InvariantCulture)) : 1000;
            var until = parts.ContainsKey("UNTIL") ? ParseDate(parts["UNTIL"], "UNTIL") : to;
            var list = new List<CalendarEvent>();
            var current = original.Start;
            for (var i = 0; i < count && current <= until && current < to; i++)
            {
                if (current.Add(original.End - original.Start) > from)
                {
                    var clone = Clone(original); clone.Start = current; clone.End = current.Add(original.End - original.Start); clone.Id += "-" + current.Ticks; list.Add(clone);
                }
                if (parts["FREQ"] == "DAILY") current = current.AddDays(interval);
                else if (parts["FREQ"] == "WEEKLY") current = current.AddDays(7 * interval);
                else if (parts["FREQ"] == "MONTHLY") current = current.AddMonths(interval);
                else break;
            }
            return list;
        }

        private static CalendarEvent Clone(CalendarEvent x) => new CalendarEvent { Id=x.Id, Title=x.Title, Notes=x.Notes, Location=x.Location, EventUrl=x.EventUrl, CalendarOpenUrl=x.CalendarOpenUrl, ConferenceUrl=x.ConferenceUrl, CalendarId=x.CalendarId, CalendarName=x.CalendarName, CalendarColor=x.CalendarColor, Organizer=x.Organizer, Start=x.Start, End=x.End, IsAllDay=x.IsAllDay, IsCancelled=x.IsCancelled, IsTentative=x.IsTentative, IsBusy=x.IsBusy };
        private static DateTime ParseDate(string value, string key)
        {
            var formats = new[] { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmm'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" };
            DateTime parsed;
            if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | (value.EndsWith("Z") ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal : DateTimeStyles.AssumeLocal), out parsed)) return DateTime.MinValue;
            return parsed.Kind == DateTimeKind.Utc ? parsed.ToLocalTime() : parsed;
        }
        private static string Unescape(string x) => (x ?? "").Replace("\\n", "\n").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
        private static string CalendarName(string text, string source)
        {
            var line = (text ?? "").Split('\n').FirstOrDefault(x => x.StartsWith("X-WR-CALNAME:", StringComparison.OrdinalIgnoreCase));
            if (line != null) return line.Substring(line.IndexOf(':') + 1).Trim();
            return Uri.IsWellFormedUriString(source, UriKind.Absolute) ? new Uri(source).Host : Path.GetFileNameWithoutExtension(source);
        }
        private static string StableId(string input) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).Substring(0, 16); }
        private static string ColorFor(string id) { var colors = new[] { "#6D7CFF", "#49C5B6", "#FC8C6A", "#C66BFF", "#F5C451" }; return colors[Math.Abs(id.GetHashCode()) % colors.Length]; }
    }
}
