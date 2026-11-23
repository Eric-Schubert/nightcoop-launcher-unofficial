using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace NightCoopLauncher.App.Services;

public sealed class WindowsUserPrompts : IUserPrompts
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
            == MessageBoxResult.OK;

    public string? PickGameFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose the Cyberpunk 2077 folder" };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public void OpenInShell(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
