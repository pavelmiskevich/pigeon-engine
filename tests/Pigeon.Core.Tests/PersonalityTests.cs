using Pigeon.Common.Randomness;

namespace Pigeon.Core.Tests;

/// <summary>Трейты при рождении (ТЗ §5.2).</summary>
public sealed class PersonalityTests
{
    [Fact]
    public void SameStream_GivesTheSamePersonality()
    {
        Assert.Equal(Personality.Generate(new Pcg32(7, 3)), Personality.Generate(new Pcg32(7, 3)));
        Assert.NotEqual(Personality.Generate(new Pcg32(7, 3)), Personality.Generate(new Pcg32(8, 3)));
    }

    [Fact]
    public void Traits_FormABellAroundFifty()
    {
        var random = new Pcg32(42, 1);
        var traits = Enumerable.Range(0, 2_000)
            .Select(_ => Personality.Generate(random))
            .SelectMany(p => new[] { p.Curiosity, p.Playfulness, p.Sociability, p.Boldness, p.Calmness, p.Independence, p.Energy })
            .ToList();

        var mean = traits.Average();
        var deviation = Math.Sqrt(traits.Average(t => (t - mean) * (t - mean)));

        Assert.All(traits, t => Assert.InRange(t, Personality.Min, Personality.Max));
        Assert.InRange(mean, 49.0, 51.0);
        Assert.InRange(deviation, 14.0, 16.0);
        Assert.Contains(traits, t => t < 20);
        Assert.Contains(traits, t => t > 80);
    }

    [Theory]
    [InlineData(0, -1.0)]
    [InlineData(50, 0.0)]
    [InlineData(75, 0.5)]
    [InlineData(100, 1.0)]
    public void Centered_MapsTraitToMinusOneToOne(int trait, double expected)
    {
        Assert.Equal(expected, Personality.Centered(trait));
    }

    [Fact]
    public void Validate_RejectsTraitsOutOfRange()
    {
        Assert.Throws<ArgumentException>(() => (Personality.Average with { Calmness = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (Personality.Average with { Energy = 101 }).Validate());
        Personality.Average.Validate();
    }
}
