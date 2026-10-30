using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MeetingBar.SettingsApp;

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
    public List<string> SelectedCalendarIds { get; set; } = [];
    public bool CalendarSelectionConfigured { get; set; }

    public static CalendarSourceSettings Create(CalendarProviderKind provider, string? name = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Provider = provider,
        Name = string.IsNullOrWhiteSpace(name) ? DefaultName(provider) : name
    };

    public static string DefaultName(CalendarProviderKind provider) => provider switch
    {
        CalendarProviderKind.Ics => "ICS calendar",
        CalendarProviderKind.Google => "Google Calendar",
        _ => "Classic Outlook"
    };
}

public sealed class Bookmark
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class AppSettings
{
    public WidgetMode WidgetMode { get; set; } = WidgetMode.NearTray;
    public double WidgetWidth { get; set; } = 500;
    public double WidgetOffsetX { get; set; }
    public double WidgetOffsetY { get; set; }
    public bool HideWidgetInFullscreen { get; set; }
    public List<string> FullscreenAppBlacklist { get; set; } = [];
    public bool UseAutomaticWidgetColor { get; set; } = true;
    public string WidgetBackgroundColor { get; set; } = "#202020";
    public int WidgetSettingsVersion { get; set; } = 1;
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
    public int EventWindowSettingsVersion { get; set; } = 1;
    public bool StartWithWindows { get; set; }
    public bool NotifyBeforeMeeting { get; set; } = true;
    public int NotifyMinutesBefore { get; set; } = 5;
    public List<int> NotificationMinutesBefore { get; set; } = [5];
    public int NotificationScheduleVersion { get; set; }
    public bool NotifyAtEnd { get; set; }
    public bool FullscreenReminder { get; set; }
    public bool AutoJoin { get; set; }
    public int AutoJoinMinutesBefore { get; set; }
    public bool GlobalJoinHotkey { get; set; } = true;
    public int RefreshMinutes { get; set; } = 3;
    public int CalendarSourcesVersion { get; set; }
    public List<CalendarSourceSettings> CalendarSources { get; set; } = [];
    // Retained for automatic migration from settings written by MeetingBar 0.1.
    public CalendarProviderKind Provider { get; set; } = CalendarProviderKind.Outlook;
    public string IcsSources { get; set; } = "";
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string GoogleRefreshTokenProtected { get; set; } = "";
    public string GoogleAccountEmail { get; set; } = "";
    public List<string> SelectedCalendarIds { get; set; } = [];
    public List<string> HiddenTitlePatterns { get; set; } = [];
    public List<string> CustomMeetingRegexes { get; set; } = [];
    public List<string> DismissedEventIds { get; set; } = [];
    public List<Bookmark> Bookmarks { get; set; } = [];
    public string CreateMeetingUrl { get; set; } = "https://meet.google.com/new";
    public string PreferredBrowserPath { get; set; } = "";
    public string PreferredBrowserArguments { get; set; } = "";
    public string MapProvider { get; set; } = "Google Maps";
    // Empty follows the Windows display language; otherwise a tag from Localizer.Languages.
    public string Language { get; set; } = "";

    public bool EnsureCalendarSources()
    {
        var changed = false;
        CalendarSources ??= [];
        if (CalendarSourcesVersion < 1)
        {
            var source = CalendarSourceSettings.Create(Provider);
            source.IcsSources = IcsSources ?? "";
            source.GoogleRefreshTokenProtected = GoogleRefreshTokenProtected ?? "";
            source.GoogleAccountEmail = GoogleAccountEmail ?? "";
            source.SelectedCalendarIds = SelectedCalendarIds ?? [];
            source.CalendarSelectionConfigured = source.SelectedCalendarIds.Count > 0;
            if (source.Provider == CalendarProviderKind.Google && !string.IsNullOrWhiteSpace(source.GoogleAccountEmail)) source.Name = source.GoogleAccountEmail;
            CalendarSources.Add(source);
            CalendarSourcesVersion = 1;
            changed = true;
        }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in CalendarSources)
        {
            if (string.IsNullOrWhiteSpace(source.Id) || !ids.Add(source.Id))
            {
                source.Id = Guid.NewGuid().ToString("N");
                ids.Add(source.Id);
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(source.Name)) { source.Name = CalendarSourceSettings.DefaultName(source.Provider); changed = true; }
            if (source.SelectedCalendarIds == null) { source.SelectedCalendarIds = []; changed = true; }
        }
        return changed;
    }
}

public sealed class CalendarState
{
    public string SourceId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#6D7CFF";
}

public sealed class SettingsState
{
    public string StatusMessage { get; set; } = "";
    public bool IsBusy { get; set; }
    public bool Succeeded { get; set; }
    public string ActiveSourceId { get; set; } = "";
    public string GoogleAccountEmail { get; set; } = "";
    public List<CalendarState> Calendars { get; set; } = [];
}

public sealed class DebugRequest
{
    public string Title { get; set; } = "Test meeting";
    public int StartMinutes { get; set; } = 5;
    public int DurationMinutes { get; set; } = 30;
    public string Service { get; set; } = "Google Meet";
    public string Color { get; set; } = "#0078D4";
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string FolderPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingBar");
    public static string SettingsPath { get; } = Path.Combine(FolderPath, "settings.json");
    public static string StatePath { get; } = Path.Combine(FolderPath, "settings-state.json");
    public static string DebugRequestPath { get; } = Path.Combine(FolderPath, "debug-request.json");

    public static AppSettings Load()
    {
        try
        {
            var settings = !File.Exists(SettingsPath)
                ? new AppSettings()
                : JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8), JsonOptions) ?? new AppSettings();
            settings.EnsureCalendarSources();
            settings.FullscreenAppBlacklist ??= [];
            return settings;
        }
        catch (Exception error)
        {
            SettingsLog.Write("Load settings: " + error.Message);
            return new AppSettings();
        }
    }

    public static SettingsState LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return new SettingsState();
            return JsonSerializer.Deserialize<SettingsState>(File.ReadAllText(StatePath, Encoding.UTF8), JsonOptions) ?? new SettingsState();
        }
        catch { return new SettingsState(); }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(FolderPath);
        var temporary = SettingsPath + ".winui.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, SettingsPath, true);
    }

    public static void SaveDebugRequest(DebugRequest request)
    {
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(DebugRequestPath, JsonSerializer.Serialize(request, JsonOptions), new UTF8Encoding(false));
    }

    public static async Task SendCommandAsync(string command)
    {
        using var pipe = new NamedPipeClientStream(".", "MeetingBar.Windows.Commands", PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(1800);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync(command);
    }
}

public static class SettingsLog
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.FolderPath);
            File.AppendAllText(Path.Combine(SettingsStore.FolderPath, "meetingbar.log"),
                DateTimeOffset.Now.ToString("o") + " ERROR Settings UI: " + message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine);
        }
        catch { }
    }
}
