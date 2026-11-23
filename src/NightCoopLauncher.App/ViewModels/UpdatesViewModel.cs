using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Mvvm;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.Core.Releases;

namespace NightCoopLauncher.App.ViewModels;

public sealed class UpdateStepViewModel(UpdateStep step, int number) : ObservableObject
{
    private string _stateText = string.Empty;
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private string _state = "Pending";

    public UpdateStep Step { get; } = step;

    public string Number { get; } = number.ToString("00", CultureInfo.InvariantCulture);

    public string StateText { get => _stateText; set => SetProperty(ref _stateText, value); }

    public string Title { get => _title; set => SetProperty(ref _title, value); }

    public string Detail { get => _detail; set => SetProperty(ref _detail, value); }

    /// <summary>Pending, Active or Done.</summary>
    public string State { get => _state; set => SetProperty(ref _state, value); }
}

public sealed record HistoryItemViewModel(
    string Version,
    string Status,
    bool IsActive,
    string ActiveText,
    bool CanInstall,
    string ActionText,
    ICommand InstallCommand,
    ModRelease Release);

public sealed class UpdatesViewModel : PageViewModel
{
    private static readonly UpdateStep[] StepOrder =
        [UpdateStep.Backup, UpdateStep.Download, UpdateStep.Verify, UpdateStep.Install, UpdateStep.Done];

    private readonly IUserPrompts _prompts;
    private readonly RelayCommand _installVersionCommand;

    private string _subtitle = string.Empty;
    private string _cardLabel = string.Empty;
    private string _cardKind = "Neutral";
    private string _fromVersion = string.Empty;
    private string _toVersion = string.Empty;
    private bool _showArrow;
    private string _meta = string.Empty;
    private bool _showUpdateButton;
    private string _updateButtonText = string.Empty;
    private bool _showSteps;
    private string _progressLabel = string.Empty;
    private string _progressPercentText = string.Empty;
    private double _progressPercent;
    private string _notesTitle = string.Empty;

    public UpdatesViewModel(LauncherController controller, Localizer localizer, IUserPrompts prompts)
        : base(controller, localizer)
    {
        _prompts = prompts;

        CheckCommand = new RelayCommand(Controller.RefreshAsync, () => IsIdle);
        UpdateCommand = new RelayCommand(Controller.UpdateToLatestAsync, () => IsIdle && Controller.IsUpdateAvailable);
        OpenPatchNotesCommand = new RelayCommand(() => _prompts.OpenInShell(ReleaseManifestClient.DownloadPageUri.ToString()));
        _installVersionCommand = new RelayCommand(parameter => InstallVersionAsync(parameter as HistoryItemViewModel), () => IsIdle);

        Steps = [.. StepOrder.Select((step, index) => new UpdateStepViewModel(step, index + 1))];
        Refresh();
    }

    public ICommand CheckCommand { get; }

    public ICommand UpdateCommand { get; }

    public ICommand OpenPatchNotesCommand { get; }

    public IReadOnlyList<UpdateStepViewModel> Steps { get; }

    public ObservableCollection<string> Notes { get; } = [];

    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }

    public string CardLabel { get => _cardLabel; private set => SetProperty(ref _cardLabel, value); }

    /// <summary>Neutral, Ok, Attention or Error: picks the colour of the card's frame and label.</summary>
    public string CardKind { get => _cardKind; private set => SetProperty(ref _cardKind, value); }

    public string FromVersion { get => _fromVersion; private set => SetProperty(ref _fromVersion, value); }

    public string ToVersion { get => _toVersion; private set => SetProperty(ref _toVersion, value); }

    public bool ShowArrow { get => _showArrow; private set => SetProperty(ref _showArrow, value); }

    public string Meta { get => _meta; private set => SetProperty(ref _meta, value); }

    public bool ShowUpdateButton { get => _showUpdateButton; private set => SetProperty(ref _showUpdateButton, value); }

    public string UpdateButtonText { get => _updateButtonText; private set => SetProperty(ref _updateButtonText, value); }

    public bool ShowSteps { get => _showSteps; private set => SetProperty(ref _showSteps, value); }

    public string ProgressLabel { get => _progressLabel; private set => SetProperty(ref _progressLabel, value); }

    public string ProgressPercentText { get => _progressPercentText; private set => SetProperty(ref _progressPercentText, value); }

    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }

    public string NotesTitle { get => _notesTitle; private set => SetProperty(ref _notesTitle, value); }

    public bool HasNotes => Notes.Count > 0;

    public bool HasNoNotes => Notes.Count == 0;

    public bool HasHistory => History.Count > 0;

    public bool AutoInstallUpdates
    {
        get => Controller.Settings.AutoInstallUpdates;
        set => Controller.ChangeSettings(settings => settings with { AutoInstallUpdates = value });
    }

    public bool BackupSavesBeforeUpdate
    {
        get => Controller.Settings.BackupSavesBeforeUpdate;
        set => Controller.ChangeSettings(settings => settings with { BackupSavesBeforeUpdate = value });
    }

    protected override void Refresh()
    {
        RefreshHeader();
        RefreshCard();
        RefreshNotes();
        RefreshHistory();
        RefreshProgress();
        OnPropertyChanged(nameof(AutoInstallUpdates));
        OnPropertyChanged(nameof(BackupSavesBeforeUpdate));
        CommandManager.InvalidateRequerySuggested();
    }

    protected override void RefreshProgress()
    {
        var progress = Controller.Progress;
        var activeIndex = Controller.IsUpdating ? Math.Max(0, Array.IndexOf(StepOrder, progress.Step)) : -1;
        // The four working steps share the bar equally; "done" is its right end.
        var overall = activeIndex < 0 ? 0 : (activeIndex + progress.Fraction) / (StepOrder.Length - 1);

        ShowSteps = Controller.IsUpdating || (Controller.IsUpdateAvailable && IsIdle);
        ProgressPercent = Math.Min(1, overall) * 100;
        ProgressPercentText = ProgressPercent.ToString("0", Localizer.Culture) + " %";
        ProgressLabel = activeIndex < 0 ? T("updates.progress.ready") : T("step." + StepOrder[activeIndex]);

        for (var index = 0; index < Steps.Count; index++)
        {
            var step = Steps[index];
            var skipped = step.Step == UpdateStep.Backup && !Controller.Settings.BackupSavesBeforeUpdate;
            step.State = activeIndex < 0 || index > activeIndex ? "Pending" : index == activeIndex ? "Active" : "Done";
            step.StateText = T(skipped ? "updates.step.state.Skipped" : "updates.step.state." + step.State);
            step.Title = T($"updates.step.{step.Step}.title");
            step.Detail = T($"updates.step.{step.Step}.detail");
        }
    }

    private void RefreshHeader()
    {
        var game = Controller.GameVersion is null || Controller.Game is null
            ? T("play.chip.gameMissing")
            : T("updates.subtitle.game", Controller.GameVersion);
        Subtitle = Controller.LastCheckedAt is { } checkedAt
            ? $"{T("updates.subtitle.checked", checkedAt.ToString("HH:mm", Localizer.Culture))} · {game}"
            : game;
    }

    private void RefreshCard()
    {
        var latest = Controller.Latest;
        var installed = Controller.Installed?.Version;
        var installedText = installed is null ? T("updates.version.unknown") : Formatting.Version(installed);

        ShowUpdateButton = Controller.IsUpdateAvailable && !Controller.IsUpdating;
        UpdateButtonText = T(Controller.IsModPresent ? "updates.button.update" : "updates.button.install");

        if (latest is null || Controller.LatestFile is not { } file)
        {
            CardKind = "Error";
            CardLabel = T("updates.card.failed");
            FromVersion = Controller.IsModPresent ? installedText : T("updates.version.none");
            ToVersion = string.Empty;
            ShowArrow = false;
            Meta = T("updates.card.failed.detail");
        }
        else if (Controller.IsUpToDate)
        {
            CardKind = "Ok";
            CardLabel = T("updates.card.current");
            FromVersion = string.Empty;
            ToVersion = Formatting.Version(latest.Version);
            ShowArrow = false;
            Meta = FormatMeta(latest, file);
        }
        else
        {
            CardKind = "Attention";
            CardLabel = T(Controller.IsModPresent ? "updates.card.available" : "updates.card.notInstalled");
            FromVersion = Controller.IsModPresent ? installedText : string.Empty;
            ToVersion = Formatting.Version(latest.Version);
            ShowArrow = Controller.IsModPresent;
            Meta = FormatMeta(latest, file);
        }
    }

    private string FormatMeta(ModRelease release, ModFile file) => string.Join(
        "     ",
        Formatting.Size(file.Size, Localizer),
        T("updates.meta.compatible", string.Join(T("updates.meta.and"), release.GameVersions)),
        T("updates.meta.checksum"));

    private void RefreshNotes()
    {
        var latest = Controller.Latest;
        NotesTitle = latest is null ? T("updates.notes.titleEmpty") : T("updates.notes.title", Formatting.Version(latest.Version));

        Notes.Clear();
        foreach (var note in latest?.Notes ?? [])
        {
            Notes.Add(note);
        }

        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasNoNotes));
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var release in Controller.Manifest?.Releases ?? [])
        {
            History.Add(ToHistoryItem(release));
        }

        OnPropertyChanged(nameof(HasHistory));
    }

    private HistoryItemViewModel ToHistoryItem(ModRelease release)
    {
        var isActive = Controller.IsModPresent && Controller.Installed?.Version == release.Version;
        var supported = Controller.GameVersion is { } gameVersion && ReleaseSelector.Supports(release, gameVersion);
        var isLatest = release.Version == Controller.Latest?.Version;

        var details = new List<string>();
        if (DateTime.TryParseExact(release.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            details.Add(date.ToString("d", Localizer.Culture));
        }

        if (release.Protocol is { } protocol)
        {
            details.Add(T("updates.history.protocol", protocol));
        }

        if (release.Withdrawn)
        {
            details.Add(T("updates.history.withdrawn"));
        }
        else if (!supported)
        {
            details.Add(T("updates.history.incompatible"));
        }

        return new HistoryItemViewModel(
            Formatting.Version(release.Version),
            string.Join(" · ", details),
            isActive,
            T("updates.history.active"),
            // The latest build is installed with the main button, not from the history.
            !isActive && !isLatest && supported && !release.Withdrawn,
            T("updates.history.rollback"),
            _installVersionCommand,
            release);
    }

    private async Task InstallVersionAsync(HistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (_prompts.Confirm(T("updates.rollback.title"), T("updates.rollback.message", item.Version)))
        {
            await Controller.InstallVersionAsync(item.Release);
        }
    }
}
