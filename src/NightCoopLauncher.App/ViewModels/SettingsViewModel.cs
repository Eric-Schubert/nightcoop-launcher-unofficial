using System.Reflection;
using System.Windows.Input;
using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Mvvm;
using NightCoopLauncher.App.Services;

namespace NightCoopLauncher.App.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly IUserPrompts _prompts;

    public SettingsViewModel(LauncherController controller, Localizer localizer, IUserPrompts prompts)
        : base(controller, localizer)
    {
        _prompts = prompts;

        ChangeGameFolderCommand = new RelayCommand(ChangeGameFolderAsync, () => IsIdle);
        ChangeBackupLocationCommand = new RelayCommand(ChangeBackupLocation, () => IsIdle);
    }

    public static string LauncherVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public ICommand ChangeGameFolderCommand { get; }

    public ICommand ChangeBackupLocationCommand { get; }

    public IReadOnlyList<LanguageInfo> Languages => Localizer.Languages;

    public LanguageInfo? SelectedLanguage
    {
        get => Languages.FirstOrDefault(language => language.Code == Localizer.LanguageCode);
        set
        {
            if (value is null || value.Code == Localizer.LanguageCode)
            {
                return;
            }

            Controller.ChangeSettings(settings => settings with { Language = value.Code });
            Localizer.SetLanguage(value.Code);
        }
    }

    public string GamePath => Controller.Game?.Path ?? T("settings.game.none");

    public string GameSummary => Controller.Game is { } game
        ? $"{Formatting.Store(game.Store, Localizer)} · {Controller.GameVersion}"
        : "—";

    public string BackupLocation => Controller.BackupDirectory;

    public string VersionText => T("settings.about.version", LauncherVersion);

    protected override void Refresh()
    {
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(GameSummary));
        OnPropertyChanged(nameof(BackupLocation));
        OnPropertyChanged(nameof(VersionText));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ChangeGameFolderAsync()
    {
        if (_prompts.PickFolder(T("settings.game.pick")) is { } folder)
        {
            await Controller.SetGameFolderAsync(folder);
        }
    }

    private void ChangeBackupLocation()
    {
        if (_prompts.PickFolder(T("backups.location.pick")) is { } folder)
        {
            Controller.ChangeSettings(settings => settings with { BackupDirectory = folder });
        }
    }
}
