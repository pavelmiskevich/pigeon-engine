using Pigeon.Common.Numerics;

namespace Pigeon.Common.Tests;

public sealed class DeterministicMathTests
{
    [Theory]
    [InlineData(2.0, 0, 1.0)]
    [InlineData(2.0, 1, 2.0)]
    [InlineData(2.0, 10, 1024.0)]
    [InlineData(-3.0, 3, -27.0)]
    [InlineData(0.5, 4, 0.0625)]
    [InlineData(0.0, 5, 0.0)]
    public void PowInt_MatchesExactPowers(double value, long exponent, double expected)
    {
        Assert.Equal(expected, DeterministicMath.PowInt(value, exponent));
    }

    [Fact]
    public void PowInt_StaysCloseToRepeatedMultiplication()
    {
        const double factor = 1.0 - 1.0 / 600.0;
        var repeated = 1.0;
        for (var i = 0; i < 28_800; i++)
        {
            repeated *= factor;
        }

        Assert.Equal(repeated, DeterministicMath.PowInt(factor, 28_800), 1e-12);
    }

    [Fact]
    public void PowInt_RejectsNegativeExponent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicMath.PowInt(2.0, -1));
    }
}
