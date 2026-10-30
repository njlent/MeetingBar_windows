using System;
using System.Threading;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Media;
using MeetingBar.Windows.Interop;
using MeetingBar.Windows.Services;
using MeetingBar.Windows.Views;

namespace MeetingBar.Windows
{
    public partial class App : Application
    {
        private Mutex _singleInstance;
        private AppController _controller;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool created;
            _singleInstance = new Mutex(true, "Local\\MeetingBar.Windows", out created);
            if (!created) { SendCommand(e.Args.Length == 0 ? "popup" : e.Args[0].TrimStart('-')); Shutdown(); return; }
            base.OnStartup(e);
            ApplyWindowsAccent();
            LanguageSource.Initialize();
            AppDomain.CurrentDomain.UnhandledException += (s, args) => AppLog.Error("Unhandled", args.ExceptionObject as Exception ?? new Exception("Unknown fatal error"));
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) => { AppLog.Error("Task", args.Exception); args.SetObserved(); };
            DispatcherUnhandledException += (s, args) =>
            {
                AppLog.Error("Dispatcher", args.Exception);
                MessageBox.Show(args.Exception.Message, "MeetingBar", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            _controller = new AppController();
            AppLog.Info("Application started");
            _controller.Start();
            if (e.Args.Length > 0) _controller.ExecuteCommand(e.Args[0].TrimStart('-'));
        }

        internal static void ApplyWindowsAccent()
        {
            try
            {
                uint value; bool opaque;
                if (NativeMethods.DwmGetColorizationColor(out value, out opaque) != 0) return;
                var color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
                Current.Resources["Accent"] = color;
                Current.Resources["AccentBrush"] = new SolidColorBrush(color);
            }
            catch { }
        }

        private static void SendCommand(string command)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", "MeetingBar.Windows.Commands", PipeDirection.Out))
                using (var writer = new StreamWriter(pipe))
                { pipe.Connect(1500); writer.WriteLine(command); writer.Flush(); }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _controller?.Dispose();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
