using Pigeon.AI;
using Pigeon.Common.Hashing;
using Pigeon.Common.Randomness;
using Pigeon.Core;

namespace Pigeon.Simulation;

/// <summary>
/// Детерминированная симуляция одного голубя (ТЗ §3.3, ADR-0016): одинаковые seed, параметры
/// и журнал входов дают одинаковое состояние на любой ОС и архитектуре.
/// </summary>
/// <remarks>
/// Входы применяются в начале тика, в порядке поступления. Живой вход (<see cref="Submit"/>)
/// попадает в ближайший тик и записывается в <see cref="Journal"/>; при воспроизведении те же
/// записи подаются через <see cref="Replay"/>. Снимок (<see cref="CreateSnapshot"/>) вместе с
/// журналом после него восстанавливает симуляцию без повтора всей истории (ADR-0017).
/// </remarks>
public sealed class PigeonSimulation
{
    public const string DecisionStreamName = "ai.decision";

    /// <summary>Поток, из которого при рождении берутся трейты (ТЗ §5.2).</summary>
    public const string PersonalityStreamName = "pigeon.personality";

    // Тычок: голубь вздрагивает, получает внимание и новый стимул; разбуженный — недоволен.
    internal const double PokeFatigue = 0.05;
    internal const double PokeAttentionRelief = 0.25;
    internal const double PokeBoredomRelief = 0.1;
    internal const double PokeValence = 0.1;
    internal const double PokeArousal = 0.15;
    internal const double WokenValence = -0.15;
    internal const double WokenArousal = 0.25;

    private static readonly NeedModel NeedRules = NeedModel.Default;
    private static readonly MoodModel MoodRules = MoodModel.Default;

    private readonly SimulationOptions _options;
    private readonly FixedStepScheduler _scheduler;
    private readonly RandomStreams _random;
    private readonly IRandomSource _decisionRandom;
    private readonly RestOrWanderPolicy _policy = new();
    private readonly Queue<JournalEntry> _pending = new();
    private readonly List<JournalEntry> _journal = [];
    private readonly long _journalOffset;
    private readonly double _stepSeconds;

    /// <summary>Рождение голубя: трейты выводятся из <paramref name="seed"/>, настроение — в базовой точке.</summary>
    public PigeonSimulation(ulong seed, SimulationOptions options)
        : this(options, CreateScheduler(options), new RandomStreams(seed), tick: 0, journalOffset: 0)
    {
    }

    private PigeonSimulation(
        SimulationOptions options,
        FixedStepScheduler scheduler,
        RandomStreams random,
        long tick,
        long journalOffset)
        : this(options, scheduler, random, Born(random), tick, journalOffset)
    {
    }

    private PigeonSimulation(
        SimulationOptions options,
        FixedStepScheduler scheduler,
        RandomStreams random,
        PigeonState pigeon,
        long tick,
        long journalOffset)
    {
        _options = options;
        _scheduler = scheduler;
        _random = random;
        _decisionRandom = _random.Get(DecisionStreamName);
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

    /// <summary>
    /// Применённые входы в порядке применения — с начала симуляции или с момента восстановления
    /// из снимка. Глобальная позиция первой записи — <see cref="JournalOffset"/>.
    /// </summary>
    public IReadOnlyList<JournalEntry> Journal => _journal;

    /// <summary>Сколько входов было применено до восстановления из снимка.</summary>
    public long JournalOffset => _journalOffset;

    /// <summary>Полная длина журнала с начала жизни симуляции.</summary>
    public long JournalLength => _journalOffset + _journal.Count;

    /// <summary>Восстанавливает симуляцию из снимка: дальше она идёт так же, как исходная.</summary>
    public static PigeonSimulation Restore(SimulationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();

        var simulation = new PigeonSimulation(
            snapshot.Options,
            new FixedStepScheduler(snapshot.Options, snapshot.Accumulated),
            RandomStreams.Restore(snapshot.Seed, snapshot.RandomStreams),
            PigeonState.FromSnapshot(snapshot.Pigeon),
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
            [.. _pending]);

    /// <summary>Записи журнала, начиная с глобальной позиции <paramref name="position"/>.</summary>
    public IReadOnlyList<JournalEntry> GetJournalFrom(long position)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, _journalOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, JournalLength);

        return [.. _journal.Skip((int)(position - _journalOffset))];
    }

    /// <summary>Воспроизводит симуляцию по журналу и выполняет <paramref name="ticks"/> тиков.</summary>
    public static PigeonSimulation Replay(
        ulong seed,
        SimulationOptions options,
        IEnumerable<JournalEntry> journal,
        long ticks)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var simulation = new PigeonSimulation(seed, options);
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
        hasher.Add(JournalLength);
        return hasher.Value;
    }

    private static FixedStepScheduler CreateScheduler(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new FixedStepScheduler(options);
    }

    internal static PigeonState Born(RandomStreams random)
    {
        var personality = Personality.Generate(random.Get(PersonalityStreamName));
        return new PigeonState(personality, MoodRules.Baseline(personality));
    }

    private void ExecuteTick()
    {
        var tick = Tick + 1;

        while (_pending.TryPeek(out var entry) && entry.Tick <= tick)
        {
            _pending.Dequeue();
            var applied = entry with { Tick = tick };
            Apply(applied.Event, tick);
            _journal.Add(applied);
        }

        var resting = Pigeon.Intent == IntentKind.Rest;
        var needs = NeedRules.Advance(Pigeon.Needs, Pigeon.Personality, resting, _stepSeconds);
        Pigeon.SetNeeds(needs);
        Pigeon.SetMood(MoodRules.Advance(Pigeon.Mood, Pigeon.Personality, needs, NeedRules, _stepSeconds));

        if (tick % _options.DecisionIntervalTicks == 0)
        {
            var next = _policy.Decide(Pigeon, _decisionRandom);
            if (next != Pigeon.Intent)
            {
                Pigeon.StartIntent(next, tick);
            }
        }

        Tick = tick;
    }

    private void Apply(InputEvent inputEvent, long tick)
    {
        switch (inputEvent)
        {
            case UserPoked:
                var woken = Pigeon.Intent == IntentKind.Rest;
                Pigeon.SetNeeds(Pigeon.Needs
                    .Change(NeedKind.Fatigue, PokeFatigue)
                    .Change(NeedKind.Attention, -PokeAttentionRelief)
                    .Change(NeedKind.Boredom, -PokeBoredomRelief));
                Pigeon.SetMood(woken
                    ? Pigeon.Mood.Nudge(WokenValence, WokenArousal)
                    : Pigeon.Mood.Nudge(PokeValence, PokeArousal));
                if (woken)
                {
                    Pigeon.StartIntent(IntentKind.Wander, tick);
                }

                break;
            case AbsenceEnded absence:
                var seconds = NeedRules.AbsenceSeconds(absence.Duration);
                Pigeon.SetNeeds(NeedRules.CatchUpAbsence(Pigeon.Needs, Pigeon.Personality, absence.Duration));
                var rested = MoodRules.CatchUpAbsence(Pigeon.Mood, Pigeon.Personality, seconds);
                Pigeon.SetMood(MoodRules.WelcomeBack(rested, Pigeon.Personality, absence.Duration));
                break;
            default:
                throw new NotSupportedException($"Неизвестный вход симуляции: {inputEvent.GetType().Name}.");
        }
    }
}
