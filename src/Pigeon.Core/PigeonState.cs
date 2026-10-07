using Pigeon.Common.Hashing;

namespace Pigeon.Core;

/// <summary>
/// Состояние голубя. В MVP-0 — одна потребность и текущее намерение (ТЗ §5.1, §6.1);
/// остальные части домена добавляются в MVP-1.
/// </summary>
public sealed class PigeonState
{
    /// <summary>Срочность усталости: 0 — бодр, 1 — валится с лап (ТЗ §6.1).</summary>
    public double Fatigue { get; private set; }

    public IntentKind Intent { get; private set; } = IntentKind.Wander;

    /// <summary>Тик, в котором началось текущее намерение.</summary>
    public long IntentStartedTick { get; private set; }

    public static PigeonState FromSnapshot(PigeonStateSnapshot snapshot)
    {
        if (!Enum.IsDefined(snapshot.Intent))
        {
            throw new ArgumentException($"Неизвестное намерение в снимке: {(int)snapshot.Intent}.", nameof(snapshot));
        }

        if (snapshot.Fatigue is < 0.0 or > 1.0 || double.IsNaN(snapshot.Fatigue))
        {
            throw new ArgumentException($"Усталость в снимке вне диапазона 0..1: {snapshot.Fatigue}.", nameof(snapshot));
        }

        return new PigeonState
        {
            Fatigue = snapshot.Fatigue,
            Intent = snapshot.Intent,
            IntentStartedTick = snapshot.IntentStartedTick,
        };
    }

    public PigeonStateSnapshot ToSnapshot() => new(Fatigue, Intent, IntentStartedTick);

    public void ChangeFatigue(double delta) => Fatigue = Math.Clamp(Fatigue + delta, 0.0, 1.0);

    public void StartIntent(IntentKind intent, long tick)
    {
        Intent = intent;
        IntentStartedTick = tick;
    }

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        hasher.Add(Fatigue);
        hasher.Add((int)Intent);
        hasher.Add(IntentStartedTick);
    }
}
