using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MeetingBar.Windows.Models;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public sealed class CalendarSyncService : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly OutlookCalendarProvider _outlook = new OutlookCalendarProvider();
        private readonly IcsCalendarProvider _ics = new IcsCalendarProvider();
        private readonly GoogleCalendarProvider _google = new GoogleCalendarProvider();
        private Timer _timer;
        private int _refreshing;
        private List<CalendarEvent> _lastGoodEvents = new List<CalendarEvent>();
        private readonly Dictionary<string, Tuple<List<CalendarInfo>, List<CalendarEvent>>> _lastGoodBySource = new Dictionary<string, Tuple<List<CalendarInfo>, List<CalendarEvent>>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CalendarEvent> _debugEvents = new List<CalendarEvent>();

        public IReadOnlyList<CalendarEvent> Events { get; private set; } = new List<CalendarEvent>();
        public IReadOnlyList<CalendarEvent> TimelineEvents { get; private set; } = new List<CalendarEvent>();
        public IReadOnlyList<CalendarInfo> Calendars { get; private set; } = new List<CalendarInfo>();
        public ProviderStatus Status { get; private set; } = new ProviderStatus { Provider = "Calendar", Message = T("Starting…") };
        public event EventHandler Changed;

        public CalendarSyncService(SettingsService settings) { _settings = settings; }
        public void Start() { Schedule(); _ = RefreshAsync(); }
        public void Schedule()
        {
            _timer?.Dispose();
            _timer = new Timer(async _ => await RefreshAsync(), null, TimeSpan.FromMinutes(Math.Max(1, _settings.Current.RefreshMinutes)), TimeSpan.FromMinutes(Math.Max(1, _settings.Current.RefreshMinutes)));
        }

        public async Task RefreshAsync()
        {
            if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
            try
            {
                var now = DateTime.Now;
                var fetchRange = TimelineDayRange(_settings.Current, now);
                var from = fetchRange.Item1;
                var to = fetchRange.Item2;
                var sources = _settings.Current.CalendarSources.Where(x => x != null && x.Enabled).ToList();
                var activeIds = new HashSet<string>(sources.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
                foreach (var staleId in _lastGoodBySource.Keys.Where(x => !activeIds.Contains(x)).ToList()) _lastGoodBySource.Remove(staleId);
                var outcomes = await Task.WhenAll(sources.Select(source => FetchSourceAsync(source, from, to)));
                var errors = new List<string>();
                foreach (var outcome in outcomes)
                {
                    if (outcome.Error == null) _lastGoodBySource[outcome.Source.Id] = outcome.Result;
                    else
                    {
                        errors.Add(outcome.Source.Name + ": " + outcome.Error.Message);
                        AppLog.Error("Calendar refresh (" + outcome.Source.Provider + ")", outcome.Error);
                    }
                }
                Calendars = sources.Where(x => _lastGoodBySource.ContainsKey(x.Id)).SelectMany(x => _lastGoodBySource[x.Id].Item1).ToList();
                _lastGoodEvents = sources.Where(x => _lastGoodBySource.ContainsKey(x.Id)).SelectMany(x => _lastGoodBySource[x.Id].Item2).OrderBy(x => x.Start).ToList();
                foreach (var item in _lastGoodEvents)
                {
                    var meeting = MeetingLinkDetector.Detect(item, _settings.Current.CustomMeetingRegexes);
                    if (meeting != null) { item.MeetingUrl = meeting.Url; item.MeetingService = meeting.Service; }
                    item.IsDismissed = item.End > now && _settings.Current.DismissedEventIds.Contains(item.Id);
                }
                var activeEventIds = new HashSet<string>(_lastGoodEvents.Where(x => x.End > now).Select(x => x.Id));
                if (_settings.Current.DismissedEventIds.RemoveAll(id => !activeEventIds.Contains(id)) > 0) _settings.Save();
                RebuildViews();
                Status = new ProviderStatus
                {
                    Provider = "Calendars",
                    IsHealthy = errors.Count == 0,
                    Message = sources.Count == 0 ? T("No calendar sources configured") : errors.Count == 0 ? T("Updated {0}", DateTime.Now.ToString("t", FormatCulture)) : string.Join(" | ", errors)
                };
                AppLog.Info("Calendar refresh completed: sources=" + sources.Count + ", errors=" + errors.Count + ", calendars=" + Calendars.Count + ", visibleEvents=" + Events.Count);
            }
            catch (Exception ex)
            {
                RebuildViews();
                Status = new ProviderStatus { Provider = "Calendars", IsHealthy = false, Message = ex.Message };
                AppLog.Error("Calendar refresh", ex);
            }
            finally { Interlocked.Exchange(ref _refreshing, 0); Changed?.Invoke(this, EventArgs.Empty); }
        }

        private async Task<SourceFetchOutcome> FetchSourceAsync(CalendarSourceSettings source, DateTime from, DateTime to)
        {
            try
            {
                Tuple<List<CalendarInfo>, List<CalendarEvent>> result;
                switch (source.Provider)
                {
                    case CalendarProviderKind.Ics: result = await _ics.FetchAsync(source.IcsSources, from, to); break;
                    case CalendarProviderKind.Google: result = await _google.FetchAsync(_settings.Current, source, from, to); break;
                    default: result = await _outlook.FetchAsync(from, to); break;
                }
                foreach (var calendar in result.Item1)
                {
                    calendar.SourceId = source.Id;
                    calendar.SourceName = source.Name;
                }
                foreach (var item in result.Item2)
                {
                    item.SourceId = source.Id;
                    item.CalendarSourceName = source.Name;
                    item.Id = SourceEventId(source.Id, item.CalendarId, item.Id);
                }
                return new SourceFetchOutcome { Source = source, Result = result };
            }
            catch (Exception error) { return new SourceFetchOutcome { Source = source, Error = error }; }
        }

        internal static string SourceEventId(string sourceId, string calendarId, string eventId) =>
            (sourceId ?? "") + "::" + (calendarId ?? "") + "::" + (eventId ?? "");

        private sealed class SourceFetchOutcome
        {
            public CalendarSourceSettings Source { get; set; }
            public Tuple<List<CalendarInfo>, List<CalendarEvent>> Result { get; set; }
            public Exception Error { get; set; }
        }

        private void RebuildViews()
        {
            var s = _settings.Current;
            var now = DateTime.Now;
            var dayRange = TimelineDayRange(s, now);
            var dayStart = dayRange.Item1;
            var dayEnd = dayRange.Item2;
            var all = FilterGeneral(_lastGoodEvents.Concat(_debugEvents)).ToList();
            TimelineEvents = all.Where(item => item.End > dayStart && item.Start < dayEnd).ToList();
            var listRange = s.ShowFullDayEventList ? Tuple.Create(dayStart, dayEnd) : EventWindowPolicy.Range(s, now);
            Events = all.Where(item => item.End > listRange.Item1 && item.Start < listRange.Item2).ToList();
        }

        private static Tuple<DateTime, DateTime> TimelineDayRange(AppSettings settings, DateTime now) => EventWindowPolicy.FullDayRange(
            settings.IncludePastDay ? settings.TimelinePastDays : 0,
            settings.IncludeNextDay ? settings.TimelineFutureDays : 0,
            now);

        private IEnumerable<CalendarEvent> FilterGeneral(IEnumerable<CalendarEvent> events)
        {
            var s = _settings.Current;
            foreach (var item in events.OrderBy(x => x.Start))
            {
                if (item.IsCancelled) continue;
                if (!s.ShowAllDayEvents && item.IsAllDay) continue;
                if (!s.ShowDeclinedEvents && item.IsDeclined) continue;
                if (!s.ShowTentativeEvents && item.IsTentative) continue;
                if (!s.ShowFreeEvents && !item.IsBusy) continue;
                if (s.RequireMeetingLink && !item.CanJoin) continue;
                if (item.CalendarId != "meetingbar-debug")
                {
                    var source = s.CalendarSources.FirstOrDefault(x => x.Id == item.SourceId);
                    if (source != null && source.CalendarSelectionConfigured && !source.SelectedCalendarIds.Contains(item.CalendarId)) continue;
                }
                if (s.HiddenTitlePatterns.Any(p => !string.IsNullOrWhiteSpace(p) && System.Text.RegularExpressions.Regex.IsMatch(item.Title ?? "", p, System.Text.RegularExpressions.RegexOptions.IgnoreCase))) continue;
                item.Use24HourClock = s.Use24HourClock;
                yield return item;
            }
        }

        public CalendarEvent LatestDebugEvent => _debugEvents.LastOrDefault();
        public void InjectDebugEvent(CalendarEvent item)
        {
            if (item == null) return;
            _debugEvents.Add(item);
            RebuildViews();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        public void ClearDebugEvents()
        {
            _debugEvents.Clear();
            RebuildViews();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void DismissEvent(CalendarEvent item)
        {
            if (!EventWindowPolicy.CanDismiss(item, DateTime.Now) || string.IsNullOrWhiteSpace(item.Id)) return;
            if (!_settings.Current.DismissedEventIds.Contains(item.Id))
            {
                _settings.Current.DismissedEventIds.Add(item.Id);
                _settings.Save();
            }
            item.IsDismissed = true;
            RebuildViews();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void RestoreExpiredDismissals(DateTime now)
        {
            var expired = _lastGoodEvents.Concat(_debugEvents).Where(item => EventWindowPolicy.ShouldRestoreDismissal(item, now)).ToList();
            if (expired.Count == 0) return;
            foreach (var item in expired) item.IsDismissed = false;
            var ids = new HashSet<string>(expired.Select(item => item.Id));
            if (_settings.Current.DismissedEventIds.RemoveAll(ids.Contains) > 0) _settings.Save();
            RebuildViews();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void RestoreEvent(CalendarEvent item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;
            if (_settings.Current.DismissedEventIds.Remove(item.Id)) _settings.Save();
            item.IsDismissed = false;
            RebuildViews();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public GoogleCalendarProvider Google => _google;
        public void Dispose() => _timer?.Dispose();
    }
}
