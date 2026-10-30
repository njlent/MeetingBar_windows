using Windows.Management.Deployment;
using static MeetingBar.Localization.Localizer;

namespace MeetingBar.SettingsApp;

internal static class WinWWidgetRegistration
{
    private const string PackageName = "MeetingBar.WinWWidget";

    public static async Task<string> ApplyAsync(bool enabled)
    {
        var manager = new PackageManager();
        if (!enabled)
        {
            var packages = manager.FindPackagesForUser(string.Empty, PackageName).ToList();
            foreach (var package in packages)
                await manager.RemovePackageAsync(package.Id.FullName, RemovalOptions.PreserveApplicationData).AsTask();
            return packages.Count == 0 ? T("Settings saved and reloaded") : T("Win+W widget provider removed");
        }

        var manifest = FindManifest();
        if (manifest == null) throw new FileNotFoundException(T("The Win+W widget provider is missing. Re-extract the complete portable package."));
        var manifestFolder = Path.GetDirectoryName(manifest)!;
        var existing = manager.FindPackagesForUser(string.Empty, PackageName).FirstOrDefault();
        if (existing != null)
        {
            if (string.Equals(Path.GetFullPath(existing.InstalledLocation.Path), manifestFolder, StringComparison.OrdinalIgnoreCase)) return T("Settings saved and reloaded");
            await manager.RemovePackageAsync(existing.Id.FullName, RemovalOptions.PreserveApplicationData).AsTask();
        }
        try { await manager.RegisterPackageAsync(new Uri(manifest), null, DeploymentOptions.DevelopmentMode).AsTask(); }
        catch (Exception error) when (unchecked((uint)error.HResult) == 0x80073CFF)
        { throw new InvalidOperationException(T("Enable Windows Developer Mode under Settings > System > Advanced > For developers, then save again."), error); }
        return T("Widget provider installed. Press Win+W, choose Add widgets, then pin MeetingBar.");
    }

    private static string? FindManifest()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "WinWWidget", "AppxManifest.xml")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WidgetProvider", "bin", "Release", "publish-folder", "AppxManifest.xml"))
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
