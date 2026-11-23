namespace NightCoopLauncher.Core.Releases;

public static class ReleaseSelector
{
    /// <summary>
    /// Same rule as the download page: the build named "latest" unless it was withdrawn,
    /// otherwise the first build in the list that is still offered.
    /// </summary>
    public static ModRelease SelectLatest(ReleaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var available = manifest.Releases.Where(release => !release.Withdrawn).ToList();
        if (available.Count == 0)
        {
            throw new LauncherException("The release list contains no available build.");
        }

        return available.FirstOrDefault(release => release.Version == manifest.Latest) ?? available[0];
    }

    public static ModFile SelectFile(ModRelease release, string gameVersion)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (!release.GameVersions.Contains(gameVersion))
        {
            throw new LauncherException(
                $"NightCoop {release.Version} supports game versions {string.Join(", ", release.GameVersions)}; installed is {gameVersion}.");
        }

        var matching = release.ModFiles.Where(file => file.GameVersions.Contains(gameVersion)).ToList();
        if (matching.Count == 1)
        {
            return matching[0];
        }

        // Early builds shipped one archive for every supported game version.
        var universal = release.ModFiles.Where(file => file.GameVersions.Count == 0).ToList();
        if (matching.Count == 0 && universal.Count == 1)
        {
            return universal[0];
        }

        throw new LauncherException($"NightCoop {release.Version} has no unambiguous archive for game version {gameVersion}.");
    }
}
