using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Mvvm;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.Core.Saves;

namespace NightCoopLauncher.App.ViewModels;

public enum BackupFilter
{
    All,
    Automatic,
    Manual,
    BeforeUpdate,
}

public sealed record BackupFilterOption(BackupFilter Filter, string Text);

public sealed record RetentionOption(int Count, string Text);

public sealed record BackupItemViewModel(SaveBackup Backup, string Title, string Meta, string KindText, bool IsUpdateKind, string SizeText);

public sealed class BackupsViewModel : PageViewModel
{
    private static readonly int[] RetentionChoices = [3, 5, 10, 20];

    private readonly IUserPrompts _prompts;

    private string _summary = string.Empty;
    private string _progressText = string.Empty;
    private double _progressPercent;
    private bool _showProgress;
    private BackupFilter _filter = BackupFilter.All;
    private BackupItemViewModel? _selected;
    private IReadOnlyList<BackupFilterOption> _filters = [];
    private IReadOnlyList<RetentionOption> _retentionOptions = [];

    public BackupsViewModel(LauncherController controller, Localizer localizer, IUserPrompts prompts)
        : base(controller, localizer)
    {
        _prompts = prompts;

        CreateCommand = new RelayCommand(Controller.CreateBackupAsync, () => IsIdle);
        RestoreCommand = new RelayCommand(RestoreAsync, () => IsIdle && Selected is not null);
        ExportCommand = new RelayCommand(ExportAsync, () => IsIdle && Selected is not null);
        DeleteCommand = new RelayCommand(DeleteAsync, () => IsIdle && Selected is not null);
        ChangeLocationCommand = new RelayCommand(ChangeLocation, () => IsIdle);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        Refresh();
    }

    public ICommand CreateCommand { get; }

    public ICommand RestoreCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand ChangeLocationCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ObservableCollection<BackupItemViewModel> Items { get; } = [];

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public string Location => Controller.BackupDirectory;

    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }

    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }

    public bool ShowProgress { get => _showProgress; private set => SetProperty(ref _showProgress, value); }

    public IReadOnlyList<BackupFilterOption> Filters { get => _filters; private set => SetProperty(ref _filters, value); }

    public BackupFilterOption? SelectedFilter
    {
        get => Filters.FirstOrDefault(option => option.Filter == _filter);
        set
        {
            // The list control clears its selection while its items are being replaced.
            if (value is null || value.Filter == _filter)
            {
                return;
            }

            _filter = value.Filter;
            RefreshItems();
        }
    }

    public BackupItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(HasNoSelection));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool HasNoSelection => Selected is null;

    public bool IsEmpty => Items.Count == 0;

    public bool BackupSavesBeforeLaunch
    {
        get => Controller.Settings.BackupSavesBeforeLaunch;
        set => Controller.ChangeSettings(settings => settings with { BackupSavesBeforeLaunch = value });
    }

    public bool BackupSavesBeforeUpdate
    {
        get => Controller.Settings.BackupSavesBeforeUpdate;
        set => Controller.ChangeSettings(settings => settings with { BackupSavesBeforeUpdate = value });
    }

    public IReadOnlyList<RetentionOption> RetentionOptions
    {
        get => _retentionOptions;
        private set => SetProperty(ref _retentionOptions, value);
    }

    public RetentionOption? SelectedRetention
    {
        get => RetentionOptions.FirstOrDefault(option => option.Count == Controller.Settings.AutomaticBackupsToKeep);
        set
        {
            if (value is not null && value.Count != Controller.Settings.AutomaticBackupsToKeep)
            {
                Controller.ChangeSettings(settings => settings with { AutomaticBackupsToKeep = value.Count });
            }
        }
    }

    protected override void Refresh()
    {
        Filters = [.. Enum.GetValues<BackupFilter>().Select(filter => new BackupFilterOption(filter, T("backups.filter." + filter)))];
        OnPropertyChanged(nameof(SelectedFilter));

        RetentionOptions = [.. RetentionChoices
            .Append(Controller.Settings.AutomaticBackupsToKeep)
            .Distinct()
            .Order()
            .Select(count => new RetentionOption(count, count.ToString(Localizer.Culture)))];
        OnPropertyChanged(nameof(SelectedRetention));

        var totalBytes = Controller.Backups.Sum(backup => backup.SizeBytes);
        Summary = Controller.Backups.Count == 0
            ? T("backups.summary.none")
            : T("backups.summary", Formatting.Size(totalBytes, Localizer), Controller.Backups.Count);

        RefreshItems();
        RefreshProgress();
        OnPropertyChanged(nameof(Location));
        OnPropertyChanged(nameof(BackupSavesBeforeLaunch));
        OnPropertyChanged(nameof(BackupSavesBeforeUpdate));
        CommandManager.InvalidateRequerySuggested();
    }

    protected override void RefreshProgress()
    {
        ShowProgress = Controller.IsBusy && !Controller.IsUpdating && !Controller.IsUpdatingLauncher && Controller.Progress.Fraction > 0;
        ProgressPercent = Controller.Progress.Fraction * 100;
        ProgressText = T("backups.progress", ProgressPercent.ToString("0", Localizer.Culture));
    }

    private void RefreshItems()
    {
        var selectedPath = Selected?.Backup.Path;

        Items.Clear();
        foreach (var backup in Controller.Backups.Where(MatchesFilter))
        {
            Items.Add(ToItem(backup));
        }

        Selected = Items.FirstOrDefault(item => item.Backup.Path == selectedPath) ?? Items.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool MatchesFilter(SaveBackup backup) => _filter switch
    {
        BackupFilter.Automatic => SaveBackupService.IsPrunable(backup.Kind),
        BackupFilter.Manual => backup.Kind == BackupKind.Manual,
        BackupFilter.BeforeUpdate => backup.Kind == BackupKind.BeforeUpdate,
        _ => true,
    };

    private BackupItemViewModel ToItem(SaveBackup backup)
    {
        var title = backup is { Kind: BackupKind.BeforeUpdate, Label: not null }
            ? T("backups.title.BeforeUpdateTo", Formatting.Version(backup.Label))
            : T("backups.title." + backup.Kind);

        return new BackupItemViewModel(
            backup,
            title,
            $"{Formatting.When(backup.CreatedAt, Localizer)} · {T("backups.content.saves")}",
            T("backups.chip." + backup.Kind),
            backup.Kind == BackupKind.BeforeUpdate,
            Formatting.Size(backup.SizeBytes, Localizer));
    }

    private async Task RestoreAsync()
    {
        if (Selected is { } item && _prompts.Confirm(T("backups.restore.title"), T("backups.restore.message", item.Meta)))
        {
            await Controller.RestoreBackupAsync(item.Backup);
        }
    }

    private async Task ExportAsync()
    {
        if (Selected is { } item
            && _prompts.PickSaveLocation(T("backups.export.title"), Path.GetFileName(item.Backup.Path)) is { } destination)
        {
            await Controller.ExportBackupAsync(item.Backup, destination);
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is { } item && _prompts.Confirm(T("backups.delete.title"), T("backups.delete.message", item.Title, item.Meta)))
        {
            await Controller.DeleteBackupAsync(item.Backup);
        }
    }

    private void ChangeLocation()
    {
        if (_prompts.PickFolder(T("backups.location.pick")) is { } folder)
        {
            Controller.ChangeSettings(settings => settings with { BackupDirectory = folder });
        }
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(Controller.BackupDirectory);
        _prompts.OpenInShell(Controller.BackupDirectory);
    }
}
