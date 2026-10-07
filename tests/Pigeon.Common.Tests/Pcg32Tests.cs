using Pigeon.Common.Randomness;

namespace Pigeon.Common.Tests;

public sealed class Pcg32Tests
{
    [Fact]
    public void Sequence_MatchesReferenceImplementation()
    {
        // pcg-c-basic, pcg32-demo: pcg32_srandom(42u, 54u).
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        var random = new Pcg32(42, 54);

        var actual = expected.Select(_ => random.NextUInt32()).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Restore_ContinuesTheSameSequence()
    {
        var original = new Pcg32(7, 3);
        original.NextUInt32();
        var restored = Pcg32.Restore(original.State);

        Assert.Equal(original.NextUInt32(), restored.NextUInt32());
        Assert.Equal(original.NextDouble(), restored.NextDouble());
    }

    [Fact]
    public void Restore_RejectsEvenIncrement()
    {
        Assert.Throws<ArgumentException>(() => Pcg32.Restore(new Pcg32State(1, 2)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void NextInt32_StaysInRange(int maxExclusive)
    {
        var random = new Pcg32(1, 1);

        for (var i = 0; i < 10_000; i++)
        {
            var value = random.NextInt32(maxExclusive);
            Assert.InRange(value, 0, maxExclusive - 1);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NextInt32_RejectsNonPositiveRange(int maxExclusive)
    {
        var random = new Pcg32(1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt32(maxExclusive));
    }

    [Fact]
    public void NextInt32_IsRoughlyUniform()
    {
        const int buckets = 6;
        const int draws = 60_000;
        var counts = new int[buckets];
        var random = new Pcg32(2026, 10);

        for (var i = 0; i < draws; i++)
        {
            counts[random.NextInt32(buckets)]++;
        }

        // Генератор детерминирован, поэтому порог не делает тест нестабильным.
        Assert.All(counts, c => Assert.InRange(c, 9_500, 10_500));
    }

    [Fact]
    public void NextDouble_StaysInUnitInterval()
    {
        var random = new Pcg32(3, 3);

        for (var i = 0; i < 10_000; i++)
        {
            var value = random.NextDouble();
            Assert.InRange(value, 0.0, Math.BitDecrement(1.0));
        }
    }
}
