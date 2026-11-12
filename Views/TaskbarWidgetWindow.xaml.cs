using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Globalization;
using Microsoft.Win32;
using MeetingBar.Windows.Interop;
using MeetingBar.Windows.Models;
using MeetingBar.Windows.Services;
using MeetingBar.Windows.ViewModels;

namespace MeetingBar.Windows.Views
{
    public partial class TaskbarWidgetWindow : Window
    {
        private readonly Action _clicked;
        private Brush _normalBackground;
        private Brush _hoverBackground;
        private Brush _highlightBackground;
        private bool _isHovered;
        private bool _isHighlighted;
        private bool _shineRunning;
        private double _shineDestination;
        public TaskbarWidgetWindow(MainViewModel vm, Action clicked)
        {
            InitializeComponent();
            DataContext = vm; _clicked = clicked;
            SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                var source = HwndSource.FromHwnd(handle); source?.AddHook(WndProc);
                EnsureAboveTaskbar();
            };
        }
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x001C) Dispatcher.BeginInvoke(new Action(EnsureAboveTaskbar));
            return IntPtr.Zero;
        }
        private void Widget_Click(object sender, RoutedEventArgs e) => _clicked();
        private void WidgetSurface_SizeChanged(object sender, SizeChangedEventArgs e) =>
            WidgetSurface.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height)), 4, 4);
        private void Widget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) { _isHovered = true; UpdateSurfaceBackground(); }
        private void Widget_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { _isHovered = false; UpdateSurfaceBackground(); }
        public void ApplyPosition(AppSettings settings)
        {
            ApplyAppearance(settings);
            Width = DesiredWidgetWidth(settings); Height = 40;
            var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(taskbar, out rect)) { Left = SystemParameters.WorkArea.Right - Width; Top = SystemParameters.WorkArea.Bottom - Height; return; }
            var dpi = VisualTreeHelper.GetDpi(this); var sx = dpi.DpiScaleX; var sy = dpi.DpiScaleY;
            var targetTop = rect.Top / sy + Math.Max(0, ((rect.Bottom - rect.Top) / sy - Height) / 2.0) + settings.WidgetOffsetY;
            Top = Clamp(targetTop, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
            if (settings.WidgetMode == WidgetMode.TaskbarLeft) Left = Clamp(rect.Left / sx + 8 + settings.WidgetOffsetX, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
            else
            {
                var notify = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                NativeMethods.RECT trayRect;
                var targetLeft = NativeMethods.GetWindowRect(notify, out trayRect) ? trayRect.Left / sx - Width - 6 + settings.WidgetOffsetX : rect.Right / sx - Width - 180 + settings.WidgetOffsetX;
                Left = Clamp(targetLeft, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
            }
            EnsureAboveTaskbar();
        }
        public void EnsureAboveTaskbar()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }
        private double DesiredWidgetWidth(AppSettings settings)
        {
            var vm = DataContext as MainViewModel;
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var title = Measure(vm?.WidgetTitle, 12, FontWeights.SemiBold, "Segoe UI", pixelsPerDip);
            var time = Measure(vm?.WidgetTime, 11, FontWeights.Normal, "Segoe UI", pixelsPerDip);
            var content = 32 + (settings.ShowServiceIcon ? 16 : 0) + title + time;
            return Clamp(Math.Ceiling(content), 140, Math.Max(140, settings.WidgetWidth));
        }
        private void ApplyAppearance(AppSettings settings)
        {
            var background = settings.UseAutomaticWidgetColor ? AutomaticTaskbarColor() : ParseColor(settings.WidgetBackgroundColor, Color.FromRgb(32, 32, 32));
            var lightText = RelativeLuminance(background) < 0.53;
            var foreground = lightText ? Colors.White : Color.FromRgb(24, 24, 24);
            var muted = Mix(background, foreground, 0.68);
            _normalBackground = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            _hoverBackground = settings.UseAutomaticWidgetColor
                ? new SolidColorBrush(Color.FromArgb(14, foreground.R, foreground.G, foreground.B))
                : new SolidColorBrush(Color.FromArgb(220, background.R, background.G, background.B));
            var vm = DataContext as MainViewModel;
            var eventColor = ParseColor(vm?.WidgetEventColor, Color.FromRgb(109, 124, 255));
            _highlightBackground = settings.WidgetHighlightStyle == WidgetHighlightStyle.Hover
                ? _hoverBackground
                : settings.UseAutomaticWidgetColor
                    ? new SolidColorBrush(Color.FromArgb(30, eventColor.R, eventColor.G, eventColor.B))
                    : new SolidColorBrush(Color.FromArgb(225, Mix(background, eventColor, 0.16).R, Mix(background, eventColor, 0.16).G, Mix(background, eventColor, 0.16).B));
            _isHighlighted = vm?.IsSignInExpired == true || WidgetHighlightPolicy.ShouldHighlight(vm?.WidgetEvent, settings, DateTime.Now);
            if (_isHighlighted && settings.WidgetHighlightStyle == WidgetHighlightStyle.Shine) StartShine(foreground); else StopShine();
            UpdateSurfaceBackground();
            WidgetSurface.BorderBrush = Brushes.Transparent;
            WidgetTitle.Foreground = new SolidColorBrush(foreground);
            WidgetTime.Foreground = new SolidColorBrush(muted);
        }

        private void UpdateSurfaceBackground() => WidgetSurface.Background = _isHovered ? _hoverBackground : _isHighlighted ? _highlightBackground : _normalBackground;

        private void StartShine(Color foreground)
        {
            var transparent = Color.FromArgb(0, foreground.R, foreground.G, foreground.B);
            var peak = Color.FromArgb(46, foreground.R, foreground.G, foreground.B);
            ShineLayer.Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(transparent, 0), new GradientStop(peak, 0.5), new GradientStop(transparent, 1)
            }, new Point(0, 0.5), new Point(1, 0.5));
            ShineLayer.Opacity = 1;
            var destination = Math.Max(230, ActualWidth + 90);
            if (_shineRunning && Math.Abs(_shineDestination - destination) < 1) return;
            _shineRunning = true;
            _shineDestination = destination;
            var sweep = new DoubleAnimation(-90, destination, TimeSpan.FromMilliseconds(1750))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            ShineTransform.BeginAnimation(TranslateTransform.XProperty, sweep, HandoffBehavior.SnapshotAndReplace);
        }

        private void StopShine()
        {
            if (!_shineRunning) { ShineLayer.Opacity = 0; return; }
            _shineRunning = false;
            ShineTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ShineTransform.X = -90;
            ShineLayer.Opacity = 0;
        }

        private static Color AutomaticTaskbarColor()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var accentTaskbar = Convert.ToInt32(key?.GetValue("ColorPrevalence", 0)) == 1;
                    if (accentTaskbar)
                    {
                        uint color; bool opaque;
                        if (NativeMethods.DwmGetColorizationColor(out color, out opaque) == 0) return Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);
                    }
                    var light = Convert.ToInt32(key?.GetValue("SystemUsesLightTheme", 0)) == 1;
                    return light ? Color.FromRgb(243, 243, 243) : Color.FromRgb(32, 32, 32);
                }
            }
            catch { return Color.FromRgb(32, 32, 32); }
        }
        private static Color ParseColor(string value, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(value); }
            catch { return fallback; }
        }
        private static double RelativeLuminance(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        private static Color Mix(Color first, Color second, double amount) => Color.FromRgb((byte)(first.R + (second.R - first.R) * amount), (byte)(first.G + (second.G - first.G) * amount), (byte)(first.B + (second.B - first.B) * amount));
        private static double Measure(string text, double size, FontWeight weight, string family, double pixelsPerDip)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.White, pixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace;
        }
        private static double Clamp(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
