using Pigeon.Testing;

namespace Pigeon.World.City.Generation.Tests;

public sealed class CityBlockGeneratorTests
{
    /// <summary>Эталон одинаков на всех ОС и архитектурах CI и в WASM (см. <see cref="GoldenCity"/>).</summary>
    [Fact]
    public void GoldenGeneration_ProducesTheSameHashOnEveryPlatform()
    {
        Assert.Equal(GoldenCity.ExpectedHash, GoldenCity.Run());
    }

    [Fact]
    public void SameSeed_GivesTheSameGrid()
    {
        var a = CityBlockGenerator.Generate(7, GoldenCity.Settings);
        var b = CityBlockGenerator.Generate(7, GoldenCity.Settings);

        Assert.Equal(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentGrids()
    {
        var a = CityBlockGenerator.Generate(1, GoldenCity.Settings);
        var b = CityBlockGenerator.Generate(2, GoldenCity.Settings);

        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void Grid_HasParksAndBuildingsWithinLimits()
    {
        var settings = GoldenCity.Settings;
        var grid = CityBlockGenerator.Generate(GoldenCity.Seed, settings);
        var floors = Enumerable.Range(0, grid.Height)
            .SelectMany(y => Enumerable.Range(0, grid.Width).Select(x => grid.FloorsAt(x, y)))
            .ToList();

        Assert.Equal(settings.BlocksX * settings.BlocksY, floors.Count);
        Assert.All(floors, f => Assert.InRange(f, 0, settings.MaxFloors));
        Assert.Contains(0, floors);
        Assert.Contains(floors, f => f > settings.MaxFloors / 2);
    }

    [Fact]
    public void CenterIsTallerThanEdge_OnAverage()
    {
        var grid = CityBlockGenerator.Generate(GoldenCity.Seed, GoldenCity.Settings);
        var center = AverageFloors(grid, d => d <= 8);
        var edge = AverageFloors(grid, d => d >= 48);

        Assert.True(center > edge * 2, $"Центр {center:F1}, край {edge:F1}.");
    }

    [Fact]
    public void Settings_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CityBlockGenerator.Generate(1, GoldenCity.Settings with { BlocksX = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CityBlockGenerator.Generate(1, GoldenCity.Settings with { ParkOneIn = 1 }));
    }

    [Fact]
    public void FloorsAt_RejectsOutOfRangeCoordinates()
    {
        var grid = CityBlockGenerator.Generate(1, GoldenCity.Settings);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.FloorsAt(grid.Width, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.FloorsAt(0, -1));
    }

    private static double AverageFloors(BlockGrid grid, Func<int, bool> distanceFilter)
    {
        var cx = grid.Width / 2;
        var cy = grid.Height / 2;
        var values = new List<int>();
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (distanceFilter(Math.Abs(x - cx) + Math.Abs(y - cy)) && grid.FloorsAt(x, y) > 0)
                {
                    values.Add(grid.FloorsAt(x, y));
                }
            }
        }

        return values.Average();
    }
}
