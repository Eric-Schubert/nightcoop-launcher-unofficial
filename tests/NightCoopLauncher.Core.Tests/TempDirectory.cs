namespace NightCoopLauncher.Core.Tests;

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("NightCoopLauncherTests-").FullName;

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string WriteFile(string relativePath, string content)
    {
        var target = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content);
        return target;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
