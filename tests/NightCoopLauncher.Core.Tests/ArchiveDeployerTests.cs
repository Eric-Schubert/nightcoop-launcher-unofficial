using System.IO.Compression;
using NightCoopLauncher.Core.Install;
using Xunit;

namespace NightCoopLauncher.Core.Tests;

public sealed class ArchiveDeployerTests : IDisposable
{
    private const string Plugin = @"red4ext\plugins\NightCoop\NightCoop.dll";
    private const string Loader = @"red4ext\RED4ext.dll";

    private readonly TempDirectory _work = new();
    private readonly TempDirectory _game = new();

    public void Dispose()
    {
        _work.Dispose();
        _game.Dispose();
    }

    private string CreateArchive(params string[] entryNames)
    {
        var path = _work.Combine("mod.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in entryNames)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("new");
        }

        return path;
    }

    [Fact]
    public void Copies_the_archive_over_the_game_folder()
    {
        _game.WriteFile(Loader, "old");
        var archive = CreateArchive("red4ext/RED4ext.dll", "red4ext/plugins/NightCoop/NightCoop.dll");

        var files = ArchiveDeployer.Deploy(archive, _work.Path, _game.Path, []);

        Assert.Equal([Loader, Plugin], files.Select(file => file.RelativePath));
        Assert.Equal("new", File.ReadAllText(_game.Combine(Loader)));
        Assert.True(File.Exists(_game.Combine(Plugin)));
    }

    [Fact]
    public void Removes_dropped_files_only_inside_the_mod_folders()
    {
        var droppedModFile = _game.WriteFile(@"red4ext\plugins\NightCoop\old.dll", "old");
        var droppedSharedFile = _game.WriteFile(@"red4ext\shared.dll", "old");
        var userFile = _game.WriteFile(@"red4ext\plugins\NightCoop\name.txt", "player");
        var archive = CreateArchive("red4ext/plugins/NightCoop/NightCoop.dll");

        ArchiveDeployer.Deploy(archive, _work.Path, _game.Path, [@"red4ext\plugins\NightCoop\old.dll", @"red4ext\shared.dll"]);

        Assert.False(File.Exists(droppedModFile));
        Assert.True(File.Exists(droppedSharedFile));
        Assert.True(File.Exists(userFile));
    }

    [Fact]
    public void Rejects_an_entry_that_escapes_the_target_folder()
    {
        var archive = CreateArchive("../outside.dll");

        Assert.Throws<LauncherException>(() => ArchiveDeployer.Deploy(archive, _work.Path, _game.Path, []));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_game.Path));
    }

    [Fact]
    public void Puts_replaced_files_back_when_copying_fails_halfway()
    {
        _game.WriteFile(Loader, "old");
        var lockedFile = _game.WriteFile(Plugin, "old");
        var archive = CreateArchive("red4ext/RED4ext.dll", "red4ext/new-file.dll", "red4ext/plugins/NightCoop/NightCoop.dll");

        using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var exception = Assert.Throws<LauncherException>(() => ArchiveDeployer.Deploy(archive, _work.Path, _game.Path, []));
            Assert.Equal(LauncherError.GameFolderNotWritable, exception.Error);
        }

        Assert.Equal("old", File.ReadAllText(_game.Combine(Loader)));
        Assert.False(File.Exists(_game.Combine(@"red4ext\new-file.dll")));
    }

    [Fact]
    public void Reports_an_installation_as_intact_until_a_file_changes()
    {
        var archive = CreateArchive("red4ext/RED4ext.dll", "red4ext/plugins/NightCoop/NightCoop.dll");
        var files = ArchiveDeployer.Deploy(archive, _work.Path, _game.Path, []);
        var state = new InstallState(
            "0.2.2-beta", 201, "2.5", "AA", "2026-11-23T12:00:00Z",
            files.Select(file => file.RelativePath).ToList(),
            files.ToDictionary(file => file.RelativePath, file => file.Sha256));

        Assert.Equal(InstallHealth.Intact, InstallVerifier.Check(_game.Path, state));

        File.WriteAllText(_game.Combine(Loader), "tampered");

        Assert.Equal(InstallHealth.Damaged, InstallVerifier.Check(_game.Path, state));
    }

    [Fact]
    public void Cannot_judge_an_installation_without_recorded_hashes()
    {
        _game.WriteFile(Plugin, "plugin");
        var stateFromScript = new InstallState("0.2.2-beta", 201, "2.5", "AA", "2026-11-23T12:00:00Z", [Plugin]);

        Assert.Equal(InstallHealth.Unknown, InstallVerifier.Check(_game.Path, stateFromScript));
        Assert.Equal(InstallHealth.Unknown, InstallVerifier.Check(_game.Path, null));
    }

    [Fact]
    public void Reports_a_game_without_the_mod_as_not_installed()
    {
        Assert.Equal(InstallHealth.NotInstalled, InstallVerifier.Check(_game.Path, null));
    }
}
