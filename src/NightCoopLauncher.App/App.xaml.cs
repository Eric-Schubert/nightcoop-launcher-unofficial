using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.App.ViewModels;
using NightCoopLauncher.Core;
using NightCoopLauncher.Core.Community;
using NightCoopLauncher.Core.Install;
using NightCoopLauncher.Core.Releases;
using NightCoopLauncher.Core.Saves;
using NightCoopLauncher.Core.SelfUpdate;
using NightCoopLauncher.Core.Settings;

namespace NightCoopLauncher.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SelfUpdater.CleanUp(Environment.ProcessPath);

        // One client for the lifetime of the process; it is released with it.
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NightCoopLauncher/1.0");

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var localizer = Localizer.Current;
        var controller = new LauncherController(
            new ReleaseManifestClient(http),
            new ModInstaller(http),
            new SaveBackupService(AppPaths.GameSavesDirectory, AppPaths.SaveBackupDirectory, TimeProvider.System),
            new DiscordClient(http),
            new LauncherSelfUpdate(new LauncherReleaseClient(http), version, Environment.ProcessPath),
            new SettingsStore(AppPaths.SettingsFile),
            () => localizer.LanguageCode);
        localizer.SetLanguage(controller.Settings.Language);
        controller.RestartRequested += (_, executable) => RestartInto(executable);

        new MainWindow(new MainViewModel(controller, localizer, new WindowsUserPrompts())).Show();
    }

    private void RestartInto(string executable)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        Shutdown();
    }
}
