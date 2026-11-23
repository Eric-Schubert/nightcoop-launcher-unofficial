using System.IO.Compression;

namespace NightCoopLauncher.Core.Install;

public static class ArchiveDeployer
{
    private const string ModFolderPrefix = "NightCoop";

    /// <summary>
    /// Unpacks the archive into a staging folder first, so a broken archive fails before
    /// the game folder is touched, then copies the files over the game.
    /// </summary>
    /// <returns>The installed files, relative to the game folder.</returns>
    public static IReadOnlyList<string> Deploy(
        string archivePath,
        string stagingDirectory,
        string gameRoot,
        IReadOnlyList<string> previouslyInstalledFiles)
    {
        ArgumentNullException.ThrowIfNull(previouslyInstalledFiles);

        var files = ExtractSafely(archivePath, stagingDirectory);
        if (files.Count == 0)
        {
            throw new LauncherException("The downloaded archive is empty.");
        }

        foreach (var relativePath in files)
        {
            var target = PathGuard.ResolveInside(gameRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(stagingDirectory, relativePath), target, overwrite: true);
        }

        RemoveStaleFiles(gameRoot, previouslyInstalledFiles, files);
        return files;
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

    private static void RemoveStaleFiles(string gameRoot, IReadOnlyList<string> previousFiles, List<string> currentFiles)
    {
        var current = currentFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in previousFiles.Where(path => !current.Contains(path) && IsInsideModFolder(path)))
        {
            string target;
            try
            {
                target = PathGuard.ResolveInside(gameRoot, relativePath);
            }
            catch (LauncherException)
            {
                continue;
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    // The archive also carries RED4ext, which other mods load too: only files inside
    // NightCoop's own folders are ever removed.
    private static bool IsInsideModFolder(string relativePath) =>
        relativePath.Split('\\', '/').Any(segment => segment.StartsWith(ModFolderPrefix, StringComparison.OrdinalIgnoreCase));
}
