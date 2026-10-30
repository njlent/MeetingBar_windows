using System;
using System.Globalization;
using System.ComponentModel;
using System.Net;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Models
{
    public sealed class CalendarEvent : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public string Location { get; set; }
        public string EventUrl { get; set; }
        public string CalendarOpenUrl { get; set; }
        public string ConferenceUrl { get; set; }
        public string SourceId { get; set; }
        public string CalendarSourceName { get; set; }
        public string CalendarId { get; set; }
        public string CalendarName { get; set; }
        public string CalendarColor { get; set; } = "#6D7CFF";
        public string Organizer { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool IsAllDay { get; set; }
        public bool IsDeclined { get; set; }
        public bool IsTentative { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsBusy { get; set; } = true;
        public string MeetingUrl { get; set; }
        public string MeetingService { get; set; }
        public bool IsDismissed { get; set; }
        public bool Use24HourClock { get; set; } = true;
        public bool ShowDayDivider { get; set; }

        public bool IsCurrent => Start <= DateTime.Now && End > DateTime.Now;
        public bool IsPast => End <= DateTime.Now;
        public bool CanJoin => !string.IsNullOrWhiteSpace(MeetingUrl);
        public bool CanRestoreNotifications => IsDismissed && !IsPast;
        public bool CanOpenDetails => !string.IsNullOrWhiteSpace(CalendarOpenUrl);
        public string StartTimeText => FormatTime(Start, Use24HourClock);
        public string TimeRange => IsAllDay ? T("All day") : FormatTime(Start, Use24HourClock) + " – " + FormatTime(End, Use24HourClock);
        public string DayLabel => Start.Date == DateTime.Today ? T("TODAY") : Start.Date == DateTime.Today.AddDays(1) ? T("TOMORROW") : FormatDayName(Start);
        /// <summary>Short weekday and date for day headings; the pattern itself is translatable.</summary>
        public static string FormatDayName(DateTime value) => value.ToString(T("ddd, d MMM"), FormatCulture).ToUpper(FormatCulture);
        public static string FormatTime(DateTime value, bool use24HourClock, bool hourOnly = false) => value.ToString(
            use24HourClock ? (hourOnly ? "HH" : "HH:mm") : (hourOnly ? "h tt" : "h:mm tt"),
            use24HourClock ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture);
        public static string FormatDate(DateTime value, string format)
        {
            switch (format)
            {
                case "MM/dd/yyyy": case "dd/MM/yyyy": case "yyyy-MM-dd": case "dd MMM yyyy": break;
                default: format = "dd.MM.yyyy"; break;
            }
            return value.ToString(format, FormatCulture);
        }
        public string CalendarDisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CalendarName)) return CalendarSourceName;
                if (string.IsNullOrWhiteSpace(CalendarSourceName) || string.Equals(CalendarName, CalendarSourceName, StringComparison.OrdinalIgnoreCase)) return CalendarName;
                return CalendarName + " · " + CalendarSourceName;
            }
        }
        public string CalendarDetails => string.IsNullOrWhiteSpace(CalendarDisplayName) ? null : T("Calendar") + "  ·  " + CalendarDisplayName;
        public string OrganizerDetails => string.IsNullOrWhiteSpace(Organizer) ? null : T("Organizer") + "  ·  " + Organizer;
        public string LocationDetails => string.IsNullOrWhiteSpace(Location) ? null : T("Location") + "  ·  " + Location;
        public string MeetingDetails => string.IsNullOrWhiteSpace(MeetingService) ? null : T("Meeting link") + "  ·  " + MeetingService;
        public string DetailsNotes => string.IsNullOrWhiteSpace(Notes) ? null : WebUtility.HtmlDecode(Regex.Replace(Notes, "<[^>]+>", " ")).Trim();
        public string Countdown
        {
            get
            {
                if (IsCurrent) return T("Now");
                if (IsPast)
                {
                    return T("Ended");
                }
                var span = Start - DateTime.Now;
                if (span.TotalMinutes < 1) return T("Soon");
                if (span.TotalHours < 1) return T("in {0}m", Math.Ceiling(span.TotalMinutes));
                if (span.TotalDays < 1) return T("in {0}h {1}m", Math.Floor(span.TotalHours), span.Minutes);
                return T("in {0}d", Math.Floor(span.TotalDays));
            }
        }
        public string DetailedCountdown
        {
            get
            {
                if (!IsPast) return Countdown;
                var elapsed = DateTime.Now - End;
                if (elapsed.TotalMinutes < 1) return T("Just ended");
                if (elapsed.TotalHours < 1) return T("Ended {0}m ago", Math.Floor(elapsed.TotalMinutes));
                if (elapsed.TotalDays < 1) return T("Ended {0}h {1}m ago", Math.Floor(elapsed.TotalHours), elapsed.Minutes);
                return T("Ended {0}d ago", Math.Floor(elapsed.TotalDays));
            }
        }
        public string Remaining
        {
            get
            {
                if (!IsCurrent) return Countdown;
                var remaining = End - DateTime.Now;
                if (remaining.TotalMinutes < 1) return T("Less than 1m left");
                if (remaining.TotalHours < 1) return T("{0}m left", Math.Ceiling(remaining.TotalMinutes));
                return T("{0}h {1}m left", Math.Floor(remaining.TotalHours), remaining.Minutes);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void RefreshComputed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
