using Pigeon.World.City.Generation;

namespace Pigeon.Testing;

/// <summary>
/// Эталонная генерация района (ТЗ §3.3, ADR-0016). Подключается ссылкой и в .NET-тесты, и в
/// браузерный харнесс. Хеш меняется только вместе с <see cref="CityBlockGenerator.GeneratorVersion"/>.
/// </summary>
internal static class GoldenCity
{
    public const ulong Seed = 0xC17_0001UL;
    public const ulong ExpectedHash = 0xE6FA_DDC1_E775_4540UL;

    public static CityGenerationSettings Settings { get; } = new(BlocksX: 64, BlocksY: 64, MaxFloors: 30, ParkOneIn: 12);

    public static ulong Run() => CityBlockGenerator.Generate(Seed, Settings).ComputeHash();
}
