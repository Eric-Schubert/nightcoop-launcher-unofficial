using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace NightCoopLauncher.App.Services;

public sealed class WindowsUserPrompts : IUserPrompts
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
            == MessageBoxResult.OK;

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public string? PickSaveLocation(string title, string suggestedFileName)
    {
        var dialog = new SaveFileDialog { Title = title, FileName = suggestedFileName, Filter = "ZIP (*.zip)|*.zip" };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public void OpenInShell(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
