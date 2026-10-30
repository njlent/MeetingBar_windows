using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeetingBar.Windows.Models;
using MeetingBar.Windows.Services;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.ViewModels
{
    public sealed class MainViewModel : BindableBase, IDisposable
    {
        private readonly CalendarSyncService _sync;
        private readonly SettingsService _settings;
        private readonly MeetingOpener _opener;
        private readonly DispatcherTimer _clock;
        private bool _isRefreshing;
        private bool _isInitialLoading = true;
        private string _statusText;
        private int _selectedDayOffset;
        public ObservableCollection<CalendarEvent> Events { get; } = new ObservableCollection<CalendarEvent>();
        public ObservableCollection<CalendarEvent> PageEvents { get; } = new ObservableCollection<CalendarEvent>();
        public ObservableCollection<CalendarEvent> TimelineEvents { get; } = new ObservableCollection<CalendarEvent>();
        public ObservableCollection<Bookmark> Bookmarks { get; } = new ObservableCollection<Bookmark>();
        public CalendarEvent TaskbarEvent => EventWindowPolicy.NextTaskbarEvent(Events, _settings.Current.IgnoreFreeEventsInTaskbar, DateTime.Now);
        public CalendarEvent WidgetEvent => EventWindowPolicy.NextWidgetEvent(Events, _settings.Current, DateTime.Now);
        public CalendarEvent NextEvent => WidgetEvent;
        public bool ShouldShowTaskbarWidget => _settings.Current.EndOfDayWidgetBehavior != EndOfDayWidgetBehavior.Hide || WidgetEvent != null;
        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
        public bool IsRefreshing { get => _isRefreshing; set => Set(ref _isRefreshing, value); }
        public Visibility LoadingVisibility => _isInitialLoading ? Visibility.Visible : Visibility.Collapsed;
        public bool HasEvents => PageEvents.Count > 0;
        public bool HasBookmarks => Bookmarks.Count > 0;
        public int TimelinePastHours => _settings.Current.PastMeetingHours;
        public int TimelineFutureHours => _settings.Current.FutureMeetingHours;
        public bool TimelineIncludesPastDay => _settings.Current.IncludePastDay;
        public bool TimelineIncludesNextDay => _settings.Current.IncludeNextDay;
        public int TimelinePastDays => _settings.Current.TimelinePastDays;
        public int TimelineFutureDays => _settings.Current.TimelineFutureDays;
        public bool Use24HourClock => _settings.Current.Use24HourClock;
        public bool ShowTimelineDate => _settings.Current.ShowTimelineDate;
        public string TimelineDateFormat => _settings.Current.TimelineDateFormat;
        public string Header => NextEvent?.IsCurrent == true ? T("CURRENT MEETING") : T("NEXT MEETING");
        public string NextTitle => NextEvent?.Title ?? T("No upcoming meetings");
        public string NextMeta => NextEvent == null ? StatusText : NextEvent.TimeRange + "  •  " + (NextEvent.IsCurrent && _settings.Current.ShowOngoingTimeRemaining ? NextEvent.Remaining : NextEvent.Countdown);
        public string WidgetTitle
        {
            get
            {
                if (WidgetEvent == null)
                {
                    switch (_settings.Current.EndOfDayWidgetBehavior)
                    {
                        case EndOfDayWidgetBehavior.NoEventsToday: return T("No events for today");
                        case EndOfDayWidgetBehavior.CustomMessage: return TruncateWidgetTitle(EndOfDayMessage(_settings.Current.EndOfDayCustomMessage));
                        default: return T("No upcoming meetings");
                    }
                }
                var mode = _settings.Current.TitleMode;
                if (mode == TitleMode.Hidden) return "";
                if (mode == TitleMode.Dot) return "•";
                if (mode == TitleMode.Generic) return T("Meeting");
                return TruncateWidgetTitle(WidgetEvent.Title ?? T("Untitled event"));
            }
        }
        public string WidgetTime => WidgetEvent == null || _settings.Current.TimeMode == TimeMode.Hidden ? "" : WidgetEvent.IsCurrent && _settings.Current.ShowOngoingTimeRemaining ? WidgetEvent.Remaining : _settings.Current.TimeMode == TimeMode.StartTime ? CalendarEvent.FormatTime(WidgetEvent.Start, _settings.Current.Use24HourClock) : WidgetEvent.Countdown;
        public Visibility WidgetIconVisibility => WidgetEvent != null && _settings.Current.ShowServiceIcon ? Visibility.Visible : Visibility.Collapsed;
        public string WidgetEventColor => WidgetEvent?.CalendarColor ?? "#6D7CFF";
        public bool CanJoinNext => NextEvent?.CanJoin == true;
        public ICommand JoinCommand { get; }
        public ICommand OpenEventCommand { get; }
        public ICommand OpenMapCommand { get; }
        public ICommand CopyMeetingLinkCommand { get; }
        public ICommand DismissEventCommand { get; }
        public ICommand RestoreEventCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CreateMeetingCommand { get; }
        public ICommand OpenCalendarCommand { get; }
        public ICommand OpenBookmarkCommand { get; }

        public MainViewModel(CalendarSyncService sync, SettingsService settings, MeetingOpener opener)
        {
            _sync = sync; _settings = settings; _opener = opener;
            JoinCommand = new RelayCommand(x => _opener.Open(x as CalendarEvent ?? NextEvent));
            OpenEventCommand = new RelayCommand(OpenEventInCalendar, CanOpenEventInCalendar);
            OpenMapCommand = new RelayCommand(x => { var item = x as CalendarEvent; _opener.Open(MapLinkService.BuildUrl(_settings.Current.MapProvider, item?.Location)); }, x => MapLinkService.HasPhysicalLocation((x as CalendarEvent)?.Location));
            CopyMeetingLinkCommand = new RelayCommand(x => { var item = x as CalendarEvent; if (item?.CanJoin == true) try { Clipboard.SetText(item.MeetingUrl); } catch { } }, x => (x as CalendarEvent)?.CanJoin == true);
            DismissEventCommand = new RelayCommand(x => _sync.DismissEvent(x as CalendarEvent), x => EventWindowPolicy.CanDismiss(x as CalendarEvent, DateTime.Now));
            RestoreEventCommand = new RelayCommand(x => _sync.RestoreEvent(x as CalendarEvent), x => (x as CalendarEvent)?.CanRestoreNotifications == true);
            RefreshCommand = new RelayCommand(async () => await Refresh());
            CreateMeetingCommand = new RelayCommand(() => _opener.Open(_settings.Current.CreateMeetingUrl));
            OpenCalendarCommand = new RelayCommand(OpenCalendar, () => CanOpenCalendar);
            OpenBookmarkCommand = new RelayCommand(x => _opener.Open((x as Bookmark)?.Url));
            _sync.Changed += SyncChanged;
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _clock.Tick += (s, e) => { _sync.RestoreExpiredDismissals(DateTime.Now); foreach (var item in TimelineEvents) item.RefreshComputed(); RaiseComputed(); };
            _clock.Start(); Rebuild();
        }
        public bool CanOpenCalendar => _settings.Current.Provider != CalendarProviderKind.Ics || !string.IsNullOrWhiteSpace(NextEvent?.CalendarOpenUrl);
        private void OpenCalendar()
        {
            switch (_settings.Current.Provider)
            {
                case CalendarProviderKind.Google: _opener.Open("https://calendar.google.com/calendar/u/0/r"); break;
                case CalendarProviderKind.Outlook:
                    try { _opener.Open("outlookcal:"); }
                    catch { _opener.Open("https://outlook.office.com/calendar/"); }
                    break;
                default: _opener.Open(NextEvent?.CalendarOpenUrl); break;
            }
        }

        private bool CanOpenEventInCalendar(object value)
        {
            var item = value as CalendarEvent;
            return item != null && (!string.IsNullOrWhiteSpace(item.CalendarOpenUrl) || !string.IsNullOrWhiteSpace(item.EventUrl) || _settings.Current.Provider != CalendarProviderKind.Ics);
        }

        private void OpenEventInCalendar(object value)
        {
            var item = value as CalendarEvent;
            if (item == null) return;
            var url = item.CalendarOpenUrl ?? item.EventUrl;
            if (!string.IsNullOrWhiteSpace(url)) { _opener.Open(url); return; }
            if (_settings.Current.Provider == CalendarProviderKind.Outlook) _opener.Open("outlookcal:");
            else if (_settings.Current.Provider == CalendarProviderKind.Google) _opener.Open("https://calendar.google.com/calendar/u/0/r");
        }

        private async Task Refresh() { IsRefreshing = true; await _sync.RefreshAsync(); IsRefreshing = false; }
        private void SyncChanged(object sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(new Action(() => { _isInitialLoading = false; Rebuild(); }));
        private void Rebuild()
        {
            Events.Clear(); foreach (var item in _sync.Events) Events.Add(item);
            TimelineEvents.Clear(); foreach (var item in _sync.TimelineEvents) TimelineEvents.Add(item);
            SelectDay(_selectedDayOffset);
            Bookmarks.Clear(); foreach (var item in _settings.Current.Bookmarks) Bookmarks.Add(item);
            StatusText = _sync.Status.Message; RaiseComputed();
        }
        public void SelectDay(int dayOffset)
        {
            var minimum = _settings.Current.IncludePastDay ? -Math.Max(0, Math.Min(30, _settings.Current.TimelinePastDays)) : 0;
            var maximum = _settings.Current.IncludeNextDay ? Math.Max(0, Math.Min(30, _settings.Current.TimelineFutureDays)) : 0;
            _selectedDayOffset = Math.Max(minimum, Math.Min(maximum, dayOffset));
            PageEvents.Clear();
            DateTime? previousDay = null;
            foreach (var item in EventWindowPolicy.Page(TimelineEvents, _settings.Current, _selectedDayOffset, DateTime.Now))
            {
                item.ShowDayDivider = previousDay.HasValue && previousDay.Value != item.Start.Date;
                previousDay = item.Start.Date;
                PageEvents.Add(item);
            }
            Raise(nameof(HasEvents));
        }
        // The stored default message is English, so show it translated until the user writes their own.
        internal static string EndOfDayMessage(string custom) =>
            string.IsNullOrWhiteSpace(custom) || custom.Trim() == "Done for today" ? T("Done for today") : custom.Trim();
        private string TruncateWidgetTitle(string title)
        {
            var max = Math.Max(5, _settings.Current.TitleMaxLength);
            return title.Length > max ? title.Substring(0, max - 1) + "…" : title;
        }
        private void RaiseComputed() { Raise(nameof(NextEvent)); Raise(nameof(TaskbarEvent)); Raise(nameof(WidgetEvent)); Raise(nameof(ShouldShowTaskbarWidget)); Raise(nameof(Header)); Raise(nameof(NextTitle)); Raise(nameof(NextMeta)); Raise(nameof(WidgetTitle)); Raise(nameof(WidgetTime)); Raise(nameof(WidgetIconVisibility)); Raise(nameof(WidgetEventColor)); Raise(nameof(CanJoinNext)); Raise(nameof(CanOpenCalendar)); Raise(nameof(HasEvents)); Raise(nameof(HasBookmarks)); Raise(nameof(TimelinePastHours)); Raise(nameof(TimelineFutureHours)); Raise(nameof(TimelineIncludesPastDay)); Raise(nameof(TimelineIncludesNextDay)); Raise(nameof(TimelinePastDays)); Raise(nameof(TimelineFutureDays)); Raise(nameof(Use24HourClock)); Raise(nameof(ShowTimelineDate)); Raise(nameof(TimelineDateFormat)); Raise(nameof(LoadingVisibility)); }
        public void RefreshSettings() { _isInitialLoading = true; RaiseComputed(); }
        public void Dispose() { _sync.Changed -= SyncChanged; _clock.Stop(); }
    }
}
