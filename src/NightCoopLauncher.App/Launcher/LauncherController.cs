using System.IO;
using NightCoopLauncher.Core;
using NightCoopLauncher.Core.Community;
using NightCoopLauncher.Core.Game;
using NightCoopLauncher.Core.Install;
using NightCoopLauncher.Core.Releases;
using NightCoopLauncher.Core.Saves;
using NightCoopLauncher.Core.Settings;

namespace NightCoopLauncher.App.Launcher;

public enum UpdateStep
{
    None,
    Backup,
    Download,
    Verify,
    Install,
    Done,
}

/// <param name="Step">The update step that is running; None for work outside an update.</param>
/// <param name="Fraction">Progress of that step, from 0 to 1.</param>
public readonly record struct OperationProgress(UpdateStep Step, double Fraction);

/// <summary>
/// Holds what the launcher knows about the game, the mod and the backups, and runs the
/// operations on them one at a time. The pages only translate this state into texts.
/// </summary>
public sealed class LauncherController(
    ReleaseManifestClient releases,
    ModInstaller installer,
    SaveBackupService backups,
    DiscordClient discord,
    SettingsStore settingsStore,
    Func<string> language)
{
    /// <summary>Raised after anything observable changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised while an operation advances; much more often than <see cref="Changed"/>.</summary>
    public event EventHandler? ProgressChanged;

    public event EventHandler? GameLaunched;

    public LauncherSettings Settings { get; private set; } = settingsStore.Load();

    public GameInstall? Game { get; private set; }

    public string? GameVersion { get; private set; }

    public InstallState? Installed { get; private set; }

    public InstallHealth Health { get; private set; }

    public ReleaseManifest? Manifest { get; private set; }

    public ModRelease? Latest { get; private set; }

    public ModFile? LatestFile { get; private set; }

    public DateTime? LastCheckedAt { get; private set; }

    public IReadOnlyList<SaveBackup> Backups { get; private set; } = [];

    public DiscordServer? Discord { get; private set; }

    public LauncherException? Error { get; private set; }

    public bool IsBusy { get; private set; }

    public bool IsUpdating { get; private set; }

    public OperationProgress Progress { get; private set; }

    public string BackupDirectory => backups.BackupDirectory;

    public bool IsModPresent => Game is not null && InstallState.IsModPresent(Game.Path);

    public bool IsUpToDate =>
        Game is not null && Latest is not null && LatestFile is not null && InstallState.Matches(Installed, Latest, LatestFile, Game.Path);

    public bool IsUpdateAvailable => Game is not null && Latest is not null && LatestFile is not null && !IsUpToDate;

    public async Task InitializeAsync()
    {
        ApplyBackupDirectory();

        var discordTask = discord.FetchAsync(CancellationToken.None);
        await RunAsync(CheckAsync);

        if (ShouldAutoInstall())
        {
            await UpdateToLatestAsync();
        }

        Discord = await discordTask;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task RefreshAsync() => RunAsync(CheckAsync);

    public Task UpdateToLatestAsync() => RunAsync(async () =>
    {
        await InstallAsync(Latest!, LatestFile!);
        SaveSettings(Settings with { HeldVersion = null });
    });

    /// <summary>Installs a build picked from the version history and keeps automatic updates away from it.</summary>
    public Task InstallVersionAsync(ModRelease release) => RunAsync(async () =>
    {
        await InstallAsync(release, ReleaseSelector.SelectFile(release, GameVersion!));
        SaveSettings(Settings with { HeldVersion = release.Version == Latest?.Version ? null : release.Version });
    });

    /// <summary>Reinstalls the build that is installed, or the latest one if that build is no longer offered.</summary>
    public Task RepairAsync() => RunAsync(async () =>
    {
        var release = Manifest?.Releases.FirstOrDefault(candidate => candidate.Version == Installed?.Version && !candidate.Withdrawn) ?? Latest;
        if (release is null)
        {
            throw new LauncherException(LauncherError.NoAvailableBuild);
        }

        await InstallAsync(release, ReleaseSelector.SelectFile(release, GameVersion!));
    });

    public Task VerifyAsync() => RunAsync(CheckHealthAsync);

    public Task PlayAsync() => RunAsync(async () =>
    {
        if (Settings.BackupSavesBeforeLaunch && backups.HasSaves && !GameProcess.IsRunning())
        {
            await backups.CreateAsync(BackupKind.BeforeLaunch, null, ReportStep(UpdateStep.None), CancellationToken.None);
            backups.Prune(Settings.AutomaticBackupsToKeep);
        }

        GameProcess.Launch(Game!);
        GameLaunched?.Invoke(this, EventArgs.Empty);
    });

    public Task CreateBackupAsync() => RunAsync(
        () => backups.CreateAsync(BackupKind.Manual, null, ReportStep(UpdateStep.None), CancellationToken.None));

    public Task RestoreBackupAsync(SaveBackup backup) => RunAsync(async () =>
    {
        GameProcess.EnsureNotRunning();
        await backups.RestoreAsync(backup, ReportStep(UpdateStep.None), CancellationToken.None);
        backups.Prune(Settings.AutomaticBackupsToKeep);
    });

    public Task DeleteBackupAsync(SaveBackup backup) => RunAsync(() =>
    {
        SaveBackupService.Delete(backup);
        return Task.CompletedTask;
    });

    public Task ExportBackupAsync(SaveBackup backup, string destinationPath) =>
        RunAsync(() => Task.Run(() => SaveBackupService.Export(backup, destinationPath)));

    public Task SetGameFolderAsync(string path) => RunAsync(async () =>
    {
        if (!GameLocator.IsGameFolder(path))
        {
            throw new LauncherException(LauncherError.NotAGameFolder, path);
        }

        SaveSettings(Settings with { GamePath = path });
        await CheckAsync();
    });

    public void ChangeSettings(Func<LauncherSettings, LauncherSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        SaveSettings(change(Settings));
        ApplyBackupDirectory();
        Backups = backups.List();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DismissError()
    {
        Error = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task CheckAsync()
    {
        Manifest = null;
        Latest = null;
        LatestFile = null;
        Game = null;
        Installed = null;
        Health = InstallHealth.NotInstalled;

        Game = GameLocator.Resolve(Settings.GamePath);
        GameVersion = GameProcess.ReadVersion(Game);
        Installed = InstallStateStore.Load(Game.Path);
        await CheckHealthAsync();

        Manifest = await releases.FetchAsync(language(), CancellationToken.None);
        LastCheckedAt = DateTime.Now;
        Latest = ReleaseSelector.SelectLatest(Manifest);
        LatestFile = ReleaseSelector.SelectFile(Latest, GameVersion);
    }

    private async Task CheckHealthAsync()
    {
        if (Game is { } game)
        {
            var state = Installed;
            Health = await Task.Run(() => InstallVerifier.Check(game.Path, state));
        }
    }

    private async Task InstallAsync(ModRelease release, ModFile file)
    {
        GameProcess.EnsureNotRunning();
        IsUpdating = true;
        try
        {
            if (Settings.BackupSavesBeforeUpdate && backups.HasSaves)
            {
                await backups.CreateAsync(BackupKind.BeforeUpdate, release.Version, ReportStep(UpdateStep.Backup), CancellationToken.None);
            }

            var progress = new Progress<InstallProgress>(report => SetProgress(ToStep(report.Stage), report.Fraction));
            Installed = await installer.InstallAsync(Game!, GameVersion!, release, file, Installed, progress, CancellationToken.None);
            await CheckHealthAsync();
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private bool ShouldAutoInstall() =>
        Settings.AutoInstallUpdates
        && IsUpdateAvailable
        && IsModPresent
        && (Settings.HeldVersion is null || Installed?.Version != Settings.HeldVersion)
        && !GameProcess.IsRunning();

    private static UpdateStep ToStep(InstallStage stage) => stage switch
    {
        InstallStage.Downloading => UpdateStep.Download,
        InstallStage.Verifying => UpdateStep.Verify,
        _ => UpdateStep.Install,
    };

    private Progress<double> ReportStep(UpdateStep step) => new(fraction => SetProgress(step, fraction));

    private void SetProgress(UpdateStep step, double fraction)
    {
        // Progress callbacks are queued and can arrive after the work has finished.
        if (!IsBusy)
        {
            return;
        }

        Progress = new OperationProgress(step, Math.Clamp(fraction, 0, 1));
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveSettings(LauncherSettings settings)
    {
        Settings = settings;
        settingsStore.Save(settings);
    }

    private void ApplyBackupDirectory() =>
        backups.BackupDirectory = string.IsNullOrWhiteSpace(Settings.BackupDirectory) ? AppPaths.SaveBackupDirectory : Settings.BackupDirectory;

    private async Task RunAsync(Func<Task> work)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        Progress = default;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await work();
        }
        catch (LauncherException exception)
        {
            Error = exception;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = new LauncherException(LauncherError.Unexpected, exception, exception.Message);
        }
        finally
        {
            IsBusy = false;
            Progress = default;
            Backups = backups.List();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
