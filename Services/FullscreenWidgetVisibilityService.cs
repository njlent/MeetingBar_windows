using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MeetingBar.Windows.Interop;
using MeetingBar.Windows.Models;

namespace MeetingBar.Windows.Services
{
    public static class FullscreenWidgetVisibilityService
    {
        private static readonly int CurrentProcessId = ReadCurrentProcessId();

        /// <param name="widgetMonitor">The display showing the widget. A fullscreen app on another display leaves the widget visible.</param>
        public static bool ShouldHide(AppSettings settings, MeetingBar.Display.ConnectedMonitor widgetMonitor = null)
        {
            if (settings?.HideWidgetInFullscreen != true) return false;
            var window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero || !NativeMethods.IsWindowVisible(window)) return false;

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == CurrentProcessId) return false;

            string processPath = null;
            string processName = null;
            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    processName = process.ProcessName;
                    if (settings.FullscreenAppBlacklist?.Count > 0)
                        try { processPath = process.MainModule?.FileName; } catch { }
                }
            }
            catch { }

            if (IsMeetingBarOrShell(processName)) return false;
            if (MatchesBlacklist(settings.FullscreenAppBlacklist, processPath, processName)) return true;
            if (!NativeMethods.GetWindowRect(window, out var windowRect)) return false;

            var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;
            var monitorInfo = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) return false;
            if (widgetMonitor != null && (monitorInfo.Monitor.Left != widgetMonitor.Left || monitorInfo.Monitor.Top != widgetMonitor.Top)) return false;

            return CoversMonitor(ToRectangle(windowRect), ToRectangle(monitorInfo.Monitor));
        }

        public static bool CoversMonitor(Rectangle window, Rectangle monitor, int tolerance = 2) =>
            window.Width > 0 && window.Height > 0 && monitor.Width > 0 && monitor.Height > 0
            && window.Left <= monitor.Left + tolerance && window.Top <= monitor.Top + tolerance
            && window.Right >= monitor.Right - tolerance && window.Bottom >= monitor.Bottom - tolerance;

        public static bool MatchesBlacklist(IEnumerable<string> blacklist, string processPath, string processName)
        {
            foreach (var entry in blacklist ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (!string.IsNullOrWhiteSpace(processPath) && PathsEqual(entry, processPath)) return true;
                if (string.IsNullOrWhiteSpace(processPath) && !string.IsNullOrWhiteSpace(processName)
                    && string.Equals(Path.GetFileNameWithoutExtension(entry), processName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool PathsEqual(string left, string right)
        {
            try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
        }

        private static bool IsMeetingBarOrShell(string processName) =>
            string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(processName) && processName.StartsWith("MeetingBar", StringComparison.OrdinalIgnoreCase));

        private static int ReadCurrentProcessId()
        {
            using (var process = Process.GetCurrentProcess()) return process.Id;
        }

        private static Rectangle ToRectangle(NativeMethods.RECT value) =>
            Rectangle.FromLTRB(value.Left, value.Top, value.Right, value.Bottom);
    }
}
