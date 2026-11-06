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
public readonly record struct PlaceKey
{
    public PlaceKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
