using Pigeon.Common.Hashing;

namespace Pigeon.Core.Tests;

/// <summary>Состояние голубя и его снимок (ADR-0017): память, хроника, время отсутствия (#33).</summary>
public sealed class PigeonStateTests
{
    private static readonly PlaceKey Taskbar = new("desktop/A/taskbar");

    [Fact]
    public void Snapshot_RoundTripsMemoryLifeStoryAndAbsence()
    {
        var state = new PigeonState(Personality.Average, new Mood(0, 0.3, 0));
        state.Memory.Visit(Taskbar, 30, territory: false);
        state.LifeStory.Record(LifeEventKind.FirstSleep, null, 12, 30);
        state.AddAbsence(3_600);
        state.StartIntent(IntentKind.Relocate, new AffordanceId(4), new PlaceKey("desktop/B"), 20, 40);

        var restored = PigeonState.FromSnapshot(state.ToSnapshot());

        Assert.Equal(Hash(state), Hash(restored));
        Assert.Equal(new PlaceKey("desktop/B"), restored.IntentPlace);
        Assert.Equal(3_600, restored.AbsenceSeconds);
        Assert.Equal(state.Memory.Entries, restored.Memory.Entries);
        Assert.Equal(state.LifeStory.Events, restored.LifeStory.Events);
    }

    [Fact]
    public void EmptySnapshotParts_MeanEmptyMemoryAndStory()
    {
        var snapshot = new PigeonState(Personality.Average, new Mood(0, 0.3, 0)).ToSnapshot() with { Memory = null, LifeStory = null };

        var restored = PigeonState.FromSnapshot(snapshot);

        Assert.Empty(restored.Memory.Entries);
        Assert.Empty(restored.LifeStory.Events);
        Assert.Null(restored.IntentPlace);
    }

    [Fact]
    public void HashDependsOnMemoryStoryAbsenceAndPlace()
    {
        static PigeonState Fresh() => new(Personality.Average, new Mood(0, 0.3, 0));
        var baseline = Hash(Fresh());

        var visited = Fresh();
        visited.Memory.Visit(Taskbar, 0, territory: false);
        var story = Fresh();
        story.LifeStory.Record(LifeEventKind.FirstWalk, null, 0, 0);
        var away = Fresh();
        away.AddAbsence(1);
        var placed = Fresh();
        placed.StartIntent(IntentKind.Wander, null, new PlaceKey("desktop/A"), 0, 0);

        Assert.Equal(4, new[] { Hash(visited), Hash(story), Hash(away), Hash(placed) }.Where(h => h != baseline).Distinct().Count());
    }

    [Fact]
    public void AddAbsence_RejectsNegative_AndSaturates()
    {
        var state = new PigeonState(Personality.Average, new Mood(0, 0.3, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => state.AddAbsence(-1));
        state.AddAbsence(long.MaxValue - 5);
        state.AddAbsence(100);

        Assert.Equal(long.MaxValue, state.AbsenceSeconds);
    }

    [Fact]
    public void FromSnapshot_RejectsNegativeAbsence()
    {
        var snapshot = new PigeonState(Personality.Average, new Mood(0, 0.3, 0)).ToSnapshot() with { AbsenceSeconds = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => PigeonState.FromSnapshot(snapshot));
    }

    private static ulong Hash(PigeonState state)
    {
        var hasher = new StableHasher();
        state.AddTo(hasher);
        return hasher.Value;
    }
}
