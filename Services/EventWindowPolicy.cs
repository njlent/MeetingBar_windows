using System;
using System.Collections.Generic;
using System.Linq;
using MeetingBar.Windows.Models;

namespace MeetingBar.Windows.Services
{
    public static class EventWindowPolicy
    {
        public static Tuple<DateTime, DateTime> Range(AppSettings settings, DateTime now) => Range(
            settings?.PastMeetingHours ?? 3,
            settings?.FutureMeetingHours ?? 24,
            settings?.IncludePastDay == true,
            settings?.IncludeNextDay != false,
            now);

        public static Tuple<DateTime, DateTime> Range(int pastHours, int futureHours, bool includePastDay, bool includeNextDay, DateTime now)
        {
            var start = now.AddHours(-Math.Max(0, Math.Min(168, pastHours)));
            var end = now.AddHours(Math.Max(1, Math.Min(168, futureHours)));
            if (!includePastDay && start.Date < now.Date) start = now.Date;
            if (!includeNextDay && end.Date > now.Date) end = now.Date.AddDays(1);
            if (end <= start) end = start.AddHours(1);
            return Tuple.Create(start, end);
        }

        public static Tuple<DateTime, DateTime> FullDayRange(bool includePastDay, bool includeNextDay, DateTime now)
            => FullDayRange(includePastDay ? 1 : 0, includeNextDay ? 1 : 0, now);

        public static Tuple<DateTime, DateTime> FullDayRange(int pastDays, int futureDays, DateTime now)
        {
            var start = now.Date.AddDays(-Math.Max(0, Math.Min(30, pastDays)));
            var end = now.Date.AddDays(Math.Max(0, Math.Min(30, futureDays)) + 1);
            return Tuple.Create(start, end);
        }

        public static IReadOnlyList<CalendarEvent> Page(IEnumerable<CalendarEvent> events, AppSettings settings, int dayOffset, DateTime now)
        {
            var dayStart = now.Date.AddDays(dayOffset);
            var dayEnd = dayStart.AddDays(1);
            var window = Range(settings, now);
            return (events ?? Enumerable.Empty<CalendarEvent>())
                .Where(item => dayOffset == 0
                    ? (settings.ShowFullDayEventList && item.End > dayStart && item.Start < dayEnd)
                      || item.End > window.Item1 && item.Start < window.Item2
                    : item.End > dayStart && item.Start < dayEnd)
                .OrderBy(item => item.Start).ThenBy(item => item.End).ToList();
        }

        public static CalendarEvent NextTaskbarEvent(IEnumerable<CalendarEvent> events, bool ignoreFreeEvents, DateTime now) =>
            (events ?? Enumerable.Empty<CalendarEvent>()).FirstOrDefault(item => item.End > now && !item.IsDismissed && (!ignoreFreeEvents || item.IsBusy));

        /// <summary>Days searched for the next meeting, independent of the days shown in the popup.</summary>
        public const int UpcomingSearchDays = 30;

        /// <summary>
        /// Picks the event for the taskbar and tray. Timed meetings outrank all-day events such as holidays
        /// or out-of-office blocks, which would otherwise hold the taskbar for the whole day. An all-day
        /// event is shown only while no timed meeting is left today.
        /// </summary>
        public static CalendarEvent NextPrioritizedEvent(IEnumerable<CalendarEvent> events, AppSettings settings, DateTime now)
        {
            var list = (events ?? Enumerable.Empty<CalendarEvent>()).ToList();
            var ignoreFree = settings?.IgnoreFreeEventsInTaskbar == true;
            var timed = NextTaskbarEvent(list.Where(item => !item.IsAllDay), ignoreFree, now);
            if (settings != null && (!settings.ShowAllDayEvents || !settings.ShowAllDayEventsInWidget)) return timed;
            if (timed != null && timed.Start < now.Date.AddDays(1)) return timed;
            var allDay = NextTaskbarEvent(list.Where(item => item.IsAllDay), ignoreFree, now);
            if (allDay != null && allDay.Start <= now) return allDay;
            return timed ?? allDay;
        }

        public static CalendarEvent NextWidgetEvent(IEnumerable<CalendarEvent> events, AppSettings settings, DateTime now)
        {
            var next = NextPrioritizedEvent(events, settings, now);
            if (next == null || settings == null || settings.EndOfDayWidgetBehavior == EndOfDayWidgetBehavior.NextEvent) return next;
            return next.Start.Date == now.Date || next.Start <= now ? next : null;
        }

        public static bool CanDismiss(CalendarEvent item, DateTime now) => item != null && !item.IsDismissed && item.End > now;
        public static bool ShouldRestoreDismissal(CalendarEvent item, DateTime now) => item != null && item.IsDismissed && item.End <= now;
    }
}
