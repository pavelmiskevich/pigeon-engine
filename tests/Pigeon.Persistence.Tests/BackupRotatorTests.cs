using Pigeon.Core;
using Pigeon.Persistence.Sqlite;

namespace Pigeon.Persistence.Tests;

public sealed class BackupRotatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly PetId Pet = new(Guid.Parse("4b2e8d10-3c5a-4e7f-9b1d-6a8c0e2f4a61"));

    [Fact]
    public async Task Backup_IsAConsistentStandaloneCopy()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var simulation = TestDatabase.RunSimulation(17, 800);
        await store.SaveAsync(Pet, simulation.CreateSnapshot(), simulation.Journal, TestContext.Current.CancellationToken);
        var rotator = new SqliteBackupRotator(db.Path, Path.Combine(db.Directory, "backups"));

        var backup = rotator.CreateBackup(Start);

        Assert.Equal("ok", TestDatabase.Query(backup, "PRAGMA integrity_check;"));
        Assert.Equal("delete", TestDatabase.Query(backup, "PRAGMA journal_mode;"));
        var fromBackup = await SqlitePetStore.OpenAsync(backup, TestContext.Current.CancellationToken);
        Assert.Equal(simulation.Tick, (await fromBackup.LoadLatestAsync(Pet, TestContext.Current.CancellationToken))!.Tick);
    }

    [Fact]
    public async Task Rotation_KeepsTheNewestSevenDailyBackups()
    {
        using var db = new TestDatabase();
        await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var rotator = new SqliteBackupRotator(db.Path, Path.Combine(db.Directory, "backups"));

        for (var day = 0; day < 10; day++)
        {
            Assert.NotNull(rotator.BackupIfDue(Start.AddDays(day)));
            Assert.Null(rotator.BackupIfDue(Start.AddDays(day).AddHours(5)));
        }

        var backups = rotator.ListBackups().Select(Path.GetFileName).ToList();
        Assert.Equal(SqliteBackupRotator.DefaultKeep, backups.Count);
        Assert.Equal("pigeon-20261010-093000.db", backups[0]);
        Assert.Equal("pigeon-20261004-093000.db", backups[^1]);
    }
}
