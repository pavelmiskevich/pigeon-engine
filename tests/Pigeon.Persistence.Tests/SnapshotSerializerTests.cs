using Pigeon.Core;
using Pigeon.Persistence.Sqlite.Serialization;
using Pigeon.Simulation;
using Pigeon.World.Abstractions;

namespace Pigeon.Persistence.Tests;

/// <summary>Снимок формата 4 (#33): память мест и хроника жизни в хранилище, перевод старых снимков.</summary>
public sealed class SnapshotSerializerTests
{
    /// <summary>
    /// Снимок формата 3, записанный сериализатором до #33 (seed 21, 1500 тиков, мир из двух мест,
    /// ожидающий тычок) — таким он лежит в базе у пользователей.
    /// </summary>
    private const string Version3 = """
            {"formatVersion":3,"seed":21,"stepTicks":1000000,"maxTicksPerAdvance":50,"decisionIntervalTicks":30,"tick":1500,
            "accumulatedTicks":0,"fatigue":0.07375000000000227,"intent":0,"intentStartedTick":891,"randomStreams":[{"name":"ai.decision",
            "state":17593936112754293765,"increment":3817855922996920113},{"name":"pigeon.personality","state":12252045893537163445,
            "increment":17332533355682889279}],"journalLength":1,"pending":[{"tick":1501,"type":"user-poked"}],"personality":{"curiosity":46,
            "playfulness":40,"sociability":28,"boldness":84,"calmness":51,"independence":55,"energy":32},"boredom":0.11999999999999646,
            "attention":0.0308750000000004,"mood":{"valence":0.036,"arousal":0.397,"stress":0},"intentTarget":null,
            "intentEndsTick":1565,"cooldowns":[],"affordances":[{"id":1,"placeKey":"desktop/laptop/taskbar","kind":0,
            "attractiveness":0.5,"risk":0,"travelCostTicks":50000000},{"id":2,"placeKey":"desktop/external","kind":8,
            "attractiveness":0.6,"risk":0.1,"travelCostTicks":300000000}]}
            """;

    private static readonly Affordance Taskbar =
        new(new AffordanceId(1), new PlaceKey("desktop/laptop/taskbar"), AffordanceKind.PerchSpot, 0.5, 0.0, TimeSpan.FromSeconds(5));

    private static readonly Affordance External =
        new(new AffordanceId(2), new PlaceKey("desktop/external"), AffordanceKind.TerritoryZone, 0.6, 0.1, TimeSpan.FromSeconds(30));

    [Fact]
    public void Version4_RoundTripsMemoryAndLifeStory()
    {
        var original = LivedSnapshot();

        var restored = SnapshotSerializer.Deserialize(SnapshotSerializer.Serialize(original));

        Assert.Equal(SimulationSnapshot.CurrentFormatVersion, restored.FormatVersion);
        Assert.Equal(original.Pigeon.Memory, restored.Pigeon.Memory);
        Assert.Equal(original.Pigeon.LifeStory, restored.Pigeon.LifeStory);
        Assert.Equal(External.PlaceKey, restored.Pigeon.IntentPlace);
        Assert.Equal(86_400, restored.Pigeon.AbsenceSeconds);
        Assert.Equal(
            PigeonSimulation.Restore(original).ComputeStateHash(),
            PigeonSimulation.Restore(restored).ComputeStateHash());
    }

    [Fact]
    public void Version3_IsUpgradedWithEmptyMemoryAndBornEntry()
    {
        var snapshot = SnapshotSerializer.Deserialize(Version3);
        var simulation = PigeonSimulation.Restore(snapshot);
        simulation.RunTicks(10);

        Assert.Equal(4, snapshot.FormatVersion);
        Assert.Empty(snapshot.Pigeon.Memory!);
        Assert.Equal([new LifeEvent("born", LifeEventKind.Born, null, 0, 0, 1)], snapshot.Pigeon.LifeStory!);
        Assert.Equal(0, snapshot.Pigeon.AbsenceSeconds);
        Assert.Null(snapshot.Pigeon.IntentPlace);
        Assert.Equal(1510, simulation.Tick);
        Assert.Contains(simulation.LifeStory, e => e.Kind == LifeEventKind.FirstInteraction);
        Assert.Equal(1, simulation.LifeStory.Count(e => e.Kind == LifeEventKind.Born));
    }

    [Fact]
    public void Version2_IsUpgradedWithTheBornEntryToo()
    {
        const string v2 = """
            {"formatVersion":2,"seed":7,"stepTicks":1000000,"maxTicksPerAdvance":50,"decisionIntervalTicks":30,
             "tick":500,"accumulatedTicks":0,"fatigue":0.4,"intent":1,"intentStartedTick":450,
             "randomStreams":[],"journalLength":0,"pending":[],
             "personality":{"curiosity":50,"playfulness":50,"sociability":50,"boldness":50,"calmness":50,"independence":50,"energy":50},
             "boredom":0.2,"attention":0.3,"mood":{"valence":0.1,"arousal":0.4,"stress":0}}
            """;

        var snapshot = SnapshotSerializer.Deserialize(v2);

        Assert.Equal(["born"], snapshot.Pigeon.LifeStory!.Select(e => e.Key));
        Assert.Empty(snapshot.Pigeon.Memory!);
    }

    [Fact]
    public void UnknownLifeEventKind_IsAFormatError()
    {
        var json = SnapshotSerializer.Serialize(LivedSnapshot());
        var broken = json.Replace("\"key\":\"born\",\"kind\":0", "\"key\":\"born\",\"kind\":42", StringComparison.Ordinal);
        Assert.NotEqual(json, broken);

        Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Deserialize(broken));
    }

    [Theory]
    [InlineData("\"lifeStory\":", "\"lifeStoryGone\":")]
    [InlineData("\"memory\":", "\"memoryGone\":")]
    [InlineData("\"familiarity\":0.35", "\"familiarity\":1.35")]
    [InlineData("\"key\":\"first-interaction\"", "\"key\":\"born\"")]
    [InlineData("\"place\":\"desktop/laptop/taskbar\"", "\"place\":\"\"")]
    public void BrokenVersion4_IsAFormatError(string from, string to)
    {
        var json = SnapshotSerializer.Serialize(LivedSnapshot());
        var broken = json.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(json, broken);

        Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Deserialize(broken));
    }

    /// <summary>Голубь родился у панели задач, его тыкали, приложение не работало сутки, он летит на другой монитор.</summary>
    private static SimulationSnapshot LivedSnapshot()
    {
        var simulation = new PigeonSimulation(21, SimulationOptions.Desktop);
        simulation.Submit(new AffordancesObserved([Taskbar, External]));
        simulation.RunTicks(100);
        simulation.Submit(new UserPoked());
        simulation.Submit(new AbsenceEnded(TimeSpan.FromDays(1)));
        simulation.RunTicks(100);

        var snapshot = simulation.CreateSnapshot();
        Assert.Equal(["born", "first-interaction"], snapshot.Pigeon.LifeStory!.Select(e => e.Key));
        Assert.Contains(snapshot.Pigeon.Memory!, e => e.Place == Taskbar.PlaceKey);
        return snapshot with { Pigeon = snapshot.Pigeon with { IntentTarget = External.Id, IntentPlace = External.PlaceKey } };
    }
}
