using Pigeon.Common.Hashing;

namespace Pigeon.Core;

/// <summary>
/// Дискретная метка настроения (ТЗ §7) для анимаций, UI и контекста провайдеров. Выводится из
/// <see cref="Mood"/> и доминирующей потребности, сама в состоянии не хранится.
/// </summary>
public enum MoodLabel
{
    Content = 0,
    Happy = 1,
    Playful = 2,
    Excited = 3,
    Bored = 4,
    Grumpy = 5,
    Anxious = 6,
    Startled = 7,
    Sleepy = 8,
}

/// <summary>
/// Настроение (ТЗ §7): <see cref="Valence"/> −1..1 (неприятно ↔ приятно), <see cref="Arousal"/>
/// 0..1 (вялый ↔ возбуждённый) и производный <see cref="Stress"/> 0..1 — скользящее среднее
/// отрицательной valence при высоком arousal. Правила изменения — в <see cref="MoodModel"/>.
/// </summary>
public readonly record struct Mood(double Valence, double Arousal, double Stress)
{
    /// <summary>Импульс события: сдвиг valence и arousal в пределах шкал.</summary>
    public Mood Nudge(double valence, double arousal) =>
        this with
        {
            Valence = Math.Clamp(Valence + valence, -1.0, 1.0),
            Arousal = Math.Clamp(Arousal + arousal, 0.0, 1.0),
        };

    public void Validate()
    {
        if (Valence is < -1.0 or > 1.0 || double.IsNaN(Valence))
        {
            throw new ArgumentException($"Valence вне диапазона −1..1: {Valence}.");
        }

        if (Arousal is < 0.0 or > 1.0 || double.IsNaN(Arousal))
        {
            throw new ArgumentException($"Arousal вне диапазона 0..1: {Arousal}.");
        }

        if (Stress is < 0.0 or > 1.0 || double.IsNaN(Stress))
        {
            throw new ArgumentException($"Stress вне диапазона 0..1: {Stress}.");
        }
    }

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        hasher.Add(Valence);
        hasher.Add(Arousal);
        hasher.Add(Stress);
    }
}
