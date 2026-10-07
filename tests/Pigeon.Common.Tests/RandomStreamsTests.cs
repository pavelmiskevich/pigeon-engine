using Pigeon.Common.Hashing;
using Pigeon.Common.Randomness;

namespace Pigeon.Common.Tests;

public sealed class RandomStreamsTests
{
    [Fact]
    public void Stream_DoesNotDependOnCreationOrder()
    {
        var first = new RandomStreams(99);
        first.Get("a");
        var fromFirst = Draw(first.Get("b"), 5);

        var second = new RandomStreams(99);
        var fromSecond = Draw(second.Get("b"), 5);

        Assert.Equal(fromFirst, fromSecond);
    }

    [Fact]
    public void ConsumingOneStream_DoesNotShiftAnother()
    {
        var quiet = new RandomStreams(5);
        var expected = Draw(quiet.Get("ai"), 10);

        var busy = new RandomStreams(5);
        Draw(busy.Get("world"), 1_000);
        var actual = Draw(busy.Get("ai"), 10);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Get_ReturnsTheSameStreamForTheSameName()
    {
        var streams = new RandomStreams(1);

        Assert.Same(streams.Get("ai"), streams.Get("ai"));
    }

    [Fact]
    public void DifferentNamesAndSeeds_GiveDifferentSequences()
    {
        var streams = new RandomStreams(1);

        Assert.NotEqual(Draw(streams.Get("a"), 4), Draw(streams.Get("b"), 4));
        Assert.NotEqual(Draw(new RandomStreams(1).Get("a"), 4), Draw(new RandomStreams(2).Get("a"), 4));
    }

    [Fact]
    public void Snapshot_IsOrderedByName()
    {
        var streams = new RandomStreams(1);
        streams.Get("zeta");
        streams.Get("alpha");
        streams.Get("Mid");

        Assert.Equal(["Mid", "alpha", "zeta"], streams.Snapshot().Select(s => s.Key));
    }

    [Fact]
    public void Hash_ChangesWhenAStreamAdvances()
    {
        var streams = new RandomStreams(1);
        var stream = streams.Get("ai");
        var before = HashOf(streams);

        stream.NextUInt32();

        Assert.NotEqual(before, HashOf(streams));
    }

    private static uint[] Draw(IRandomSource source, int count) =>
        Enumerable.Range(0, count).Select(_ => source.NextUInt32()).ToArray();

    private static ulong HashOf(RandomStreams streams)
    {
        var hasher = new StableHasher();
        streams.AddTo(hasher);
        return hasher.Value;
    }
}
