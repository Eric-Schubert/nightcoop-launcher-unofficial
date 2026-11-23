using System.Text.Json;

namespace NightCoopLauncher.Core.Settings;

public sealed record LauncherSettings
{
    /// <summary>Language code of the interface; null means the default language, English.</summary>
    public string? Language { get; init; }

    /// <summary>Game folder chosen by hand; null means the detected installation.</summary>
    public string? GamePath { get; init; }

    /// <summary>Where save backups go; null means the launcher's own data folder.</summary>
    public string? BackupDirectory { get; init; }

    public bool AutoInstallUpdates { get; init; }

    public bool BackupSavesBeforeUpdate { get; init; } = true;

    // Off by default: a long playthrough has hundreds of megabytes of saves, and copying
    // them delays every start of the game.
    public bool BackupSavesBeforeLaunch { get; init; }

    public int AutomaticBackupsToKeep { get; init; } = 10;

    /// <summary>
    /// A version installed deliberately through the version history. Automatic updates
    /// leave it alone until the user updates by hand again.
    /// </summary>
    public string? HeldVersion { get; init; }
}

public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public LauncherSettings Load()
    {
        if (!File.Exists(filePath))
        {
            return new LauncherSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(filePath), Options) ?? new LauncherSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, Options));
    }
}
