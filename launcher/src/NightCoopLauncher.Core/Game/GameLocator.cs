using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NightCoopLauncher.Core.Game;

public static partial class GameLocator
{
    private const string SteamAppId = "1091500";
    private const string GogGameId = "1423049311";
    private const string EpicDisplayName = "Cyberpunk 2077";

    public static bool IsGameFolder(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, GameInstall.ExecutableRelativePath));

    public static IReadOnlyList<GameInstall> Detect() => [.. FindSteam(), .. FindGog(), .. FindEpic()];

    /// <summary>Returns the installation to manage: the chosen folder, or the only one detected.</summary>
    public static GameInstall Resolve(string? explicitPath)
    {
        var detected = Detect();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!IsGameFolder(explicitPath))
            {
                throw new LauncherException($"No Cyberpunk 2077 found in '{explicitPath}'.");
            }

            var fullPath = Normalize(explicitPath);
            return detected.FirstOrDefault(install => string.Equals(Normalize(install.Path), fullPath, StringComparison.OrdinalIgnoreCase))
                ?? new GameInstall(GameStore.Custom, fullPath, Path.Combine(fullPath, GameInstall.ExecutableRelativePath));
        }

        return detected.Count switch
        {
            0 => throw new LauncherException("Cyberpunk 2077 was not found in Steam, GOG or Epic. Choose the game folder."),
            1 => detected[0],
            _ => throw new LauncherException("Several Cyberpunk 2077 installations were found. Choose the game folder to use."),
        };
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static IEnumerable<GameInstall> FindSteam()
    {
        var steamRoot = ReadRegistry("InstallPath", @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam");
        if (steamRoot is null)
        {
            yield break;
        }

        var libraries = new List<string> { steamRoot };
        var libraryIndex = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryIndex))
        {
            libraries.AddRange(VdfPathLine().Matches(File.ReadAllText(libraryIndex))
                .Select(match => match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal)));
        }

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var appManifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(appManifest))
            {
                continue;
            }

            var installDirectory = VdfInstallDirLine().Match(File.ReadAllText(appManifest));
            if (!installDirectory.Success)
            {
                continue;
            }

            var path = Path.Combine(library, "steamapps", "common", installDirectory.Groups[1].Value);
            if (IsGameFolder(path))
            {
                yield return new GameInstall(GameStore.Steam, path, $"steam://rungameid/{SteamAppId}");
            }
        }
    }

    private static IEnumerable<GameInstall> FindGog()
    {
        var path = ReadRegistry("path", $@"SOFTWARE\WOW6432Node\GOG.com\Games\{GogGameId}", $@"SOFTWARE\GOG.com\Games\{GogGameId}");
        if (IsGameFolder(path))
        {
            yield return new GameInstall(GameStore.Gog, path!, Path.Combine(path!, GameInstall.ExecutableRelativePath));
        }
    }

    private static IEnumerable<GameInstall> FindEpic()
    {
        var manifestDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(manifestDirectory, "*.item"))
        {
            var install = ReadEpicManifest(file);
            if (install is not null)
            {
                yield return install;
            }
        }
    }

    private static GameInstall? ReadEpicManifest(string file)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            if (GetString(root, "DisplayName") != EpicDisplayName)
            {
                return null;
            }

            var path = GetString(root, "InstallLocation");
            var appName = GetString(root, "AppName");
            return IsGameFolder(path)
                ? new GameInstall(GameStore.Epic, path!, $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true")
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ReadRegistry(string valueName, params string[] localMachineKeys) =>
        localMachineKeys
            .Select(key => Registry.GetValue($@"HKEY_LOCAL_MACHINE\{key}", valueName, null) as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    [GeneratedRegex("""^\s*"path"\s+"(.+)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex VdfPathLine();

    [GeneratedRegex("""^\s*"installdir"\s+"(.+)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex VdfInstallDirLine();
}
