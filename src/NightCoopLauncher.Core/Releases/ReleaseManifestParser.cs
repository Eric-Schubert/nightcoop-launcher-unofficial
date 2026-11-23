using System.Text.Json;

namespace NightCoopLauncher.Core.Releases;

public static class ReleaseManifestParser
{
    private const int SupportedSchema = 1;
    private const string FallbackLanguage = "en";
    private const string ModFileKind = "mod";

    /// <param name="json">The release list as published by the site.</param>
    /// <param name="siteBaseUri">Base address the relative download addresses resolve against.</param>
    /// <param name="language">Preferred language of titles and notes; English is used where it is missing.</param>
    public static ReleaseManifest Parse(string json, Uri siteBaseUri, string language = FallbackLanguage)
    {
        ArgumentNullException.ThrowIfNull(siteBaseUri);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (GetInt(root, "schema") != SupportedSchema)
        {
            throw new LauncherException(LauncherError.ManifestSchemaUnsupported);
        }

        if (root.TryGetProperty("demo", out var demo) && demo.ValueKind == JsonValueKind.True)
        {
            throw new LauncherException(LauncherError.ManifestDemoData);
        }

        var releases = root.TryGetProperty("releases", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(release => ParseRelease(release, siteBaseUri, language)).ToList()
            : [];

        return new ReleaseManifest(GetString(root, "latest"), releases);
    }

    private static ModRelease ParseRelease(JsonElement release, Uri siteBaseUri, string language)
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
            GetString(release, "version") ?? throw new LauncherException(LauncherError.ManifestInvalid),
            GetString(release, "date"),
            GetInt(release, "protocol"),
            GetStringList(release, "game"),
            GetLocalizedText(release, "title", language),
            GetLocalizedList(release, "notes", language),
            files,
            withdrawn);
    }

    private static ModFile ParseFile(JsonElement file, Uri siteBaseUri)
    {
        var name = GetString(file, "name") ?? "NightCoop.zip";
        var url = GetString(file, "url") ?? throw new LauncherException(LauncherError.ManifestInvalid);
        var sha256 = GetString(file, "sha256") ?? throw new LauncherException(LauncherError.ManifestInvalid);
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
            throw new LauncherException(LauncherError.UnsafeDownloadAddress, resolved, siteBaseUri.Host);
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

    private static string? GetLocalizedText(JsonElement element, string name, string language)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var localized = PickLanguage(value, language);
        return localized.ValueKind == JsonValueKind.String ? localized.GetString() : null;
    }

    private static List<string> GetLocalizedList(JsonElement element, string name, string language) =>
        element.TryGetProperty(name, out var value) ? ToStringList(PickLanguage(value, language)) : [];

    private static JsonElement PickLanguage(JsonElement value, string language)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return value;
        }

        if (value.TryGetProperty(language, out var preferred) || value.TryGetProperty(FallbackLanguage, out preferred))
        {
            return preferred;
        }

        using var languages = value.EnumerateObject();
        return languages.MoveNext() ? languages.Current.Value : default;
    }
}
