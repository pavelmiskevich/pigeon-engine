using Pigeon.Common.Hashing;
using Pigeon.Core;

namespace Pigeon.World.Abstractions;

/// <summary>Тип возможности мира (ТЗ §32). Числа — часть формата журнала и снимка.</summary>
public enum AffordanceKind
{
    PerchSpot = 0,
    RestSpot = 1,
    FoodSource = 2,
    WaterSource = 3,
    PointOfInterest = 4,
    AttentionTarget = 5,
    Hazard = 6,
    ToiletSpot = 7,
    TerritoryZone = 8,
    OffscreenExit = 9,
}

/// <summary>
/// Возможность, которую мир предлагает голубю (ТЗ §8.1, §32): место для насеста, точка интереса,
/// цель внимания, другая территория. Ядро видит тип и оценки, но не координаты. Новизны здесь
/// нет: мир не знает, что голубь уже видел, — её вычисляет Memory.
/// </summary>
/// <param name="Attractiveness">0..1 — оценка мира: высота, тепло, движение.</param>
/// <param name="Risk">0..1.</param>
/// <param name="TravelCost">Оценка времени пути.</param>
/// <remarks>
/// Оценки — <see cref="double"/>, а не <see cref="float"/>, как в эскизе ТЗ: вся симуляция считает
/// в double, и смешение точностей дало бы лишние округления.
/// </remarks>
public sealed record Affordance(
    AffordanceId Id,
    PlaceKey PlaceKey,
    AffordanceKind Kind,
    double Attractiveness,
    double Risk,
    TimeSpan TravelCost)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentException($"Неизвестный тип affordance: {(int)Kind}.");
        }

        if (PlaceKey.Value is null)
        {
            throw new ArgumentException($"У affordance {Id} нет ключа места.");
        }

        if (Attractiveness is < 0.0 or > 1.0 || double.IsNaN(Attractiveness)
            || Risk is < 0.0 or > 1.0 || double.IsNaN(Risk))
        {
            throw new ArgumentException($"Оценки affordance {Id} вне диапазона 0..1.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(TravelCost, TimeSpan.Zero, nameof(TravelCost));
    }

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        hasher.Add(Id.Value);
        hasher.Add(PlaceKey.Value);
        hasher.Add((int)Kind);
        hasher.Add(Attractiveness);
        hasher.Add(Risk);
        hasher.Add(TravelCost.Ticks);
    }
}

/// <summary>
/// Откуда симуляция узнаёт, что сейчас есть в мире. Опрашивается в тиках решений; ответ
/// записывается в журнал, поэтому при воспроизведении мир не нужен (ADR-0016).
/// </summary>
public interface IAffordanceSource
{
    IReadOnlyList<Affordance> Current();
}

/// <summary>
/// Почему голубь лишился опоры (ТЗ §9.1, §11.4, §12): мир сообщает об этом событием, а голубь
/// пугается и выбирает, что делать дальше, — вместо того чтобы молча оказаться в другом месте.
/// Числа — часть формата журнала.
/// </summary>
public enum DisplacementReason
{
    /// <summary>Место, где сидел голубь, исчезло: окно закрыли, панель скрылась.</summary>
    PlaceGone = 0,

    /// <summary>Отключили монитор, на котором был голубь.</summary>
    TerritoryGone = 1,

    /// <summary>Платформа перестала давать возможность, на которой держался мир.</summary>
    CapabilityLost = 2,
}
