using Pigeon.Common.Hashing;
using Pigeon.Common.Randomness;

namespace Pigeon.Core;

/// <summary>Трейт по имени — для конфигурации поведения (ТЗ §9.2). Порядок — порядок полей <see cref="Personality"/>.</summary>
public enum TraitKind
{
    Curiosity = 0,
    Playfulness = 1,
    Sociability = 2,
    Boldness = 3,
    Calmness = 4,
    Independence = 5,
    Energy = 6,
}

/// <summary>
/// Семь базовых трейтов MVP-1 (ТЗ §5.2), каждый 0..100. Задаются при рождении из seed и дальше
/// не меняются (Q6). Это не настройки для пользователя: трейты влияют на рост потребностей,
/// настроение и выбор намерений.
/// </summary>
public readonly record struct Personality(
    int Curiosity,
    int Playfulness,
    int Sociability,
    int Boldness,
    int Calmness,
    int Independence,
    int Energy)
{
    public const int Min = 0;
    public const int Max = 100;
    public const int Neutral = 50;

    /// <summary>Сколько равномерных слагаемых в одном трейте; их сумма близка к нормальному распределению.</summary>
    internal const int Summands = 4;

    /// <summary>Все трейты по 50 — «средний» голубь для тестов и сравнения.</summary>
    public static Personality Average { get; } = new(Neutral, Neutral, Neutral, Neutral, Neutral, Neutral, Neutral);

    /// <summary>
    /// Трейты при рождении: сумма четырёх равномерных 0..25 даёт колокол вокруг 50 с отклонением
    /// около 15 и ровно в диапазоне 0..100 (ТЗ §5.2). Только целые числа, поэтому результат
    /// одинаков везде; порядок трейтов — порядок полей.
    /// </summary>
    public static Personality Generate(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return new Personality(
            Roll(random),
            Roll(random),
            Roll(random),
            Roll(random),
            Roll(random),
            Roll(random),
            Roll(random));
    }

    public int this[TraitKind trait] => trait switch
    {
        TraitKind.Curiosity => Curiosity,
        TraitKind.Playfulness => Playfulness,
        TraitKind.Sociability => Sociability,
        TraitKind.Boldness => Boldness,
        TraitKind.Calmness => Calmness,
        TraitKind.Independence => Independence,
        TraitKind.Energy => Energy,
        _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, "Неизвестный трейт."),
    };

    /// <summary>Трейт как сдвиг от середины: −1 при 0, 0 при 50, +1 при 100.</summary>
    public static double Centered(int trait) => (trait - Neutral) / (double)Neutral;

    public void Validate()
    {
        foreach (var trait in Traits())
        {
            if (trait is < Min or > Max)
            {
                throw new ArgumentException($"Трейт вне диапазона {Min}..{Max}: {trait}.");
            }
        }
    }

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        foreach (var trait in Traits())
        {
            hasher.Add(trait);
        }
    }

    private int[] Traits() => [Curiosity, Playfulness, Sociability, Boldness, Calmness, Independence, Energy];

    private static int Roll(IRandomSource random)
    {
        var sum = 0;
        for (var i = 0; i < Summands; i++)
        {
            sum += random.NextInt32(Max / Summands + 1);
        }

        return sum;
    }
}
