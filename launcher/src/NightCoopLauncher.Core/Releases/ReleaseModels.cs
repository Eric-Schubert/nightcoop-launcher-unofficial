namespace NightCoopLauncher.Core.Releases;

public sealed record ModFile(
    string Name,
    Uri Url,
    long Size,
    string Sha256,
    IReadOnlyList<string> GameVersions);

public sealed record ModRelease(
    string Version,
    string? Date,
    int? Protocol,
    IReadOnlyList<string> GameVersions,
    string? Title,
    IReadOnlyList<string> Notes,
    IReadOnlyList<ModFile> ModFiles,
    bool Withdrawn);

public sealed record ReleaseManifest(string? Latest, IReadOnlyList<ModRelease> Releases);
