using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Mvvm;

namespace NightCoopLauncher.App.ViewModels;

/// <summary>
/// A page shows the controller's state as text. It rebuilds that text whenever the state
/// or the language changes.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(LauncherController controller, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(localizer);

        Controller = controller;
        Localizer = localizer;
        controller.Changed += (_, _) => Refresh();
        controller.ProgressChanged += (_, _) => RefreshProgress();
        localizer.LanguageChanged += (_, _) => Refresh();
    }

    protected LauncherController Controller { get; }

    protected Localizer Localizer { get; }

    protected bool IsIdle => !Controller.IsBusy;

    protected string T(string key) => Localizer[key];

    protected string T(string key, params object?[] arguments) => Localizer.Format(key, arguments);

    protected abstract void Refresh();

    protected virtual void RefreshProgress()
    {
    }
}
