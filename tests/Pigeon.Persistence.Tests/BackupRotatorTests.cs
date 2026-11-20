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

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("blank")]
    public async Task NoPetDatabase_MakesNoBackupAndKeepsTheOldOnes(string source)
    {
        // #59: несуществующая, нулевая или пустая (без схемы) база не копируется, и ротация не
        // вытесняет настоящие копии пустыми.
        using var db = new TestDatabase();
        await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var backups = Path.Combine(db.Directory, "backups");
        var real = new SqliteBackupRotator(db.Path, backups);
        for (var day = 0; day < SqliteBackupRotator.DefaultKeep; day++)
        {
            real.CreateBackup(Start.AddDays(day));
        }

        var before = real.ListBackups();
        var path = Path.Combine(db.Directory, source + ".db");
        if (source == "empty")
        {
            await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        }
        else if (source == "blank")
        {
            TestDatabase.Query(path, "PRAGMA user_version;");
        }

        var rotator = new SqliteBackupRotator(path, backups);

        Assert.ThrowsAny<IOException>(() => rotator.CreateBackup(Start.AddDays(30)));
        Assert.Equal(before, rotator.ListBackups());
        Assert.Empty(Directory.EnumerateFiles(backups, "*.tmp"));
        Assert.Equal(source != "missing", File.Exists(path));
    }

    [Fact]
    public async Task LeftoverTemporaryFiles_AreRemovedByTheNextBackup()
    {
        using var db = new TestDatabase();
        await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var backups = Path.Combine(db.Directory, "backups");
        Directory.CreateDirectory(backups);
        await File.WriteAllTextAsync(Path.Combine(backups, "pigeon-20261001-000000.db.tmp"), "обрыв", TestContext.Current.CancellationToken);

        new SqliteBackupRotator(db.Path, backups).CreateBackup(Start);

        Assert.Empty(Directory.EnumerateFiles(backups, "*.tmp"));
    }
}
