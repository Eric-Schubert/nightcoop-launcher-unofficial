using System.Security.Cryptography;
using System.Text.Json;

namespace NightCoopLauncher.Core.SelfUpdate;

/// <param name="Version">The release's version, read from its tag.</param>
/// <param name="DownloadUrl">Address of the launcher executable attached to the release.</param>
/// <param name="Sha256">Checksum of that executable, as GitHub computed it on upload.</param>
public sealed record LauncherRelease(Version Version, Uri DownloadUrl, string Sha256);

/// <summary>Finds and downloads new versions of the launcher itself from its GitHub releases.</summary>
public sealed class LauncherReleaseClient(HttpClient http)
{
    public const string ExecutableName = "NightCoopLauncher.exe";

    private const string Repository = "dominique-boerner/nightcoop-launcher-unofficial";
    private const string DownloadHost = "github.com";
    private const string DigestPrefix = "sha256:";
    private const int BufferSize = 81920;

    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <returns>
    /// The latest release, or null when it cannot be determined. Not knowing about an
    /// update is no reason to bother the user.
    /// </returns>
    public async Task<LauncherRelease?> FetchLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Parse(await http.GetStringAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Downloads the release's executable and deletes it again unless its checksum matches.</summary>
    public async Task DownloadAsync(LauncherRelease release, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);

        try
        {
            using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = Math.Max(1, response.Content.Headers.ContentLength ?? 0);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var destination = File.Create(targetPath);
                await using (destination.ConfigureAwait(false))
                {
                    var buffer = new byte[BufferSize];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        done += read;
                        progress?.Report(Math.Min(1, (double)done / total));
                    }
                }
            }

            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LauncherException(LauncherError.ChecksumMismatch, ExecutableName);
            }
        }
        catch (HttpRequestException exception)
        {
            File.Delete(targetPath);
            throw new LauncherException(LauncherError.DownloadFailed, exception, exception.Message);
        }
        catch (Exception exception) when (exception is LauncherException or OperationCanceledException)
        {
            // A partial or unverified executable must not be left where it could be started.
            File.Delete(targetPath);
            throw;
        }
    }

    internal static LauncherRelease? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (IsTrue(root, "draft") || IsTrue(root, "prerelease") || ParseVersion(GetString(root, "tag_name")) is not { } version)
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray().Where(asset => GetString(asset, "name") == ExecutableName))
        {
            var digest = GetString(asset, "digest");
            var url = GetString(asset, "browser_download_url");

            // Without a checksum the download could not be verified, so the release is
            // treated as if it did not exist.
            if (digest is null || !digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase) || !IsTrustedDownload(url))
            {
                return null;
            }

            return new LauncherRelease(version, new Uri(url!), digest[DigestPrefix.Length..]);
        }

        return null;
    }

    /// <summary>Reads "v1.2.3" as a version; a tag that is not a plain release version yields null.</summary>
    internal static Version? ParseVersion(string? tag)
    {
        var text = tag?.TrimStart('v', 'V');
        return Version.TryParse(text, out var version) && version.Build >= 0 ? version : null;
    }

    private static bool IsTrustedDownload(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, DownloadHost, StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
