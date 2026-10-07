using Pigeon.Common.Hashing;

namespace Pigeon.World.City.Generation;

/// <summary>Сетка кварталов района: этажность каждого квартала, 0 — сквер.</summary>
public sealed class BlockGrid
{
    private readonly int[] _floors;

    internal BlockGrid(int generatorVersion, int width, int height, int[] floors)
    {
        GeneratorVersion = generatorVersion;
        Width = width;
        Height = height;
        _floors = floors;
    }

    public int GeneratorVersion { get; }

    public int Width { get; }

    public int Height { get; }

    public int FloorsAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return _floors[(y * Width) + x];
    }

    /// <summary>Хеш результата генерации: одинаков в CoreCLR и WASM (ТЗ §3.3).</summary>
    public ulong ComputeHash()
    {
        var hasher = new StableHasher();
        hasher.Add(GeneratorVersion);
        hasher.Add(Width);
        hasher.Add(Height);
        foreach (var floors in _floors)
        {
            hasher.Add(floors);
        }

        return hasher.Value;
    }
}
