using System;
using System.IO;
using System.Text;

namespace MeetingBar.Windows.Services
{
    internal static class AppLog
    {
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
                    if (File.Exists(PathValue) && new FileInfo(PathValue).Length > 1024 * 1024)
                    {
                        // File.Move refuses to overwrite, so without this the second rotation failed and logging stopped for good.
                        File.Delete(PathValue + ".old");
                        File.Move(PathValue, PathValue + ".old");
                    }
                    File.AppendAllText(PathValue, DateTimeOffset.Now.ToString("o") + " " + level + " " + message.Replace("\r", " ").Replace("\n", " ") + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
