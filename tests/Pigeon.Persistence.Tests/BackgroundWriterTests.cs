using Microsoft.Data.Sqlite;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Simulation;
using Pigeon.Simulation.Persistence;

namespace Pigeon.Persistence.Tests;

/// <summary>Фоновая запись переживает ошибки хранилища и сообщает о них сразу (#51).</summary>
public sealed class BackgroundWriterTests
{
    private const ulong Seed = 0x51UL;
    private static readonly PetId Pet = new(Guid.Parse("51515151-0000-4000-8000-000000000051"));

    [Fact]
    public async Task OneFailedSave_IsReportedAtOnceAndTheNextAttemptSavesEverything()
    {
        using var db = new TestDatabase();
        var store = new FlakyStore(await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken)) { FailNext = 1 };
        var failed = new TaskCompletionSource<PetWriteFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
        var simulation = new PigeonSimulation(Seed, SimulationOptions.Desktop);
        await using var writer = new BackgroundPetWriter(store, store, Pet, null, (_, _) => Task.CompletedTask);
        writer.Failed += (_, failure) => failed.TrySetResult(failure);

        long enqueued = 0;
        for (var i = 0; i < 5; i++)
        {
            simulation.Submit(new UserPoked());
            simulation.RunTicks(10);
            writer.Enqueue(simulation.CreateSnapshot(), simulation.GetJournalFrom(enqueued));
            enqueued = simulation.JournalLength;
        }

        // Хост узнаёт об ошибке без FlushAsync.
        var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(failure.WillRetry);
        Assert.IsType<SqliteException>(failure.Error);

        await writer.FlushAsync();
        Assert.Equal(simulation.Tick, (await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken))!.Tick);
        Assert.Equal(simulation.JournalLength, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
        Assert.Equal(simulation.JournalLength, writer.ConfirmedLength);
    }

    [Fact]
    public async Task SaveThatCommittedButReportedAnError_IsNotWrittenTwice()
    {
        // Ответ об успехе потерялся: база записала, а писатель получил ошибку. Длина журнала
        // сверяется с базой, и повтор не упирается в «записи уже есть».
        using var db = new TestDatabase();
        var store = new FlakyStore(await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken)) { FailAfterSaving = 1 };
        var simulation = new PigeonSimulation(Seed, SimulationOptions.Desktop);
        await using var writer = new BackgroundPetWriter(store, store, Pet, null, (_, _) => Task.CompletedTask);

        simulation.Submit(new UserPoked());
        simulation.RunTicks(10);
        writer.Enqueue(simulation.CreateSnapshot(), simulation.Journal);
        try
        {
            // Ошибку или уже удачный повтор — зависит от того, что писатель успел раньше.
            await writer.FlushAsync();
        }
        catch (InvalidOperationException)
        {
        }

        var saved = simulation.JournalLength;
        simulation.Submit(new UserPoked());
        simulation.RunTicks(10);
        writer.Enqueue(simulation.CreateSnapshot(), simulation.GetJournalFrom(saved));
        await writer.FlushAsync();

        Assert.Equal(simulation.JournalLength, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedSave_IsRetriedWithoutANewSnapshot()
    {
        using var db = new TestDatabase();
        var store = new FlakyStore(await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken)) { FailNext = 2 };
        var simulation = TestDatabase.RunSimulation(Seed, 700);
        var pauses = new List<TimeSpan>();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Saved += () => saved.TrySetResult();
        await using var writer = new BackgroundPetWriter(store, store, Pet, null, (pause, _) => { lock (pauses) { pauses.Add(pause); } return Task.CompletedTask; });

        writer.Enqueue(simulation.CreateSnapshot(), simulation.Journal);

        await saved.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(simulation.Tick, (await store.LoadLatestAsync(Pet, TestContext.Current.CancellationToken))!.Tick);
        lock (pauses)
        {
            Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], pauses);
        }
    }

    [Fact]
    public async Task CorruptedDatabase_StopsTheWriterAndSaysSo()
    {
        using var db = new TestDatabase();
        var store = new FlakyStore(await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken))
        {
            FailNext = int.MaxValue,
            Error = new SqliteException("database disk image is malformed", 11),
        };
        var failures = new List<PetWriteFailure>();
        var simulation = TestDatabase.RunSimulation(Seed, 400);
        var writer = new BackgroundPetWriter(store, store, Pet, null, (_, _) => Task.CompletedTask);
        writer.Failed += (_, failure) => { lock (failures) { failures.Add(failure); } };

        writer.Enqueue(simulation.CreateSnapshot(), simulation.Journal);

        await Assert.ThrowsAsync<InvalidOperationException>(writer.FlushAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.DisposeAsync().AsTask());
        lock (failures)
        {
            Assert.False(Assert.Single(failures).WillRetry);
        }

        Assert.Equal(1, store.Attempts);
    }

    [Fact]
    public async Task Journal_IsTrimmedEverySoManySaves()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var simulation = new PigeonSimulation(Seed, SimulationOptions.Desktop);
        var options = PetWriterOptions.Default with { Retention = new JournalRetention(0, TimeSpan.Zero), TrimEvery = 3 };
        await using var writer = new BackgroundPetWriter(store, Pet, options);

        long enqueued = 0;
        for (var i = 0; i < 6; i++)
        {
            simulation.Submit(new UserPoked());
            simulation.RunTicks(10);
            writer.Enqueue(simulation.CreateSnapshot(), simulation.GetJournalFrom(enqueued));
            enqueued = simulation.JournalLength;
            await writer.FlushAsync();
        }

        // После шестого сохранения журнал усечён до последнего снимка, длина та же.
        Assert.Equal("0", TestDatabase.Query(db.Path, "SELECT COUNT(*) FROM journal;"));
        Assert.Equal("1", TestDatabase.Query(db.Path, "SELECT COUNT(*) FROM snapshots;"));
        Assert.Equal(simulation.JournalLength, await store.GetLengthAsync(Pet, TestContext.Current.CancellationToken));
    }

    /// <summary>Хранилище, которое отказывает заданное число раз.</summary>
    private sealed class FlakyStore(SqlitePetStore inner) : IPetStore, IEventJournal
    {
        public int FailNext { get; set; }

        public int FailAfterSaving { get; set; }

        public Exception Error { get; init; } = new SqliteException("disk I/O error", 10, 3850);

        public int Attempts { get; private set; }

        public event Action? Saved;

        public ValueTask<SimulationSnapshot?> LoadLatestAsync(PetId petId, CancellationToken cancellationToken) =>
            inner.LoadLatestAsync(petId, cancellationToken);

        public async ValueTask SaveAsync(PetId petId, SimulationSnapshot snapshot, IReadOnlyList<JournalEntry> newJournalEntries, CancellationToken cancellationToken)
        {
            Attempts++;
            if (FailNext > 0)
            {
                FailNext--;
                throw Error;
            }

            await inner.SaveAsync(petId, snapshot, newJournalEntries, cancellationToken);
            if (FailAfterSaving > 0)
            {
                FailAfterSaving--;
                throw Error;
            }

            Saved?.Invoke();
        }

        public ValueTask<long> GetLengthAsync(PetId petId, CancellationToken cancellationToken) => inner.GetLengthAsync(petId, cancellationToken);

        public ValueTask AppendAsync(PetId petId, long firstPosition, IReadOnlyList<JournalEntry> entries, CancellationToken cancellationToken) =>
            inner.AppendAsync(petId, firstPosition, entries, cancellationToken);

        public IAsyncEnumerable<JournalEntry> ReadAsync(PetId petId, long fromPosition, CancellationToken cancellationToken) =>
            inner.ReadAsync(petId, fromPosition, cancellationToken);
    }
}
