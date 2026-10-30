using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Web.Script.Serialization;
using MeetingBar.Localization;
using MeetingBar.Windows.Interop;
using MeetingBar.Windows.Models;
using MeetingBar.Windows.ViewModels;
using MeetingBar.Windows.Views;
using Forms = System.Windows.Forms;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.Windows.Services
{
    public sealed class AppController : IDisposable
    {
        private readonly SettingsService _settings = new SettingsService();
        private readonly CalendarSyncService _sync;
        private readonly MeetingOpener _opener;
        private readonly Forms.NotifyIcon _tray;
        private Icon _trayIcon;
        private string _trayIconState;
        private readonly HwndSource _messageWindow;
        private readonly MainViewModel _main;
        private readonly NotificationService _notifications;
        private readonly DispatcherTimer _positionTimer;
        private PopupWindow _popup;
        private TaskbarWidgetWindow _widget;
        private readonly CancellationTokenSource _ipcCancellation = new CancellationTokenSource();
        private readonly object _settingsStateGate = new object();

        public AppController()
        {
            Localizer.SetLanguage(_settings.Current.Language);
            _sync = new CalendarSyncService(_settings); _opener = new MeetingOpener(_settings); _main = new MainViewModel(_sync, _settings, _opener);
            _trayIcon = CreateTrayIcon(AppAccentColor(), false, System.Drawing.Color.Transparent);
            _tray = new Forms.NotifyIcon { Icon = _trayIcon, Visible = true, Text = "MeetingBar" };
            _tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) TogglePopup(); };
            BuildTrayMenu();
            Localizer.LanguageChanged += (s, e) => { BuildTrayMenu(); UpdateTray(); };
            _notifications = new NotificationService(_settings, _opener, _tray);
            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; _positionTimer.Tick += (s, e) => { EnsureWidgetVisible(); UpdateTrayIcon(_main.TaskbarEvent); };
            _sync.Changed += (s, e) => Application.Current.Dispatcher.BeginInvoke(new Action(() => { UpdateTray(); WriteSettingsState(); WriteWinWWidgetState(); }));
            var parameters = new HwndSourceParameters("MeetingBar.MessageWindow") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) };
            _messageWindow = new HwndSource(parameters); _messageWindow.AddHook(WndProc);
        }

        private void BuildTrayMenu()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(T("Open MeetingBar"), null, (s, e) => ShowPopup());
            menu.Items.Add(T("Refresh"), null, async (s, e) => await _sync.RefreshAsync());
            menu.Items.Add(T("Settings"), null, (s, e) => OpenSettings());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(T("Quit"), null, (s, e) => Application.Current.Shutdown());
            var previous = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = menu;
            previous?.Dispose();
        }

        public void Start()
        {
            ApplySettings();
            _popup = new PopupWindow(_main, OpenSettings);
            _popup.Preload();
            _sync.Start(); _notifications.Start(() => _sync.Events); _positionTimer.Start(); UpdateTray(); WriteSettingsState(); WriteWinWWidgetState(); _ = ListenForCommands();
        }

        private async Task ListenForCommands()
        {
            while (!_ipcCancellation.IsCancellationRequested)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream("MeetingBar.Windows.Commands", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        await pipe.WaitForConnectionAsync(_ipcCancellation.Token);
                        using (var reader = new StreamReader(pipe))
                        {
                            var command = await reader.ReadLineAsync();
                            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() => ExecuteCommand(command)));
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { if (!_ipcCancellation.IsCancellationRequested) await Task.Delay(250); }
            }
        }

        public void ExecuteCommand(string command)
        {
            var normalized = (command ?? "popup").Trim().TrimStart('-').ToLowerInvariant();
            if (normalized.StartsWith("connect-google|", StringComparison.Ordinal))
            {
                _ = ConnectGoogleFromSettings(normalized.Substring("connect-google|".Length));
                return;
            }
            switch (normalized)
            {
                case "join": _opener.Open(_main.NextEvent); break;
                case "create": _opener.Open(_settings.Current.CreateMeetingUrl); break;
                case "calendar": if (_main.OpenCalendarCommand.CanExecute(null)) _main.OpenCalendarCommand.Execute(null); break;
                case "refresh": _ = _sync.RefreshAsync(); break;
                case "settings": OpenSettings(); break;
                case "reload": _ = ReloadSettings(); break;
                case "connect-google": _ = ConnectGoogleFromSettings(null); break;
                case "debug-inject": InjectDebugEvent(); break;
                case "debug-notification": TriggerDebugNotification(false); break;
                case "debug-reminder": TriggerDebugNotification(true); break;
                case "debug-clear": _sync.ClearDebugEvents(); break;
                case "quit": Application.Current.Shutdown(); break;
                default: ShowPopup(); break;
            }
        }

        private void ApplySettings()
        {
            Localizer.SetLanguage(_settings.Current.Language);
            _main.RefreshSettings();
            EnsureWidgetVisible();
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, 1);
            if (_settings.Current.GlobalJoinHotkey) NativeMethods.RegisterHotKey(_messageWindow.Handle, 1, NativeMethods.ModControl | NativeMethods.ModAlt, (uint)Forms.Keys.J);
            _sync.Schedule(); UpdateTray();
        }

        private void CreateWidget()
        {
            _widget = new TaskbarWidgetWindow(_main, TogglePopup);
            _widget.Closed += (s, e) => _widget = null;
        }

        private void EnsureWidgetVisible()
        {
            if (_settings.Current.WidgetMode == WidgetMode.TrayOnly || !_main.ShouldShowTaskbarWidget
                || FullscreenWidgetVisibilityService.ShouldHide(_settings.Current))
            {
                _widget?.Hide();
                return;
            }
            if (_widget == null) CreateWidget();
            if (!_widget.IsVisible) _widget.Show();
            var widgetHandle = new WindowInteropHelper(_widget).Handle;
            if (widgetHandle != IntPtr.Zero && !NativeMethods.IsWindowVisible(widgetHandle)) NativeMethods.ShowWindow(widgetHandle, 5);
            _widget.ApplyPosition(_settings.Current);
            _widget.EnsureAboveTaskbar();
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == NativeMethods.WmHotKey && wParam.ToInt32() == 1) { _opener.Open(_main.NextEvent); handled = true; }
            return IntPtr.Zero;
        }

        private void TogglePopup()
        {
            if (_popup?.IsOpen == true) { _popup.HideAnimated(); return; }
            if (_popup?.WasJustDismissed == true) return;
            ShowPopup();
        }
        private void ShowPopup()
        {
            if (_popup == null) _popup = new PopupWindow(_main, OpenSettings);
            PositionPopup(_popup); _popup.ShowAnimated();
        }

        private void PositionPopup(Window popup)
        {
            popup.UpdateLayout();
            var work = SystemParameters.WorkArea;
            if (_widget?.IsVisible == true)
            {
                popup.Left = Math.Max(work.Left + 8, Math.Min(work.Right - popup.Width - 8, _widget.Left + _widget.Width - popup.Width));
                popup.Top = Math.Max(work.Top + 12, _widget.Top - popup.Height - 12);
            }
            else { popup.Left = work.Right - popup.Width - 10; popup.Top = work.Bottom - popup.Height - 10; }
        }

        private void OpenSettings()
        {
            WriteSettingsState();
            var executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings", "MeetingBar.Settings.exe");
            if (!File.Exists(executable))
            {
                MessageBox.Show(T("The WinUI Settings folder is missing. Re-extract the complete portable package."), "MeetingBar", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var existing = Process.GetProcessesByName("MeetingBar.Settings").FirstOrDefault();
            if (existing != null)
            {
                if (existing.MainWindowHandle != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(existing.MainWindowHandle, 9);
                    NativeMethods.SetForegroundWindow(existing.MainWindowHandle);
                }
                existing.Dispose();
                return;
            }
            Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory, UseShellExecute = true });
        }

        private async Task ReloadSettings()
        {
            _settings.Reload();
            StartupService.SetEnabled(_settings.Current.StartWithWindows);
            ApplySettings();
            await _sync.RefreshAsync();
            WriteSettingsState();
            WriteWinWWidgetState();
        }

        private void WriteWinWWidgetState()
        {
            try { WinWWidgetStateService.Write(_sync.Events, _settings.Current); }
            catch (Exception error) { AppLog.Error("Win+W widget state", error); }
        }

        private async Task ConnectGoogleFromSettings(string sourceId)
        {
            _settings.Reload();
            var source = string.IsNullOrWhiteSpace(sourceId)
                ? _settings.Current.CalendarSources.FirstOrDefault(x => x.Provider == CalendarProviderKind.Google)
                : _settings.Current.CalendarSources.FirstOrDefault(x => string.Equals(x.Id, sourceId, StringComparison.OrdinalIgnoreCase));
            if (source == null || source.Provider != CalendarProviderKind.Google)
            {
                WriteSettingsState(T("The Google calendar source no longer exists."), false, sourceId);
                return;
            }
            WriteSettingsState(T("Opening Google sign-in. Choose the account to add and approve read-only calendar access."), true, source.Id);
            try
            {
                await _sync.Google.ConnectAsync(_settings.Current, source);
                _settings.Save();
                ApplySettings();
                WriteSettingsState(T("Connected as {0}. Loading calendars…", source.GoogleAccountEmail), true, source.Id, true);
                await _sync.RefreshAsync();
                WriteSettingsState(T("Connected as {0}", source.GoogleAccountEmail), false, source.Id, true);
            }
            catch (Exception error)
            {
                AppLog.Error("Google connect", error);
                WriteSettingsState(error.Message, false, source.Id);
            }
        }

        private void InjectDebugEvent()
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingBar", "debug-request.json");
                var request = File.Exists(path) ? new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)) : null;
                var title = DebugString(request, "Title", "Test meeting");
                var start = DebugInt(request, "StartMinutes", 5);
                var duration = DebugInt(request, "DurationMinutes", 30);
                var service = DebugString(request, "Service", "Google Meet");
                var color = DebugString(request, "Color", "#0078D4");
                _sync.InjectDebugEvent(DebugEventFactory.Create(title, start, duration, service, color, DateTime.Now));
            }
            catch (Exception error) { AppLog.Error("Debug event", error); }
        }

        private void TriggerDebugNotification(bool fullScreen)
        {
            var item = _sync.LatestDebugEvent ?? DebugEventFactory.Create(T("Test meeting reminder"), 5, 30, "Google Meet", "#0078D4", DateTime.Now);
            _notifications.ShowTest(item, fullScreen);
        }

        private static string DebugString(Dictionary<string, object> values, string key, string fallback) =>
            values != null && values.ContainsKey(key) && values[key] != null ? Convert.ToString(values[key]) : fallback;
        private static int DebugInt(Dictionary<string, object> values, string key, int fallback)
        {
            try { return values != null && values.ContainsKey(key) ? Convert.ToInt32(values[key]) : fallback; }
            catch { return fallback; }
        }

        private void WriteSettingsState(string message = null, bool busy = false, string activeSourceId = null, bool succeeded = false)
        {
            try
            {
                var status = message;
                if (string.IsNullOrWhiteSpace(status))
                {
                    status = !string.IsNullOrWhiteSpace(_sync.Status.Message) ? _sync.Status.Message : T("Calendar sources ready");
                }
                var connectedEmails = _settings.Current.CalendarSources
                    .Where(x => x.Provider == CalendarProviderKind.Google && _sync.Google.IsConnected(x))
                    .Select(x => x.GoogleAccountEmail).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                var state = new Dictionary<string, object>
                {
                    ["StatusMessage"] = status,
                    ["IsBusy"] = busy,
                    ["Succeeded"] = succeeded,
                    ["ActiveSourceId"] = activeSourceId ?? "",
                    ["GoogleAccountEmail"] = string.Join(", ", connectedEmails),
                    ["Calendars"] = _sync.Calendars.Select(x => new Dictionary<string, object>
                    {
                        ["SourceId"] = x.SourceId ?? "", ["SourceName"] = x.SourceName ?? "", ["SourceKind"] = x.Source ?? "",
                        ["Id"] = x.Id ?? "", ["Name"] = x.Name ?? "", ["Color"] = x.Color ?? "#6D7CFF"
                    }).ToList()
                };
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingBar");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "settings-state.json");
                var temporary = path + ".tmp";
                var json = new JavaScriptSerializer().Serialize(state);
                lock (_settingsStateGate)
                {
                    File.WriteAllText(temporary, json, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
            }
            catch { }
        }

        private void UpdateTray()
        {
            var item = _main.TaskbarEvent;
            var text = item == null ? "MeetingBar — " + T("No upcoming meetings") : item.Title + " — " + item.Countdown;
            _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            UpdateTrayIcon(item);
            _widget?.ApplyPosition(_settings.Current);
        }

        private void UpdateTrayIcon(CalendarEvent item)
        {
            var now = DateTime.Now;
            var ongoing = item?.IsCurrent == true;
            var soon = item != null && !ongoing && item.Start > now && item.Start <= now.AddMinutes(30);
            var eventColor = ParseDrawingColor(item?.CalendarColor, AppAccentColor());
            var baseColor = ongoing ? eventColor : AppAccentColor();
            var state = baseColor.ToArgb().ToString("X8") + ":" + (soon ? eventColor.ToArgb().ToString("X8") : "none");
            if (state == _trayIconState) return;
            var replacement = CreateTrayIcon(baseColor, soon, eventColor);
            var previous = _trayIcon;
            _trayIcon = replacement;
            _tray.Icon = replacement;
            _trayIconState = state;
            previous?.Dispose();
        }

        private static System.Drawing.Color AppAccentColor()
        {
            try
            {
                uint value; bool opaque;
                if (NativeMethods.DwmGetColorizationColor(out value, out opaque) == 0)
                    return System.Drawing.Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            }
            catch { }
            return System.Drawing.Color.FromArgb(255, 0, 120, 212);
        }

        private static System.Drawing.Color ParseDrawingColor(string value, System.Drawing.Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { return System.Drawing.ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }

        private static Icon CreateTrayIcon(System.Drawing.Color backgroundColor, bool showDot, System.Drawing.Color dotColor)
        {
            var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(backgroundColor))
            using (var pen = new Pen(System.Drawing.Color.White, 2.2f))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.FillRoundedRectangle(background, 2, 2, 28, 28, 7);
                pen.StartCap = pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawRoundedRectangle(pen, 8, 8, 16, 16, 3);
                g.DrawLine(pen, 8, 13, 24, 13);
                g.DrawLine(pen, 12, 6, 12, 10);
                g.DrawLine(pen, 20, 6, 20, 10);
                if (showDot)
                {
                    using (var outline = new SolidBrush(System.Drawing.Color.FromArgb(230, 30, 30, 30))) g.FillEllipse(outline, 20, 20, 11, 11);
                    using (var dot = new SolidBrush(dotColor)) g.FillEllipse(dot, 22, 22, 7, 7);
                }
            }
            var handle = bitmap.GetHicon();
            try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
            finally { NativeMethods.DestroyIcon(handle); bitmap.Dispose(); }
        }

        public void Dispose()
        {
            _ipcCancellation.Cancel(); _positionTimer.Stop(); NativeMethods.UnregisterHotKey(_messageWindow.Handle, 1); _messageWindow.Dispose(); _notifications.Dispose(); _main.Dispose(); _sync.Dispose(); _tray.Visible = false; _tray.Dispose(); _trayIcon.Dispose(); _ipcCancellation.Dispose();
        }
    }

    internal static class DrawingExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var d = radius * 2; path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + width - d, y, d, d, 270, 90); path.AddArc(x + width - d, y + height - d, d, d, 0, 90); path.AddArc(x, y + height - d, d, d, 90, 90); path.CloseFigure(); graphics.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, float x, float y, float width, float height, float radius)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var d = radius * 2; path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + width - d, y, d, d, 270, 90); path.AddArc(x + width - d, y + height - d, d, d, 0, 90); path.AddArc(x, y + height - d, d, d, 90, 90); path.CloseFigure(); graphics.DrawPath(pen, path);
            }
        }
    }
}
