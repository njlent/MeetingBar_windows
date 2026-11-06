#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MeetingBar.Display
{
    /// <summary>A connected display. Bounds and work area are in physical pixels.</summary>
    public sealed class ConnectedMonitor
    {
        public string DeviceName { get; set; } = "";
        public bool IsPrimary { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public int WorkLeft { get; set; }
        public int WorkTop { get; set; }
        public int WorkRight { get; set; }
        public int WorkBottom { get; set; }
        public uint Dpi { get; set; } = 96;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public double Scale => Dpi / 96.0;

        /// <summary>The number in the device name, such as 2 for DISPLAY2, or 0 if there is none.</summary>
        public int Number => NumberOf(DeviceName);

        internal static int NumberOf(string deviceName)
        {
            var digits = new string((deviceName ?? "").Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return int.TryParse(digits, out var number) ? number : 0;
        }
    }

    /// <summary>Lists displays for the widget's monitor setting. Shared by the tray app and Settings.</summary>
    public static class MonitorCatalog
    {
        public static IReadOnlyList<ConnectedMonitor> GetAll()
        {
            var result = new List<ConnectedMonitor>();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, data) =>
                {
                    var info = new MonitorInfoEx { Size = Marshal.SizeOf(typeof(MonitorInfoEx)) };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        result.Add(new ConnectedMonitor
                        {
                            DeviceName = info.Device ?? "", IsPrimary = (info.Flags & 1) != 0,
                            Left = info.Bounds.Left, Top = info.Bounds.Top, Right = info.Bounds.Right, Bottom = info.Bounds.Bottom,
                            WorkLeft = info.Work.Left, WorkTop = info.Work.Top, WorkRight = info.Work.Right, WorkBottom = info.Work.Bottom,
                            Dpi = EffectiveDpi(monitor)
                        });
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return result;
        }

        /// <summary>
        /// The saved display when it is connected, otherwise the primary display, so the widget returns to its
        /// display once it reconnects. Returns null only when no display is reported.
        /// </summary>
        public static ConnectedMonitor Resolve(IEnumerable<ConnectedMonitor> monitors, string deviceName)
        {
            var list = (monitors ?? Enumerable.Empty<ConnectedMonitor>()).ToList();
            return (string.IsNullOrWhiteSpace(deviceName) ? null : list.FirstOrDefault(x => string.Equals(x.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)))
                ?? list.FirstOrDefault(x => x.IsPrimary)
                ?? list.FirstOrDefault();
        }

        private static uint EffectiveDpi(IntPtr monitor)
        {
            try { return GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0 ? x : 96; } // MDT_EFFECTIVE_DPI
            catch { return 96; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size; public Rect Bounds; public Rect Work; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    }
}
