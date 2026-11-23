using System.Globalization;
using System.Windows.Input;
using NightCoopLauncher.Core.Saves;

namespace NightCoopLauncher.App.ViewModels;

public sealed class BackupItemViewModel(SaveBackup backup, ICommand restoreCommand)
{
    public SaveBackup Backup { get; } = backup;

    public ICommand RestoreCommand { get; } = restoreCommand;

    public string CreatedText { get; } = backup.CreatedAt.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    public string SizeText { get; } = Formatting.Megabytes(backup.SizeBytes);

    public string KindText { get; } = backup.Kind switch
    {
        BackupKind.BeforeUpdate => "BEFORE UPDATE",
        BackupKind.BeforeRestore => "BEFORE RESTORE",
        _ => "MANUAL",
    };
}
