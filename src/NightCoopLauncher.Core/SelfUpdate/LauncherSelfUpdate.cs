namespace NightCoopLauncher.Core.SelfUpdate;

/// <summary>Offers and installs newer versions of the launcher that is running.</summary>
/// <param name="client">Access to the launcher's releases.</param>
/// <param name="currentVersion">Version of the running launcher.</param>
/// <param name="executablePath">Path of the running launcher's executable, if known.</param>
public sealed class LauncherSelfUpdate(LauncherReleaseClient client, Version currentVersion, string? executablePath)
{
    /// <returns>The release to update to, or null when there is none or this build cannot replace itself.</returns>
    public async Task<LauncherRelease?> FindUpdateAsync(CancellationToken cancellationToken)
    {
        if (!SelfUpdater.IsSupported(executablePath))
        {
            return null;
        }

        var latest = await client.FetchLatestAsync(cancellationToken).ConfigureAwait(false);
        return latest is not null && latest.Version > currentVersion ? latest : null;
    }

    /// <summary>Downloads the release and puts it in place of the running executable.</summary>
    /// <returns>The executable to start instead of the running one.</returns>
    public async Task<string> InstallAsync(LauncherRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!SelfUpdater.IsSupported(executablePath))
        {
            throw new LauncherException(LauncherError.LauncherNotReplaceable, executablePath);
        }

        var downloaded = SelfUpdater.DownloadPathFor(executablePath!);
        try
        {
            await client.DownloadAsync(release, downloaded, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new LauncherException(LauncherError.LauncherNotReplaceable, exception, Path.GetDirectoryName(executablePath));
        }

        SelfUpdater.Swap(executablePath!, downloaded);
        return executablePath!;
    }
}
