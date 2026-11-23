using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using NightCoopLauncher.Core.Install;

namespace NightCoopLauncher.Core.Saves;

public enum BackupKind
{
    Manual,
    BeforeUpdate,
    BeforeRestore,
}

public sealed record SaveBackup(string Path, DateTime CreatedAt, BackupKind Kind, long SizeBytes);

public sealed partial class SaveBackupService(string savesDirectory, string backupDirectory, TimeProvider timeProvider)
{
    private const string TimestampFormat = "yyyyMMdd-HHmmss";
    private const string RestoringSuffix = ".restoring";
    private const string ReplacedSuffix = ".replaced";

    private static readonly Dictionary<BackupKind, string> KindTags = new()
    {
        [BackupKind.Manual] = "manual",
        [BackupKind.BeforeUpdate] = "auto",
        [BackupKind.BeforeRestore] = "prerestore",
    };

    public string BackupDirectory => backupDirectory;

    public bool HasSaves => Directory.Exists(savesDirectory) && Directory.EnumerateFileSystemEntries(savesDirectory).Any();

    public IReadOnlyList<SaveBackup> List()
    {
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(backupDirectory, "saves-*.zip")
            .Select(ParseBackup)
            .OfType<SaveBackup>()
            .OrderByDescending(backup => backup.CreatedAt)
            .ToList();
    }

    public Task<SaveBackup> CreateAsync(BackupKind kind, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Create(kind, progress, cancellationToken), cancellationToken);

    /// <summary>
    /// Replaces the current saves with a backup. The saves being replaced are backed up
    /// first, so a restore of the wrong backup can itself be undone.
    /// </summary>
    public Task RestoreAsync(SaveBackup backup, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return Task.Run(() => Restore(backup, progress, cancellationToken), cancellationToken);
    }

    /// <summary>Deletes the oldest automatic backups. Manual ones are never removed.</summary>
    public void Prune(int automaticBackupsToKeep)
    {
        foreach (var backup in List().Where(backup => backup.Kind != BackupKind.Manual).Skip(Math.Max(0, automaticBackupsToKeep)))
        {
            File.Delete(backup.Path);
        }
    }

    private SaveBackup Create(BackupKind kind, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!HasSaves)
        {
            throw new LauncherException($"There are no saves in '{savesDirectory}'.");
        }

        Directory.CreateDirectory(backupDirectory);
        var createdAt = timeProvider.GetLocalNow().DateTime;
        var name = $"saves-{createdAt.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{KindTags[kind]}.zip";
        var target = Path.Combine(backupDirectory, name);

        // Written under a temporary name: an interrupted backup must never be listed as one.
        var temporary = target + ".tmp";
        try
        {
            WriteArchive(temporary, progress, cancellationToken);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }

        return new SaveBackup(target, createdAt, kind, new FileInfo(target).Length);
    }

    private void WriteArchive(string archivePath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var files = new DirectoryInfo(savesDirectory).GetFiles("*", SearchOption.AllDirectories);
        var totalBytes = Math.Max(1, files.Sum(file => file.Length));
        long doneBytes = 0;

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryName = Path.GetRelativePath(savesDirectory, file.FullName).Replace('\\', '/');
            // Saves are already compressed by the game, so a stronger level only costs time.
            var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
            entry.LastWriteTime = file.LastWriteTime;

            using (var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var destination = entry.Open())
            {
                source.CopyTo(destination);
            }

            doneBytes += file.Length;
            progress?.Report((double)doneBytes / totalBytes);
        }
    }

    private void Restore(SaveBackup backup, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(backup.Path))
        {
            throw new LauncherException("The backup file no longer exists.");
        }

        if (HasSaves)
        {
            Create(BackupKind.BeforeRestore, new Progress<double>(value => progress?.Report(value / 2)), cancellationToken);
        }

        var incoming = savesDirectory + RestoringSuffix;
        var outgoing = savesDirectory + ReplacedSuffix;
        DeleteDirectory(incoming);
        DeleteDirectory(outgoing);

        // Unpacked next to the saves and swapped in by rename: a failure halfway through
        // leaves the current saves untouched.
        ArchiveDeployer.ExtractSafely(backup.Path, incoming);
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(savesDirectory))
        {
            Directory.Move(savesDirectory, outgoing);
        }

        Directory.Move(incoming, savesDirectory);
        DeleteDirectory(outgoing);
        progress?.Report(1);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static SaveBackup? ParseBackup(string path)
    {
        var match = BackupFileName().Match(Path.GetFileName(path));
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups[1].Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt))
        {
            return null;
        }

        var kind = KindTags.First(pair => pair.Value == match.Groups[2].Value).Key;
        return new SaveBackup(path, createdAt, kind, new FileInfo(path).Length);
    }

    [GeneratedRegex(@"^saves-(\d{8}-\d{6})-(manual|auto|prerestore)\.zip$")]
    private static partial Regex BackupFileName();
}
