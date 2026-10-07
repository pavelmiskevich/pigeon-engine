using Pigeon.Common.Hashing;

namespace Pigeon.Common.Tests;

public sealed class StableHasherTests
{
    [Fact]
    public void EmptyHasher_StartsFromFnvOffsetBasis()
    {
        Assert.Equal(14695981039346656037UL, new StableHasher().Value);
    }

    [Fact]
    public void SingleByte_MatchesFnv1a()
    {
        // FNV-1a 64 от восьми нулевых байт — эталон, посчитанный по определению алгоритма.
        var expected = 14695981039346656037UL;
        for (var i = 0; i < 8; i++)
        {
            expected = unchecked(expected * 1099511628211UL);
        }

        var hasher = new StableHasher();
        hasher.Add(0UL);

        Assert.Equal(expected, hasher.Value);
    }

    [Fact]
    public void HashString_IsStableAcrossRuns()
    {
        // Эталон посчитан независимо: FNV-1a 64 от длины (8 байт LE) и UTF-16LE символов.
        // Значение не должно меняться между запусками, ОС и версиями .NET — от него зависят seed потоков.
        Assert.Equal(0x9A7D_DFDD_F6ED_1F98UL, StableHasher.HashString("ai.decision"));
        Assert.NotEqual(StableHasher.HashString("ai.decision"), StableHasher.HashString("ai.decisioN"));
    }

    [Fact]
    public void Double_IsHashedByExactBits()
    {
        Assert.NotEqual(Hash(0.0), Hash(-0.0));
        Assert.Equal(Hash(0.1 + 0.2), Hash(0.1 + 0.2));
    }

    [Fact]
    public void StringLength_SeparatesConcatenations()
    {
        var ab = new StableHasher();
        ab.Add("a");
        ab.Add("b");

        var single = new StableHasher();
        single.Add("ab");

        Assert.NotEqual(ab.Value, single.Value);
    }

    private static ulong Hash(double value)
    {
        var hasher = new StableHasher();
        hasher.Add(value);
        return hasher.Value;
    }
}
