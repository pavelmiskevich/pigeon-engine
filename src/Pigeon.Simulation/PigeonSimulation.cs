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
/// записи подаются через <see cref="Replay"/>.
/// </remarks>
public sealed class PigeonSimulation
{
    public const string DecisionStreamName = "ai.decision";

    internal const double WanderFatiguePerTick = 0.002;
    internal const double RestRecoveryPerTick = 0.004;
    internal const double PokeFatigue = 0.05;

    private readonly SimulationOptions _options;
    private readonly FixedStepScheduler _scheduler;
    private readonly RandomStreams _random;
    private readonly IRandomSource _decisionRandom;
    private readonly RestOrWanderPolicy _policy = new();
    private readonly Queue<JournalEntry> _pending = new();
    private readonly List<JournalEntry> _journal = [];

    public PigeonSimulation(ulong seed, SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _scheduler = new FixedStepScheduler(options);
        _random = new RandomStreams(seed);
        _decisionRandom = _random.Get(DecisionStreamName);
    }

    public ulong Seed => _random.RootSeed;

    /// <summary>Номер последнего выполненного тика; первый тик — 1.</summary>
    public long Tick { get; private set; }

    public PigeonState Pigeon { get; } = new();

    /// <summary>Все применённые входы в порядке применения.</summary>
    public IReadOnlyList<JournalEntry> Journal => _journal;

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
        hasher.Add(_journal.Count);
        return hasher.Value;
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

        Pigeon.ChangeFatigue(Pigeon.Intent == IntentKind.Rest ? -RestRecoveryPerTick : WanderFatiguePerTick);

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
                Pigeon.ChangeFatigue(PokeFatigue);
                if (Pigeon.Intent == IntentKind.Rest)
                {
                    Pigeon.StartIntent(IntentKind.Wander, tick);
                }

                break;
            default:
                throw new NotSupportedException($"Неизвестный вход симуляции: {inputEvent.GetType().Name}.");
        }
    }
}
