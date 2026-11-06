using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Persistence.Sqlite.Serialization;
using Pigeon.Simulation;
using Pigeon.World.Abstractions;

namespace Pigeon.Persistence.Tests;

/// <summary>Хранение личности, потребностей, настроения, намерений и входов с данными (M1.1, M1.2).</summary>
public sealed class PigeonDomainStorageTests
{
    private const ulong Seed = 0x5EED_0016UL;
    private static readonly PetId Pet = new(Guid.Parse("5b2e8d14-9c3a-4f71-a6d0-7e1f3b5c9a28"));

    [Fact]
    public async Task AbsenceEntry_KeepsItsDurationInTheJournal()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        var away = TimeSpan.FromHours(9) + TimeSpan.FromTicks(1_234_567);
        JournalEntry[] entries = [new(3, new UserPoked()), new(4, new AbsenceEnded(away))];

        await store.AppendAsync(Pet, 0, entries, TestContext.Current.CancellationToken);
        var read = await store.ReadAsync(Pet, 0, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(entries, read);
        Assert.Equal(away, Assert.IsType<AbsenceEnded>(read[1].Event).Duration);
        Assert.Equal("", TestDatabase.Query(db.Path, "SELECT COALESCE(payload, '') FROM journal WHERE position = 0;"));
    }

    [Fact]
    public void Snapshot_RoundTripsPersonalityNeedsMoodAndPendingAbsence()
    {
        var simulation = TestDatabase.RunSimulation(Seed, 4_000);
        simulation.Submit(new AbsenceEnded(TimeSpan.FromMinutes(42)));
        var snapshot = simulation.CreateSnapshot();

        var restored = SnapshotSerializer.Deserialize(SnapshotSerializer.Serialize(snapshot));

        Assert.Equal(SimulationSnapshot.CurrentFormatVersion, restored.FormatVersion);
        Assert.Equal(snapshot.Pigeon, restored.Pigeon);
        Assert.Equal(snapshot.Pending, restored.Pending);
    }

    private static readonly Affordance[] World =
    [
        new(new AffordanceId(7), new PlaceKey("poi:клумба"), AffordanceKind.PointOfInterest, 0.6, 0.15, TimeSpan.FromSeconds(12.5)),
        new(new AffordanceId(8), new PlaceKey("cursor"), AffordanceKind.AttentionTarget, 0.7, 0.2, TimeSpan.FromSeconds(8)),
    ];

    [Fact]
    public async Task ObservedWorld_RoundTripsThroughTheJournal()
    {
        using var db = new TestDatabase();
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        JournalEntry[] entries =
        [
            new(1, new AffordancesObserved(World)),
            new(30, new AffordancesObserved([])),
            new(31, new FootingLost(DisplacementReason.CapabilityLost)),
            new(32, new ActionFinished(IntentKind.Relocate, 20, ActionOutcome.Completed)),
            new(33, new ActionFinished(IntentKind.Explore, 25, ActionOutcome.Failed, ActionFailure.TargetGone)),
        ];

        await store.AppendAsync(Pet, 0, entries, TestContext.Current.CancellationToken);
        var read = await store.ReadAsync(Pet, 0, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(entries, read);
        Assert.Equal(World, Assert.IsType<AffordancesObserved>(read[0].Event).Affordances);
    }

    [Fact]
    public void Snapshot_RoundTripsIntentPlanCooldownsAndWorld()
    {
        var simulation = new PigeonSimulation(Seed, SimulationOptions.Desktop, Personality.Average with { Curiosity = 95 });
        simulation.Submit(new AffordancesObserved(World));
        simulation.RunTicks(9_000);
        var snapshot = simulation.CreateSnapshot();

        var restored = SnapshotSerializer.Deserialize(SnapshotSerializer.Serialize(snapshot));

        Assert.Equal(snapshot.Pigeon, restored.Pigeon);
        Assert.NotEmpty(snapshot.Cooldowns);
        Assert.Equal(snapshot.Cooldowns, restored.Cooldowns);
        Assert.Equal(World, restored.Affordances);
        var original = PigeonSimulation.Restore(snapshot);
        var reloaded = PigeonSimulation.Restore(restored);
        original.RunTicks(3_000);
        reloaded.RunTicks(3_000);
        Assert.Equal(original.ComputeStateHash(), reloaded.ComputeStateHash());
    }

    [Fact]
    public void VersionTwoSnapshot_EndsItsIntentOnTheNextTick()
    {
        const string v2 = """
            {"formatVersion":2,"seed":7,"stepTicks":1000000,"maxTicksPerAdvance":50,"decisionIntervalTicks":30,
             "tick":500,"accumulatedTicks":0,"fatigue":0.4,"intent":1,"intentStartedTick":450,
             "randomStreams":[],"journalLength":0,"pending":[],
             "personality":{"curiosity":50,"playfulness":50,"sociability":50,"boldness":50,"calmness":50,"independence":50,"energy":50},
             "boredom":0.2,"attention":0.3,"mood":{"valence":0.1,"arousal":0.4,"stress":0}}
            """;

        var snapshot = SnapshotSerializer.Deserialize(v2);
        var simulation = PigeonSimulation.Restore(snapshot);
        simulation.RunTicks(1);

        Assert.Equal(SimulationSnapshot.CurrentFormatVersion, snapshot.FormatVersion);
        Assert.Equal(500, snapshot.Pigeon.IntentEndsTick);
        Assert.Empty(snapshot.Cooldowns);
        Assert.Empty(snapshot.Affordances);
        Assert.Equal(501, simulation.Pigeon.IntentStartedTick);
    }

    [Fact]
    public void VersionOneSnapshot_GetsTheNewbornPersonalityOfItsSeed()
    {
        const string v1 = """
            {"formatVersion":1,"seed":7,"stepTicks":1000000,"maxTicksPerAdvance":50,"decisionIntervalTicks":10,
             "tick":30,"accumulatedTicks":0,"fatigue":0.16,"intent":1,"intentStartedTick":20,
             "randomStreams":[{"name":"ai.decision","state":9876543210987,"increment":4242424243}],
             "journalLength":2,"pending":[]}
            """;

        var snapshot = SnapshotSerializer.Deserialize(v1);
        var newborn = new PigeonSimulation(7, SimulationOptions.Desktop).Pigeon;

        Assert.Equal(SimulationSnapshot.CurrentFormatVersion, snapshot.FormatVersion);
        Assert.Equal(newborn.Personality, snapshot.Pigeon.Personality);
        Assert.Equal(newborn.Mood, snapshot.Pigeon.Mood);
        Assert.Equal(new Needs(0.16, 0.0, 0.0), snapshot.Pigeon.Needs);
        Assert.Equal(IntentKind.Rest, snapshot.Pigeon.Intent);
        Assert.Contains(snapshot.RandomStreams, s => s.Key == "ai.decision" && s.Value == new Pcg32State(9876543210987, 4242424243));
        Assert.Contains(snapshot.RandomStreams, s => s.Key == PigeonSimulation.PersonalityStreamName);
        Assert.Equal(30, PigeonSimulation.Restore(snapshot).Tick);
    }

    [Fact]
    public void VersionTwoSnapshot_WithoutDomainFields_IsRejected()
    {
        var json = SnapshotSerializer.Serialize(TestDatabase.RunSimulation(Seed, 10).CreateSnapshot());
        var broken = json[..json.IndexOf(",\"personality\"", StringComparison.Ordinal)] + "}";

        Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Deserialize(broken));
    }

    [Fact]
    public async Task VersionOneFixture_JournalReadsWithoutPayload()
    {
        using var db = new TestDatabase();
        TestDatabase.CreateFromFixture(db.Path, "schema-v1.sql");
        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);

        var journal = await store
            .ReadAsync(new PetId(Guid.Parse("6f1c2a5e-0b7d-4c1e-9a3b-2d4e6f8a0c11")), 0, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new JournalEntry(12, new UserPoked()), new JournalEntry(25, new UserPoked())], journal);
        Assert.Equal("1", TestDatabase.Query(db.Path, "SELECT COUNT(*) FROM pragma_table_info('journal') WHERE name = 'payload';"));
    }
}
