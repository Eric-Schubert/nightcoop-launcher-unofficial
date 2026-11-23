using System.Security.Cryptography;

namespace NightCoopLauncher.Core.Install;

public enum InstallHealth
{
    NotInstalled,

    /// <summary>The mod is there, but nothing recorded what its files should look like.</summary>
    Unknown,
    Intact,
    Damaged,
}

public static class InstallVerifier
{
    /// <summary>Compares the installed files with the hashes recorded when they were installed.</summary>
    public static InstallHealth Check(string gameRoot, InstallState? state)
    {
        if (!InstallState.IsModPresent(gameRoot))
        {
            return state is null ? InstallHealth.NotInstalled : InstallHealth.Damaged;
        }

        if (state?.FileHashes is not { Count: > 0 } hashes)
        {
            return InstallHealth.Unknown;
        }

        return hashes.All(entry => HasHash(gameRoot, entry.Key, entry.Value)) ? InstallHealth.Intact : InstallHealth.Damaged;
    }

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool HasHash(string gameRoot, string relativePath, string expected)
    {
        try
        {
            var path = PathGuard.ResolveInside(gameRoot, relativePath);
            return File.Exists(path) && string.Equals(HashFile(path), expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is LauncherException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
