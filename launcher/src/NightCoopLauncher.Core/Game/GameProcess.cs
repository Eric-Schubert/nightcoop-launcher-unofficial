using System.Diagnostics;

namespace NightCoopLauncher.Core.Game;

public static class GameProcess
{
    private const string ProcessName = "Cyberpunk2077";

    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static string ReadVersion(GameInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        var version = FileVersionInfo.GetVersionInfo(install.ExecutablePath).ProductVersion;
        return string.IsNullOrWhiteSpace(version)
            ? throw new LauncherException("Could not read the game version from Cyberpunk2077.exe.")
            : version.Trim();
    }

    public static void Launch(GameInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        // Shell execution: the Steam and Epic targets are protocol links, not executables.
        using var process = Process.Start(new ProcessStartInfo(install.LaunchTarget) { UseShellExecute = true });
    }
}
