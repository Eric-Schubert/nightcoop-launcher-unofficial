namespace NightCoopLauncher.Core.Releases;

public sealed class ReleaseManifestClient(HttpClient http)
{
    public static Uri SiteBaseUri { get; } = new("https://nightcoop.net/");

    public static Uri DownloadPageUri { get; } = new(SiteBaseUri, "download.html");

    private static readonly Uri ManifestUri = new(SiteBaseUri, "releases.json");

    public async Task<ReleaseManifest> FetchAsync(string language, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await http.GetStringAsync(ManifestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new LauncherException(LauncherError.ManifestUnreachable, exception, SiteBaseUri.Host);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LauncherException(LauncherError.ManifestTimeout, exception, SiteBaseUri.Host);
        }

        try
        {
            return ReleaseManifestParser.Parse(json, SiteBaseUri, language);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new LauncherException(LauncherError.ManifestInvalid, exception);
        }
    }
}
