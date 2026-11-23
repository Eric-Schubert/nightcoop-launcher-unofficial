using NightCoopLauncher.Core.Saves;
using Xunit;

namespace NightCoopLauncher.Core.Tests;

public sealed class SaveBackupServiceTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly SteppingClock _clock = new();
    private readonly SaveBackupService _service;

    public SaveBackupServiceTests()
    {
        _service = new SaveBackupService(_root.Combine("saves"), _root.Combine("backups"), _clock);
    }

    public void Dispose() => _root.Dispose();

    private Task<SaveBackup> BackUp(BackupKind kind, string? label = null) =>
        _service.CreateAsync(kind, label, null, CancellationToken.None);

    [Fact]
    public async Task Restores_the_saves_as_they_were_when_backed_up()
    {
        _root.WriteFile(@"saves\ManualSave-1\sav.dat", "before");
        var backup = await BackUp(BackupKind.Manual);
        _root.WriteFile(@"saves\ManualSave-1\sav.dat", "after");
        _root.WriteFile(@"saves\ManualSave-2\sav.dat", "newer");

        await _service.RestoreAsync(backup, null, CancellationToken.None);

        Assert.Equal("before", File.ReadAllText(_root.Combine(@"saves\ManualSave-1\sav.dat")));
        Assert.False(Directory.Exists(_root.Combine(@"saves\ManualSave-2")));
    }

    [Fact]
    public async Task Backs_up_the_replaced_saves_before_a_restore()
    {
        _root.WriteFile(@"saves\ManualSave-1\sav.dat", "before");
        var backup = await BackUp(BackupKind.Manual);
        _root.WriteFile(@"saves\ManualSave-1\sav.dat", "after");

        await _service.RestoreAsync(backup, null, CancellationToken.None);
        var safetyBackup = Assert.Single(_service.List(), item => item.Kind == BackupKind.BeforeRestore);
        await _service.RestoreAsync(safetyBackup, null, CancellationToken.None);

        Assert.Equal("after", File.ReadAllText(_root.Combine(@"saves\ManualSave-1\sav.dat")));
    }

    [Fact]
    public async Task Lists_backups_newest_first()
    {
        _root.WriteFile(@"saves\sav.dat", "x");
        var older = await BackUp(BackupKind.BeforeUpdate);
        var newer = await BackUp(BackupKind.Manual);

        Assert.Equal([newer.Path, older.Path], _service.List().Select(backup => backup.Path));
    }

    [Fact]
    public async Task Remembers_the_version_a_backup_was_made_for()
    {
        _root.WriteFile(@"saves\sav.dat", "x");
        await BackUp(BackupKind.BeforeUpdate, "0.2.2-beta");

        var listed = Assert.Single(_service.List());

        Assert.Equal(BackupKind.BeforeUpdate, listed.Kind);
        Assert.Equal("0.2.2-beta", listed.Label);
    }

    [Fact]
    public async Task Pruning_thins_out_backups_before_game_starts_and_keeps_the_rest()
    {
        _root.WriteFile(@"saves\sav.dat", "x");
        var manual = await BackUp(BackupKind.Manual);
        var beforeUpdate = await BackUp(BackupKind.BeforeUpdate, "0.2.1-beta");
        var oldLaunch = await BackUp(BackupKind.BeforeLaunch);
        var newLaunch = await BackUp(BackupKind.BeforeLaunch);

        _service.Prune(automaticBackupsToKeep: 1);

        Assert.True(File.Exists(manual.Path));
        Assert.True(File.Exists(beforeUpdate.Path));
        Assert.False(File.Exists(oldLaunch.Path));
        Assert.True(File.Exists(newLaunch.Path));
    }

    [Fact]
    public async Task Refuses_to_back_up_when_there_are_no_saves()
    {
        var exception = await Assert.ThrowsAsync<LauncherException>(() => BackUp(BackupKind.Manual));

        Assert.Equal(LauncherError.NoSaves, exception.Error);
    }

    /// <summary>Advances on every read, so backups made in one test get distinct timestamps.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow()
        {
            _now = _now.AddMinutes(1);
            return _now;
        }
    }
}
