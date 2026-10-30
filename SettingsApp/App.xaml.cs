using MeetingBar.Localization;
using Microsoft.UI.Xaml;

namespace MeetingBar.SettingsApp;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        Localizer.SetLanguage(SettingsStore.Load().Language);
        // Built-in WinUI control text follows the app language when one is chosen.
        try { Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = Localizer.IsOverride ? Localizer.Culture.Name : ""; } catch { }
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            SettingsLog.Write("Unhandled: " + args.Exception.Message);
            args.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
