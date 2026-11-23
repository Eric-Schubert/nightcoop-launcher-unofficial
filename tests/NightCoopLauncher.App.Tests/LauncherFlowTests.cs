using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using NightCoopLauncher.App.Launcher;
using NightCoopLauncher.App.Localization;
using NightCoopLauncher.App.Services;
using NightCoopLauncher.App.ViewModels;
using NightCoopLauncher.Core.Community;
using NightCoopLauncher.Core.Game;
using NightCoopLauncher.Core.Install;
using NightCoopLauncher.Core.Releases;
using NightCoopLauncher.Core.Saves;
using NightCoopLauncher.Core.Settings;
using Xunit;

namespace NightCoopLauncher.App.Tests;

/// <summary>The localizer is one shared instance, so tests that switch its language must not overlap.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedLocalizer
{
    public const string Name = "Launcher";
}

/// <summary>
/// Drives the launcher the way the window does, against a game folder in a temporary
/// directory and a web server that exists only in memory.
/// </summary>
[Collection(SharedLocalizer.Name)]
public sealed class LauncherFlowTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("NightCoopLauncherAppTests-").FullName;
    private readonly string _gameVersion;
    private readonly FakeSite _site;
    private readonly LauncherController _controller;
    private readonly MainViewModel _window;

    public LauncherFlowTests()
    {
        // Any executable with a product version can stand in for the game.
        var gameExecutable = Path.Combine(GameFolder, GameInstall.ExecutableRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutable)!);
        File.Copy(Environment.ProcessPath!, gameExecutable);
        _gameVersion = FileVersionInfo.GetVersionInfo(gameExecutable).ProductVersion!.Trim();

        Directory.CreateDirectory(SavesFolder);
        File.WriteAllText(Path.Combine(SavesFolder, "sav.dat"), "save");

        var settingsStore = new SettingsStore(Path.Combine(_root, "settings.json"));
        settingsStore.Save(new LauncherSettings { GamePath = GameFolder, BackupDirectory = BackupFolder, Language = "en" });

        _site = new FakeSite(_gameVersion);
        var http = new HttpClient(_site);
        var localizer = Localizer.Current;
        localizer.SetLanguage("en");

        _controller = new LauncherController(
            new ReleaseManifestClient(http),
            new ModInstaller(http),
            new SaveBackupService(SavesFolder, BackupFolder, TimeProvider.System),
            new DiscordClient(http),
            settingsStore,
            () => localizer.LanguageCode);
        _window = new MainViewModel(_controller, localizer, new SilentPrompts());
    }

    private string GameFolder => Path.Combine(_root, "game");

    private string SavesFolder => Path.Combine(_root, "saves");

    private string BackupFolder => Path.Combine(_root, "backups");

    public void Dispose()
    {
        _site.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Offers_to_install_first_and_to_play_only_once_the_mod_is_current()
    {
        await _window.InitializeAsync();

        Assert.Equal("INSTALL", _window.Play.PrimaryText);
        Assert.True(_window.HasUpdate);

        await _controller.UpdateToLatestAsync();

        Assert.Null(_controller.Error);
        Assert.Equal("PLAY", _window.Play.PrimaryText);
        Assert.Equal("READY TO PLAY", _window.Play.StatusText);
        Assert.False(_window.HasUpdate);
        Assert.Equal(InstallHealth.Intact, _controller.Health);
        Assert.Equal("Launcher 1.0.0 · Mod v0.2.2-beta", _window.Footer);
    }

    [Fact]
    public async Task Backs_up_the_saves_before_an_update_and_names_the_version()
    {
        await _window.InitializeAsync();

        await _controller.UpdateToLatestAsync();

        var backup = Assert.Single(_controller.Backups);
        Assert.Equal(BackupKind.BeforeUpdate, backup.Kind);
        Assert.Equal("Before update to v0.2.2-beta", Assert.Single(_window.Backups.Items).Title);
    }

    [Fact]
    public async Task Skips_the_backup_when_it_is_switched_off()
    {
        await _window.InitializeAsync();
        _window.Updates.BackupSavesBeforeUpdate = false;

        await _controller.UpdateToLatestAsync();

        Assert.Empty(_controller.Backups);
    }

    [Fact]
    public async Task Rolls_back_to_an_older_version_and_offers_the_update_again()
    {
        await _window.InitializeAsync();
        await _controller.UpdateToLatestAsync();
        var older = _controller.Manifest!.Releases.Single(release => release.Version == "0.2.1-beta");

        await _controller.InstallVersionAsync(older);

        Assert.Null(_controller.Error);
        Assert.Equal("0.2.1-beta", _controller.Installed!.Version);
        Assert.Equal("0.2.1-beta", _controller.Settings.HeldVersion);
        Assert.Equal("UPDATE", _window.Play.PrimaryText);
        Assert.True(_window.Updates.History.Single(item => item.Version == "v0.2.1-beta").IsActive);
    }

    [Fact]
    public async Task Leaves_a_rolled_back_version_alone_when_updates_install_automatically()
    {
        await _window.InitializeAsync();
        await _controller.UpdateToLatestAsync();
        await _controller.InstallVersionAsync(_controller.Manifest!.Releases.Single(release => release.Version == "0.2.1-beta"));
        _window.Updates.AutoInstallUpdates = true;

        await _window.InitializeAsync();

        Assert.Equal("0.2.1-beta", _controller.Installed!.Version);
    }

    [Fact]
    public async Task Installs_an_update_on_start_when_that_is_switched_on()
    {
        await _window.InitializeAsync();
        await _controller.InstallVersionAsync(_controller.Manifest!.Releases.Single(release => release.Version == "0.2.1-beta"));
        _controller.ChangeSettings(settings => settings with { AutoInstallUpdates = true, HeldVersion = null });

        await _window.InitializeAsync();

        Assert.Equal("0.2.2-beta", _controller.Installed!.Version);
    }

    [Fact]
    public async Task Reports_a_damaged_installation_and_repairs_it()
    {
        await _window.InitializeAsync();
        await _controller.UpdateToLatestAsync();
        File.WriteAllText(Path.Combine(GameFolder, InstallState.PluginRelativePath), "tampered");

        await _controller.VerifyAsync();
        Assert.Equal("Damaged", _window.Play.HealthTitle);
        Assert.True(_window.Play.CanRepair);

        await _controller.RepairAsync();
        Assert.Equal("Intact", _window.Play.HealthTitle);
    }

    [Fact]
    public async Task Shows_a_failed_check_in_the_chosen_language_and_keeps_the_installed_mod_playable()
    {
        await _window.InitializeAsync();
        await _controller.UpdateToLatestAsync();
        _site.IsOffline = true;

        await _controller.RefreshAsync();

        Assert.Equal("nightcoop.net cannot be reached. Check your internet connection.", _window.ErrorText);
        Assert.Equal("PLAY", _window.Play.PrimaryText);

        Localizer.Current.SetLanguage("de");
        Assert.Equal("nightcoop.net ist nicht erreichbar. Prüfe deine Internetverbindung.", _window.ErrorText);
        Assert.Equal("SPIELEN", _window.Play.PrimaryText);
    }

    [Fact]
    public async Task Shows_the_discord_server_when_it_can_be_reached()
    {
        await _window.InitializeAsync();

        Assert.True(_window.HasDiscord);
        Assert.Equal("NightCoop", _window.DiscordName);
        Assert.Equal("15,798", _window.DiscordOnline);
    }

    private sealed class SilentPrompts : IUserPrompts
    {
        public bool Confirm(string title, string message) => true;

        public string? PickFolder(string title) => null;

        public string? PickSaveLocation(string title, string suggestedFileName) => null;

        public void OpenInShell(string target)
        {
        }
    }

    /// <summary>Answers for nightcoop.net and the Discord invite with two builds of a one-file mod.</summary>
    private sealed class FakeSite(string gameVersion) : HttpMessageHandler
    {
        private static readonly string[] Versions = ["0.2.2-beta", "0.2.1-beta"];

        private readonly Dictionary<string, byte[]> _archives = Versions.ToDictionary(version => version, CreateArchive);

        public bool IsOffline { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsOffline)
            {
                throw new HttpRequestException("offline");
            }

            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "discord.com")
            {
                return Respond("""{ "approximate_presence_count": 15798, "guild": { "name": "NightCoop" } }"""u8.ToArray());
            }

            if (path == "/releases.json")
            {
                return Respond(System.Text.Encoding.UTF8.GetBytes(Manifest()));
            }

            var version = Versions.FirstOrDefault(candidate => path == $"/dl/{candidate}.zip");
            return version is null
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
                : Respond(_archives[version]);
        }

        private static Task<HttpResponseMessage> Respond(byte[] body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

        private string Manifest()
        {
            var releases = Versions.Select(version => $$"""
                {
                  "version": "{{version}}", "date": "2026-11-21", "game": ["{{gameVersion}}"], "protocol": 201,
                  "notes": { "en": ["A fix."] },
                  "files": [ {
                    "kind": "mod", "name": "{{version}}.zip", "url": "dl/{{version}}.zip", "size": {{_archives[version].Length}},
                    "sha256": "{{Convert.ToHexString(SHA256.HashData(_archives[version]))}}"
                  } ],
                  "yanked": null
                }
                """);
            return $$"""{ "schema": 1, "latest": "{{Versions[0]}}", "releases": [ {{string.Join(",", releases)}} ] }""";
        }

        private static byte[] CreateArchive(string version)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(archive.CreateEntry("red4ext/plugins/NightCoop/NightCoop.dll").Open()))
            {
                writer.Write("plugin " + version);
            }

            return buffer.ToArray();
        }
    }
}
