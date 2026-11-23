using System.Windows.Input;

namespace NightCoopLauncher.App.Mvvm;

public sealed class RelayCommand(Func<object?, Task> execute, Func<bool>? canExecute = null) : ICommand
{
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute)
    {
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => { execute(); return Task.CompletedTask; }, canExecute)
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    // async void is the only shape ICommand allows; the view model handles its own
    // failures, so nothing can escape here.
    public async void Execute(object? parameter) => await execute(parameter);
}
