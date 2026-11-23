using System.Text.Json;
using NightCoopLauncher.Core.Releases;

namespace NightCoopLauncher.Core.Install;

/// <summary>
/// What is installed in a game folder. The file format is shared with Update-NightCoop.ps1,
/// so the launcher and the script can be used on the same installation.
/// </summary>
public sealed record InstallState(
    string Version,
    int? Protocol,
    string GameVersion,
    string ArchiveSha256,
    string InstalledAt,
    IReadOnlyList<string> Files)
{
    public const string PluginRelativePath = @"red4ext\plugins\NightCoop\NightCoop.dll";

    /// <summary>
    /// Compares the archive hash rather than the version alone: after a game patch the
    /// same mod version needs the archive built for the new game version.
    /// </summary>
    public static bool IsUpToDate(InstallState? state, ModRelease release, ModFile file, string gameRoot)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(file);

        return state is not null
            && state.Version == release.Version
            && string.Equals(state.ArchiveSha256, file.Sha256, StringComparison.OrdinalIgnoreCase)
            && IsModPresent(gameRoot);
    }

    public static bool IsModPresent(string gameRoot) => File.Exists(Path.Combine(gameRoot, PluginRelativePath));
}

public static class InstallStateStore
{
    private const string FileName = "NightCoop.updater.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static InstallState? Load(string gameRoot)
    {
        var path = Path.Combine(gameRoot, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(path), Options);
            return state is { Version: not null, ArchiveSha256: not null } ? state with { Files = state.Files ?? [] } : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable state only costs one reinstall, which rewrites it.
            return null;
        }
    }

    public static void Save(string gameRoot, InstallState state) =>
        File.WriteAllText(Path.Combine(gameRoot, FileName), JsonSerializer.Serialize(state, Options));
}
