using System.Windows.Input;
using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Mvvm;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.Core.Install;

namespace NightCoopLauncher.App.ViewModels;

public sealed class PlayViewModel : PageViewModel
{
    private const string PlayGlyph = "";
    private const string DownloadGlyph = "";
    private const string FolderGlyph = "";

    private enum PrimaryAction
    {
        None,
        LocateGame,
        Update,
        Play,
    }

    private readonly IUserPrompts _prompts;
    private PrimaryAction _primaryAction;

    private string _statusText = string.Empty;
    private string _statusKind = "Neutral";
    private string _modChip = string.Empty;
    private string _gameChip = string.Empty;
    private string _primaryText = string.Empty;
    private string _primaryGlyph = PlayGlyph;
    private string _hint = string.Empty;
    private string _progressText = string.Empty;
    private double _progressPercent;
    private bool _showProgress;
    private bool _showUpdateBanner;
    private string _bannerTitle = string.Empty;
    private string _lastBackupTitle = string.Empty;
    private string _lastBackupDetail = string.Empty;
    private string _healthTitle = string.Empty;
    private string _healthKind = "Neutral";
    private string _healthDetail = string.Empty;
    private bool _canRepair;
    private string _protocolTitle = string.Empty;

    public PlayViewModel(LauncherController controller, Localizer localizer, IUserPrompts prompts, Action<Page> navigate)
        : base(controller, localizer)
    {
        _prompts = prompts;

        PrimaryCommand = new RelayCommand(RunPrimaryActionAsync, () => IsIdle && _primaryAction != PrimaryAction.None);
        ShowUpdatesCommand = new RelayCommand(() => navigate(Page.Updates));
        ShowBackupsCommand = new RelayCommand(() => navigate(Page.Backups));
        VerifyCommand = new RelayCommand(Controller.VerifyAsync, () => IsIdle && Controller.Game is not null);
        RepairCommand = new RelayCommand(Controller.RepairAsync, () => IsIdle && Controller.Manifest is not null);
        Refresh();
    }

    public ICommand PrimaryCommand { get; }

    public ICommand ShowUpdatesCommand { get; }

    public ICommand ShowBackupsCommand { get; }

    public ICommand VerifyCommand { get; }

    public ICommand RepairCommand { get; }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Neutral, Ok, Attention or Error: picks the colour of the status chip.</summary>
    public string StatusKind { get => _statusKind; private set => SetProperty(ref _statusKind, value); }

    public string ModChip { get => _modChip; private set => SetProperty(ref _modChip, value); }

    public string GameChip { get => _gameChip; private set => SetProperty(ref _gameChip, value); }

    public string PrimaryText { get => _primaryText; private set => SetProperty(ref _primaryText, value); }

    public string PrimaryGlyph { get => _primaryGlyph; private set => SetProperty(ref _primaryGlyph, value); }

    public string Hint { get => _hint; private set => SetProperty(ref _hint, value); }

    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }

    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }

    public bool ShowProgress { get => _showProgress; private set => SetProperty(ref _showProgress, value); }

    public bool ShowUpdateBanner { get => _showUpdateBanner; private set => SetProperty(ref _showUpdateBanner, value); }

    public string BannerTitle { get => _bannerTitle; private set => SetProperty(ref _bannerTitle, value); }

    public string LastBackupTitle { get => _lastBackupTitle; private set => SetProperty(ref _lastBackupTitle, value); }

    public string LastBackupDetail { get => _lastBackupDetail; private set => SetProperty(ref _lastBackupDetail, value); }

    public string HealthTitle { get => _healthTitle; private set => SetProperty(ref _healthTitle, value); }

    public string HealthKind { get => _healthKind; private set => SetProperty(ref _healthKind, value); }

    public string HealthDetail { get => _healthDetail; private set => SetProperty(ref _healthDetail, value); }

    public bool CanRepair { get => _canRepair; private set => SetProperty(ref _canRepair, value); }

    public string ProtocolTitle { get => _protocolTitle; private set => SetProperty(ref _protocolTitle, value); }

    protected override void Refresh()
    {
        RefreshStatus();
        RefreshCards();
        RefreshProgress();
        CommandManager.InvalidateRequerySuggested();
    }

    protected override void RefreshProgress()
    {
        var progress = Controller.Progress;
        ShowProgress = Controller.IsBusy && !Controller.IsUpdatingLauncher && (Controller.IsUpdating || progress.Fraction > 0);
        ProgressPercent = progress.Fraction * 100;
        ProgressText = T("step." + progress.Step);
    }

    private void RefreshStatus()
    {
        var game = Controller.Game;
        var latest = Controller.Latest;

        ModChip = Controller.Installed is { } state
            ? T("play.chip.mod", Formatting.Version(state.Version))
            : T(Controller.IsModPresent ? "play.chip.modUnknown" : "play.chip.modMissing");
        GameChip = game is null
            ? T("play.chip.gameMissing")
            : T(latest is null || Controller.LatestFile is not null ? "play.chip.game" : "play.chip.gameIncompatible", Controller.GameVersion);
        ShowUpdateBanner = Controller.IsUpdateAvailable && Controller.IsModPresent;
        BannerTitle = latest is null ? string.Empty : T("play.banner.title", Formatting.Version(latest.Version));

        if (Controller.IsBusy && Controller.Manifest is null && Controller.Error is null && !Controller.IsModPresent)
        {
            SetStatus("Neutral", "play.status.checking", PrimaryAction.None, "play.button.play", PlayGlyph, "play.hint.checking");
        }
        else if (game is null)
        {
            SetStatus("Error", "play.status.noGame", PrimaryAction.LocateGame, "play.button.locate", FolderGlyph, "play.hint.noGame");
        }
        else if (Controller.IsUpdateAvailable)
        {
            var installed = Controller.IsModPresent;
            SetStatus(
                "Attention",
                installed ? "play.status.update" : "play.status.notInstalled",
                PrimaryAction.Update,
                installed ? "play.button.update" : "play.button.install",
                DownloadGlyph,
                Controller.Settings.BackupSavesBeforeUpdate ? "play.hint.updateWithBackup" : "play.hint.update");
        }
        else if (Controller.IsModPresent)
        {
            SetStatus(
                Controller.IsUpToDate ? "Ok" : "Error",
                Controller.IsUpToDate ? "play.status.ready" : "play.status.checkFailed",
                PrimaryAction.Play,
                "play.button.play",
                PlayGlyph,
                Controller.Settings.BackupSavesBeforeLaunch ? "play.hint.playWithBackup" : "play.hint.play");
        }
        else
        {
            SetStatus("Error", "play.status.checkFailed", PrimaryAction.None, "play.button.play", PlayGlyph, "play.hint.notInstalled");
        }
    }

    private void SetStatus(string kind, string statusKey, PrimaryAction action, string buttonKey, string glyph, string hintKey)
    {
        StatusKind = kind;
        StatusText = T(statusKey);
        _primaryAction = action;
        PrimaryText = T(buttonKey);
        PrimaryGlyph = glyph;
        Hint = T(hintKey);
    }

    private void RefreshCards()
    {
        if (Controller.Backups.Count > 0)
        {
            var newest = Controller.Backups[0];
            LastBackupTitle = Formatting.When(newest.CreatedAt, Localizer);
            LastBackupDetail = $"{T("backup.kind." + newest.Kind)} · {Formatting.Size(newest.SizeBytes, Localizer)}";
        }
        else
        {
            LastBackupTitle = T("play.card.backup.none");
            LastBackupDetail = T("play.card.backup.noneDetail");
        }

        HealthTitle = T("health." + Controller.Health);
        HealthKind = Controller.Health switch
        {
            InstallHealth.Intact => "Ok",
            InstallHealth.Damaged => "Error",
            _ => "Neutral",
        };
        HealthDetail = T("health." + Controller.Health + ".detail");
        CanRepair = Controller.Health is InstallHealth.Damaged or InstallHealth.Unknown;

        var protocol = Controller.Installed?.Protocol ?? Controller.Latest?.Protocol;
        ProtocolTitle = protocol?.ToString(Localizer.Culture) ?? "—";
    }

    private async Task RunPrimaryActionAsync()
    {
        switch (_primaryAction)
        {
            case PrimaryAction.LocateGame:
                if (_prompts.PickFolder(T("settings.game.pick")) is { } folder)
                {
                    await Controller.SetGameFolderAsync(folder);
                }

                break;
            case PrimaryAction.Update:
                await Controller.UpdateToLatestAsync();
                break;
            case PrimaryAction.Play:
                await Controller.PlayAsync();
                break;
        }
    }
}
