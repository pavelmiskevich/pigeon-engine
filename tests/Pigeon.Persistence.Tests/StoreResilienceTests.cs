using Microsoft.Data.Sqlite;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Simulation;

namespace Pigeon.Persistence.Tests;

/// <summary>Повтор открытия, восстановление испорченной базы и ограничение журнала (#17).</summary>
public sealed class StoreResilienceTests
{
    private const ulong Seed = 0x17_17UL;
    private static readonly PetId Pet = new(Guid.Parse("0d6f2c1a-17aa-4b3e-9c5d-7e8f9a0b1c2d"));
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TransientIoError_IsRetriedWithGrowingPauses()
    {
        var calls = 0;
        var pauses = new List<TimeSpan>();

        var (result, attempts, errors) = await TransientRetry.RunAsync(
            () => ++calls < 3 ? throw new SqliteException("disk I/O error", 10, 3850) : ValueTask.FromResult(42),
            TimeSpan.FromSeconds(10),
            (pause, _) => { pauses.Add(pause); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        Assert.Equal((42, 3), (result, attempts));
        Assert.Equal([3850, 3850], errors);
        Assert.Equal(2, pauses.Count);
        Assert.True(pauses[1] > pauses[0]);
    }

    [Fact]
    public async Task TransientErrorThatOutlastsTheBudget_IsRethrown()
    {
        var pauses = TimeSpan.Zero;

        var error = await Assert.ThrowsAsync<SqliteException>(async () => await TransientRetry.RunAsync<int>(
            () => throw new SqliteException("database is locked", 5),
            TimeSpan.FromSeconds(10),
            (pause, _) => { pauses += pause; return Task.CompletedTask; },
            TestContext.Current.CancellationToken));

        Assert.Equal(5, error.SqliteErrorCode);
        Assert.InRange(pauses, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Corruption_IsNotRetried()
    {
        var calls = 0;

        await Assert.ThrowsAsync<SqliteException>(async () => await TransientRetry.RunAsync<int>(
            () => { calls++; throw new SqliteException("file is not a database", 26); },
            TimeSpan.FromSeconds(10),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CorruptedDatabase_IsRestoredFromTheLatestBackupAndKeptAside()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var simulation = TestDatabase.RunSimulation(Seed, 1200);
        await store.SaveAsync(Pet, simulation.CreateSnapshot(), simulation.Journal, TestContext.Current.CancellationToken);
        var backups = Path.Combine(db.Directory, "backups");
        var backup = new SqliteBackupRotator(db.Path, backups).CreateBackup(Now);
        await File.WriteAllTextAsync(db.Path, new string('x', 8192), TestContext.Current.CancellationToken);

        var restored = await SqlitePetStore.OpenAsync(db.Path, new StoreOpenOptions(backups), TestContext.Current.CancellationToken);

        Assert.Equal(backup, restored.OpenReport.RestoredFrom);
        Assert.True(File.Exists(restored.OpenReport.QuarantinedTo), "испорченная база отложена рядом");
        Assert.Equal(simulation.Tick, (await restored.LoadLatestAsync(Pet, TestContext.Current.CancellationToken))!.Tick);
    }

    [Fact]
    public async Task CorruptedDatabaseWithoutBackups_IsAClearError()
    {
        using var db = new TestDatabase();
        await File.WriteAllTextAsync(db.Path, new string('x', 8192), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<PetStoreCorruptedException>(async () =>
            await SqlitePetStore.OpenAsync(db.Path, new StoreOpenOptions(Path.Combine(db.Directory, "backups")), TestContext.Current.CancellationToken));

        Assert.Contains(db.Path, error.Message, StringComparison.Ordinal);
        Assert.IsType<SqliteException>(error.InnerException);
    }

    [Fact]
    public async Task TrimmingBySize_KeepsTheLoadedStateAndReplayFromTheOldestSnapshot()
    {
        using var db = new TestDatabase();
        var (store, simulation) = await LiveAsync(db.Path, saves: 10);
        var latestBefore = PigeonSimulation.Restore((await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken))!).ComputeStateHash();
        var journalBefore = await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken);

        var result = await store.TrimJournalAsync(Pet, new JournalRetention(MaxBytes: 300, MaxAge: TimeSpan.FromDays(365)), TestContext.Current.CancellationToken);

        Assert.InRange(result.SnapshotsRemoved, 1, 9);
        Assert.True(result.EntriesRemoved > 0);
        var latest = (await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken))!;
        Assert.Equal(latestBefore, PigeonSimulation.Restore(latest).ComputeStateHash());
        Assert.Equal(journalBefore, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));

        // Воспроизведение — с самого старого оставшегося снимка по хвосту журнала.
        var oldest = (await store.LoadOldestAsync(Pet, TestContext.Current.CancellationToken))!;
        Assert.Equal(result.ReplayFrom, new ReplayStart(oldest.Tick, oldest.JournalLength));
        var tail = await store.ReadAsync(Pet, oldest.JournalLength, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(latestBefore, PigeonSimulation.ReplayFrom(oldest, tail, latest.Tick).ComputeStateHash());
        Assert.Equal(simulation.ComputeStateHash(), latestBefore);
    }

    [Fact]
    public async Task TrimmingByAge_LeavesTheLatestSnapshotAndTheStoreKeepsSaving()
    {
        using var db = new TestDatabase();
        var (store, simulation) = await LiveAsync(db.Path, saves: 5);

        // Снимки идут через 1000 тиков (100 с): моложе 150 с от последнего — только два.
        var result = await store.TrimJournalAsync(Pet, new JournalRetention(MaxBytes: long.MaxValue, MaxAge: TimeSpan.FromSeconds(150)), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.SnapshotsRemoved);
        Assert.Equal(simulation.Tick - 1000, result.ReplayFrom!.Tick);

        // Журнал после усечения продолжается с той же позиции.
        var saved = simulation.JournalLength;
        Advance(simulation);
        await store.SaveAsync(Pet, simulation.CreateSnapshot(), simulation.GetJournalFrom(saved), TestContext.Current.CancellationToken);
        Assert.Equal(simulation.JournalLength, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JournalTrimmedToNothing_StillKnowsItsLength()
    {
        using var db = new TestDatabase();
        var (store, simulation) = await LiveAsync(db.Path, saves: 3);

        await store.TrimJournalAsync(Pet, new JournalRetention(MaxBytes: 0, MaxAge: TimeSpan.Zero), TestContext.Current.CancellationToken);

        Assert.Equal("0", TestDatabase.Query(db.Path, "SELECT COUNT(*) FROM journal;"));
        Assert.Equal(simulation.JournalLength, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    /// <summary>Питомец живёт и сохраняется каждые 1000 тиков, как в продукте.</summary>
    private static async Task<(SqlitePetStore Store, PigeonSimulation Simulation)> LiveAsync(string path, int saves)
    {
        var store = await SqlitePetStore.OpenAsync(path, TestContext.Current.CancellationToken);
        var simulation = new PigeonSimulation(Seed, SimulationOptions.Desktop);
        long saved = 0;
        for (var i = 0; i < saves; i++)
        {
            Advance(simulation);
            await store.SaveAsync(Pet, simulation.CreateSnapshot(), simulation.GetJournalFrom(saved), TestContext.Current.CancellationToken);
            saved = simulation.JournalLength;
        }

        return (store, simulation);
    }

    private static void Advance(PigeonSimulation simulation)
    {
        for (var i = 0; i < 1000; i++)
        {
            if ((simulation.Tick + 1) % 97 == 0)
            {
                simulation.Submit(new UserPoked());
            }

            simulation.RunTicks(1);
        }
    }
}
