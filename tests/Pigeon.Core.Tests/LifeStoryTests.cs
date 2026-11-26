using Pigeon.Common.Hashing;

namespace Pigeon.Core.Tests;

/// <summary>Хроника жизни (#33, ТЗ §22): события «впервые» — ровно по одному.</summary>
public sealed class LifeStoryTests
{
    [Fact]
    public void Record_AddsOnce_AndTheSecondTimeIsIgnored()
    {
        var story = new LifeStory();

        Assert.True(story.Record(LifeEventKind.FirstSleep, null, 10, 1));
        Assert.False(story.Record(LifeEventKind.FirstSleep, null, 20, 2));

        var entry = Assert.Single(story.Events);
        Assert.Equal(new LifeEvent("first-sleep", LifeEventKind.FirstSleep, null, 10, 1, LifeStory.CurrentVersion), entry);
        Assert.True(story.Contains("first-sleep"));
    }

    [Theory]
    [InlineData(LifeEventKind.Born, null, "born")]
    [InlineData(LifeEventKind.FirstWalk, null, "first-walk")]
    [InlineData(LifeEventKind.FirstInteraction, null, "first-interaction")]
    [InlineData(LifeEventKind.FirstSleep, null, "first-sleep")]
    [InlineData(LifeEventKind.FirstAdventure, null, "first-adventure")]
    [InlineData(LifeEventKind.FirstMultiMonitorTransition, null, "first-multi-monitor-transition")]
    [InlineData(LifeEventKind.FirstVisitToMonitor, "desktop/B", "first-visit:desktop/B")]
    [InlineData(LifeEventKind.FirstFavoriteTerritory, null, "first-favorite-territory")]
    public void Keys_AreStable(LifeEventKind kind, string? place, string expected)
    {
        // Ключи — формат снимка: по ним хроника не повторяет события после восстановления.
        Assert.Equal(expected, LifeStory.KeyOf(kind, place is null ? null : new PlaceKey(place)));
    }

    [Fact]
    public void Kinds_KeepTheirNumbers()
    {
        Assert.Equal(
            [0, 1, 2, 3, 4, 5, 6, 7],
            new[]
            {
                LifeEventKind.Born, LifeEventKind.FirstWalk, LifeEventKind.FirstInteraction, LifeEventKind.FirstSleep,
                LifeEventKind.FirstAdventure, LifeEventKind.FirstMultiMonitorTransition, LifeEventKind.FirstVisitToMonitor,
                LifeEventKind.FirstFavoriteTerritory,
            }.Select(k => (int)k));
    }

    [Fact]
    public void FirstVisit_IsPerMonitor_AndCapped()
    {
        var story = new LifeStory();

        for (var i = 0; i < LifeStory.MaxMonitorVisits; i++)
        {
            Assert.True(story.Record(LifeEventKind.FirstVisitToMonitor, new PlaceKey($"desktop/{i}"), i, i));
        }

        Assert.False(story.Record(LifeEventKind.FirstVisitToMonitor, new PlaceKey("desktop/extra"), 99, 99));
        Assert.Equal(LifeStory.MaxMonitorVisits, story.Events.Count);
        Assert.True(story.Record(LifeEventKind.FirstSleep, null, 100, 100));
    }

    [Fact]
    public void FirstVisit_RequiresAPlace()
    {
        Assert.Throws<ArgumentException>(() => new LifeStory().Record(LifeEventKind.FirstVisitToMonitor, null, 0, 0));
        Assert.Throws<ArgumentException>(() => LifeStory.KeyOf(LifeEventKind.FirstVisitToMonitor, null));
    }

    public static TheoryData<LifeEvent[]> BrokenStories => new()
    {
        new LifeEvent[] { new("born", LifeEventKind.Born, null, 0, 0, 1), new("born", LifeEventKind.Born, null, 0, 0, 1) },
        new LifeEvent[] { new("born", (LifeEventKind)99, null, 0, 0, 1) },
        new LifeEvent[] { new("first-sleep", LifeEventKind.Born, null, 0, 0, 1) },
        new LifeEvent[] { new("first-visit:desktop/A", LifeEventKind.FirstVisitToMonitor, new PlaceKey("desktop/B"), 0, 0, 1) },
        new LifeEvent[] { new("born", LifeEventKind.Born, null, -1, 0, 1) },
    };

    [Theory]
    [MemberData(nameof(BrokenStories))]
    public void FromSnapshot_RejectsDuplicatesAndUnknownKinds(LifeEvent[] events)
    {
        Assert.Throws<ArgumentException>(() => LifeStory.FromSnapshot(events));
    }

    [Fact]
    public void Events_KeepInsertionOrder_AndHashFollowsThem()
    {
        var story = new LifeStory();
        story.Record(LifeEventKind.FirstSleep, null, 5, 5);
        story.Record(LifeEventKind.FirstVisitToMonitor, new PlaceKey("desktop/B"), 7, 7);
        story.Record(LifeEventKind.Born, null, 9, 9);

        var restored = LifeStory.FromSnapshot(story.Events);

        Assert.Equal(["first-sleep", "first-visit:desktop/B", "born"], story.Events.Select(e => e.Key));
        Assert.Equal(story.Events, restored.Events);
        Assert.Equal(Hash(story), Hash(restored));

        var reordered = new LifeStory();
        reordered.Record(LifeEventKind.Born, null, 9, 9);
        reordered.Record(LifeEventKind.FirstSleep, null, 5, 5);
        reordered.Record(LifeEventKind.FirstVisitToMonitor, new PlaceKey("desktop/B"), 7, 7);
        Assert.NotEqual(Hash(story), Hash(reordered));
    }

    private static ulong Hash(LifeStory story)
    {
        var hasher = new StableHasher();
        story.AddTo(hasher);
        return hasher.Value;
    }
}
