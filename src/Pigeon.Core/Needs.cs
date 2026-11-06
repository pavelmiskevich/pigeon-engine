using Pigeon.Common.Hashing;

namespace Pigeon.Core;

/// <summary>Потребности MVP-1 (ТЗ §6.1). Порядок значений — порядок выбора при равной срочности.</summary>
public enum NeedKind
{
    Fatigue = 0,
    Boredom = 1,
    Attention = 2,
}

/// <summary>Насколько острая потребность (ТЗ §6.1): пороги задаёт <see cref="NeedModel"/>.</summary>
public enum NeedLevel
{
    Comfort = 0,
    Want = 1,
    Urgent = 2,
}

/// <summary>
/// Срочность потребностей, каждая 0..1: больше — острее (ТЗ §6.1). Все растут со временем и
/// снижаются удовлетворяющими намерениями и взаимодействием.
/// </summary>
public readonly record struct Needs(double Fatigue, double Boredom, double Attention)
{
    public static IReadOnlyList<NeedKind> Kinds { get; } = [NeedKind.Fatigue, NeedKind.Boredom, NeedKind.Attention];

    public static Needs None { get; } = new(0.0, 0.0, 0.0);

    public double this[NeedKind kind] => kind switch
    {
        NeedKind.Fatigue => Fatigue,
        NeedKind.Boredom => Boredom,
        NeedKind.Attention => Attention,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестная потребность."),
    };

    /// <summary>Самая острая потребность; при равенстве — первая в порядке <see cref="NeedKind"/>.</summary>
    public NeedKind Dominant
    {
        get
        {
            var dominant = NeedKind.Fatigue;
            foreach (var kind in Kinds)
            {
                if (this[kind] > this[dominant])
                {
                    dominant = kind;
                }
            }

            return dominant;
        }
    }

    /// <summary>Та же срочность, но у <paramref name="kind"/> — <paramref name="value"/>, приведённое к 0..1.</summary>
    public Needs With(NeedKind kind, double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        return kind switch
        {
            NeedKind.Fatigue => this with { Fatigue = clamped },
            NeedKind.Boredom => this with { Boredom = clamped },
            NeedKind.Attention => this with { Attention = clamped },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестная потребность."),
        };
    }

    /// <summary>Сдвигает срочность <paramref name="kind"/> на <paramref name="delta"/> в пределах 0..1.</summary>
    public Needs Change(NeedKind kind, double delta) => With(kind, this[kind] + delta);

    public void Validate()
    {
        foreach (var kind in Kinds)
        {
            var value = this[kind];
            if (value is < 0.0 or > 1.0 || double.IsNaN(value))
            {
                throw new ArgumentException($"Срочность {kind} вне диапазона 0..1: {value}.");
            }
        }
    }

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        foreach (var kind in Kinds)
        {
            hasher.Add(this[kind]);
        }
    }
}
