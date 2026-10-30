using System;
using System.Collections.Generic;

namespace MeetingBar.Windows.Models
{
    public enum WidgetMode { TrayOnly, NearTray, TaskbarLeft }
    public enum CalendarProviderKind { Outlook, Ics, Google }
    public enum TitleMode { Full, Generic, Dot, Hidden }
    public enum TimeMode { Countdown, StartTime, Hidden }
    public enum WidgetHighlightStyle { Hover, Shine }
    public enum EndOfDayWidgetBehavior { NextEvent, Hide, NoEventsToday, CustomMessage }

    public sealed class CalendarSourceSettings
    {
        public string Id { get; set; } = "";
        public CalendarProviderKind Provider { get; set; } = CalendarProviderKind.Outlook;
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string IcsSources { get; set; } = "";
        public string GoogleRefreshTokenProtected { get; set; } = "";
        public string GoogleAccountEmail { get; set; } = "";
        public List<string> SelectedCalendarIds { get; set; } = new List<string>();
        public bool CalendarSelectionConfigured { get; set; }

        public static CalendarSourceSettings Create(CalendarProviderKind provider, string name = null) => new CalendarSourceSettings
        {
            Id = Guid.NewGuid().ToString("N"),
            Provider = provider,
            Name = string.IsNullOrWhiteSpace(name) ? DefaultName(provider) : name
        };

        public static string DefaultName(CalendarProviderKind provider)
        {
            switch (provider)
            {
                case CalendarProviderKind.Ics: return "ICS calendar";
                case CalendarProviderKind.Google: return "Google Calendar";
                default: return "Classic Outlook";
            }
        }
    }

    public sealed class AppSettings
    {
        public WidgetMode WidgetMode { get; set; } = WidgetMode.NearTray;
        public double WidgetWidth { get; set; } = 500;
        public double WidgetOffsetX { get; set; }
        public double WidgetOffsetY { get; set; }
        public bool HideWidgetInFullscreen { get; set; }
        public List<string> FullscreenAppBlacklist { get; set; } = new List<string>();
        public bool UseAutomaticWidgetColor { get; set; } = true;
        public string WidgetBackgroundColor { get; set; } = "#202020";
        public int WidgetSettingsVersion { get; set; }
        public TitleMode TitleMode { get; set; } = TitleMode.Full;
        public TimeMode TimeMode { get; set; } = TimeMode.Countdown;
        public bool ShowOngoingTimeRemaining { get; set; } = true;
        public int TitleMaxLength { get; set; } = 64;
        public bool ShowServiceIcon { get; set; } = true;
        public EndOfDayWidgetBehavior EndOfDayWidgetBehavior { get; set; } = EndOfDayWidgetBehavior.NextEvent;
        public string EndOfDayCustomMessage { get; set; } = "Done for today";
        public bool EnableWinWWidget { get; set; }
        public bool HighlightStartingMeetings { get; set; } = true;
        public int HighlightMinutesBefore { get; set; } = 5;
        public int HighlightMinutesAfterStart { get; set; } = 10;
        public bool HighlightWholeMeeting { get; set; }
        public WidgetHighlightStyle WidgetHighlightStyle { get; set; } = WidgetHighlightStyle.Shine;
        public bool ShowAllDayEvents { get; set; } = true;
        public bool ShowDeclinedEvents { get; set; }
        public bool ShowTentativeEvents { get; set; } = true;
        public bool ShowFreeEvents { get; set; } = true;
        public bool IgnoreFreeEventsInTaskbar { get; set; }
        public bool RequireMeetingLink { get; set; }
        public bool IncludeTomorrow { get; set; } = true;
        public int PastMeetingHours { get; set; } = 3;
        public int FutureMeetingHours { get; set; } = 24;
        public bool IncludePastDay { get; set; }
        public bool IncludeNextDay { get; set; } = true;
        public int TimelinePastDays { get; set; } = 1;
        public int TimelineFutureDays { get; set; } = 1;
        public bool ShowFullDayEventList { get; set; } = true;
        public bool Use24HourClock { get; set; } = true;
        public bool ShowTimelineDate { get; set; } = true;
        public string TimelineDateFormat { get; set; } = "dd.MM.yyyy";
        public int EventWindowSettingsVersion { get; set; }
        public bool StartWithWindows { get; set; }
        public bool NotifyBeforeMeeting { get; set; } = true;
        public int NotifyMinutesBefore { get; set; } = 5;
        public List<int> NotificationMinutesBefore { get; set; } = new List<int> { 5 };
        public int NotificationScheduleVersion { get; set; }
        public bool NotifyAtEnd { get; set; }
        public bool FullscreenReminder { get; set; }
        public bool AutoJoin { get; set; }
        public int AutoJoinMinutesBefore { get; set; }
        public bool GlobalJoinHotkey { get; set; } = true;
        public int RefreshMinutes { get; set; } = 3;
        public int CalendarSourcesVersion { get; set; }
        public List<CalendarSourceSettings> CalendarSources { get; set; } = new List<CalendarSourceSettings>();
        // Retained for automatic migration from settings written by MeetingBar 0.1.
        public CalendarProviderKind Provider { get; set; } = CalendarProviderKind.Outlook;
        public string IcsSources { get; set; } = "";
        public string GoogleClientId { get; set; } = "";
        public string GoogleClientSecret { get; set; } = "";
        public string GoogleRefreshTokenProtected { get; set; } = "";
        public string GoogleAccountEmail { get; set; } = "";
        public List<string> SelectedCalendarIds { get; set; } = new List<string>();
        public List<string> HiddenTitlePatterns { get; set; } = new List<string>();
        public List<string> CustomMeetingRegexes { get; set; } = new List<string>();
        public List<string> DismissedEventIds { get; set; } = new List<string>();
        public List<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();
        public string CreateMeetingUrl { get; set; } = "https://meet.google.com/new";
        public string PreferredBrowserPath { get; set; } = "";
        public string PreferredBrowserArguments { get; set; } = "";
        public string MapProvider { get; set; } = "Google Maps";
        // Empty follows the Windows display language; otherwise a tag from Localizer.Languages.
        public string Language { get; set; } = "";

        public bool EnsureCalendarSources()
        {
            var changed = false;
            if (CalendarSources == null) { CalendarSources = new List<CalendarSourceSettings>(); changed = true; }
            if (CalendarSourcesVersion < 1)
            {
                var source = CalendarSourceSettings.Create(Provider);
                source.IcsSources = IcsSources ?? "";
                source.GoogleRefreshTokenProtected = GoogleRefreshTokenProtected ?? "";
                source.GoogleAccountEmail = GoogleAccountEmail ?? "";
                source.SelectedCalendarIds = SelectedCalendarIds ?? new List<string>();
                source.CalendarSelectionConfigured = source.SelectedCalendarIds.Count > 0;
                if (source.Provider == CalendarProviderKind.Google && !string.IsNullOrWhiteSpace(source.GoogleAccountEmail)) source.Name = source.GoogleAccountEmail;
                CalendarSources.Add(source);
                CalendarSourcesVersion = 1;
                changed = true;
            }
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in CalendarSources)
            {
                if (source == null) continue;
                if (string.IsNullOrWhiteSpace(source.Id) || !ids.Add(source.Id))
                {
                    source.Id = Guid.NewGuid().ToString("N");
                    ids.Add(source.Id);
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(source.Name)) { source.Name = CalendarSourceSettings.DefaultName(source.Provider); changed = true; }
                if (source.SelectedCalendarIds == null) { source.SelectedCalendarIds = new List<string>(); changed = true; }
            }
            return changed;
        }
    }
}
