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

    [Fact]
    public async Task Restores_the_saves_as_they_were_when_backed_up()
    {
        _root.WriteFile(@"saves\ManualSave-1\sav.dat", "before");
        var backup = await _service.CreateAsync(BackupKind.Manual, null, CancellationToken.None);
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
        var backup = await _service.CreateAsync(BackupKind.Manual, null, CancellationToken.None);
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
        var older = await _service.CreateAsync(BackupKind.BeforeUpdate, null, CancellationToken.None);
        var newer = await _service.CreateAsync(BackupKind.Manual, null, CancellationToken.None);

        Assert.Equal([newer.Path, older.Path], _service.List().Select(backup => backup.Path));
    }

    [Fact]
    public async Task Pruning_keeps_the_newest_automatic_backups_and_every_manual_one()
    {
        _root.WriteFile(@"saves\sav.dat", "x");
        var manual = await _service.CreateAsync(BackupKind.Manual, null, CancellationToken.None);
        var oldAutomatic = await _service.CreateAsync(BackupKind.BeforeUpdate, null, CancellationToken.None);
        var newAutomatic = await _service.CreateAsync(BackupKind.BeforeUpdate, null, CancellationToken.None);

        _service.Prune(automaticBackupsToKeep: 1);

        Assert.True(File.Exists(manual.Path));
        Assert.False(File.Exists(oldAutomatic.Path));
        Assert.True(File.Exists(newAutomatic.Path));
    }

    [Fact]
    public async Task Refuses_to_back_up_when_there_are_no_saves()
    {
        await Assert.ThrowsAsync<LauncherException>(() => _service.CreateAsync(BackupKind.Manual, null, CancellationToken.None));
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
