using System;
using System.Drawing;
using System.Runtime.InteropServices;
using MeetingBar.Display;
using MeetingBar.Windows.Interop;
using MeetingBar.Windows.Models;

namespace MeetingBar.Windows.Services
{
    /// <summary>
    /// Places the taskbar widget in physical pixels. WPF converts Left and Top with the DPI of the display a
    /// window is on now, which is wrong while it moves to a display with a different scale.
    /// </summary>
    internal static class WidgetPlacement
    {
        /// <summary>The widget's bounds on <paramref name="monitor"/>, given its taskbar and notification area when found.</summary>
        public static Rectangle Place(AppSettings settings, ConnectedMonitor monitor, double width, double height, NativeMethods.RECT? taskbar, NativeMethods.RECT? tray)
        {
            var scale = monitor.Scale;
            var w = Math.Min((int)Math.Ceiling(width * scale), Math.Max(1, monitor.Width));
            var h = (int)Math.Ceiling(height * scale);
            double x, y;
            if (taskbar.HasValue)
            {
                var bar = taskbar.Value;
                y = bar.Top + Math.Max(0, (bar.Bottom - bar.Top - h) / 2.0);
                if (settings.WidgetMode == WidgetMode.TaskbarLeft) x = bar.Left + 8 * scale;
                else x = tray.HasValue ? tray.Value.Left - w - 6 * scale : bar.Right - w - 180 * scale;
            }
            else
            {
                y = monitor.WorkBottom - h - 8 * scale;
                x = settings.WidgetMode == WidgetMode.TaskbarLeft ? monitor.WorkLeft + 8 * scale : monitor.WorkRight - w - 8 * scale;
            }
            x = Clamp(x + settings.WidgetOffsetX * scale, monitor.Left, monitor.Right - w);
            y = Clamp(y + settings.WidgetOffsetY * scale, monitor.Top, monitor.Bottom - h);
            return new Rectangle((int)Math.Round(x), (int)Math.Round(y), w, h);
        }

        /// <summary>The taskbar on <paramref name="monitor"/>: Shell_TrayWnd on the primary display, Shell_SecondaryTrayWnd elsewhere.</summary>
        public static IntPtr FindTaskbar(ConnectedMonitor monitor)
        {
            var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (IsOnMonitor(primary, monitor)) return primary;
            for (var secondary = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_SecondaryTrayWnd", null); secondary != IntPtr.Zero;
                 secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null))
            {
                if (IsOnMonitor(secondary, monitor)) return secondary;
            }
            return IntPtr.Zero;
        }

        // Uses the nearest display, so an auto-hidden taskbar that is mostly off screen still matches.
        private static bool IsOnMonitor(IntPtr window, ConnectedMonitor monitor)
        {
            if (window == IntPtr.Zero) return false;
            var handle = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
            var info = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            return handle != IntPtr.Zero && NativeMethods.GetMonitorInfo(handle, ref info) && info.Monitor.Left == monitor.Left && info.Monitor.Top == monitor.Top;
        }

        private static double Clamp(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
