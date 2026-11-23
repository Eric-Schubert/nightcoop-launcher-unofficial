namespace NightCoopLauncher.Core;

/// <summary>What went wrong, as a code the user interface can translate.</summary>
public enum LauncherError
{
    Unexpected,
    ManifestUnreachable,
    ManifestTimeout,
    ManifestInvalid,
    ManifestSchemaUnsupported,
    ManifestDemoData,
    NoAvailableBuild,
    GameVersionUnsupported,
    NoArchiveForGameVersion,
    UnsafeDownloadAddress,
    DownloadFailed,
    ChecksumMismatch,
    ArchiveEmpty,
    UnsafePath,
    GameFolderNotWritable,
    GameNotFound,
    MultipleGamesFound,
    NotAGameFolder,
    GameVersionUnreadable,
    GameRunning,
    GameStartFailed,
    NoSaves,
    BackupMissing,
}

/// <summary>
/// A failure meant for the person in front of the launcher. It carries a code and its
/// arguments instead of a finished sentence, so the message can be shown in any language.
/// </summary>
public sealed class LauncherException : Exception
{
    public LauncherException(LauncherError error, params object?[] arguments)
        : base(Describe(error, arguments))
    {
        Error = error;
        Arguments = arguments;
    }

    public LauncherException(LauncherError error, Exception innerException, params object?[] arguments)
        : base(Describe(error, arguments), innerException)
    {
        Error = error;
        Arguments = arguments;
    }

    public LauncherError Error { get; }

    public IReadOnlyList<object?> Arguments { get; }

    private static string Describe(LauncherError error, object?[] arguments) =>
        arguments.Length == 0 ? error.ToString() : $"{error}: {string.Join(", ", arguments)}";
}
