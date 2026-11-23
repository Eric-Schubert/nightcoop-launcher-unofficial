using System.Globalization;

namespace NightCoopLauncher.App.ViewModels;

internal static class Formatting
{
    private const double BytesPerMegabyte = 1024 * 1024;

    public static string Megabytes(long bytes) =>
        (bytes / BytesPerMegabyte).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
}
