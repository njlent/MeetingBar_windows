using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using MeetingBar.Windows.Models;
using MeetingBar.Windows.Views;
using Forms = System.Windows.Forms;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public sealed class NotificationService : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly MeetingOpener _opener;
        private readonly Forms.NotifyIcon _tray;
        private readonly DispatcherTimer _timer;
        private readonly HashSet<string> _sent = new HashSet<string>();
        private Func<IReadOnlyList<CalendarEvent>> _events;

        public NotificationService(SettingsService settings, MeetingOpener opener, Forms.NotifyIcon tray)
        {
            _settings = settings; _opener = opener; _tray = tray;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _timer.Tick += (s, e) => Check();
        }

        public void Start(Func<IReadOnlyList<CalendarEvent>> events) { _events = events; _timer.Start(); }
        public void ShowTest(CalendarEvent item, bool fullScreen)
        {
            if (item == null) return;
            _tray.BalloonTipTitle = T("TEST · {0}", item.Title);
            _tray.BalloonTipText = item.CanJoin ? T("{0} · Click MeetingBar to join", item.Countdown) : item.Countdown;
            _tray.ShowBalloonTip(7000);
            if (fullScreen) new ReminderWindow(item, () => _opener.Open(item)).Show();
        }
        private void Check()
        {
            var now = DateTime.Now; var settings = _settings.Current;
            foreach (var item in (_events?.Invoke() ?? new List<CalendarEvent>()).Where(x => IsNotificationEligible(x, now)))
            {
                var canSendReminders = IsNotificationEligible(item, settings, now);
                if (canSendReminders)
                {
                foreach (var minutesBefore in ReminderOffsets(settings))
                {
                    var notifyAt = item.Start.AddMinutes(-minutesBefore);
                    if (settings.NotifyBeforeMeeting && now >= notifyAt && now < notifyAt.AddMinutes(1) && _sent.Add(item.Id + ":start:" + minutesBefore))
                    {
                    _tray.BalloonTipTitle = item.Title; _tray.BalloonTipText = item.CanJoin ? T("{0} · Click MeetingBar to join", item.Countdown) : item.Countdown; _tray.ShowBalloonTip(7000);
                    if (settings.FullscreenReminder) new ReminderWindow(item, () => _opener.Open(item)).Show();
                    }
                }
                }
                if (settings.AutoJoin && item.CanJoin && now >= item.Start.AddMinutes(-settings.AutoJoinMinutesBefore) && now < item.Start.AddMinutes(-settings.AutoJoinMinutesBefore).AddMinutes(1) && _sent.Add(item.Id + ":auto")) _opener.Open(item);
                if (canSendReminders && settings.NotifyAtEnd && now >= item.End && now < item.End.AddMinutes(1) && _sent.Add(item.Id + ":end"))
                { _tray.BalloonTipTitle = item.Title; _tray.BalloonTipText = T("Meeting ended"); _tray.ShowBalloonTip(5000); }
            }
            _sent.RemoveWhere(id => !(_events?.Invoke() ?? new List<CalendarEvent>()).Any(x => id.StartsWith(x.Id + ":")));
        }
        public static IReadOnlyList<int> ReminderOffsets(AppSettings settings)
        {
            IEnumerable<int> values = settings.NotificationScheduleVersion < 1
                ? new[] { settings.NotifyMinutesBefore }
                : settings.NotificationMinutesBefore ?? new List<int>();
            return values.Where(x => x >= 0 && x <= 120).Distinct().OrderByDescending(x => x).ToList();
        }
        public static bool IsNotificationEligible(CalendarEvent item, AppSettings settings, DateTime now) =>
            IsNotificationEligible(item, now) && (settings?.IgnoreFreeEventsInTaskbar != true || item.IsBusy);
        public static bool IsNotificationEligible(CalendarEvent item, DateTime now) => item != null && !item.IsDismissed && item.End > now.AddMinutes(-1);
        public void Dispose() => _timer.Stop();
    }
}
