using System;
using System.IO;
using System.Text;

namespace MeetingBar.Windows.Services
{
    internal static class AppLog
    {
        private const long MaximumLogBytes = 1024 * 1024;
        private static readonly object Gate = new object();
        private static readonly string PathValue = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingBar", "meetingbar.log");
        public static string Path => PathValue;
        public static void Info(string message) => Write("INFO", message);
        public static void Error(string area, Exception error) => Write("ERROR", area + ": " + error.GetType().Name + " — " + error.Message);
        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathValue));
                    RotateIfNeeded(PathValue, MaximumLogBytes);
                    File.AppendAllText(PathValue, DateTimeOffset.Now.ToString("o") + " " + level + " " + message.Replace("\r", " ").Replace("\n", " ") + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        // File.Move cannot overwrite on .NET Framework, so remove the previous archive first.
        // Otherwise the second rotation throws and every later write is dropped.
        internal static void RotateIfNeeded(string path, long maximumBytes)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= maximumBytes) return;
            var archive = path + ".old";
            if (File.Exists(archive)) File.Delete(archive);
            File.Move(path, archive);
        }
    }
}
