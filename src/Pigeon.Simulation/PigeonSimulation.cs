using Pigeon.AI;
using Pigeon.AI.Behavior;
using Pigeon.Common.Hashing;
using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.Simulation;

/// <summary>
/// Детерминированная симуляция одного голубя (ТЗ §3.3, ADR-0016): одинаковые seed, параметры,
/// конфигурация поведения и журнал входов дают одинаковое состояние на любой ОС и архитектуре.
/// </summary>
/// <remarks>
/// <para>
/// Входы применяются в начале тика, в порядке поступления. Живой вход (<see cref="Submit"/>)
/// попадает в ближайший тик и записывается в <see cref="Journal"/>; при воспроизведении те же
/// записи подаются через <see cref="Replay"/>. Снимок (<see cref="CreateSnapshot"/>) вместе с
/// журналом после него восстанавливает симуляцию без повтора всей истории (ADR-0017).
/// </para>
/// <para>
/// Мир (<see cref="AttachWorld"/>) опрашивается в тиках решений сразу после входов. Если ответ
/// изменился, он записывается в журнал как <see cref="AffordancesObserved"/> — как ответ любого
/// внешнего провайдера, — поэтому при воспроизведении мир не нужен.
/// </para>
/// </remarks>
public sealed class PigeonSimulation
{
    public const string DecisionStreamName = "ai.decision";

    /// <summary>Поток, из которого при рождении берутся трейты (ТЗ §5.2).</summary>
    public const string PersonalityStreamName = "pigeon.personality";

    // Тычок: голубь вздрагивает, получает внимание и новый стимул; разбуженный — недоволен.
    internal const double PokeFatigue = 0.02;
    internal const double PokeAttentionRelief = 0.25;
    internal const double PokeBoredomRelief = 0.1;
    internal const double PokeValence = 0.1;
    internal const double PokeArousal = 0.15;
    internal const double WokenValence = -0.15;
    internal const double WokenArousal = 0.25;

    // Тычок, когда голубь и так взвинчен (тыкают слишком часто): раздражение.
    internal const double AnnoyedAboveArousal = 0.7;
    internal const double AnnoyedValence = -0.12;
    internal const double AnnoyedArousal = 0.1;

    // Ласка: много внимания, немного радости и спокойствия.
    internal const double PetAttentionRelief = 0.35;
    internal const double PetBoredomRelief = 0.05;
    internal const double PetValence = 0.12;
    internal const double PetArousal = -0.05;

    // Взяли в руки: испуг по смелости, внимание по общительности.
    internal const double PickUpAttentionRelief = 0.15;
    internal const double PickUpValence = -0.12;
    internal const double PickUpArousal = 0.3;
    internal const double DropArousal = 0.05;

    // Курсор рядом: голубь настораживается.
    internal const double CursorArousal = 0.05;

    // Потеря опоры: испуг, у робкого — до полутора раз сильнее, у смелого — вдвое слабее.
    internal const double StartleValence = -0.2;
    internal const double StartleArousal = 0.35;

    // Неудача исполнения: голубь вспархивает и оглядывается — лёгкое возбуждение.
    internal const double FailureArousal = 0.1;

    private static readonly NeedModel NeedRules = NeedModel.Default;
    private static readonly MoodModel MoodRules = MoodModel.Default;

    private readonly SimulationOptions _options;
    private readonly FixedStepScheduler _scheduler;
    private readonly RandomStreams _random;
    private readonly IRandomSource _decisionRandom;
    private readonly UtilityBrain _brain;
    private readonly IntentCooldowns _cooldowns;
    private readonly Queue<JournalEntry> _pending = new();
    private readonly List<JournalEntry> _journal = [];
    private readonly long _journalOffset;
    private readonly double _stepSeconds;

    private IReadOnlyList<Affordance> _affordances;
    private IAffordanceSource? _world;
    private DecisionTrigger? _forcedTrigger;

    /// <summary>
    /// Рождение голубя: трейты выводятся из <paramref name="seed"/> или задаются явно
    /// (<paramref name="personality"/> — архетип для тестов и Developer Mode), настроение — в
    /// базовой точке.
    /// </summary>
    public PigeonSimulation(ulong seed, SimulationOptions options, Personality? personality = null, BehaviorConfig? behavior = null)
        : this(options, CreateScheduler(options), new RandomStreams(seed), personality, behavior)
    {
    }

    private PigeonSimulation(
        SimulationOptions options,
        FixedStepScheduler scheduler,
        RandomStreams random,
        Personality? personality,
        BehaviorConfig? behavior)
        : this(options, scheduler, random, Born(random, personality), new IntentCooldowns(), [], behavior, tick: 0, journalOffset: 0)
    {
    }

    private PigeonSimulation(
        SimulationOptions options,
        FixedStepScheduler scheduler,
        RandomStreams random,
        PigeonState pigeon,
        IntentCooldowns cooldowns,
        IReadOnlyList<Affordance> affordances,
        BehaviorConfig? behavior,
        long tick,
        long journalOffset)
    {
        _options = options;
        _scheduler = scheduler;
        _random = random;
        _decisionRandom = _random.Get(DecisionStreamName);
        _brain = new UtilityBrain(behavior ?? BehaviorConfig.Default);
        _cooldowns = cooldowns;
        _affordances = affordances;
        _journalOffset = journalOffset;
        _stepSeconds = options.Step.Ticks / (double)TimeSpan.TicksPerSecond;
        Pigeon = pigeon;
        Tick = tick;
    }

    public ulong Seed => _random.RootSeed;

    public SimulationOptions Options => _options;

    /// <summary>Номер последнего выполненного тика; первый тик — 1.</summary>
    public long Tick { get; private set; }

    public PigeonState Pigeon { get; }

    /// <summary>Метка настроения сейчас (ТЗ §7) — для анимаций, UI и контекста провайдеров.</summary>
    public MoodLabel MoodLabel => MoodRules.Classify(Pigeon.Mood, Pigeon.Needs, NeedRules);

    /// <summary>Что мир предлагал при последнем опросе, по возрастанию идентификатора.</summary>
    public IReadOnlyList<Affordance> Affordances => _affordances;

    /// <summary>
    /// Последнее решение с ведущими факторами (ТЗ §9.2, §36) — для Developer Mode и объяснения
    /// пользователю. В снимок не входит: после восстановления появится при следующем решении.
    /// </summary>
    public Decision? LastDecision { get; private set; }

    /// <summary>
    /// Применённые входы в порядке применения — с начала симуляции или с момента восстановления
    /// из снимка. Глобальная позиция первой записи — <see cref="JournalOffset"/>.
    /// </summary>
    public IReadOnlyList<JournalEntry> Journal => _journal;

    /// <summary>Сколько входов было применено до восстановления из снимка.</summary>
    public long JournalOffset => _journalOffset;

    /// <summary>Полная длина журнала с начала жизни симуляции.</summary>
    public long JournalLength => _journalOffset + _journal.Count;

    /// <summary>
    /// Восстанавливает симуляцию из снимка: дальше она идёт так же, как исходная, если
    /// конфигурация поведения та же (по умолчанию — встроенная).
    /// </summary>
    public static PigeonSimulation Restore(SimulationSnapshot snapshot, BehaviorConfig? behavior = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();

        var simulation = new PigeonSimulation(
            snapshot.Options,
            new FixedStepScheduler(snapshot.Options, snapshot.Accumulated),
            RandomStreams.Restore(snapshot.Seed, snapshot.RandomStreams),
            PigeonState.FromSnapshot(snapshot.Pigeon),
            IntentCooldowns.Restore(snapshot.Cooldowns),
            new AffordancesObserved(snapshot.Affordances).Affordances,
            behavior,
            snapshot.Tick,
            snapshot.JournalLength);

        foreach (var entry in snapshot.Pending)
        {
            simulation._pending.Enqueue(entry);
        }

        return simulation;
    }

    /// <summary>Неизменяемый снимок текущего состояния; безопасно передавать в другой поток.</summary>
    public SimulationSnapshot CreateSnapshot() =>
        new(
            SimulationSnapshot.CurrentFormatVersion,
            Seed,
            _options,
            Tick,
            _scheduler.Accumulated,
            Pigeon.ToSnapshot(),
            [.. _random.Snapshot()],
            JournalLength,
            [.. _pending],
            _cooldowns.Snapshot(),
            _affordances);

    /// <summary>Записи журнала, начиная с глобальной позиции <paramref name="position"/>.</summary>
    public IReadOnlyList<JournalEntry> GetJournalFrom(long position)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, _journalOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, JournalLength);

        return [.. _journal.Skip((int)(position - _journalOffset))];
    }

    /// <summary>
    /// Воспроизводит симуляцию по журналу и выполняет <paramref name="ticks"/> тиков. Рождение и
    /// конфигурация должны совпадать с исходными.
    /// </summary>
    public static PigeonSimulation Replay(
        ulong seed,
        SimulationOptions options,
        IEnumerable<JournalEntry> journal,
        long ticks,
        Personality? personality = null,
        BehaviorConfig? behavior = null)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var simulation = new PigeonSimulation(seed, options, personality, behavior);
        long previousTick = 0;
        foreach (var entry in journal)
        {
            if (entry.Tick < previousTick || entry.Tick < 1)
            {
                throw new ArgumentException($"Журнал не упорядочен по тикам: запись для тика {entry.Tick}.", nameof(journal));
            }

            previousTick = entry.Tick;
            simulation._pending.Enqueue(entry);
        }

        simulation.RunTicks(ticks);
        return simulation;
    }

    /// <summary>
    /// Подключает мир, который симуляция будет опрашивать в тиках решений; <c>null</c> — отключает.
    /// В состояние не входит: после восстановления мир подключается заново.
    /// </summary>
    public void AttachWorld(IAffordanceSource? world) => _world = world;

    /// <summary>Подаёт вход; он будет применён в начале ближайшего тика.</summary>
    public void Submit(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        _pending.Enqueue(new JournalEntry(Tick + 1, inputEvent));
    }

    /// <summary>Продвигает симуляцию на прошедшее реальное время.</summary>
    public AdvanceResult Advance(TimeSpan realDelta)
    {
        var result = _scheduler.Advance(realDelta);
        RunTicks(result.TicksToRun);
        return result;
    }

    public void RunTicks(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        for (long i = 0; i < count; i++)
        {
            ExecuteTick();
        }
    }

    public ulong ComputeStateHash()
    {
        var hasher = new StableHasher();
        hasher.Add(Tick);
        Pigeon.AddTo(hasher);
        _random.AddTo(hasher);
        _cooldowns.AddTo(hasher);
        hasher.Add(_affordances.Count);
        foreach (var affordance in _affordances)
        {
            affordance.AddTo(hasher);
        }

        hasher.Add(JournalLength);
        return hasher.Value;
    }

    private static FixedStepScheduler CreateScheduler(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new FixedStepScheduler(options);
    }

    internal static PigeonState Born(RandomStreams random, Personality? personality = null)
    {
        var traits = personality ?? Personality.Generate(random.Get(PersonalityStreamName));
        return new PigeonState(traits, MoodRules.Baseline(traits));
    }

    private void ExecuteTick()
    {
        var tick = Tick + 1;

        while (_pending.TryPeek(out var entry) && entry.Tick <= tick)
        {
            _pending.Dequeue();
            Record(entry with { Tick = tick });
        }

        // Мир смотрится на каждой плановой границе, даже если решение ещё рано пересматривать:
        // пропавшая цель должна обнаружиться сразу, а не после минимальной длительности.
        if (_world is not null && (tick % _options.DecisionIntervalTicks == 0 || DueTrigger(tick) is not null))
        {
            ObserveWorld(tick);
        }

        var before = Pigeon.Needs;
        var needs = NeedRules.Advance(Pigeon.Needs, Pigeon.Personality, _brain.EffectsOf(Pigeon.Intent), _stepSeconds);
        Pigeon.SetNeeds(needs);
        Pigeon.SetMood(MoodRules.Advance(Pigeon.Mood, Pigeon.Personality, needs, NeedRules, _stepSeconds));

        if (BecameUrgent(before, needs))
        {
            Force(DecisionTrigger.Interrupted);
        }

        if (DueTrigger(tick) is { } trigger)
        {
            Decide(tick, trigger);
        }

        Tick = tick;
    }

    private void Record(JournalEntry entry)
    {
        Apply(entry.Event, entry.Tick);
        _journal.Add(entry);
    }

    private void ObserveWorld(long tick)
    {
        var observed = new AffordancesObserved(_world!.Current());
        if (!observed.Affordances.SequenceEqual(_affordances))
        {
            Record(new JournalEntry(tick, observed));
        }
    }

    /// <summary>Нужно ли решать в этом тике и почему (ТЗ §9.2, «когда пересчитывать»).</summary>
    private DecisionTrigger? DueTrigger(long tick)
    {
        if (_forcedTrigger is DecisionTrigger.TargetGone or DecisionTrigger.Failed)
        {
            return _forcedTrigger;
        }

        if (tick >= Pigeon.IntentEndsTick)
        {
            return DecisionTrigger.Completed;
        }

        if (_forcedTrigger is { } forced)
        {
            return forced;
        }

        var elapsed = TimeSpan.FromTicks((tick - Pigeon.IntentStartedTick) * _options.Step.Ticks);
        return tick % _options.DecisionIntervalTicks == 0 && elapsed >= _brain.MinDurationOf(Pigeon.Intent)
            ? DecisionTrigger.Periodic
            : null;
    }

    private void Decide(long tick, DecisionTrigger trigger)
    {
        _forcedTrigger = null;

        // Завершённое или потерявшее цель намерение уходит на кулдаун до выбора, чтобы не
        // выбрать его снова сразу же.
        if (trigger is DecisionTrigger.Completed or DecisionTrigger.TargetGone or DecisionTrigger.Failed)
        {
            StartCooldown(Pigeon.Intent, tick);
        }

        _cooldowns.Prune(tick);
        var decision = _brain.Decide(Pigeon, _affordances, _cooldowns, tick, trigger, _decisionRandom);
        LastDecision = decision;

        if (decision.KeptCurrent)
        {
            return;
        }

        if (trigger is DecisionTrigger.Periodic or DecisionTrigger.Interrupted)
        {
            StartCooldown(Pigeon.Intent, tick);
        }

        var choice = decision.Choice;
        var duration = _brain.Config.Find(choice.Intent) is null ? _options.Step : _brain.PlanDuration(choice.Intent, _decisionRandom);
        Pigeon.StartIntent(choice.Intent, choice.Target, tick, tick + Math.Max(1, TicksOf(duration)));
    }

    private void StartCooldown(IntentKind intent, long tick)
    {
        var cooldown = _brain.CooldownOf(intent);
        if (cooldown > TimeSpan.Zero)
        {
            _cooldowns.Start(intent, tick + TicksOf(cooldown));
        }
    }

    private long TicksOf(TimeSpan duration) => (duration.Ticks + _options.Step.Ticks - 1) / _options.Step.Ticks;

    /// <summary>
    /// Значимое событие пересматривает решение раньше минимальной длительности; потерянная цель и
    /// неудача исполнения важнее остальных причин.
    /// </summary>
    private void Force(DecisionTrigger trigger)
    {
        if (_forcedTrigger is not (DecisionTrigger.TargetGone or DecisionTrigger.Failed))
        {
            _forcedTrigger = trigger;
        }
    }

    private static bool BecameUrgent(Needs before, Needs after)
    {
        foreach (var kind in Needs.Kinds)
        {
            if (NeedRules.LevelOf(after[kind]) == NeedLevel.Urgent && NeedRules.LevelOf(before[kind]) != NeedLevel.Urgent)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Насколько голубь пугается: робкий — до полутора раз сильнее, смелый — вдвое слабее.</summary>
    private double Fright() => 1.0 - 0.5 * Personality.Centered(Pigeon.Personality.Boldness);

    /// <summary>
    /// Насколько голубю приятно внимание человека, −1..1: общительному — больше, независимому —
    /// меньше. Отношений (ТЗ §21) в домене ещё нет, пока только личность.
    /// </summary>
    private double Affinity() =>
        0.5 * Personality.Centered(Pigeon.Personality.Sociability) - 0.5 * Personality.Centered(Pigeon.Personality.Independence);

    private void Apply(InputEvent inputEvent, long tick)
    {
        switch (inputEvent)
        {
            case UserPoked:
                var woken = Pigeon.Intent is IntentKind.Rest or IntentKind.Sleep;
                Pigeon.SetNeeds(Pigeon.Needs
                    .Change(NeedKind.Fatigue, PokeFatigue)
                    .Change(NeedKind.Attention, -PokeAttentionRelief)
                    .Change(NeedKind.Boredom, -PokeBoredomRelief));
                Pigeon.SetMood(woken ? Pigeon.Mood.Nudge(WokenValence, WokenArousal)
                    : Pigeon.Mood.Arousal >= AnnoyedAboveArousal ? Pigeon.Mood.Nudge(AnnoyedValence, AnnoyedArousal)
                    : Pigeon.Mood.Nudge(PokeValence * (1.0 + Affinity()), PokeArousal));
                if (woken)
                {
                    // Разбуженный голубь сразу снова не засыпает: отдых уходит на кулдаун.
                    StartCooldown(Pigeon.Intent, tick);
                }

                Force(DecisionTrigger.Interrupted);
                break;
            case UserPetted:
                Pigeon.SetNeeds(Pigeon.Needs
                    .Change(NeedKind.Attention, -PetAttentionRelief)
                    .Change(NeedKind.Boredom, -PetBoredomRelief));
                Pigeon.SetMood(Pigeon.Mood.Nudge(PetValence * (1.0 + Affinity()), PetArousal));
                break;
            case UserPickedUp:
                var scared = Fright();
                Pigeon.SetNeeds(Pigeon.Needs.Change(NeedKind.Attention, -PickUpAttentionRelief));
                Pigeon.SetMood(Pigeon.Mood.Nudge(PickUpValence * scared + PetValence * Affinity(), PickUpArousal * scared));
                Force(DecisionTrigger.Interrupted);
                break;
            case UserDropped:
                Pigeon.SetMood(Pigeon.Mood.Nudge(0.0, DropArousal));
                Force(DecisionTrigger.Interrupted);
                break;
            case CursorApproached:
                if (Pigeon.Intent != IntentKind.Sleep)
                {
                    Pigeon.SetMood(Pigeon.Mood.Nudge(0.0, CursorArousal));
                    Force(DecisionTrigger.Interrupted);
                }

                break;
            case AbsenceEnded absence:
                var seconds = NeedRules.AbsenceSeconds(absence.Duration);
                Pigeon.SetNeeds(NeedRules.CatchUpAbsence(Pigeon.Needs, Pigeon.Personality, absence.Duration));
                var rested = MoodRules.CatchUpAbsence(Pigeon.Mood, Pigeon.Personality, seconds);
                Pigeon.SetMood(MoodRules.WelcomeBack(rested, Pigeon.Personality, absence.Duration));
                Force(DecisionTrigger.Interrupted);
                break;
            case FootingLost lost:
                if (!Enum.IsDefined(lost.Reason))
                {
                    throw new ArgumentException($"Неизвестная причина потери опоры: {(int)lost.Reason}.", nameof(inputEvent));
                }

                var fright = Fright();
                Pigeon.SetMood(Pigeon.Mood.Nudge(StartleValence * fright, StartleArousal * fright));
                Force(DecisionTrigger.Interrupted);
                break;
            case ActionFinished finished:
                if (finished.Intent != Pigeon.Intent || finished.IntentStartedTick != Pigeon.IntentStartedTick)
                {
                    break;
                }

                if (finished.Outcome == ActionOutcome.Completed)
                {
                    Pigeon.EndIntentAt(tick);
                }
                else
                {
                    Pigeon.SetMood(Pigeon.Mood.Nudge(0.0, FailureArousal));
                    Force(DecisionTrigger.Failed);
                }

                break;
            case AffordancesObserved observed:
                _affordances = observed.Affordances;
                if (Pigeon.IntentTarget is { } target && !_affordances.Any(a => a.Id == target))
                {
                    Force(DecisionTrigger.TargetGone);
                }

                break;
            default:
                throw new NotSupportedException($"Неизвестный вход симуляции: {inputEvent.GetType().Name}.");
        }
    }
}
