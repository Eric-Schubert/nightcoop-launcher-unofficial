namespace NightCoopLauncher.Core.Releases;

public sealed class ReleaseManifestClient(HttpClient http)
{
    public static Uri SiteBaseUri { get; } = new("https://nightcoop.net/");

    public static Uri DownloadPageUri { get; } = new(SiteBaseUri, "download.html");

    private static readonly Uri ManifestUri = new(SiteBaseUri, "releases.json");

    public async Task<ReleaseManifest> FetchAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await http.GetStringAsync(ManifestUri, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new LauncherException($"Could not reach {SiteBaseUri.Host}: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LauncherException($"{SiteBaseUri.Host} did not answer in time.", exception);
        }

        try
        {
            return ReleaseManifestParser.Parse(json, SiteBaseUri);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new LauncherException("The release list could not be read.", exception);
        }
    }
}
