using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Persistence.Sqlite.Serialization;
using Pigeon.Simulation;

namespace Pigeon.Persistence.Tests;

public sealed class PetStoreTests
{
    private const ulong Seed = 0x5EED_0006UL;
    private static readonly PetId Pet = new(Guid.Parse("0a6c2f4e-6b1d-4f20-8c3e-5a7b9d1e3f50"));

    [Fact]
    public async Task SavedSnapshot_RestoresTheSameSimulation()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var original = TestDatabase.RunSimulation(Seed, 2_500);

        await store.SaveAsync(Pet, original.CreateSnapshot(), original.Journal, TestContext.Current.CancellationToken);
        var loaded = await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken);
        var restored = PigeonSimulation.Restore(loaded!);
        original.RunTicks(1_000);
        restored.RunTicks(1_000);

        Assert.Equal(original.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void SnapshotJson_RoundTripsExactly()
    {
        var simulation = TestDatabase.RunSimulation(Seed, 1_234);
        simulation.Advance(TimeSpan.FromMilliseconds(37));
        simulation.Submit(new UserPoked());
        var snapshot = simulation.CreateSnapshot();

        var restored = SnapshotSerializer.Deserialize(SnapshotSerializer.Serialize(snapshot));

        Assert.Equal(snapshot.Pigeon, restored.Pigeon);
        Assert.Equal(snapshot.Accumulated, restored.Accumulated);
        Assert.Equal(snapshot.RandomStreams, restored.RandomStreams);
        Assert.Equal(snapshot.Pending, restored.Pending);
        Assert.Equal(snapshot.Options, restored.Options);
        Assert.Equal(PigeonSimulation.Restore(snapshot).ComputeStateHash(), PigeonSimulation.Restore(restored).ComputeStateHash());
    }

    [Fact]
    public async Task LoadLatest_ReturnsTheNewestSnapshotAndPrunesOldOnes()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var simulation = new PigeonSimulation(Seed, SimulationOptions.Desktop);
        long saved = 0;

        for (var i = 0; i < SqlitePetStore.SnapshotsToKeep + 5; i++)
        {
            simulation.Submit(new UserPoked());
            simulation.RunTicks(10);
            await store.SaveAsync(Pet, simulation.CreateSnapshot(), simulation.GetJournalFrom(saved), TestContext.Current.CancellationToken);
            saved = simulation.JournalLength;
        }

        var latest = await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken);

        Assert.Equal(simulation.Tick, latest!.Tick);
        Assert.Equal(simulation.JournalLength, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
        Assert.Equal(SqlitePetStore.SnapshotsToKeep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TestDatabase.Query(db.Path, "SELECT COUNT(*) FROM snapshots;"));
    }

    [Fact]
    public async Task UnknownPet_HasNoSnapshotAndEmptyJournal()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);

        Assert.Null(await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken));
        Assert.Equal(0, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Journal_ReadsFromAPositionAndRejectsGapsAndOverlaps()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        JournalEntry[] entries = [new(5, new UserPoked()), new(9, new UserPoked()), new(20, new UserPoked())];

        await store.AppendAsync(Pet, 0, entries, TestContext.Current.CancellationToken);
        var tail = await store.ReadAsync(Pet, 1, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(entries[1..], tail);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.AppendAsync(Pet, 5, entries, TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.AppendAsync(Pet, 2, entries, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(3, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedSave_LeavesNeitherSnapshotNorJournal()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var simulation = TestDatabase.RunSimulation(Seed, 1_000);
        await store.AppendAsync(Pet, 0, [new JournalEntry(1, new UserPoked())], TestContext.Current.CancellationToken);

        // Журнал в базе длиннее, чем ожидает снимок: сохранение отклоняется целиком.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(Pet, simulation.CreateSnapshot(), simulation.Journal, TestContext.Current.CancellationToken).AsTask());

        Assert.Null(await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken));
        Assert.Equal(1, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BackgroundWriter_PersistsInOrderOffTheCallingThread()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var simulation = new PigeonSimulation(Seed, SimulationOptions.Desktop);

        await using (var writer = new BackgroundPetWriter(store, Pet))
        {
            long saved = 0;
            for (var i = 0; i < 20; i++)
            {
                simulation.Submit(new UserPoked());
                simulation.RunTicks(25);
                writer.Enqueue(simulation.CreateSnapshot(), simulation.GetJournalFrom(saved));
                saved = simulation.JournalLength;
            }

            await writer.FlushAsync();
            Assert.Equal(simulation.Tick, (await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken))!.Tick);
        }

        Assert.Equal(20, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BackgroundWriter_ReportsAFailedSave()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var simulation = TestDatabase.RunSimulation(Seed, 1_000);
        var writer = new BackgroundPetWriter(store, Pet);

        // Новые записи не стыкуются с пустым журналом в базе.
        writer.Enqueue(simulation.CreateSnapshot(), []);

        await Assert.ThrowsAsync<InvalidOperationException>(writer.FlushAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.DisposeAsync().AsTask());
    }
}
