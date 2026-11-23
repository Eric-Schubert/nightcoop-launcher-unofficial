using System.IO.Compression;

namespace NightCoopLauncher.Core.Install;

public sealed record DeployedFile(string RelativePath, string Sha256);

public static class ArchiveDeployer
{
    private const string ModFolderPrefix = "NightCoop";

    /// <summary>
    /// Unpacks the archive into a staging folder first, so a broken archive fails before
    /// the game folder is touched, then copies the files over the game. If copying fails
    /// halfway, the files already replaced are put back.
    /// </summary>
    /// <param name="archivePath">The verified archive.</param>
    /// <param name="workDirectory">Scratch space for staging and for the files being replaced.</param>
    /// <param name="gameRoot">The game folder.</param>
    /// <param name="previouslyInstalledFiles">Files of the build being replaced, relative to the game folder.</param>
    public static IReadOnlyList<DeployedFile> Deploy(
        string archivePath,
        string workDirectory,
        string gameRoot,
        IReadOnlyList<string> previouslyInstalledFiles)
    {
        ArgumentNullException.ThrowIfNull(previouslyInstalledFiles);

        var staging = Path.Combine(workDirectory, "staging");
        var files = ExtractSafely(archivePath, staging);
        if (files.Count == 0)
        {
            throw new LauncherException(LauncherError.ArchiveEmpty);
        }

        var replaced = new List<ReplacedFile>();
        try
        {
            foreach (var relativePath in files)
            {
                var target = PathGuard.ResolveInside(gameRoot, relativePath);
                replaced.Add(SetAside(target, Path.Combine(workDirectory, "replaced", relativePath)));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(staging, relativePath), target, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PutBack(replaced);
            throw new LauncherException(LauncherError.GameFolderNotWritable, exception, exception.Message);
        }

        RemoveStaleFiles(gameRoot, previouslyInstalledFiles, files);
        return files
            .Select(relativePath => new DeployedFile(relativePath, InstallVerifier.HashFile(Path.Combine(staging, relativePath))))
            .ToList();
    }

    internal static List<string> ExtractSafely(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        var files = new List<string>();

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var target = PathGuard.ResolveInside(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            files.Add(PathGuard.ToRelative(destination, target));
        }

        return files;
    }

    private static ReplacedFile SetAside(string target, string backupPath)
    {
        if (!File.Exists(target))
        {
            return new ReplacedFile(target, null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(target, backupPath, overwrite: true);
        return new ReplacedFile(target, backupPath);
    }

    private static void PutBack(List<ReplacedFile> replaced)
    {
        foreach (var file in Enumerable.Reverse(replaced))
        {
            try
            {
                if (file.BackupPath is null)
                {
                    File.Delete(file.Target);
                }
                else
                {
                    File.Copy(file.BackupPath, file.Target, overwrite: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The file that broke the installation is usually the one that cannot be
                // restored either; the remaining files are still worth putting back.
            }
        }
    }

    private static void RemoveStaleFiles(string gameRoot, IReadOnlyList<string> previousFiles, List<string> currentFiles)
    {
        var current = currentFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in previousFiles.Where(path => !current.Contains(path) && IsInsideModFolder(path)))
        {
            try
            {
                File.Delete(PathGuard.ResolveInside(gameRoot, relativePath));
            }
            catch (Exception exception) when (exception is LauncherException or IOException or UnauthorizedAccessException)
            {
                // A leftover file of an older build does not break the new one.
            }
        }
    }

    // The archive also carries RED4ext, which other mods load too: only files inside
    // NightCoop's own folders are ever removed.
    private static bool IsInsideModFolder(string relativePath) =>
        relativePath.Split('\\', '/').Any(segment => segment.StartsWith(ModFolderPrefix, StringComparison.OrdinalIgnoreCase));

    private sealed record ReplacedFile(string Target, string? BackupPath);
}
