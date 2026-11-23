using System.Net.Http;
using System.Windows;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.App.ViewModels;
using NightCoopLauncher.Core;
using NightCoopLauncher.Core.Install;
using NightCoopLauncher.Core.Releases;
using NightCoopLauncher.Core.Saves;
using NightCoopLauncher.Core.Settings;

namespace NightCoopLauncher.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One client for the lifetime of the process; it is released with it.
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NightCoopLauncher/1.0");

        var viewModel = new MainViewModel(
            new ReleaseManifestClient(http),
            new ModInstaller(http),
            new SaveBackupService(AppPaths.GameSavesDirectory, AppPaths.SaveBackupDirectory, TimeProvider.System),
            new SettingsStore(AppPaths.SettingsFile),
            new WindowsUserPrompts());

        new MainWindow(viewModel).Show();
    }
}
