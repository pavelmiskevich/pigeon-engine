using Pigeon.Common.Randomness;

namespace Pigeon.World.City.Generation;

/// <summary>
/// Заглушка генератора города MVP-0 (S0.7): сетка кварталов с этажностью, которая растёт к
/// центру района, и редкими скверами. Только целочисленная арифметика — результат побитово
/// одинаков в любом рантайме (ТЗ §3.3, ADR-0016). Настоящий генератор — MVP-4 (ТЗ §24).
/// </summary>
public static class CityBlockGenerator
{
    /// <summary>Меняется при любом изменении результата генерации при том же seed.</summary>
    public const int GeneratorVersion = 1;

    public const string StreamName = "city.blocks";

    public static BlockGrid Generate(ulong worldSeed, CityGenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var random = new RandomStreams(worldSeed).Get(StreamName);
        var width = settings.BlocksX;
        var height = settings.BlocksY;
        var centerX = width / 2;
        var centerY = height / 2;
        var maxDistance = Math.Max(1, centerX + centerY);
        var floors = new int[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var isPark = random.NextInt32(settings.ParkOneIn) == 0;
                var distance = Math.Abs(x - centerX) + Math.Abs(y - centerY);

                // Потолок этажности линейно падает от центра к краю, но не ниже одного этажа.
                var ceiling = Math.Max(1, settings.MaxFloors * (maxDistance - distance) / maxDistance);
                var value = 1 + random.NextInt32(ceiling);
                floors[(y * width) + x] = isPark ? 0 : value;
            }
        }

        return new BlockGrid(GeneratorVersion, width, height, floors);
    }
}
