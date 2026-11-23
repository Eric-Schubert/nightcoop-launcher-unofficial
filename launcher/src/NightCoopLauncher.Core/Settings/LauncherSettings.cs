using System.Text.Json;

namespace NightCoopLauncher.Core.Settings;

public sealed record LauncherSettings
{
    public string? GamePath { get; init; }

    public bool BackupSavesBeforeUpdate { get; init; } = true;

    public int AutomaticBackupsToKeep { get; init; } = 3;
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
