namespace Pigeon.Common.Hashing;

/// <summary>
/// FNV-1a 64 для хеша состояния симуляции. В отличие от <see cref="string.GetHashCode()"/> и
/// <see cref="HashCode"/>, результат стабилен между запусками, ОС и архитектурами (ТЗ §3.3).
/// Не криптографический.
/// </summary>
public sealed class StableHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _value = OffsetBasis;

    public ulong Value => _value;

    public static ulong HashString(string value)
    {
        var hasher = new StableHasher();
        hasher.Add(value);
        return hasher.Value;
    }

    public void Add(ulong value)
    {
        unchecked
        {
            for (var shift = 0; shift < 64; shift += 8)
            {
                _value ^= (byte)(value >> shift);
                _value *= Prime;
            }
        }
    }

    public void Add(long value) => Add(unchecked((ulong)value));

    public void Add(int value) => Add(unchecked((ulong)(uint)value));

    public void Add(bool value) => Add(value ? 1UL : 0UL);

    /// <summary>Добавляет точные биты значения: одинаковые числа дают одинаковый хеш.</summary>
    public void Add(double value) => Add(BitConverter.DoubleToInt64Bits(value));

    public void Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Add(value.Length);
        unchecked
        {
            foreach (var c in value)
            {
                _value ^= (byte)c;
                _value *= Prime;
                _value ^= (byte)(c >> 8);
                _value *= Prime;
            }
        }
    }
}
