namespace NightCoopLauncher.Core.Game;

public enum GameStore
{
    Steam,
    Gog,
    Epic,
    Custom,
}

public sealed record GameInstall(GameStore Store, string Path, string LaunchTarget)
{
    public const string ExecutableRelativePath = @"bin\x64\Cyberpunk2077.exe";

    public string ExecutablePath => System.IO.Path.Combine(Path, ExecutableRelativePath);
}
