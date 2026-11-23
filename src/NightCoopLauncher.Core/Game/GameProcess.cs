using System.ComponentModel;
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

    /// <summary>Game files are locked while the game runs, and saves may be written at any moment.</summary>
    public static void EnsureNotRunning()
    {
        if (IsRunning())
        {
            throw new LauncherException(LauncherError.GameRunning);
        }
    }

    public static string ReadVersion(GameInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        var version = FileVersionInfo.GetVersionInfo(install.ExecutablePath).ProductVersion;
        return string.IsNullOrWhiteSpace(version)
            ? throw new LauncherException(LauncherError.GameVersionUnreadable)
            : version.Trim();
    }

    public static void Launch(GameInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        try
        {
            // Shell execution: the Steam and Epic targets are protocol links, not executables.
            using var process = Process.Start(new ProcessStartInfo(install.LaunchTarget) { UseShellExecute = true });
        }
        catch (Win32Exception exception)
        {
            throw new LauncherException(LauncherError.GameStartFailed, exception, exception.Message);
        }
    }
}
