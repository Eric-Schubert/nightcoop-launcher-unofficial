using System.IO.Compression;
using NightCoopLauncher.Core.Install;
using Xunit;

namespace NightCoopLauncher.Core.Tests;

public class ArchiveDeployerTests
{
    private static string CreateArchive(TempDirectory directory, params string[] entryNames)
    {
        var path = directory.Combine("mod.zip");
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
        using var work = new TempDirectory();
        using var game = new TempDirectory();
        game.WriteFile(@"red4ext\RED4ext.dll", "old");
        var archive = CreateArchive(work, "red4ext/RED4ext.dll", "red4ext/plugins/NightCoop/NightCoop.dll");

        var files = ArchiveDeployer.Deploy(archive, work.Combine("staging"), game.Path, []);

        Assert.Equal([@"red4ext\RED4ext.dll", @"red4ext\plugins\NightCoop\NightCoop.dll"], files);
        Assert.Equal("new", File.ReadAllText(game.Combine(@"red4ext\RED4ext.dll")));
        Assert.True(File.Exists(game.Combine(@"red4ext\plugins\NightCoop\NightCoop.dll")));
    }

    [Fact]
    public void Removes_dropped_files_only_inside_the_mod_folders()
    {
        using var work = new TempDirectory();
        using var game = new TempDirectory();
        var droppedModFile = game.WriteFile(@"red4ext\plugins\NightCoop\old.dll", "old");
        var droppedSharedFile = game.WriteFile(@"red4ext\shared.dll", "old");
        var userFile = game.WriteFile(@"red4ext\plugins\NightCoop\name.txt", "player");
        var archive = CreateArchive(work, "red4ext/plugins/NightCoop/NightCoop.dll");

        ArchiveDeployer.Deploy(
            archive,
            work.Combine("staging"),
            game.Path,
            [@"red4ext\plugins\NightCoop\old.dll", @"red4ext\shared.dll"]);

        Assert.False(File.Exists(droppedModFile));
        Assert.True(File.Exists(droppedSharedFile));
        Assert.True(File.Exists(userFile));
    }

    [Fact]
    public void Rejects_an_entry_that_escapes_the_target_folder()
    {
        using var work = new TempDirectory();
        using var game = new TempDirectory();
        var archive = CreateArchive(work, "../outside.dll");

        Assert.Throws<LauncherException>(() => ArchiveDeployer.Deploy(archive, work.Combine("staging"), game.Path, []));
        Assert.Empty(Directory.EnumerateFileSystemEntries(game.Path));
    }
}
