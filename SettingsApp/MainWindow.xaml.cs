using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace MeetingBar.SettingsApp;

public sealed partial class MainWindow : Window
{
    private static readonly string[] TimelineDatePatterns = { "dd.MM.yyyy", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "dd MMM yyyy" };
    private static readonly string[] TimelineDateChoices = { "31.12.2026  (dd.MM.yyyy)", "12/31/2026  (MM/dd/yyyy)", "31/12/2026  (dd/MM/yyyy)", "2026-12-31  (yyyy-MM-dd)", "31 Dec 2026  (dd MMM yyyy)" };
    private const int MinimumWindowWidth = 420, MinimumWindowHeight = 480;
    private const double NarrowPageWidth = 560, HeaderIconMinimumWidth = 440;
    private readonly DispatcherTimer _stateTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, CheckBox> _calendarChecks = new();
    private readonly Dictionary<string, CalendarSourceEditor> _calendarSourceEditors = new();
    private readonly Dictionary<string, InfoBar> _googleStatuses = new();
    private readonly List<(TextBox Name, TextBox Url)> _bookmarkEditors = new();
    private AppSettings _settings = SettingsStore.Load();
    private string _page = "";
    private string _stateSignature = "";

    private ComboBox? _widgetMode, _titleMode, _timeMode, _endOfDayBehavior, _highlightStyle, _timelineDateFormat, _mapProvider;
    private Slider? _widgetWidth;
    private NumberBox? _offsetX, _offsetY, _titleLength, _highlightBefore, _highlightAfter, _refreshMinutes, _autoJoinBefore, _pastMeetingHours, _futureMeetingHours, _timelinePastDays, _timelineFutureDays;
    private TextBox? _widgetColor, _endOfDayCustomMessage, _googleClientId, _browserPath, _browserArgs, _createUrl, _debugTitle, _debugColor;
    private PasswordBox? _googleClientSecret;
    private ToggleSwitch? _autoWidgetColor, _ongoingTime, _showIcon, _winWWidget, _highlightMeetings, _highlightWholeMeeting, _allDay, _declined, _tentative, _free, _ignoreFreeTaskbar, _requireLink, _includePastDay, _includeNextDay, _fullDayEventList, _use24HourClock, _showTimelineDate;
    private ToggleSwitch? _notify, _fullScreen, _notifyEnd, _autoJoin, _hotkey, _startWithWindows;
    private ToggleSwitch? _hideWidgetInFullscreen;
    private ComboBox? _runningFullscreenApp;
    private Button? _addRunningFullscreenApp;
    private StackPanel? _fullscreenAppList;
    private ComboBox? _debugService;
    private readonly Dictionary<int, CheckBox> _notificationChecks = new();
    private NumberBox? _debugStartMinutes, _debugDurationMinutes;
    private InfoBar? _debugStatus;

    private sealed class CalendarSourceEditor
    {
        public required TextBox Name { get; init; }
        public required ToggleSwitch Enabled { get; init; }
        public TextBox? IcsSources { get; init; }
    }

    private sealed class RunningAppOption
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public string Display => Name + "  ·  " + System.IO.Path.GetFileName(Path);
    }

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        ShowPage("Widget");
        Navigation.Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            Navigation.SelectedItem = WidgetItem;
            _page = "";
            ShowPage("Widget");
        });
        _stateTimer.Tick += StateTimer_Tick;
        _stateTimer.Start();
        Closed += (_, _) => _stateTimer.Stop();
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try { SystemBackdrop = new MicaBackdrop(); } catch { }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Title = "MeetingBar Settings";
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
        // AppWindow sizes are physical pixels, so scale the layout sizes for the display's DPI.
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWindowWidth * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(MinimumWindowHeight * scale);
        }
        var display = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary);
        if (display != null)
        {
            var area = display.WorkArea;
            var width = Math.Min((int)Math.Round(1080 * scale), area.Width - 32);
            var height = Math.Min((int)Math.Round(900 * scale), area.Height - 32);
            appWindow.Resize(new SizeInt32(width, height));
            appWindow.Move(new PointInt32(area.X + Math.Max(0, (area.Width - width) / 2), area.Y + Math.Max(0, (area.Height - height) / 2)));
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void Navigation_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args) => UpdateAdaptiveLayout(sender.IsPaneOpen);
    private void Navigation_PaneOpening(NavigationView sender, object args) => UpdateAdaptiveLayout(true);
    private void Navigation_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => UpdateAdaptiveLayout(false);
    private void PageScroller_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateAdaptiveLayout(Navigation.IsPaneOpen);

    private void UpdateAdaptiveLayout(bool paneOpen)
    {
        // The brand header only fits the full-width pane; the compact rail shows just the icons.
        PaneBrand.Visibility = paneOpen ? Visibility.Visible : Visibility.Collapsed;
        var mode = Navigation.DisplayMode;
        var narrow = PageScroller.ActualWidth > 0 && PageScroller.ActualWidth < NarrowPageWidth;
        // In minimal mode the pane toggle button floats over the page, so leave room for it.
        var top = mode == NavigationViewDisplayMode.Minimal ? 52 : 14;
        PageHost.Padding = narrow ? new Thickness(16, top, 16, 28) : new Thickness(36, top, 40, 38);
        var inset = mode switch
        {
            NavigationViewDisplayMode.Minimal => 16,
            NavigationViewDisplayMode.Expanded when paneOpen => Navigation.OpenPaneLength,
            _ => Navigation.CompactPaneLength
        };
        FooterContent.Margin = new Thickness(inset, 0, narrow ? 16 : 38, 0);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string page) ShowPage(page);
    }

    private void ShowPage(string page)
    {
        if (_page == page && PagePanel.Children.Count > 0) return;
        CaptureCurrentPage();
        _page = page;
        PagePanel.Children.Clear();
        PageScroller.ChangeView(null, 0, null, true);

        switch (page)
        {
            case "Calendars": BuildCalendarsPage(); break;
            case "Events": BuildEventsPage(); break;
            case "Meetings": BuildMeetingsPage(); break;
            case "Reminders": BuildRemindersPage(); break;
            case "General": BuildGeneralPage(); break;
            case "Debug": BuildDebugPage(); break;
            default: BuildWidgetPage(); break;
        }
    }

    private void AddHeader(string title, string description, string glyph)
    {
        var titleRow = new Grid { Margin = new Thickness(0, 2, 0, 8), ColumnSpacing = 18 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new FontIcon { Glyph = glyph, FontSize = 48, Foreground = ThemeBrush("AccentFillColorDefaultBrush"), Width = 72, Height = 72 };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        text.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["PageTitleStyle"] });
        text.Children.Add(new TextBlock { Text = description, Foreground = ThemeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(text, 1);
        titleRow.Children.Add(icon);
        titleRow.Children.Add(text);
        titleRow.SizeChanged += (_, e) =>
        {
            var showIcon = e.NewSize.Width >= HeaderIconMinimumWidth;
            icon.Visibility = showIcon ? Visibility.Visible : Visibility.Collapsed;
            titleRow.ColumnSpacing = showIcon ? 18 : 0;
        };
        PagePanel.Children.Add(titleRow);
    }

    private void AddSection(string title) => PagePanel.Children.Add(new TextBlock
    {
        Text = title,
        Style = (Style)Application.Current.Resources["SectionHeaderStyle"]
    });

    private void AddCard(string title, string description, FrameworkElement? control = null)
    {
        var card = new Border { Style = (Style)Application.Current.Resources["SettingsCardStyle"] };
        var row = new SettingsCardRow();
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 490 };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description))
            copy.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(copy);
        if (control != null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(control);
        }
        card.Child = row;
        PagePanel.Children.Add(card);
    }

    private void AddWideCard(string title, string description, FrameworkElement control)
    {
        var card = new Border { Style = (Style)Application.Current.Resources["SettingsCardStyle"] };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -8, 0, 0) });
        content.Children.Add(control);
        card.Child = content;
        PagePanel.Children.Add(card);
    }

    private static Brush ThemeBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

    private static ComboBox Choice(IEnumerable<string> values, int selected)
    {
        var combo = new ComboBox { Width = 260, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var value in values) combo.Items.Add(value);
        return combo;
    }

    private static NumberBox Number(double value, double minimum, double maximum, double step = 1) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        SmallChange = step,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Width = 150
    };

    private static ToggleSwitch Toggle(bool value) => new() { IsOn = value, OnContent = "On", OffContent = "Off", HorizontalAlignment = HorizontalAlignment.Right };

    private static TextBox TextEntry(string value, double width = 310) => new() { Text = value ?? "", MinWidth = width };

    private void BuildWidgetPage()
    {
        AddHeader("Widget", "Choose where MeetingBar appears and what it shows.", "\uE7F4");
        AddSection("Taskbar surface");
        _widgetMode = Choice(new[] { "Tray icon only", "Right side", "Left side" }, (int)_settings.WidgetMode);
        AddCard("Mode", "Taskbar modes use a borderless overlay because Windows has no third-party text-widget slot.", _widgetMode);

        _widgetWidth = new Slider { Minimum = 140, Maximum = 900, Value = _settings.WidgetWidth, Width = 280, StepFrequency = 10 };
        AddCard("Maximum widget width", "Short titles shrink automatically; longer titles can use this limit.", _widgetWidth);

        _offsetX = Number(_settings.WidgetOffsetX, -1000, 1000);
        AddCard("Horizontal offset", "Fine-tune placement for taskbar tools or display scaling.", _offsetX);
        _offsetY = Number(_settings.WidgetOffsetY, -500, 500);
        AddCard("Vertical offset", "Move the surface relative to the taskbar.", _offsetY);

        _autoWidgetColor = Toggle(_settings.UseAutomaticWidgetColor);
        AddCard("Match Windows taskbar color", "Uses the current Windows light, dark, and taskbar appearance.", _autoWidgetColor);
        _widgetColor = TextEntry(_settings.WidgetBackgroundColor, 170);
        AddCard("Custom widget color", "Used when automatic matching is off. Enter #RRGGBB.", _widgetColor);

        AddSection("Fullscreen behavior");
        _hideWidgetInFullscreen = Toggle(_settings.HideWidgetInFullscreen);
        AddCard("Hide during fullscreen apps", "Temporarily hide only the taskbar widget while a game, video, or other app fills its monitor. The tray icon stays available.", _hideWidgetInFullscreen);
        var fullscreenApps = BuildFullscreenAppEditor();
        fullscreenApps.IsEnabled = _hideWidgetInFullscreen.IsOn;
        _hideWidgetInFullscreen.Toggled += (_, _) => fullscreenApps.IsEnabled = _hideWidgetInFullscreen.IsOn;
        AddWideCard("Also hide for selected apps", "Optional fallback for borderless or windowed games: hide whenever one of these apps is in the foreground. Add an app that is open now or browse for its executable.", fullscreenApps);

        AddSection("Display");
        _titleMode = Choice(Enum.GetNames<TitleMode>(), (int)_settings.TitleMode);
        AddCard("Meeting title", "Choose full title, a generic label, a dot, or hidden.", _titleMode);
        _timeMode = Choice(new[] { "Countdown", "Start time", "Hidden" }, (int)_settings.TimeMode);
        AddCard("Meeting time", "Controls the time text in the taskbar surface.", _timeMode);
        _ongoingTime = Toggle(_settings.ShowOngoingTimeRemaining);
        AddCard("Time remaining", "Show how much time is left during an ongoing meeting.", _ongoingTime);
        _titleLength = Number(_settings.TitleMaxLength, 8, 200);
        AddCard("Maximum title length", "Longer event names are shortened at this character count.", _titleLength);
        _showIcon = Toggle(_settings.ShowServiceIcon);
        AddCard("Colored event icon", "Show the event-colored square beside the title.", _showIcon);
        _endOfDayBehavior = Choice(new[] { "Show next event", "Hide taskbar widget", "Show no events for today", "Show custom message" }, (int)_settings.EndOfDayWidgetBehavior);
        AddCard("After today's events", "Choose what the taskbar widget does when no current or upcoming event remains today.", _endOfDayBehavior);
        _endOfDayCustomMessage = TextEntry(_settings.EndOfDayCustomMessage, 240);
        _endOfDayCustomMessage.MaxLength = 200;
        AddCard("Custom end-of-day message", "Used when Show custom message is selected.", _endOfDayCustomMessage);

        AddSection("Starting meeting highlight");
        _highlightMeetings = Toggle(_settings.HighlightStartingMeetings);
        AddCard("Highlight starting meetings", "Keep the taskbar widget visibly active around the meeting start.", _highlightMeetings);
        _highlightBefore = Number(_settings.HighlightMinutesBefore, 0, 60);
        AddCard("Start highlighting before", "Minutes before the scheduled start.", _highlightBefore);
        _highlightAfter = Number(_settings.HighlightMinutesAfterStart, 1, 120);
        AddCard("Initial meeting highlight", "Minutes after the scheduled start when whole-meeting highlighting is off.", _highlightAfter);
        _highlightWholeMeeting = Toggle(_settings.HighlightWholeMeeting);
        AddCard("Highlight whole meeting", "Keep the active state until the scheduled meeting ends.", _highlightWholeMeeting);
        _highlightStyle = Choice(new[] { "Same as hover", "Animated shine" }, (int)_settings.WidgetHighlightStyle);
        AddCard("Highlight style", "Use the native hover surface or a subtle in-progress sweep.", _highlightStyle);

        AddSection("Windows Widgets Board (beta)");
        _winWWidget = Toggle(_settings.EnableWinWWidget);
        AddCard("Win+W widget", "Register MeetingBar in the native Windows 11 widget picker. After saving, press Win+W, choose Add widgets, then pin MeetingBar. Developer Mode is required by this portable package.", _winWWidget);
    }

    private Control BuildFullscreenAppEditor()
    {
        _settings.FullscreenAppBlacklist ??= [];
        var content = new StackPanel { Spacing = 10 };
        var pickerRow = new Grid { ColumnSpacing = 8 };
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition());
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pickerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _runningFullscreenApp = new ComboBox { PlaceholderText = "Choose an open app", DisplayMemberPath = nameof(RunningAppOption.Display), HorizontalAlignment = HorizontalAlignment.Stretch };
        _runningFullscreenApp.SelectionChanged += (_, _) => { if (_addRunningFullscreenApp != null) _addRunningFullscreenApp.IsEnabled = _runningFullscreenApp.SelectedItem != null; };
        pickerRow.Children.Add(_runningFullscreenApp);

        _addRunningFullscreenApp = new Button { Content = "Add", IsEnabled = false, MinWidth = 72 };
        _addRunningFullscreenApp.Click += (_, _) =>
        {
            if (_runningFullscreenApp?.SelectedItem is RunningAppOption app) AddFullscreenApp(app.Path);
        };
        Grid.SetColumn(_addRunningFullscreenApp, 1);
        pickerRow.Children.Add(_addRunningFullscreenApp);

        var refresh = new Button { Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 }, Width = 40 };
        AutomationProperties.SetName(refresh, "Refresh open applications");
        ToolTipService.SetToolTip(refresh, "Refresh open applications");
        refresh.Click += (_, _) => RefreshRunningApps();
        Grid.SetColumn(refresh, 2);
        pickerRow.Children.Add(refresh);
        content.Children.Add(pickerRow);

        var browse = new Button { Content = "Browse for an executable", HorizontalAlignment = HorizontalAlignment.Left };
        browse.Click += BrowseFullscreenApp_Click;
        content.Children.Add(browse);

        _fullscreenAppList = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        content.Children.Add(_fullscreenAppList);
        RefreshRunningApps();
        RebuildFullscreenAppList();
        return new ContentControl { Content = content };
    }

    private void RefreshRunningApps()
    {
        if (_runningFullscreenApp == null) return;
        var apps = new Dictionary<string, RunningAppOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero || process.ProcessName.StartsWith("MeetingBar", StringComparison.OrdinalIgnoreCase)) continue;
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var details = FileVersionInfo.GetVersionInfo(path);
                    var name = details.FileDescription;
                    if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(path);
                    apps[path] = new RunningAppOption { Name = name, Path = path };
                }
                catch { }
            }
        }
        _runningFullscreenApp.Items.Clear();
        foreach (var app in apps.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) _runningFullscreenApp.Items.Add(app);
        _runningFullscreenApp.SelectedIndex = -1;
        if (_addRunningFullscreenApp != null) _addRunningFullscreenApp.IsEnabled = false;
    }

    private async void BrowseFullscreenApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, ViewMode = PickerViewMode.List };
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file != null) AddFullscreenApp(file.Path);
        }
        catch (Exception error)
        {
            SaveStatus.Severity = InfoBarSeverity.Error;
            SaveStatus.Message = "Could not choose an application: " + error.Message;
            SaveStatus.IsOpen = true;
        }
    }

    private void AddFullscreenApp(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { path = System.IO.Path.GetFullPath(path); } catch { }
        _settings.FullscreenAppBlacklist ??= [];
        if (!_settings.FullscreenAppBlacklist.Contains(path, StringComparer.OrdinalIgnoreCase)) _settings.FullscreenAppBlacklist.Add(path);
        RebuildFullscreenAppList();
        if (_runningFullscreenApp != null) _runningFullscreenApp.SelectedIndex = -1;
    }

    private void RebuildFullscreenAppList()
    {
        if (_fullscreenAppList == null) return;
        _fullscreenAppList.Children.Clear();
        var paths = (_settings.FullscreenAppBlacklist ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(System.IO.Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (paths.Count == 0)
        {
            _fullscreenAppList.Children.Add(new TextBlock { Text = "No apps added. Automatic fullscreen detection still works.", FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
            return;
        }

        foreach (var path in paths)
        {
            var row = new Border { Background = ThemeBrush("SubtleFillColorSecondaryBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 7, 7) };
            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new FontIcon { Glyph = "\uE7C5", FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(path), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = path, FontSize = 11, Foreground = ThemeBrush("TextFillColorSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Width = 36, Height = 32, Padding = new Thickness(0) };
            AutomationProperties.SetName(remove, "Remove " + System.IO.Path.GetFileNameWithoutExtension(path));
            ToolTipService.SetToolTip(remove, "Remove");
            remove.Click += (_, _) =>
            {
                (_settings.FullscreenAppBlacklist ??= []).RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
                RebuildFullscreenAppList();
            };
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);
            row.Child = grid;
            _fullscreenAppList.Children.Add(row);
        }
    }

    private void BuildEventsPage()
    {
        AddHeader("Events", "Control which calendar events appear in MeetingBar.", "\uE8FD");
        AddSection("Event filters");
        _allDay = Toggle(_settings.ShowAllDayEvents); AddCard("All-day events", "Show events without a specific time.", _allDay);
        _declined = Toggle(_settings.ShowDeclinedEvents); AddCard("Declined events", "Keep meetings you declined in the list.", _declined);
        _tentative = Toggle(_settings.ShowTentativeEvents); AddCard("Tentative and pending events", "Include invitations that are not accepted yet.", _tentative);
        _free = Toggle(_settings.ShowFreeEvents); AddCard("Free and non-busy events", "Include events that do not block your calendar.", _free);
        _ignoreFreeTaskbar = Toggle(_settings.IgnoreFreeEventsInTaskbar); AddCard("Ignore free events in taskbar and reminders", "Keep free events in the expanded popup, but skip them for the taskbar, tray preview, and reminders.", _ignoreFreeTaskbar);
        _requireLink = Toggle(_settings.RequireMeetingLink); AddCard("Require a meeting link", "Hide events that have no joinable video or web link.", _requireLink);
        AddSection("Event list range");
        _fullDayEventList = Toggle(_settings.ShowFullDayEventList); AddCard("Show complete days", "Show every event on each allowed day. When enabled, the hour values below affect only the timeline.", _fullDayEventList);
        _includePastDay = Toggle(_settings.IncludePastDay); AddCard("Allow previous days", "Enable past-day pages and backward timeline scrubbing.", _includePastDay);
        _includeNextDay = Toggle(_settings.IncludeNextDay); AddCard("Allow following days", "Enable future-day pages and forward timeline scrubbing.", _includeNextDay);
        AddSection("Timeline range");
        _timelinePastDays = Number(_settings.TimelinePastDays, 0, 30); AddCard("Days backward", "Maximum number of earlier days available while scrubbing.", _timelinePastDays);
        _timelineFutureDays = Number(_settings.TimelineFutureDays, 0, 30); AddCard("Days forward", "Maximum number of later days available while scrubbing.", _timelineFutureDays);
        _pastMeetingHours = Number(_settings.PastMeetingHours, 0, 168); AddCard("Past meetings", "Hours before now shown on today's timeline.", _pastMeetingHours);
        _futureMeetingHours = Number(_settings.FutureMeetingHours, 1, 168); AddCard("Next meetings", "Hours after now shown on today's timeline.", _futureMeetingHours);
        AddSection("Time format");
        _use24HourClock = Toggle(_settings.Use24HourClock); AddCard("24-hour clock", "Use 13:30 instead of 1:30 PM throughout MeetingBar.", _use24HourClock);
        _showTimelineDate = Toggle(_settings.ShowTimelineDate); AddCard("Show timeline date", "Display the scrubbed date at the top-right of the timeline.", _showTimelineDate);
        _timelineDateFormat = Choice(TimelineDateChoices, Math.Max(0, Array.IndexOf(TimelineDatePatterns, _settings.TimelineDateFormat))); AddCard("Timeline date format", "Choose the full-date order and separators.", _timelineDateFormat);
        _refreshMinutes = Number(_settings.RefreshMinutes, 1, 120); AddCard("Refresh interval", "Minutes between automatic calendar updates.", _refreshMinutes);
    }

    private void BuildRemindersPage()
    {
        AddHeader("Reminders", "Choose when MeetingBar alerts or joins for you.", "\uEA8F");
        AddSection("Notifications");
        _notify = Toggle(_settings.NotifyBeforeMeeting); AddCard("Meeting notifications", "Show Windows notifications at every selected time.", _notify);
        _notificationChecks.Clear();
        IEnumerable<int> configured = _settings.NotificationScheduleVersion < 1 ? new[] { _settings.NotifyMinutesBefore } : _settings.NotificationMinutesBefore ?? new List<int>();
        var notificationTimes = new[] { 60, 30, 15, 10, 5, 0 }.Concat(configured).Where(x => x >= 0 && x <= 120).Distinct().OrderByDescending(x => x);
        var notificationChoices = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var minutes in notificationTimes)
        {
            var choice = new CheckBox { Content = minutes == 0 ? "At meeting start" : minutes + " minutes before", IsChecked = configured?.Contains(minutes) == true, HorizontalAlignment = HorizontalAlignment.Right };
            _notificationChecks[minutes] = choice;
            notificationChoices.Children.Add(choice);
        }
        AddCard("Notification times", "Select multiple reminders, for example 5 minutes before and at meeting start.", notificationChoices);
        _fullScreen = Toggle(_settings.FullscreenReminder); AddCard("Full-screen reminder", "Show a prominent meeting reminder with a join action.", _fullScreen);
        _notifyEnd = Toggle(_settings.NotifyAtEnd); AddCard("Meeting-end notification", "Notify when the scheduled meeting time ends.", _notifyEnd);
        AddSection("Automation");
        _autoJoin = Toggle(_settings.AutoJoin); AddCard("Automatically join meetings", "Open a detected meeting link at the configured time.", _autoJoin);
        _autoJoinBefore = Number(_settings.AutoJoinMinutesBefore, 0, 30); AddCard("Join lead time", "Minutes before the meeting starts.", _autoJoinBefore);
        _hotkey = Toggle(_settings.GlobalJoinHotkey); AddCard("Global join shortcut", "Ctrl+Alt+J joins the nearest meeting.", _hotkey);
    }

    private void BuildGeneralPage()
    {
        AddHeader("General", "App startup, privacy, and Windows integration.", "\uE713");
        AddSection("App behavior");
        _startWithWindows = Toggle(_settings.StartWithWindows);
        AddCard("Start MeetingBar with Windows", "Launch the tray and taskbar surfaces when you sign in.", _startWithWindows);
        AddSection("Privacy");
        AddCard("Calendar data stays on this PC", "Google tokens are encrypted with Windows Data Protection for the signed-in user.");
        AddCard("Diagnostics", "Provider results and crash details are stored locally without event titles or meeting URLs.");
    }

    private void BuildDebugPage()
    {
        AddHeader("Debug", "Inject local-only meetings and trigger UI states without changing a real calendar.", "\uE9D9");
        AddSection("Test event");
        _debugTitle = TextEntry("Test meeting");
        AddCard("Title", "Displayed in the taskbar widget, popup, and reminders.", _debugTitle);
        _debugService = Choice(new[] { "Google Meet", "Microsoft Teams", "Zoom", "No meeting link" }, 0);
        AddCard("Meeting service", "Selects the join icon and test link behavior.", _debugService);
        _debugStartMinutes = Number(5, -1440, 1440);
        AddCard("Starts in", "Minutes from now. Use a negative value for an ongoing or past event.", _debugStartMinutes);
        _debugDurationMinutes = Number(30, 1, 1440);
        AddCard("Duration", "Meeting duration in minutes.", _debugDurationMinutes);
        _debugColor = TextEntry("#0078D4", 180);
        AddCard("Calendar color", "Hex color used by the event, tray icon, widget, and timeline.", _debugColor);

        AddSection("Actions");
        var actions = new WrapPanel();
        var inject = new Button { Content = "Inject event", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        inject.Click += DebugInject_Click;
        var notification = new Button { Content = "Test notification" };
        notification.Click += DebugNotification_Click;
        var reminder = new Button { Content = "Test reminder" };
        reminder.Click += DebugReminder_Click;
        var clear = new Button { Content = "Clear events" };
        clear.Click += DebugClear_Click;
        actions.Children.Add(inject); actions.Children.Add(notification); actions.Children.Add(reminder); actions.Children.Add(clear);
        AddCard("Run debug action", "Injected events live only until MeetingBar exits or Clear events is pressed.", actions);
        _debugStatus = new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Message = "Ready. Debug events never write to your calendar.", Margin = new Thickness(0, 10, 0, 0) };
        PagePanel.Children.Add(_debugStatus);
    }

    private DebugRequest CurrentDebugRequest() => new()
    {
        Title = _debugTitle?.Text.Trim() ?? "Test meeting",
        StartMinutes = (int)Finite(_debugStartMinutes?.Value ?? 5, 5),
        DurationMinutes = (int)Finite(_debugDurationMinutes?.Value ?? 30, 30),
        Service = _debugService?.SelectedItem?.ToString() ?? "Google Meet",
        Color = _debugColor?.Text.Trim() ?? "#0078D4"
    };

    private async void DebugInject_Click(object sender, RoutedEventArgs e) => await RunDebugCommand("debug-inject", "Test event injected into the widget and popup.", true);
    private async void DebugNotification_Click(object sender, RoutedEventArgs e) => await RunDebugCommand("debug-notification", "Test notification triggered.", false);
    private async void DebugReminder_Click(object sender, RoutedEventArgs e) => await RunDebugCommand("debug-reminder", "Test full-screen reminder triggered.", false);
    private async void DebugClear_Click(object sender, RoutedEventArgs e) => await RunDebugCommand("debug-clear", "All injected events cleared.", false);

    private async Task RunDebugCommand(string command, string success, bool saveRequest)
    {
        try
        {
            if (saveRequest) SettingsStore.SaveDebugRequest(CurrentDebugRequest());
            await SettingsStore.SendCommandAsync(command);
            if (_debugStatus != null) { _debugStatus.Severity = InfoBarSeverity.Success; _debugStatus.Message = success; }
        }
        catch (Exception error)
        {
            if (_debugStatus != null) { _debugStatus.Severity = InfoBarSeverity.Error; _debugStatus.Message = error.Message; }
        }
    }

    private void BuildMeetingsPage()
    {
        AddHeader("Meetings", "Choose how links open and keep recurring rooms nearby.", "\uE714");
        AddSection("Opening");
        _browserPath = TextEntry(_settings.PreferredBrowserPath); AddCard("Preferred browser executable", "Leave blank to use the Windows default browser.", _browserPath);
        _browserArgs = TextEntry(_settings.PreferredBrowserArguments); AddCard("Browser arguments", "Use {url} where MeetingBar should insert the meeting link.", _browserArgs);
        _createUrl = TextEntry(_settings.CreateMeetingUrl); AddCard("Create meeting URL", "Opened by the Create meeting button.", _createUrl);
        _mapProvider = Choice(new[] { "Google Maps", "Bing Maps", "OpenStreetMap" }, Math.Max(0, Array.IndexOf(new[] { "Google Maps", "Bing Maps", "OpenStreetMap" }, _settings.MapProvider)));
        AddCard("Map provider", "Used by the event-details location button.", _mapProvider);

        var heading = new Grid { Margin = new Thickness(0, 24, 0, 9) };
        heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = "Bookmarks", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var add = new Button { Content = "Add bookmark" }; add.Click += AddBookmark_Click; Grid.SetColumn(add, 1); heading.Children.Add(add); PagePanel.Children.Add(heading);
        _bookmarkEditors.Clear();
        foreach (var bookmark in _settings.Bookmarks) AddBookmarkCard(bookmark);
        if (_settings.Bookmarks.Count == 0) AddCard("No bookmarks", "Add a recurring meeting room for one-click access.");
    }

    private void AddBookmarkCard(Bookmark bookmark)
    {
        var name = TextEntry(bookmark.Name, 0);
        var url = TextEntry(bookmark.Url, 0);
        var remove = new Button { Content = "Remove", Tag = bookmark };
        remove.Click += RemoveBookmark_Click;
        var controls = new Grid { ColumnSpacing = 8 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(url, 1); Grid.SetColumn(remove, 2);
        controls.Children.Add(name); controls.Children.Add(url); controls.Children.Add(remove);
        AddWideCard("Meeting bookmark", "Name and join URL", controls);
        _bookmarkEditors.Add((name, url));
    }

    private void AddBookmark_Click(object sender, RoutedEventArgs e)
    {
        CaptureCurrentPage();
        _settings.Bookmarks.Add(new Bookmark { Name = "New meeting", Url = "https://" });
        _page = "";
        ShowPage("Meetings");
    }

    private void RemoveBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Bookmark bookmark) return;
        CaptureCurrentPage();
        _settings.Bookmarks.Remove(bookmark);
        _page = "";
        ShowPage("Meetings");
    }

    private void BuildCalendarsPage()
    {
        AddHeader("Calendars", "Combine multiple providers and accounts, then choose which calendars are visible.", "\uE787");
        AddSection("Calendar sources");
        var addButtons = new WrapPanel();
        var addOutlook = new Button { Content = "Add Outlook", IsEnabled = !_settings.CalendarSources.Any(x => x.Provider == CalendarProviderKind.Outlook) };
        addOutlook.Click += AddOutlookSource_Click;
        var addIcs = new Button { Content = "Add ICS" }; addIcs.Click += AddIcsSource_Click;
        var addGoogle = new Button { Content = "Add Google account", Style = (Style)Application.Current.Resources["AccentButtonStyle"] }; addGoogle.Click += AddGoogleSource_Click;
        addButtons.Children.Add(addOutlook); addButtons.Children.Add(addIcs); addButtons.Children.Add(addGoogle);
        AddCard("Add a calendar source", "Google accounts share the OAuth client below but keep separate encrypted sign-in tokens. Classic Outlook can be added once because its local API already returns every Outlook store.", addButtons);

        _calendarSourceEditors.Clear();
        _googleStatuses.Clear();
        var state = SettingsStore.LoadState();
        foreach (var source in _settings.CalendarSources) BuildCalendarSourceCard(source, state);
        if (_settings.CalendarSources.Count == 0) AddCard("No calendar sources", "Add Outlook, an ICS feed, or a Google account to begin.");

        AddSection("Shared Google OAuth setup");
        BuildGoogleSetup();

        AddSection("Visible calendars");
        _calendarChecks.Clear();
        if (state.Calendars.Count == 0)
        {
            AddCard("No calendars loaded yet", "Save, connect any Google accounts, then refresh MeetingBar.");
        }
        else
        {
            foreach (var group in state.Calendars.GroupBy(x => x.SourceId))
            {
                var source = _settings.CalendarSources.FirstOrDefault(x => x.Id == group.Key);
                if (source == null) continue;
                PagePanel.Children.Add(new TextBlock { Text = source.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(8, 6, 0, 0) });
                foreach (var calendar in group)
                {
                    var selected = !source.CalendarSelectionConfigured || source.SelectedCalendarIds.Contains(calendar.Id);
                    var check = new CheckBox { IsChecked = selected, Content = calendar.Name, MinWidth = 260 };
                    _calendarChecks[CalendarKey(source.Id, calendar.Id)] = check;
                    AddCard(calendar.Name, calendar.SourceKind + " · Calendar color " + calendar.Color, check);
                }
            }
        }
    }

    private void BuildCalendarSourceCard(CalendarSourceSettings source, SettingsState state)
    {
        var body = new StackPanel { Spacing = 10 };
        var title = source.Provider switch { CalendarProviderKind.Ics => "ICS files or feeds", CalendarProviderKind.Google => "Google Calendar", _ => "Classic Outlook" };
        body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var name = new TextBox { Text = source.Name, Header = "Display name" };
        var enabled = new ToggleSwitch { IsOn = source.Enabled, Header = "Include this source", OnContent = "On", OffContent = "Off" };
        body.Children.Add(name); body.Children.Add(enabled);
        TextBox? icsSources = null;
        if (source.Provider == CalendarProviderKind.Ics)
        {
            icsSources = new TextBox { Text = source.IcsSources, Header = "ICS files, HTTPS URLs, and webcal:// links (one per line)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90 };
            body.Children.Add(icsSources);
        }
        if (source.Provider == CalendarProviderKind.Google)
        {
            body.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(source.GoogleAccountEmail) ? "Not connected" : "Account: " + source.GoogleAccountEmail, Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
            var buttons = new WrapPanel();
            var connect = new Button { Content = string.IsNullOrWhiteSpace(source.GoogleAccountEmail) ? "Connect account" : "Reconnect / change account", Tag = source.Id, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            connect.Click += ConnectGoogle_Click;
            var disconnect = new Button { Content = "Disconnect", Tag = source.Id, IsEnabled = !string.IsNullOrWhiteSpace(source.GoogleRefreshTokenProtected) };
            disconnect.Click += DisconnectGoogle_Click;
            buttons.Children.Add(connect); buttons.Children.Add(disconnect); body.Children.Add(buttons);
            var active = string.Equals(state.ActiveSourceId, source.Id, StringComparison.OrdinalIgnoreCase);
            var status = new InfoBar
            {
                IsOpen = active || !string.IsNullOrWhiteSpace(source.GoogleAccountEmail),
                IsClosable = false,
                Severity = active && state.IsBusy ? InfoBarSeverity.Informational : string.IsNullOrWhiteSpace(source.GoogleAccountEmail) ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                Message = active && !string.IsNullOrWhiteSpace(state.StatusMessage) ? state.StatusMessage : string.IsNullOrWhiteSpace(source.GoogleAccountEmail) ? "Not connected" : "Connected as " + source.GoogleAccountEmail
            };
            _googleStatuses[source.Id] = status;
            body.Children.Add(status);
        }
        var remove = new Button { Content = "Remove source", Tag = source.Id, HorizontalAlignment = HorizontalAlignment.Right };
        remove.Click += RemoveCalendarSource_Click; body.Children.Add(remove);
        _calendarSourceEditors[source.Id] = new CalendarSourceEditor { Name = name, Enabled = enabled, IcsSources = icsSources };
        var card = new Border { Style = (Style)Application.Current.Resources["SettingsCardStyle"] };
        card.Child = body; PagePanel.Children.Add(card);
    }

    private void BuildGoogleSetup()
    {
        var expander = new Expander
        {
            IsExpanded = string.IsNullOrWhiteSpace(_settings.GoogleClientId),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Google OAuth client", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "One client configuration for every Google account · read-only access", FontSize = 12, Foreground = ThemeBrush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(0, 10, 0, 6) };
        body.Children.Add(new TextBlock { Text = "MeetingBar never sees your Google password. Google asks for read-only calendar access in your browser.", TextWrapping = TextWrapping.Wrap, Foreground = ThemeBrush("TextFillColorSecondaryBrush") });
        body.Children.Add(GoogleStep("1", "Create or select a Google Cloud project", "Open Google Cloud projects", "https://console.cloud.google.com/projectselector2/home/dashboard"));
        body.Children.Add(GoogleStep("2", "Enable the Google Calendar API in that same project", "Open Calendar API page", "https://console.cloud.google.com/apis/library/calendar-json.googleapis.com"));
        body.Children.Add(GoogleStep("3", "Configure OAuth consent, add yourself as a test user if needed, and grant calendar.readonly under Data Access", "Open OAuth consent", "https://console.cloud.google.com/auth/overview"));
        body.Children.Add(new TextBox { Text = "https://www.googleapis.com/auth/calendar.readonly", IsReadOnly = true, Header = "Required Google scope" });
        body.Children.Add(GoogleStep("4", "Create an OAuth client of type Desktop app. No redirect URL is needed.", "Open OAuth clients", "https://console.cloud.google.com/auth/clients"));
        _googleClientId = new TextBox { Text = _settings.GoogleClientId, Header = "Desktop app client ID", PlaceholderText = "…apps.googleusercontent.com" };
        body.Children.Add(_googleClientId);
        _googleClientSecret = new PasswordBox { Password = _settings.GoogleClientSecret, Header = "Client secret (hidden)", PasswordRevealMode = PasswordRevealMode.Hidden };
        body.Children.Add(_googleClientSecret);
        expander.Content = body;
        var card = new Border { Style = (Style)Application.Current.Resources["SettingsCardStyle"], Padding = new Thickness(8, 2, 8, 2) };
        card.Child = expander;
        PagePanel.Children.Add(card);
    }

    private Grid GoogleStep(string number, string text, string linkText, string url)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Button { Content = number, Width = 28, Height = 28, Padding = new Thickness(0), CornerRadius = new CornerRadius(14), Style = (Style)Application.Current.Resources["AccentButtonStyle"], IsHitTestVisible = false };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        var link = new HyperlinkButton { Content = linkText + " ↗", Tag = url, Padding = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        link.Click += OpenLink_Click;
        content.Children.Add(link);
        Grid.SetColumn(content, 1); grid.Children.Add(badge); grid.Children.Add(content);
        return grid;
    }

    private static void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton { Tag: string url }) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void ConnectGoogle_Click(object sender, RoutedEventArgs e)
    {
        CaptureCurrentPage();
        if (sender is not Button { Tag: string sourceId }) return;
        if (string.IsNullOrWhiteSpace(_settings.GoogleClientId) || !_settings.GoogleClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            if (_googleStatuses.TryGetValue(sourceId, out var invalidStatus)) { invalidStatus.IsOpen = true; invalidStatus.Severity = InfoBarSeverity.Error; invalidStatus.Message = "Enter the Desktop app client ID ending in .apps.googleusercontent.com below."; }
            return;
        }
        try
        {
            SettingsStore.Save(_settings);
            if (_googleStatuses.TryGetValue(sourceId, out var status)) { status.IsOpen = true; status.Severity = InfoBarSeverity.Informational; status.Message = "Opening Google sign-in…"; }
            await SettingsStore.SendCommandAsync("connect-google|" + sourceId);
        }
        catch (Exception error)
        {
            if (_googleStatuses.TryGetValue(sourceId, out var status)) { status.IsOpen = true; status.Severity = InfoBarSeverity.Error; status.Message = error.Message; }
        }
    }

    private async void DisconnectGoogle_Click(object sender, RoutedEventArgs e)
    {
        CaptureCurrentPage();
        if (sender is not Button { Tag: string sourceId }) return;
        var source = _settings.CalendarSources.FirstOrDefault(x => x.Id == sourceId);
        if (source == null) return;
        source.GoogleRefreshTokenProtected = "";
        source.GoogleAccountEmail = "";
        SettingsStore.Save(_settings);
        try { await SettingsStore.SendCommandAsync("reload"); } catch { }
        _page = "";
        ShowPage("Calendars");
    }

    private void AddOutlookSource_Click(object sender, RoutedEventArgs e) => AddCalendarSource(CalendarProviderKind.Outlook);
    private void AddIcsSource_Click(object sender, RoutedEventArgs e) => AddCalendarSource(CalendarProviderKind.Ics);
    private void AddGoogleSource_Click(object sender, RoutedEventArgs e) => AddCalendarSource(CalendarProviderKind.Google);

    private void AddCalendarSource(CalendarProviderKind provider)
    {
        CaptureCurrentPage();
        if (provider == CalendarProviderKind.Outlook && _settings.CalendarSources.Any(x => x.Provider == provider)) return;
        _settings.CalendarSources.Add(CalendarSourceSettings.Create(provider));
        _page = "";
        ShowPage("Calendars");
    }

    private void RemoveCalendarSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sourceId }) return;
        CaptureCurrentPage();
        _settings.CalendarSources.RemoveAll(x => x.Id == sourceId);
        _page = "";
        ShowPage("Calendars");
    }

    private static string CalendarKey(string sourceId, string calendarId) => sourceId + "|" + calendarId;

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureCurrentPage();
        var widgetRegistrationCompleted = false;
        try
        {
            SettingsStore.Save(_settings);
            var widgetStatus = await WinWWidgetRegistration.ApplyAsync(_settings.EnableWinWWidget);
            widgetRegistrationCompleted = true;
            await SettingsStore.SendCommandAsync("reload");
            SaveStatus.Severity = InfoBarSeverity.Success;
            SaveStatus.Message = string.IsNullOrWhiteSpace(widgetStatus) ? "Settings saved and reloaded" : widgetStatus;
            SaveStatus.IsOpen = true;
            var hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            hide.Tick += (_, _) => { hide.Stop(); SaveStatus.IsOpen = false; };
            hide.Start();
        }
        catch (Exception error)
        {
            if (_settings.EnableWinWWidget && !widgetRegistrationCompleted)
            {
                _settings.EnableWinWWidget = false;
                if (_winWWidget != null) _winWWidget.IsOn = false;
                SettingsStore.Save(_settings);
                try { await SettingsStore.SendCommandAsync("reload"); } catch { }
            }
            SaveStatus.Severity = InfoBarSeverity.Error;
            SaveStatus.Message = "Could not reload MeetingBar: " + error.Message;
            SaveStatus.IsOpen = true;
        }
    }

    private void CaptureCurrentPage()
    {
        switch (_page)
        {
            case "Widget":
                if (_widgetMode != null) _settings.WidgetMode = (WidgetMode)Math.Max(0, _widgetMode.SelectedIndex);
                if (_widgetWidth != null) _settings.WidgetWidth = _widgetWidth.Value;
                if (_offsetX != null) _settings.WidgetOffsetX = Finite(_offsetX.Value, 0);
                if (_offsetY != null) _settings.WidgetOffsetY = Finite(_offsetY.Value, 0);
                if (_hideWidgetInFullscreen != null) _settings.HideWidgetInFullscreen = _hideWidgetInFullscreen.IsOn;
                if (_autoWidgetColor != null) _settings.UseAutomaticWidgetColor = _autoWidgetColor.IsOn;
                if (_widgetColor != null) _settings.WidgetBackgroundColor = _widgetColor.Text.Trim();
                if (_titleMode != null) _settings.TitleMode = (TitleMode)Math.Max(0, _titleMode.SelectedIndex);
                if (_timeMode != null) _settings.TimeMode = (TimeMode)Math.Max(0, _timeMode.SelectedIndex);
                if (_ongoingTime != null) _settings.ShowOngoingTimeRemaining = _ongoingTime.IsOn;
                if (_titleLength != null) _settings.TitleMaxLength = (int)Finite(_titleLength.Value, 64);
                if (_showIcon != null) _settings.ShowServiceIcon = _showIcon.IsOn;
                if (_endOfDayBehavior != null) _settings.EndOfDayWidgetBehavior = (EndOfDayWidgetBehavior)Math.Max(0, _endOfDayBehavior.SelectedIndex);
                if (_endOfDayCustomMessage != null) _settings.EndOfDayCustomMessage = _endOfDayCustomMessage.Text.Trim();
                if (_highlightMeetings != null) _settings.HighlightStartingMeetings = _highlightMeetings.IsOn;
                if (_highlightBefore != null) _settings.HighlightMinutesBefore = (int)Finite(_highlightBefore.Value, 5);
                if (_highlightAfter != null) _settings.HighlightMinutesAfterStart = (int)Finite(_highlightAfter.Value, 10);
                if (_highlightWholeMeeting != null) _settings.HighlightWholeMeeting = _highlightWholeMeeting.IsOn;
                if (_highlightStyle != null) _settings.WidgetHighlightStyle = (WidgetHighlightStyle)Math.Max(0, _highlightStyle.SelectedIndex);
                if (_winWWidget != null) _settings.EnableWinWWidget = _winWWidget.IsOn;
                break;
            case "Calendars":
                if (_googleClientId != null) _settings.GoogleClientId = _googleClientId.Text.Trim();
                if (_googleClientSecret != null) _settings.GoogleClientSecret = _googleClientSecret.Password.Trim();
                foreach (var source in _settings.CalendarSources)
                {
                    if (_calendarSourceEditors.TryGetValue(source.Id, out var editor))
                    {
                        source.Name = string.IsNullOrWhiteSpace(editor.Name.Text) ? CalendarSourceSettings.DefaultName(source.Provider) : editor.Name.Text.Trim();
                        source.Enabled = editor.Enabled.IsOn;
                        if (editor.IcsSources != null) source.IcsSources = editor.IcsSources.Text;
                    }
                    var prefix = source.Id + "|";
                    var checks = _calendarChecks.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                    if (checks.Count > 0)
                    {
                        source.CalendarSelectionConfigured = true;
                        source.SelectedCalendarIds = checks.Where(x => x.Value.IsChecked == true).Select(x => x.Key.Substring(prefix.Length)).ToList();
                    }
                }
                break;
            case "Events":
                if (_allDay != null) _settings.ShowAllDayEvents = _allDay.IsOn;
                if (_declined != null) _settings.ShowDeclinedEvents = _declined.IsOn;
                if (_tentative != null) _settings.ShowTentativeEvents = _tentative.IsOn;
                if (_free != null) _settings.ShowFreeEvents = _free.IsOn;
                if (_ignoreFreeTaskbar != null) _settings.IgnoreFreeEventsInTaskbar = _ignoreFreeTaskbar.IsOn;
                if (_requireLink != null) _settings.RequireMeetingLink = _requireLink.IsOn;
                if (_pastMeetingHours != null) _settings.PastMeetingHours = (int)Finite(_pastMeetingHours.Value, 3);
                if (_futureMeetingHours != null) _settings.FutureMeetingHours = (int)Finite(_futureMeetingHours.Value, 24);
                if (_includePastDay != null) _settings.IncludePastDay = _includePastDay.IsOn;
                if (_includeNextDay != null) { _settings.IncludeNextDay = _includeNextDay.IsOn; _settings.IncludeTomorrow = _includeNextDay.IsOn; }
                if (_timelinePastDays != null) _settings.TimelinePastDays = (int)Finite(_timelinePastDays.Value, 1);
                if (_timelineFutureDays != null) _settings.TimelineFutureDays = (int)Finite(_timelineFutureDays.Value, 1);
                if (_fullDayEventList != null) _settings.ShowFullDayEventList = _fullDayEventList.IsOn;
                if (_use24HourClock != null) _settings.Use24HourClock = _use24HourClock.IsOn;
                if (_showTimelineDate != null) _settings.ShowTimelineDate = _showTimelineDate.IsOn;
                if (_timelineDateFormat != null) _settings.TimelineDateFormat = TimelineDatePatterns[Math.Max(0, _timelineDateFormat.SelectedIndex)];
                if (_refreshMinutes != null) _settings.RefreshMinutes = (int)Finite(_refreshMinutes.Value, 3);
                break;
            case "Meetings":
                if (_browserPath != null) _settings.PreferredBrowserPath = _browserPath.Text.Trim();
                if (_browserArgs != null) _settings.PreferredBrowserArguments = _browserArgs.Text;
                if (_createUrl != null) _settings.CreateMeetingUrl = _createUrl.Text.Trim();
                if (_mapProvider?.SelectedItem is string mapProvider) _settings.MapProvider = mapProvider;
                _settings.Bookmarks = _bookmarkEditors.Select(x => new Bookmark { Name = x.Name.Text.Trim(), Url = x.Url.Text.Trim() })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name) && Uri.IsWellFormedUriString(x.Url, UriKind.Absolute)).ToList();
                break;
            case "Reminders":
                if (_notify != null) _settings.NotifyBeforeMeeting = _notify.IsOn;
                _settings.NotificationMinutesBefore = _notificationChecks.Where(x => x.Value.IsChecked == true).Select(x => x.Key).OrderByDescending(x => x).ToList();
                _settings.NotifyMinutesBefore = _settings.NotificationMinutesBefore.FirstOrDefault();
                _settings.NotificationScheduleVersion = 1;
                if (_fullScreen != null) _settings.FullscreenReminder = _fullScreen.IsOn;
                if (_notifyEnd != null) _settings.NotifyAtEnd = _notifyEnd.IsOn;
                if (_autoJoin != null) _settings.AutoJoin = _autoJoin.IsOn;
                if (_autoJoinBefore != null) _settings.AutoJoinMinutesBefore = (int)Finite(_autoJoinBefore.Value, 0);
                if (_hotkey != null) _settings.GlobalJoinHotkey = _hotkey.IsOn;
                break;
            case "General":
                if (_startWithWindows != null) _settings.StartWithWindows = _startWithWindows.IsOn;
                break;
        }
    }

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private void StateTimer_Tick(object? sender, object e)
    {
        var state = SettingsStore.LoadState();
        var signature = state.IsBusy + "|" + state.ActiveSourceId + "|" + state.StatusMessage + "|" + state.GoogleAccountEmail + "|" + state.Calendars.Count;
        if (signature == _stateSignature) return;
        _stateSignature = signature;
        if (_page == "Calendars" && !string.IsNullOrWhiteSpace(state.ActiveSourceId) && _googleStatuses.TryGetValue(state.ActiveSourceId, out var status))
        {
            status.IsOpen = true;
            status.Message = string.IsNullOrWhiteSpace(state.StatusMessage) ? "Not connected" : state.StatusMessage;
            status.Severity = state.IsBusy ? InfoBarSeverity.Informational : state.StatusMessage.StartsWith("Connected as ", StringComparison.OrdinalIgnoreCase) ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        }
        if (!state.IsBusy && !string.IsNullOrWhiteSpace(state.ActiveSourceId))
        {
            _settings = SettingsStore.Load();
            if (_page == "Calendars")
            {
                _page = "";
                ShowPage("Calendars");
            }
        }
    }
}
