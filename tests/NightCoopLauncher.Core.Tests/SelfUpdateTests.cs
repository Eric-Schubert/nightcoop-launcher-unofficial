using System.Net;
using System.Security.Cryptography;
using NightCoopLauncher.Core.SelfUpdate;
using Xunit;

namespace NightCoopLauncher.Core.Tests;

public sealed class SelfUpdateTests : IDisposable
{
    private const string Download = "https://github.com/dominique-boerner/nightcoop-launcher-unofficial/releases/download/v1.2.0/NightCoopLauncher.exe";

    private readonly TempDirectory _folder = new();

    public void Dispose() => _folder.Dispose();

    private static string Release(
        string tag = "v1.2.0",
        string url = Download,
        string? digest = "sha256:ABCDEF",
        string flags = "\"draft\": false, \"prerelease\": false") =>
        $$"""
        {
          "tag_name": "{{tag}}", {{flags}},
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://github.com/x/y", "digest": "sha256:00" },
            { "name": "NightCoopLauncher.exe", "browser_download_url": "{{url}}", "digest": {{(digest is null ? "null" : $"\"{digest}\"")}} }
          ]
        }
        """;

    [Fact]
    public void Reads_version_download_and_checksum_of_a_release()
    {
        var release = LauncherReleaseClient.Parse(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal(new Uri(Download), release.DownloadUrl);
        Assert.Equal("ABCDEF", release.Sha256);
    }

    [Theory]
    [InlineData("https://evil.example/dominique-boerner/nightcoop-launcher-unofficial/releases/download/v1.2.0/NightCoopLauncher.exe")]
    [InlineData("http://github.com/dominique-boerner/nightcoop-launcher-unofficial/releases/download/v1.2.0/NightCoopLauncher.exe")]
    [InlineData("https://github.com/someone-else/fork/releases/download/v1.2.0/NightCoopLauncher.exe")]
    public void Ignores_a_release_whose_download_is_not_from_this_repository(string url)
    {
        Assert.Null(LauncherReleaseClient.Parse(Release(url: url)));
    }

    [Fact]
    public void Ignores_a_release_without_a_checksum()
    {
        Assert.Null(LauncherReleaseClient.Parse(Release(digest: null)));
    }

    [Theory]
    [InlineData("\"draft\": true, \"prerelease\": false")]
    [InlineData("\"draft\": false, \"prerelease\": true")]
    public void Ignores_drafts_and_pre_releases(string flags)
    {
        Assert.Null(LauncherReleaseClient.Parse(Release(flags: flags)));
    }

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.10.3", "1.10.3")]
    public void Reads_a_version_tag(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), LauncherReleaseClient.ParseVersion(tag));
    }

    [Theory]
    [InlineData("v1.2.0-beta.1")]
    [InlineData("nightly")]
    [InlineData("v2")]
    [InlineData(null)]
    public void Does_not_read_other_tags_as_versions(string? tag)
    {
        Assert.Null(LauncherReleaseClient.ParseVersion(tag));
    }

    [Fact]
    public async Task Keeps_a_download_whose_checksum_matches()
    {
        byte[] body = [1, 2, 3];
        var target = _folder.Combine("NightCoopLauncher.exe.new");
        using var http = new HttpClient(new FixedResponseHandler(body));
        var release = new LauncherRelease(new Version(1, 2, 0), new Uri(Download), Convert.ToHexString(SHA256.HashData(body)));

        await new LauncherReleaseClient(http).DownloadAsync(release, target, null, CancellationToken.None);

        Assert.Equal(body, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task Deletes_a_download_whose_checksum_does_not_match()
    {
        var target = _folder.Combine("NightCoopLauncher.exe.new");
        using var http = new HttpClient(new FixedResponseHandler([1, 2, 3]));
        var release = new LauncherRelease(new Version(1, 2, 0), new Uri(Download), new string('0', 64));

        var exception = await Assert.ThrowsAsync<LauncherException>(
            () => new LauncherReleaseClient(http).DownloadAsync(release, target, null, CancellationToken.None));

        Assert.Equal(LauncherError.ChecksumMismatch, exception.Error);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void Swaps_the_executable_and_cleans_up_on_the_next_start()
    {
        var executable = _folder.WriteFile("NightCoopLauncher.exe", "old");
        var downloaded = _folder.WriteFile("NightCoopLauncher.exe.new", "new");

        SelfUpdater.Swap(executable, downloaded);

        Assert.Equal("new", File.ReadAllText(executable));
        Assert.Equal("old", File.ReadAllText(executable + ".old"));

        SelfUpdater.CleanUp(executable);

        Assert.Equal(["NightCoopLauncher.exe"], Directory.EnumerateFiles(_folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Keeps_the_running_executable_when_the_new_one_cannot_be_put_in_place()
    {
        var executable = _folder.WriteFile("NightCoopLauncher.exe", "old");

        var exception = Assert.Throws<LauncherException>(() => SelfUpdater.Swap(executable, _folder.Combine("missing.new")));

        Assert.Equal(LauncherError.LauncherNotReplaceable, exception.Error);
        Assert.Equal("old", File.ReadAllText(executable));
    }

    [Fact]
    public void Replaces_itself_only_as_the_published_single_file()
    {
        var published = _folder.WriteFile(@"published\NightCoopLauncher.exe", "exe");
        var development = _folder.WriteFile(@"development\NightCoopLauncher.exe", "exe");
        _folder.WriteFile(@"development\NightCoopLauncher.dll", "dll");

        Assert.True(SelfUpdater.IsSupported(published));
        Assert.False(SelfUpdater.IsSupported(development));
        Assert.False(SelfUpdater.IsSupported(_folder.Combine("testhost.exe")));
        Assert.False(SelfUpdater.IsSupported(null));
    }

    private sealed class FixedResponseHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
