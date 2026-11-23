using System.Text.Json;

namespace NightCoopLauncher.Core.Releases;

public static class ReleaseManifestParser
{
    private const int SupportedSchema = 1;
    private const string PreferredLanguage = "en";
    private const string ModFileKind = "mod";

    public static ReleaseManifest Parse(string json, Uri siteBaseUri)
    {
        ArgumentNullException.ThrowIfNull(siteBaseUri);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var schema = GetInt(root, "schema");
        if (schema != SupportedSchema)
        {
            throw new LauncherException(
                $"The release list uses a format this launcher does not understand (expected format {SupportedSchema}). Update the launcher or install manually.");
        }

        if (root.TryGetProperty("demo", out var demo) && demo.ValueKind == JsonValueKind.True)
        {
            throw new LauncherException("The site currently publishes demo data only: there are no files behind the links.");
        }

        var releases = root.TryGetProperty("releases", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(release => ParseRelease(release, siteBaseUri)).ToList()
            : [];

        return new ReleaseManifest(GetString(root, "latest"), releases);
    }

    private static ModRelease ParseRelease(JsonElement release, Uri siteBaseUri)
    {
        var files = release.TryGetProperty("files", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Where(file => GetString(file, "kind") == ModFileKind)
                .Select(file => ParseFile(file, siteBaseUri))
                .ToList()
            : [];

        var withdrawn = release.TryGetProperty("yanked", out var yanked)
            && yanked.ValueKind is not (JsonValueKind.Null or JsonValueKind.False or JsonValueKind.Undefined);

        return new ModRelease(
            GetString(release, "version") ?? throw new LauncherException("The release list contains a build without a version."),
            GetString(release, "date"),
            GetInt(release, "protocol"),
            GetStringList(release, "game"),
            GetLocalizedText(release, "title"),
            GetLocalizedList(release, "notes"),
            files,
            withdrawn);
    }

    private static ModFile ParseFile(JsonElement file, Uri siteBaseUri)
    {
        var name = GetString(file, "name") ?? "NightCoop.zip";
        var url = GetString(file, "url") ?? throw new LauncherException($"The release list gives no address for {name}.");
        var sha256 = GetString(file, "sha256") ?? throw new LauncherException($"The release list gives no SHA-256 for {name}.");
        var size = file.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes) ? bytes : 0;

        return new ModFile(name, ResolveDownloadUri(siteBaseUri, url), size, sha256, GetStringList(file, "game"));
    }

    private static Uri ResolveDownloadUri(Uri siteBaseUri, string url)
    {
        var resolved = new Uri(siteBaseUri, url);

        // The release list is the trust anchor for the checksums, so it must not be able
        // to send the download to another host or downgrade it to plain HTTP.
        if (resolved.Scheme != Uri.UriSchemeHttps || !string.Equals(resolved.Host, siteBaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherException($"Refusing to download from '{resolved}': not an HTTPS address on {siteBaseUri.Host}.");
        }

        return resolved;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static List<string> GetStringList(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? ToStringList(value) : [];

    private static List<string> ToStringList(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToList()
            : [];

    private static string? GetLocalizedText(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var localized = PickLanguage(value);
        return localized.ValueKind == JsonValueKind.String ? localized.GetString() : null;
    }

    private static List<string> GetLocalizedList(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? ToStringList(PickLanguage(value)) : [];

    private static JsonElement PickLanguage(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return value;
        }

        if (value.TryGetProperty(PreferredLanguage, out var preferred))
        {
            return preferred;
        }

        using var languages = value.EnumerateObject();
        return languages.MoveNext() ? languages.Current.Value : default;
    }
}
