using NightCoopLauncher.App.Localization;
using NightCoopLauncher.Core.Game;

namespace NightCoopLauncher.App.ViewModels;

internal static class Formatting
{
    private const double BytesPerMegabyte = 1024 * 1024;
    private const double BytesPerGigabyte = BytesPerMegabyte * 1024;

    public static string Size(long bytes, Localizer localizer) =>
        bytes >= BytesPerGigabyte
            ? (bytes / BytesPerGigabyte).ToString("0.0", localizer.Culture) + " GB"
            : (bytes / BytesPerMegabyte).ToString(bytes >= 100 * BytesPerMegabyte ? "0" : "0.0", localizer.Culture) + " MB";

    /// <summary>"Today, 18:42", "Yesterday, 21:10" or a short date for anything older.</summary>
    public static string When(DateTime moment, Localizer localizer)
    {
        var time = moment.ToString("HH:mm", localizer.Culture);
        var daysAgo = (DateTime.Today - moment.Date).Days;
        return daysAgo switch
        {
            0 => localizer.Format("date.today", time),
            1 => localizer.Format("date.yesterday", time),
            _ => moment.ToString(localizer["date.pattern"], localizer.Culture),
        };
    }

    public static string Version(string version) => "v" + version;

    public static string Store(GameStore store, Localizer localizer) => store switch
    {
        GameStore.Gog => "GOG",
        GameStore.Custom => localizer["game.customFolder"],
        _ => store.ToString(),
    };
}
