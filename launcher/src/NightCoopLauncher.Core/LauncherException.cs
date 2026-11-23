namespace NightCoopLauncher.Core;

/// <summary>A failure whose message is written for the person in front of the launcher.</summary>
public sealed class LauncherException : Exception
{
    public LauncherException()
    {
    }

    public LauncherException(string message)
        : base(message)
    {
    }

    public LauncherException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
