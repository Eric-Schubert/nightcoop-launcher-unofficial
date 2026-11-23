using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using NightCoopLauncher.Core.Install;

namespace NightCoopLauncher.Core.Saves;

public enum BackupKind
{
    Manual,
    BeforeUpdate,
    BeforeLaunch,
    BeforeRestore,
}

/// <param name="Path">The backup archive.</param>
/// <param name="CreatedAt">Local time the backup was made.</param>
/// <param name="Kind">Why it was made.</param>
/// <param name="Label">For a backup before an update: the mod version being installed.</param>
/// <param name="SizeBytes">Size of the archive.</param>
public sealed record SaveBackup(string Path, DateTime CreatedAt, BackupKind Kind, string? Label, long SizeBytes);

public sealed partial class SaveBackupService(string savesDirectory, string backupDirectory, TimeProvider timeProvider)
{
    private const string TimestampFormat = "yyyyMMdd-HHmmss";
    private const string RestoringSuffix = ".restoring";
    private const string ReplacedSuffix = ".replaced";

    private static readonly Dictionary<BackupKind, string> KindTags = new()
    {
        [BackupKind.Manual] = "manual",
        [BackupKind.BeforeUpdate] = "update",
        [BackupKind.BeforeLaunch] = "launch",
        [BackupKind.BeforeRestore] = "prerestore",
    };

    public string BackupDirectory { get; set; } = backupDirectory;

    public bool HasSaves => Directory.Exists(savesDirectory) && Directory.EnumerateFileSystemEntries(savesDirectory).Any();

    /// <summary>Kinds that pile up on their own and are therefore thinned out; the rest is kept until deleted by hand.</summary>
    public static bool IsPrunable(BackupKind kind) => kind is BackupKind.BeforeLaunch or BackupKind.BeforeRestore;

    public IReadOnlyList<SaveBackup> List()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(BackupDirectory, "saves-*.zip")
            .Select(ParseBackup)
            .OfType<SaveBackup>()
            .OrderByDescending(backup => backup.CreatedAt)
            .ToList();
    }

    public Task<SaveBackup> CreateAsync(BackupKind kind, string? label, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Create(kind, label, progress, cancellationToken), cancellationToken);

    /// <summary>
    /// Replaces the current saves with a backup. The saves being replaced are backed up
    /// first, so a restore of the wrong backup can itself be undone.
    /// </summary>
    public Task RestoreAsync(SaveBackup backup, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return Task.Run(() => Restore(backup, progress, cancellationToken), cancellationToken);
    }

    public void Prune(int automaticBackupsToKeep)
    {
        foreach (var backup in List().Where(backup => IsPrunable(backup.Kind)).Skip(Math.Max(0, automaticBackupsToKeep)))
        {
            File.Delete(backup.Path);
        }
    }

    public static void Delete(SaveBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        File.Delete(backup.Path);
    }

    public static void Export(SaveBackup backup, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(backup);
        File.Copy(backup.Path, destinationPath, overwrite: true);
    }

    private SaveBackup Create(BackupKind kind, string? label, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!HasSaves)
        {
            throw new LauncherException(LauncherError.NoSaves, savesDirectory);
        }

        Directory.CreateDirectory(BackupDirectory);
        var createdAt = timeProvider.GetLocalNow().DateTime;
        var suffix = string.IsNullOrWhiteSpace(label) ? string.Empty : "-" + SanitizeLabel(label);
        var name = $"saves-{createdAt.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{KindTags[kind]}{suffix}.zip";
        var target = Path.Combine(BackupDirectory, name);

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

        return ParseBackup(target)!;
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
            throw new LauncherException(LauncherError.BackupMissing);
        }

        if (HasSaves)
        {
            Create(BackupKind.BeforeRestore, null, new HalvedProgress(progress), cancellationToken);
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

    private static string SanitizeLabel(string label) =>
        string.Concat(label.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' ? character : '_'));

    private static SaveBackup? ParseBackup(string path)
    {
        var match = BackupFileName().Match(Path.GetFileName(path));
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups[1].Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var createdAt))
        {
            return null;
        }

        var kind = KindTags.First(pair => pair.Value == match.Groups[2].Value).Key;
        var label = match.Groups[3].Success ? match.Groups[3].Value : null;
        return new SaveBackup(path, createdAt, kind, label, new FileInfo(path).Length);
    }

    [GeneratedRegex(@"^saves-(\d{8}-\d{6})-(manual|update|launch|prerestore)(?:-(.+))?\.zip$")]
    private static partial Regex BackupFileName();

    /// <summary>The safety backup is the first half of a restore.</summary>
    private sealed class HalvedProgress(IProgress<double>? inner) : IProgress<double>
    {
        public void Report(double value) => inner?.Report(value / 2);
    }
}
