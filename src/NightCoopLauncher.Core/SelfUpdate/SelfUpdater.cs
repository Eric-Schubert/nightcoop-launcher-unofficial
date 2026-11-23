namespace NightCoopLauncher.Core.SelfUpdate;

/// <summary>Replaces the running launcher executable with a newer one.</summary>
public static class SelfUpdater
{
    private const string DownloadSuffix = ".new";
    private const string PreviousSuffix = ".old";

    /// <summary>
    /// Only the published single-file launcher can replace itself. A development build
    /// consists of many files, and swapping its executable alone would break it.
    /// </summary>
    public static bool IsSupported(string? executablePath) =>
        executablePath is not null
        && string.Equals(Path.GetFileName(executablePath), LauncherReleaseClient.ExecutableName, StringComparison.OrdinalIgnoreCase)
        && !File.Exists(Path.ChangeExtension(executablePath, ".dll"));

    public static string DownloadPathFor(string executablePath) => executablePath + DownloadSuffix;

    /// <summary>
    /// Puts the downloaded executable in place of the running one. Windows does not allow
    /// overwriting a running executable, but it does allow renaming it, so the current
    /// one is moved aside and removed on the next start.
    /// </summary>
    public static void Swap(string executablePath, string downloadedPath)
    {
        var previous = executablePath + PreviousSuffix;
        try
        {
            File.Delete(previous);
            File.Move(executablePath, previous);
            try
            {
                File.Move(downloadedPath, executablePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                File.Move(previous, executablePath);
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new LauncherException(LauncherError.LauncherNotReplaceable, exception, Path.GetDirectoryName(executablePath));
        }
    }

    /// <summary>Removes what a previous update left behind.</summary>
    public static void CleanUp(string? executablePath)
    {
        if (executablePath is null)
        {
            return;
        }

        foreach (var leftover in new[] { executablePath + PreviousSuffix, executablePath + DownloadSuffix })
        {
            try
            {
                File.Delete(leftover);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The previous version may still be shutting down; the next start tries again.
            }
        }
    }
}
