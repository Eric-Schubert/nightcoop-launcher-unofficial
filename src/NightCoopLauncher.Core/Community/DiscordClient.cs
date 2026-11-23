using System.Text.Json;

namespace NightCoopLauncher.Core.Community;

public sealed record DiscordServer(string Name, int OnlineCount);

/// <summary>Reads the public invite of the NightCoop Discord server, as the mod's own site does.</summary>
public sealed class DiscordClient(HttpClient http)
{
    private const string InviteCode = "nightcoop";

    public static Uri InviteUri { get; } = new($"https://discord.gg/{InviteCode}");

    private static readonly Uri InviteApiUri = new($"https://discord.com/api/v10/invites/{InviteCode}?with_counts=true");

    /// <returns>The server, or null when Discord cannot be reached: the card is decoration, not a feature to fail on.</returns>
    public async Task<DiscordServer?> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await http.GetStringAsync(InviteApiUri, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("approximate_presence_count", out var online) || !online.TryGetInt32(out var onlineCount))
            {
                return null;
            }

            var name = root.TryGetProperty("guild", out var guild) && guild.TryGetProperty("name", out var guildName)
                ? guildName.GetString()
                : null;
            return new DiscordServer(name ?? "NightCoop", onlineCount);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}
