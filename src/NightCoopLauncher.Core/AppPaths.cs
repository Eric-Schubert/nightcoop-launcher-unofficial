namespace NightCoopLauncher.Core;

public static class AppPaths
{
    /// <summary>
    /// Names a folder to keep settings and backups in instead of the default one. It lets
    /// a second launcher run next to the regular one without sharing its settings.
    /// </summary>
    public const string DataDirectoryVariable = "NIGHTCOOP_LAUNCHER_DATA";

    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightCoopLauncher");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    // Kept out of Documents on purpose: that folder is often synced to OneDrive, and a
    // backup of a long playthrough runs to several hundred megabytes.
    public static string SaveBackupDirectory { get; } = Path.Combine(DataDirectory, "SaveBackups");

    public static string GameSavesDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Saved Games", "CD Projekt Red", "Cyberpunk 2077");
}
