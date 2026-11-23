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
            throw new LauncherException(LauncherError.NoAvailableBuild);
        }

        return available.FirstOrDefault(release => release.Version == manifest.Latest) ?? available[0];
    }

    public static bool Supports(ModRelease release, string gameVersion)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.GameVersions.Contains(gameVersion);
    }

    public static ModFile SelectFile(ModRelease release, string gameVersion)
    {
        if (!Supports(release, gameVersion))
        {
            throw new LauncherException(
                LauncherError.GameVersionUnsupported, release.Version, string.Join(", ", release.GameVersions), gameVersion);
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

        throw new LauncherException(LauncherError.NoArchiveForGameVersion, release.Version, gameVersion);
    }
}
