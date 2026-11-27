using Pigeon.Common.Hashing;
using Pigeon.Common.Numerics;

namespace Pigeon.Core.Tests;

/// <summary>Память мест (#33, ТЗ §20): знакомство, забывание, закрепление повторами, предел.</summary>
public sealed class PlaceMemoryTests
{
    private const long Day = 86_400;
    private static readonly PlaceKey Taskbar = new("desktop/M/taskbar");
    private static readonly PlaceKey Monitor = new("desktop/M");

    [Fact]
    public void UnknownPlace_IsUnfamiliar()
    {
        var memory = new PlaceMemory();

        Assert.Equal(0.0, memory.FamiliarityOf(Taskbar, 100));
        Assert.Empty(memory.Entries);
    }

    [Fact]
    public void Visit_RaisesFamiliarityByTheGain()
    {
        var memory = new PlaceMemory();

        memory.Visit(Taskbar, 0, territory: false);
        Assert.Equal(0.35, memory.FamiliarityOf(Taskbar, 0));

        memory.Visit(Taskbar, 0, territory: false);
        Assert.Equal(0.35 + 0.35 * 0.65, memory.FamiliarityOf(Taskbar, 0), 1e-15);
        Assert.Equal(2, Entry(memory, Taskbar).Visits);
    }

    [Fact]
    public void Familiarity_DecaysWithTheTimeConstant()
    {
        var memory = new PlaceMemory();
        memory.Visit(Taskbar, 0, territory: false);

        var now = memory.FamiliarityOf(Taskbar, Day);

        Assert.Equal(0.35 * DeterministicMath.PowInt(1.0 - 1.0 / Day, Day), now);
        Assert.InRange(now, 0.128, 0.129);
    }

    [Fact]
    public void RepeatedVisits_AreForgottenMoreSlowly()
    {
        var memory = new PlaceMemory();
        var once = new PlaceKey("a");
        var thrice = new PlaceKey("b");
        memory.Visit(once, 0, territory: false);
        for (var i = 0; i < 3; i++)
        {
            memory.Visit(thrice, 0, territory: false);
        }

        var later = 3 * Day;
        var onceKept = memory.FamiliarityOf(once, later) / memory.FamiliarityOf(once, 0);
        var thriceKept = memory.FamiliarityOf(thrice, later) / memory.FamiliarityOf(thrice, 0);

        Assert.True(thriceKept > onceKept, $"{thriceKept} > {onceKept}");
    }

    [Fact]
    public void TimeConstant_IsCappedAtThirtyDays()
    {
        var memory = new PlaceMemory();
        var place = new PlaceKey("a");
        for (var i = 0; i < 20; i++)
        {
            memory.Visit(place, 0, territory: false);
        }

        const long thirtyDays = 30 * Day;
        var kept = memory.FamiliarityOf(place, thirtyDays) / memory.FamiliarityOf(place, 0);

        Assert.Equal(DeterministicMath.PowInt(1.0 - 1.0 / thirtyDays, thirtyDays), kept, 1e-12);
    }

    [Fact]
    public void Visit_ReinforcesAncestorsWeakly()
    {
        var memory = new PlaceMemory();

        memory.Visit(Taskbar, 10, territory: false);

        var monitor = Entry(memory, Monitor);
        Assert.Equal(0.1, monitor.Familiarity);
        Assert.Equal(0, monitor.Visits);
        Assert.Equal(10, monitor.LastSeen);
        Assert.True(monitor.Territory);
        Assert.False(Entry(memory, Taskbar).Territory);
        Assert.DoesNotContain(memory.Entries, e => e.Place.Value == "desktop");
    }

    [Fact]
    public void Ancestors_AreTheNearestFirst_AndOnlyFromTwoSegments()
    {
        Assert.Equal([Monitor], Taskbar.Ancestors());
        Assert.Empty(new PlaceKey("desktop/cursor").Ancestors());
        Assert.Equal([new PlaceKey("a/b/c"), new PlaceKey("a/b")], new PlaceKey("a/b/c/d").Ancestors());
    }

    [Fact]
    public void FullMemory_EvictsTheLeastFamiliar()
    {
        var memory = new PlaceMemory(MemoryRules.Default with { Capacity = 3 });
        memory.Visit(new PlaceKey("a"), 0, territory: false);
        memory.Visit(new PlaceKey("b"), 0, territory: false);
        memory.Visit(new PlaceKey("b"), 0, territory: false);
        memory.Visit(new PlaceKey("c"), 0, territory: false);
        memory.Visit(new PlaceKey("c"), 0, territory: false);

        memory.Visit(new PlaceKey("d"), 0, territory: false);

        Assert.Equal(["b", "c", "d"], memory.Entries.Select(e => e.Place.Value));
    }

    [Fact]
    public void FullMemory_EvictsTheSmallerKeyOnATie()
    {
        var memory = new PlaceMemory(MemoryRules.Default with { Capacity = 3 });
        memory.Visit(new PlaceKey("b"), 0, territory: false);
        memory.Visit(new PlaceKey("a"), 0, territory: false);
        memory.Visit(new PlaceKey("c"), 0, territory: false);
        memory.Visit(new PlaceKey("c"), 0, territory: false);

        memory.Visit(new PlaceKey("d"), 0, territory: false);

        Assert.Equal(["b", "c", "d"], memory.Entries.Select(e => e.Place.Value));
    }

    [Fact]
    public void FullMemory_NeverEvictsTheVisitedPlaceOrItsAncestors()
    {
        var memory = new PlaceMemory(MemoryRules.Default with { Capacity = 2 });
        memory.Visit(new PlaceKey("z"), 0, territory: false);
        memory.Visit(new PlaceKey("z"), 0, territory: false);
        memory.Visit(Monitor, 0, territory: true);

        memory.Visit(Taskbar, Day, territory: false);

        Assert.Equal([Monitor, Taskbar], memory.Entries.Select(e => e.Place));
    }

    [Fact]
    public void YearOfAbsence_IsFiniteAndNonNegative()
    {
        var memory = new PlaceMemory();
        memory.Visit(Taskbar, 0, territory: false);

        var now = memory.FamiliarityOf(Taskbar, 365 * Day);

        Assert.True(double.IsFinite(now));
        Assert.InRange(now, 0.0, 1e-6);
        Assert.Equal(0.35, memory.FamiliarityOf(Taskbar, -5));
        Assert.True(double.IsFinite(memory.FamiliarityOf(Taskbar, long.MaxValue)));
    }

    [Fact]
    public void Favorite_NeedsAClearPreferenceAmongTwoTerritories()
    {
        static PlaceMemoryEntry Territory(string key, double familiarity) => new(new PlaceKey(key), familiarity, 0, 1, true);

        Assert.Null(PlaceMemory.FromSnapshot([Territory("desktop/a", 0.9)]).Favorite(0));
        Assert.Null(PlaceMemory.FromSnapshot([Territory("desktop/a", 0.9), Territory("desktop/b", 0.6)]).Favorite(0));
        Assert.Equal(
            new PlaceKey("desktop/a"),
            PlaceMemory.FromSnapshot([Territory("desktop/a", 0.9), Territory("desktop/b", 0.4)]).Favorite(0));
        Assert.Null(PlaceMemory.FromSnapshot([Territory("desktop/a", 0.9), new(new PlaceKey("desktop/b/floor"), 0.1, 0, 1, false)]).Favorite(0));
    }

    [Fact]
    public void Dwell_StrengthensAKnownTerritory_WithoutAVisit()
    {
        var memory = new PlaceMemory();
        memory.Visit(Monitor, 0, territory: true);

        Assert.True(memory.Dwell(Monitor, 0));
        Assert.Equal(0.35 + 0.01 * 0.65, memory.FamiliarityOf(Monitor, 0), 1e-15);
        Assert.Equal(1, Entry(memory, Monitor).Visits);
        Assert.Equal(0, Entry(memory, Monitor).LastSeen);

        Assert.True(memory.Dwell(Monitor, 60));
        Assert.Equal(60, Entry(memory, Monitor).LastSeen);
        Assert.Equal(1, Entry(memory, Monitor).Visits);
    }

    [Fact]
    public void Dwell_IgnoresUnknownPlacesAndNonTerritories()
    {
        var memory = new PlaceMemory();

        Assert.False(memory.Dwell(Monitor, 0));
        Assert.Empty(memory.Entries);

        memory.Visit(Taskbar, 0, territory: false);
        var before = memory.Entries.ToList();
        Assert.False(memory.Dwell(Taskbar, 0));
        Assert.Equal(before, memory.Entries);
    }

    [Fact]
    public void Favorite_CountsUnvisitedTerritoriesInView()
    {
        static PlaceMemory Home(double familiarity) => PlaceMemory.FromSnapshot([new(new PlaceKey("desktop/a"), familiarity, 0, 1, true)]);
        PlaceKey[] other = [new("desktop/b")];

        Assert.Equal(new PlaceKey("desktop/a"), Home(0.9).Favorite(0, other));
        Assert.Null(Home(0.7).Favorite(0, other));
        Assert.Null(Home(0.9).Favorite(0, [new PlaceKey("desktop/a")]));
        Assert.Null(Home(0.9).Favorite(0));
    }

    public static TheoryData<PlaceMemoryEntry[]> BrokenEntries => new()
    {
        new PlaceMemoryEntry[] { new(new PlaceKey("a"), 0.5, 0, 1, false), new(new PlaceKey("a"), 0.4, 0, 1, false) },
        new PlaceMemoryEntry[] { new(new PlaceKey("a"), double.NaN, 0, 1, false) },
        new PlaceMemoryEntry[] { new(new PlaceKey("a"), 1.5, 0, 1, false) },
        new PlaceMemoryEntry[] { new(new PlaceKey("a"), 0.5, 0, -1, false) },
        new PlaceMemoryEntry[] { new(new PlaceKey("a"), 0.5, -1, 1, false) },
        new PlaceMemoryEntry[] { new(default, 0.5, 0, 1, false) },
    };

    [Theory]
    [MemberData(nameof(BrokenEntries))]
    public void FromSnapshot_RejectsBrokenEntries(PlaceMemoryEntry[] entries)
    {
        Assert.Throws<ArgumentException>(() => PlaceMemory.FromSnapshot(entries));
    }

    [Fact]
    public void FromSnapshot_RejectsMoreThanTheCapacity()
    {
        PlaceMemoryEntry[] entries = [new(new PlaceKey("a"), 0.5, 0, 1, false), new(new PlaceKey("b"), 0.5, 0, 1, false)];

        Assert.Throws<ArgumentException>(() => PlaceMemory.FromSnapshot(entries, MemoryRules.Default with { Capacity = 1 }));
    }

    [Fact]
    public void FromSnapshot_RoundTripsTheEntries()
    {
        var memory = new PlaceMemory();
        memory.Visit(Taskbar, 5, territory: false);
        memory.Visit(new PlaceKey("desktop/N"), 7, territory: true);

        var restored = PlaceMemory.FromSnapshot(memory.Entries);

        Assert.Equal(memory.Entries, restored.Entries);
        Assert.Equal(Hash(memory), Hash(restored));
    }

    [Fact]
    public void SameVisits_GiveTheSameHash()
    {
        static PlaceMemory Visit(params (string Key, long At)[] visits)
        {
            var memory = new PlaceMemory();
            foreach (var (key, at) in visits)
            {
                memory.Visit(new PlaceKey(key), at, territory: false);
            }

            return memory;
        }

        Assert.Equal(Hash(Visit(("a", 0), ("b", 10))), Hash(Visit(("a", 0), ("b", 10))));
        Assert.NotEqual(Hash(Visit(("a", 0), ("b", 10))), Hash(Visit(("b", 0), ("a", 10))));
    }

    private static PlaceMemoryEntry Entry(PlaceMemory memory, PlaceKey place) => Assert.Single(memory.Entries, e => e.Place == place);

    private static ulong Hash(PlaceMemory memory)
    {
        var hasher = new StableHasher();
        memory.AddTo(hasher);
        return hasher.Value;
    }
}
