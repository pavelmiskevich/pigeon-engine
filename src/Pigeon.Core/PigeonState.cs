using Pigeon.Common.Hashing;

namespace Pigeon.Core;

/// <summary>
/// Изменяемое состояние голубя в симуляции (ТЗ §5.1): личность, потребности, настроение и
/// текущее намерение, память мест и хроника жизни (#33). Identity хранится отдельно (<see cref="PetIdentity"/>), остальные части
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

    /// <summary>
    /// Место цели, запомненное в начале намерения: к отчёту мира о завершении цели среди
    /// affordances уже может не быть (после перелёта территория прилёта исчезает).
    /// </summary>
    public PlaceKey? IntentPlace { get; private set; }

    /// <summary>
    /// Сколько секунд приложение не работало за всю жизнь голубя — полностью, без предела досчёта
    /// потребностей: память забывает и то время, пока голубя не запускали.
    /// </summary>
    public long AbsenceSeconds { get; private set; }

    /// <summary>Насколько голубь знаком с местами (ТЗ §20).</summary>
    public PlaceMemory Memory { get; private init; } = new();

    /// <summary>Хроника событий «впервые» (ТЗ §22).</summary>
    public LifeStory LifeStory { get; private init; } = new();

    public static PigeonState FromSnapshot(PigeonStateSnapshot snapshot)
    {
        if (!Enum.IsDefined(snapshot.Intent))
        {
            throw new ArgumentException($"Неизвестное намерение в снимке: {(int)snapshot.Intent}.", nameof(snapshot));
        }

        snapshot.Needs.Validate();
        ArgumentOutOfRangeException.ThrowIfLessThan(snapshot.IntentEndsTick, snapshot.IntentStartedTick, nameof(snapshot));
        ArgumentOutOfRangeException.ThrowIfNegative(snapshot.AbsenceSeconds, nameof(snapshot));
        if (snapshot.IntentPlace is { Value: null })
        {
            throw new ArgumentException("Место намерения в снимке пустое.", nameof(snapshot));
        }

        return new PigeonState(snapshot.Personality, snapshot.Mood)
        {
            Needs = snapshot.Needs,
            Intent = snapshot.Intent,
            IntentStartedTick = snapshot.IntentStartedTick,
            IntentTarget = snapshot.IntentTarget,
            IntentEndsTick = snapshot.IntentEndsTick,
            IntentPlace = snapshot.IntentPlace,
            AbsenceSeconds = snapshot.AbsenceSeconds,
            Memory = PlaceMemory.FromSnapshot(snapshot.Memory ?? []),
            LifeStory = LifeStory.FromSnapshot(snapshot.LifeStory ?? []),
        };
    }

    public PigeonStateSnapshot ToSnapshot() =>
        new(Personality, Needs, Mood, Intent, IntentStartedTick, IntentTarget, IntentEndsTick,
            IntentPlace, AbsenceSeconds, [.. Memory.Entries], [.. LifeStory.Events]);

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

    /// <param name="place">Место цели (<see cref="IntentPlace"/>); у намерений без цели — нет.</param>
    public void StartIntent(IntentKind intent, AffordanceId? target, PlaceKey? place, long tick, long endsTick)
    {
        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Неизвестное намерение.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(endsTick, tick);
        if (place is { Value: null })
        {
            throw new ArgumentException("Пустое место намерения.", nameof(place));
        }

        Intent = intent;
        IntentTarget = target;
        IntentPlace = place;
        IntentStartedTick = tick;
        IntentEndsTick = endsTick;
    }

    /// <summary>Мир сообщил, что намерение исполнено: оно заканчивается в тике <paramref name="tick"/>.</summary>
    public void EndIntentAt(long tick) => IntentEndsTick = Math.Clamp(tick, IntentStartedTick, IntentEndsTick);

    /// <summary>Приложение не работало <paramref name="seconds"/> секунд; сумма насыщается на <see cref="long.MaxValue"/>.</summary>
    public void AddAbsence(long seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        AbsenceSeconds = seconds > long.MaxValue - AbsenceSeconds ? long.MaxValue : AbsenceSeconds + seconds;
    }

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
        hasher.Add(IntentPlace.HasValue);
        hasher.Add(IntentPlace?.Value ?? string.Empty);
        hasher.Add(AbsenceSeconds);
        Memory.AddTo(hasher);
        LifeStory.AddTo(hasher);
    }
}
