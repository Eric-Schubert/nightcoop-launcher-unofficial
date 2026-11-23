namespace NightCoopLauncher.Core;

internal static class PathGuard
{
    /// <summary>
    /// Resolves a relative path below <paramref name="root"/> and rejects anything that
    /// would land outside it. Archive entry names and state files are untrusted input.
    /// </summary>
    public static string ResolveInside(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException(LauncherError.UnsafePath, relativePath);
        }

        return target;
    }

    public static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(Path.GetFullPath(root), fullPath);
}
