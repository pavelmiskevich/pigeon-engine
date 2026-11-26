namespace Pigeon.Core;

/// <summary>
/// Идентификатор affordance (ТЗ §32): живёт, пока жив объект мира. Цель намерения хранится этим
/// идентификатором, поэтому он в ядре, а сама affordance — в <c>Pigeon.World.Abstractions</c>.
/// </summary>
public readonly record struct AffordanceId(long Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Стабильный ключ места для памяти (ТЗ §32): одно и то же место между запусками и в любом мире
/// получает тот же ключ. Координат не содержит.
/// </summary>
/// <remarks>
/// Ключ — сегменты через <c>/</c>, от общего к частному: <c>desktop/&lt;монитор&gt;</c> —
/// территория, <c>desktop/&lt;монитор&gt;/taskbar</c> — место на ней. Память мест (#33) считает
/// визит в место слабым визитом его предков из двух и более сегментов (<see cref="Ancestors"/>).
/// </remarks>
public readonly record struct PlaceKey
{
    public PlaceKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Предки места из двух и более сегментов, ближайший первым: у <c>desktop/M/taskbar</c> —
    /// <c>desktop/M</c>; у <c>desktop/cursor</c> — нет. Корень мира (<c>desktop</c>) не место.
    /// </summary>
    public IEnumerable<PlaceKey> Ancestors()
    {
        var value = Value ?? string.Empty;
        for (var end = value.LastIndexOf('/'); end > 0; end = value.LastIndexOf('/', end - 1))
        {
            var ancestor = value[..end];
            if (!ancestor.Contains('/', StringComparison.Ordinal))
            {
                yield break;
            }

            yield return new PlaceKey(ancestor);
        }
    }

    public override string ToString() => Value;
}
