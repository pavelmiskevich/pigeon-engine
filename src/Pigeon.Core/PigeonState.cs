using Pigeon.Common.Hashing;

namespace Pigeon.Core;

/// <summary>
/// Изменяемое состояние голубя в симуляции (ТЗ §5.1): личность, потребности, настроение и
/// текущее намерение. Identity хранится отдельно (<see cref="PetIdentity"/>), остальные части
/// домена добавляются по мере MVP.
/// </summary>
public sealed class PigeonState
{
    public PigeonState(Personality personality, Mood mood)
    {
        personality.Validate();
        mood.Validate();

        Personality = personality;
        Mood = mood;
    }

    /// <summary>Трейты; задаются при рождении и не меняются (ТЗ §5.2, Q6).</summary>
    public Personality Personality { get; }

    public Needs Needs { get; private set; } = Needs.None;

    public Mood Mood { get; private set; }

    public IntentKind Intent { get; private set; } = IntentKind.Wander;

    /// <summary>Тик, в котором началось текущее намерение.</summary>
    public long IntentStartedTick { get; private set; }

    /// <summary>Объект намерения в мире; у намерений без цели (отдых, прогулка) — нет.</summary>
    public AffordanceId? IntentTarget { get; private set; }

    /// <summary>
    /// Тик, к которому намерение по плану завершается; после него выбирается новое. Мир может
    /// завершить его раньше — событием о результате (ТЗ §9.1).
    /// </summary>
    public long IntentEndsTick { get; private set; }

    public static PigeonState FromSnapshot(PigeonStateSnapshot snapshot)
    {
        if (!Enum.IsDefined(snapshot.Intent))
        {
            throw new ArgumentException($"Неизвестное намерение в снимке: {(int)snapshot.Intent}.", nameof(snapshot));
        }

        snapshot.Needs.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshot.IntentEndsTick, snapshot.IntentStartedTick, nameof(snapshot));

        return new PigeonState(snapshot.Personality, snapshot.Mood)
        {
            Needs = snapshot.Needs,
            Intent = snapshot.Intent,
            IntentStartedTick = snapshot.IntentStartedTick,
            IntentTarget = snapshot.IntentTarget,
            IntentEndsTick = snapshot.IntentEndsTick,
        };
    }

    public PigeonStateSnapshot ToSnapshot() =>
        new(Personality, Needs, Mood, Intent, IntentStartedTick, IntentTarget, IntentEndsTick);

    public void SetNeeds(Needs needs)
    {
        needs.Validate();
        Needs = needs;
    }

    public void SetMood(Mood mood)
    {
        mood.Validate();
        Mood = mood;
    }

    public void StartIntent(IntentKind intent, AffordanceId? target, long tick, long endsTick)
    {
        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Неизвестное намерение.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(endsTick, tick);

        Intent = intent;
        IntentTarget = target;
        IntentStartedTick = tick;
        IntentEndsTick = endsTick;
    }

    /// <summary>Мир сообщил, что намерение исполнено: оно заканчивается в тике <paramref name="tick"/>.</summary>
    public void EndIntentAt(long tick) => IntentEndsTick = Math.Clamp(tick, IntentStartedTick, IntentEndsTick);

    public void AddTo(StableHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        Personality.AddTo(hasher);
        Needs.AddTo(hasher);
        Mood.AddTo(hasher);
        hasher.Add((int)Intent);
        hasher.Add(IntentStartedTick);
        hasher.Add(IntentTarget.HasValue);
        hasher.Add(IntentTarget?.Value ?? 0);
        hasher.Add(IntentEndsTick);
    }
}
