using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using NightCoopLauncher.App.Mvvm;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.Core;
using NightCoopLauncher.Core.Game;
using NightCoopLauncher.Core.Install;
using NightCoopLauncher.Core.Releases;
using NightCoopLauncher.Core.Saves;
using NightCoopLauncher.Core.Settings;

namespace NightCoopLauncher.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string Unknown = "—";

    private enum PrimaryAction
    {
        None,
        ChooseGameFolder,
        Update,
        Play,
    }

    private readonly ReleaseManifestClient _releases;
    private readonly ModInstaller _installer;
    private readonly SaveBackupService _backups;
    private readonly SettingsStore _settingsStore;
    private readonly IUserPrompts _prompts;

    private LauncherSettings _settings;
    private GameInstall? _game;
    private string? _gameVersion;
    private InstallState? _state;
    private ModRelease? _release;
    private ModFile? _file;
    private PrimaryAction _primaryAction;

    private string _statusTitle = "Checking for updates";
    private string _statusDetail = string.Empty;
    private string _statusKind = "Neutral";
    private string _installedVersion = Unknown;
    private string _latestVersion = Unknown;
    private string _protocol = Unknown;
    private string _gameSummary = Unknown;
    private string _gamePath = Unknown;
    private string _releaseTitle = "Release notes";
    private string _releaseDate = string.Empty;
    private string _primaryText = "CHECKING";
    private string _progressText = string.Empty;
    private string? _errorText;
    private double _progressPercent;
    private bool _isBusy;
    private bool _showProgress;

    public MainViewModel(
        ReleaseManifestClient releases,
        ModInstaller installer,
        SaveBackupService backups,
        SettingsStore settingsStore,
        IUserPrompts prompts)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);

        _releases = releases;
        _installer = installer;
        _backups = backups;
        _settingsStore = settingsStore;
        _prompts = prompts;
        _settings = settingsStore.Load();

        PrimaryCommand = new RelayCommand(RunPrimaryActionAsync, () => !IsBusy && _primaryAction != PrimaryAction.None);
        RefreshCommand = new RelayCommand(RefreshAsync, () => !IsBusy);
        BackupNowCommand = new RelayCommand(BackupNowAsync, () => !IsBusy);
        RestoreCommand = new RelayCommand(parameter => RestoreAsync(parameter as BackupItemViewModel), () => !IsBusy);
        ChooseGameFolderCommand = new RelayCommand(ChooseGameFolderAsync, () => !IsBusy);
        OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
        OpenDownloadPageCommand = new RelayCommand(() => _prompts.OpenInShell(ReleaseManifestClient.DownloadPageUri.ToString()));
    }

    /// <summary>Raised after the game was started, so the window can get out of the way.</summary>
    public event EventHandler? GameLaunched;

    public ICommand PrimaryCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand BackupNowCommand { get; }

    public ICommand RestoreCommand { get; }

    public ICommand ChooseGameFolderCommand { get; }

    public ICommand OpenBackupFolderCommand { get; }

    public ICommand OpenDownloadPageCommand { get; }

    public ObservableCollection<string> Notes { get; } = [];

    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];

    public string StatusTitle { get => _statusTitle; private set => SetProperty(ref _statusTitle, value); }

    public string StatusDetail { get => _statusDetail; private set => SetProperty(ref _statusDetail, value); }

    /// <summary>Neutral, Ok, Attention or Error: picks the colour of the status marker.</summary>
    public string StatusKind { get => _statusKind; private set => SetProperty(ref _statusKind, value); }

    public string InstalledVersion { get => _installedVersion; private set => SetProperty(ref _installedVersion, value); }

    public string LatestVersion { get => _latestVersion; private set => SetProperty(ref _latestVersion, value); }

    public string Protocol { get => _protocol; private set => SetProperty(ref _protocol, value); }

    public string GameSummary { get => _gameSummary; private set => SetProperty(ref _gameSummary, value); }

    public string GamePath { get => _gamePath; private set => SetProperty(ref _gamePath, value); }

    public string ReleaseTitle { get => _releaseTitle; private set => SetProperty(ref _releaseTitle, value); }

    public string ReleaseDate { get => _releaseDate; private set => SetProperty(ref _releaseDate, value); }

    public string PrimaryText { get => _primaryText; private set => SetProperty(ref _primaryText, value); }

    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }

    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }

    public bool ShowProgress { get => _showProgress; private set => SetProperty(ref _showProgress, value); }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool HasNotes => Notes.Count > 0;

    public bool HasNoNotes => Notes.Count == 0;

    public bool HasBackups => Backups.Count > 0;

    public bool HasNoBackups => Backups.Count == 0;

    public string BackupFolder => _backups.BackupDirectory;

    public bool BackupSavesBeforeUpdate
    {
        get => _settings.BackupSavesBeforeUpdate;
        set
        {
            if (_settings.BackupSavesBeforeUpdate == value)
            {
                return;
            }

            _settings = _settings with { BackupSavesBeforeUpdate = value };
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public Task RefreshAsync() => RunBusyAsync(async () =>
    {
        _release = null;
        _file = null;
        LoadGame();

        var manifest = await _releases.FetchAsync(CancellationToken.None);
        _release = ReleaseSelector.SelectLatest(manifest);
        _file = ReleaseSelector.SelectFile(_release, _gameVersion!);
    });

    private void LoadGame()
    {
        _game = null;
        _state = null;
        _game = GameLocator.Resolve(_settings.GamePath);
        _gameVersion = GameProcess.ReadVersion(_game);
        _state = InstallStateStore.Load(_game.Path);
    }

    private Task RunPrimaryActionAsync() => _primaryAction switch
    {
        PrimaryAction.ChooseGameFolder => ChooseGameFolderAsync(),
        PrimaryAction.Update => UpdateAsync(),
        PrimaryAction.Play => PlayAsync(),
        _ => Task.CompletedTask,
    };

    private async Task UpdateAsync() =>
        await RunBusyAsync(async () =>
        {
            EnsureGameIsClosed("updating");

            if (BackupSavesBeforeUpdate && _backups.HasSaves)
            {
                await _backups.CreateAsync(BackupKind.BeforeUpdate, ReportFraction("Backing up saves"), CancellationToken.None);
                _backups.Prune(_settings.AutomaticBackupsToKeep);
            }

            var download = new Progress<TransferProgress>(progress => SetProgress(
                progress.BytesTotal > 0 ? (double)progress.BytesDone / progress.BytesTotal : 0,
                $"Downloading · {Formatting.Megabytes(progress.BytesDone)} of {Formatting.Megabytes(progress.BytesTotal)}"));
            _state = await _installer.InstallAsync(_game!, _gameVersion!, _release!, _file!, _state, download, CancellationToken.None);
        });

    private async Task PlayAsync()
    {
        var launched = await RunBusyAsync(() =>
        {
            try
            {
                GameProcess.Launch(_game!);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new LauncherException($"The game could not be started: {exception.Message}", exception);
            }

            return Task.CompletedTask;
        });

        if (launched)
        {
            GameLaunched?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task BackupNowAsync() => await RunBusyAsync(
        () => _backups.CreateAsync(BackupKind.Manual, ReportFraction("Backing up saves"), CancellationToken.None));

    private async Task RestoreAsync(BackupItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = _prompts.Confirm(
            "Restore saves",
            $"Replace your current saves with the backup from {item.CreatedText}?\n\nYour current saves are backed up first, so this can be undone.");
        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            EnsureGameIsClosed("restoring saves");
            await _backups.RestoreAsync(item.Backup, ReportFraction("Restoring saves"), CancellationToken.None);
            _backups.Prune(_settings.AutomaticBackupsToKeep);
        });
    }

    private async Task ChooseGameFolderAsync()
    {
        var folder = _prompts.PickGameFolder();
        if (folder is null)
        {
            return;
        }

        if (!GameLocator.IsGameFolder(folder))
        {
            ErrorText = $"No Cyberpunk 2077 found in '{folder}'. Choose the folder that contains 'bin' and 'archive'.";
            return;
        }

        _settings = _settings with { GamePath = folder };
        _settingsStore.Save(_settings);
        await RefreshAsync();
    }

    private void OpenBackupFolder()
    {
        Directory.CreateDirectory(_backups.BackupDirectory);
        _prompts.OpenInShell(_backups.BackupDirectory);
    }

    private static void EnsureGameIsClosed(string activity)
    {
        if (GameProcess.IsRunning())
        {
            throw new LauncherException($"Cyberpunk 2077 is running. Close the game before {activity}.");
        }
    }

    private Progress<double> ReportFraction(string label) =>
        new(fraction => SetProgress(fraction, $"{label} · {fraction.ToString("P0", CultureInfo.InvariantCulture)}"));

    private void SetProgress(double fraction, string text)
    {
        // Progress callbacks are queued and can arrive after the work has finished.
        if (!IsBusy)
        {
            return;
        }

        ShowProgress = true;
        ProgressPercent = Math.Clamp(fraction, 0, 1) * 100;
        ProgressText = text;
    }

    private async Task<bool> RunBusyAsync(Func<Task> work)
    {
        IsBusy = true;
        ErrorText = null;
        try
        {
            await work();
            return true;
        }
        catch (LauncherException exception)
        {
            ErrorText = exception.Message;
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorText = $"Something went wrong: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
            ShowProgress = false;
            UpdatePresentation();
            RefreshBackups();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void UpdatePresentation()
    {
        var modPresent = _game is not null && InstallState.IsModPresent(_game.Path);

        GamePath = _game?.Path ?? "Not found";
        GameSummary = _game is null ? Unknown : $"{StoreName(_game.Store)} · {_gameVersion}";
        InstalledVersion = _state?.Version ?? (modPresent ? "Unknown build" : "Not installed");
        LatestVersion = _release?.Version ?? Unknown;
        Protocol = _release?.Protocol?.ToString(CultureInfo.InvariantCulture) ?? Unknown;
        UpdateReleaseNotes();

        if (_game is null)
        {
            SetStatus("Error", "Game not found", "Point the launcher at your Cyberpunk 2077 folder.", PrimaryAction.ChooseGameFolder, "LOCATE GAME");
        }
        else if (_release is null || _file is null)
        {
            SetStatus(
                "Error",
                "Update check failed",
                modPresent ? "You can still play with the build you have." : "NightCoop is not installed yet.",
                modPresent ? PrimaryAction.Play : PrimaryAction.None,
                modPresent ? "PLAY" : "UNAVAILABLE");
        }
        else if (InstallState.IsUpToDate(_state, _release, _file, _game.Path))
        {
            SetStatus("Ok", "Up to date", $"NightCoop {_release.Version} is installed.", PrimaryAction.Play, "PLAY");
        }
        else
        {
            SetStatus(
                "Attention",
                modPresent ? "Update available" : "Ready to install",
                $"NightCoop {_release.Version} for game version {_gameVersion}, {Formatting.Megabytes(_file.Size)}.",
                PrimaryAction.Update,
                modPresent ? "UPDATE" : "INSTALL");
        }
    }

    private void SetStatus(string kind, string title, string detail, PrimaryAction action, string primaryText)
    {
        StatusKind = kind;
        StatusTitle = title;
        StatusDetail = detail;
        _primaryAction = action;
        PrimaryText = primaryText;
    }

    private void UpdateReleaseNotes()
    {
        ReleaseTitle = _release?.Title ?? (_release is null ? "Release notes" : $"NightCoop {_release.Version}");
        ReleaseDate = DateTime.TryParseExact(_release?.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            : string.Empty;

        Notes.Clear();
        foreach (var note in _release?.Notes ?? [])
        {
            Notes.Add(note);
        }

        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasNoNotes));
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var backup in _backups.List())
        {
            Backups.Add(new BackupItemViewModel(backup, RestoreCommand));
        }

        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(HasNoBackups));
    }

    private static string StoreName(GameStore store) => store switch
    {
        GameStore.Gog => "GOG",
        GameStore.Custom => "Custom folder",
        _ => store.ToString(),
    };
}
