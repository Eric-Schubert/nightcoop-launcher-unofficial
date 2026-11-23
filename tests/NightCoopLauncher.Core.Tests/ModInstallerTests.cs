using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using NightCoopLauncher.Core.Game;
using NightCoopLauncher.Core.Install;
using NightCoopLauncher.Core.Releases;
using Xunit;

namespace NightCoopLauncher.Core.Tests;

public sealed class ModInstallerTests : IDisposable
{
    private const string GameVersion = "2.5";

    private readonly TempDirectory _game = new();
    private readonly byte[] _archive = CreateArchive();

    public void Dispose() => _game.Dispose();

    [Fact]
    public async Task Installs_a_verified_archive_and_records_what_it_installed()
    {
        var file = FileWithHash(Convert.ToHexString(SHA256.HashData(_archive)));
        using var http = new HttpClient(new FixedResponseHandler(_archive));

        var state = await new ModInstaller(http).InstallAsync(Game(), GameVersion, Release(file), file, null, null, CancellationToken.None);

        Assert.True(InstallState.IsModPresent(_game.Path));
        Assert.Equal("0.2.2-beta", state.Version);
        Assert.Equal([InstallState.PluginRelativePath], state.Files);
        Assert.Equal(InstallHealth.Intact, InstallVerifier.Check(_game.Path, state));
        Assert.True(InstallState.Matches(InstallStateStore.Load(_game.Path), Release(file), file, _game.Path));
    }

    [Fact]
    public async Task Installs_nothing_when_the_checksum_does_not_match()
    {
        var file = FileWithHash(new string('0', 64));
        using var http = new HttpClient(new FixedResponseHandler(_archive));

        await Assert.ThrowsAsync<LauncherException>(
            () => new ModInstaller(http).InstallAsync(Game(), GameVersion, Release(file), file, null, null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_game.Path));
    }

    private GameInstall Game() => new(GameStore.Custom, _game.Path, _game.Path);

    private static ModFile FileWithHash(string sha256) =>
        new("NightCoop.zip", new Uri("https://nightcoop.net/dl/NightCoop.zip"), 0, sha256, [GameVersion]);

    private static ModRelease Release(ModFile file) =>
        new("0.2.2-beta", "2026-11-21", 201, [GameVersion], null, [], [file], Withdrawn: false);

    private static byte[] CreateArchive()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(archive.CreateEntry("red4ext/plugins/NightCoop/NightCoop.dll").Open()))
        {
            writer.Write("plugin");
        }

        return buffer.ToArray();
    }

    private sealed class FixedResponseHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
