using NightCoopLauncher.Core.Releases;
using Xunit;

namespace NightCoopLauncher.Core.Tests;

public class ReleaseManifestTests
{
    private static readonly Uri Site = new("https://nightcoop.net/");

    private const string Manifest = """
        {
          "schema": 1,
          "latest": "0.2.2-beta",
          "releases": [
            {
              "version": "0.2.2-beta", "date": "2026-11-21", "game": ["2.5", "2.31"], "protocol": 201,
              "title": { "ru": "Бета", "en": "Beta 0.2.2" },
              "notes": { "ru": ["раз"], "en": ["first", "second"] },
              "files": [
                { "kind": "mod", "game": ["2.5"], "name": "a-2.5.zip", "url": "dl/a-2.5.zip", "size": 10, "sha256": "AA" },
                { "kind": "mod", "game": ["2.31"], "name": "a-2.31.zip", "url": "dl/a-2.31.zip", "size": 11, "sha256": "BB" },
                { "kind": "symbols", "name": "a.pdb", "url": "dl/a.pdb", "size": 1, "sha256": "CC" }
              ],
              "yanked": null
            },
            {
              "version": "0.1.0-beta", "date": "2026-10-23", "game": ["2.5", "2.31"], "protocol": 128,
              "files": [ { "kind": "mod", "name": "b.zip", "url": "dl/b.zip", "size": 9, "sha256": "DD" } ],
              "yanked": null
            }
          ]
        }
        """;

    [Fact]
    public void Parses_releases_with_english_texts_and_absolute_download_addresses()
    {
        var release = ReleaseManifestParser.Parse(Manifest, Site).Releases[0];

        Assert.Equal("Beta 0.2.2", release.Title);
        Assert.Equal(["first", "second"], release.Notes);
        Assert.Equal(201, release.Protocol);
        Assert.Equal(2, release.ModFiles.Count);
        Assert.Equal(new Uri("https://nightcoop.net/dl/a-2.5.zip"), release.ModFiles[0].Url);
    }

    [Theory]
    [InlineData("https://evil.example/a.zip")]
    [InlineData("http://nightcoop.net/a.zip")]
    public void Rejects_downloads_from_another_host_or_over_plain_http(string url)
    {
        var json = Manifest.Replace("dl/a-2.5.zip", url, StringComparison.Ordinal);

        Assert.Throws<LauncherException>(() => ReleaseManifestParser.Parse(json, Site));
    }

    [Fact]
    public void Rejects_an_unknown_schema()
    {
        var json = Manifest.Replace("\"schema\": 1", "\"schema\": 2", StringComparison.Ordinal);

        Assert.Throws<LauncherException>(() => ReleaseManifestParser.Parse(json, Site));
    }

    [Fact]
    public void Rejects_demo_data()
    {
        var json = Manifest.Replace("\"schema\": 1", "\"schema\": 1, \"demo\": true", StringComparison.Ordinal);

        Assert.Throws<LauncherException>(() => ReleaseManifestParser.Parse(json, Site));
    }

    [Fact]
    public void Selects_the_build_named_latest()
    {
        var manifest = ReleaseManifestParser.Parse(Manifest, Site);

        Assert.Equal("0.2.2-beta", ReleaseSelector.SelectLatest(manifest).Version);
    }

    [Fact]
    public void Skips_a_withdrawn_latest_build()
    {
        const string notWithdrawn = "\"yanked\": null";
        var first = Manifest.IndexOf(notWithdrawn, StringComparison.Ordinal);
        var json = Manifest.Remove(first, notWithdrawn.Length).Insert(first, "\"yanked\": { \"reason\": \"crash\" }");

        var manifest = ReleaseManifestParser.Parse(json, Site);

        Assert.True(manifest.Releases[0].Withdrawn);
        Assert.Equal("0.1.0-beta", ReleaseSelector.SelectLatest(manifest).Version);
    }

    [Theory]
    [InlineData("2.5", "a-2.5.zip")]
    [InlineData("2.31", "a-2.31.zip")]
    public void Selects_the_archive_built_for_the_game_version(string gameVersion, string expectedFile)
    {
        var release = ReleaseManifestParser.Parse(Manifest, Site).Releases[0];

        Assert.Equal(expectedFile, ReleaseSelector.SelectFile(release, gameVersion).Name);
    }

    [Fact]
    public void Selects_the_single_archive_of_an_early_build()
    {
        var release = ReleaseManifestParser.Parse(Manifest, Site).Releases[1];

        Assert.Equal("b.zip", ReleaseSelector.SelectFile(release, "2.31").Name);
    }

    [Fact]
    public void Rejects_an_unsupported_game_version()
    {
        var release = ReleaseManifestParser.Parse(Manifest, Site).Releases[0];

        Assert.Throws<LauncherException>(() => ReleaseSelector.SelectFile(release, "2.2"));
    }
}
