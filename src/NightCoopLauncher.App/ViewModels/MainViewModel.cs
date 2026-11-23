using System.Windows.Input;
using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Mvvm;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.Core.Community;

namespace NightCoopLauncher.App.ViewModels;

public enum Page
{
    Play,
    Updates,
    Backups,
    Settings,
}

/// <summary>The window around the pages: navigation, the Discord card and the error line.</summary>
public sealed class MainViewModel : PageViewModel
{
    private Page _currentPage = Page.Play;

    public MainViewModel(LauncherController controller, Localizer localizer, IUserPrompts prompts)
        : base(controller, localizer)
    {
        ArgumentNullException.ThrowIfNull(prompts);

        Play = new PlayViewModel(controller, localizer, prompts, page => CurrentPage = page);
        Updates = new UpdatesViewModel(controller, localizer, prompts);
        Backups = new BackupsViewModel(controller, localizer, prompts);
        Settings = new SettingsViewModel(controller, localizer, prompts);

        JoinDiscordCommand = new RelayCommand(() => prompts.OpenInShell(DiscordClient.InviteUri.ToString()));
        DismissErrorCommand = new RelayCommand(controller.DismissError);
        controller.GameLaunched += (_, _) => GameLaunched?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after the game was started, so the window can get out of the way.</summary>
    public event EventHandler? GameLaunched;

    public PlayViewModel Play { get; }

    public UpdatesViewModel Updates { get; }

    public BackupsViewModel Backups { get; }

    public SettingsViewModel Settings { get; }

    public ICommand JoinDiscordCommand { get; }

    public ICommand DismissErrorCommand { get; }

    public Page CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsPlayPage));
                OnPropertyChanged(nameof(IsUpdatesPage));
                OnPropertyChanged(nameof(IsBackupsPage));
                OnPropertyChanged(nameof(IsSettingsPage));
            }
        }
    }

    public bool IsPlayPage { get => CurrentPage == Page.Play; set => Select(Page.Play, value); }

    public bool IsUpdatesPage { get => CurrentPage == Page.Updates; set => Select(Page.Updates, value); }

    public bool IsBackupsPage { get => CurrentPage == Page.Backups; set => Select(Page.Backups, value); }

    public bool IsSettingsPage { get => CurrentPage == Page.Settings; set => Select(Page.Settings, value); }

    public bool HasUpdate => Controller.IsUpdateAvailable;

    public bool HasDiscord => Controller.Discord is not null;

    public string DiscordName => Controller.Discord?.Name ?? string.Empty;

    public string DiscordOnline => Controller.Discord?.OnlineCount.ToString("N0", Localizer.Culture) ?? string.Empty;

    public bool HasError => Controller.Error is not null;

    public string ErrorText => Controller.Error is { } error
        ? Localizer.Format("error." + error.Error, [.. error.Arguments])
        : string.Empty;

    public string Footer
    {
        get
        {
            var launcher = T("shell.footer.launcher", SettingsViewModel.LauncherVersion);
            return Controller.Installed is { } installed
                ? $"{launcher} · {T("shell.footer.mod", Formatting.Version(installed.Version))}"
                : launcher;
        }
    }

    public Task InitializeAsync() => Controller.InitializeAsync();

    protected override void Refresh()
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(HasDiscord));
        OnPropertyChanged(nameof(DiscordName));
        OnPropertyChanged(nameof(DiscordOnline));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(Footer));
    }

    // A radio button reports both the page it leaves and the one it enters; only the latter counts.
    private void Select(Page page, bool isSelected)
    {
        if (isSelected)
        {
            CurrentPage = page;
        }
    }
}
