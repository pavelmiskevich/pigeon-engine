using Pigeon.Common.Randomness;
using Pigeon.Common.Time;
using Pigeon.Core;

namespace Pigeon.Simulation.Tests;

/// <summary>Контракт детерминизма (ТЗ §3.3, ADR-0016).</summary>
public sealed class DeterminismTests
{
    private const ulong Seed = 0x5EED_0001UL;

    /// <summary>
    /// Эталонный хеш, одинаковый на всех ОС и архитектурах CI. Если меняется логика симуляции,
    /// хеш законно меняется: новое значение фиксируется в том же PR с объяснением. Если хеш
    /// разошёлся только на одной платформе — это нарушение детерминизма, а не повод обновить эталон.
    /// </summary>
    private const ulong GoldenHash = 0x773A_5AF9_27CC_B30FUL;

    private static readonly SimulationOptions Options = SimulationOptions.Desktop;

    [Fact]
    public void GoldenRun_ProducesTheSameHashOnEveryPlatform()
    {
        JournalEntry[] journal =
        [
            new(123, new UserPoked()),
            new(456, new UserPoked()),
            new(4_567, new UserPoked()),
        ];

        var simulation = PigeonSimulation.Replay(Seed, Options, journal, 10_000);

        Assert.Equal(GoldenHash, simulation.ComputeStateHash());
    }

    [Fact]
    public void SameTotalTime_GivesTheSameState_RegardlessOfFrameSlicing()
    {
        var total = TimeSpan.FromMinutes(1);
        var jitter = new Pcg32(11, 1);

        var at60Fps = Pump(total, () => TimeSpan.FromTicks(166_667));
        var at30Fps = Pump(total, () => TimeSpan.FromMilliseconds(33));
        var jittered = Pump(total, () => TimeSpan.FromMilliseconds(1 + jitter.NextInt32(80)));

        Assert.Equal(600, at60Fps.Tick);
        Assert.Equal(at60Fps.Tick, at30Fps.Tick);
        Assert.Equal(at60Fps.Tick, jittered.Tick);
        Assert.Equal(at60Fps.ComputeStateHash(), at30Fps.ComputeStateHash());
        Assert.Equal(at60Fps.ComputeStateHash(), jittered.ComputeStateHash());
    }

    [Fact]
    public void Replay_OfALiveSession_ReproducesStateAndJournal()
    {
        var live = new PigeonSimulation(Seed, Options);
        var frames = new Pcg32(21, 2);
        for (var frame = 0; frame < 20_000; frame++)
        {
            if (frame % 1_733 == 0)
            {
                live.Submit(new UserPoked());
            }

            live.Advance(TimeSpan.FromMilliseconds(5 + frames.NextInt32(40)));
        }

        var replayed = PigeonSimulation.Replay(Seed, Options, live.Journal, live.Tick);

        Assert.NotEmpty(live.Journal);
        Assert.Equal(live.Journal, replayed.Journal);
        Assert.Equal(live.ComputeStateHash(), replayed.ComputeStateHash());
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentStates()
    {
        var a = PigeonSimulation.Replay(1, Options, [], 1_000);
        var b = PigeonSimulation.Replay(2, Options, [], 1_000);

        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void Driver_FeedsClockTimeIntoTheSimulation()
    {
        var clock = new ManualClock();
        var simulation = new PigeonSimulation(Seed, Options);
        var driver = new SimulationDriver(clock, simulation);

        clock.Advance(TimeSpan.FromMilliseconds(250));
        var result = driver.Pump();

        Assert.Equal(2, result.TicksToRun);
        Assert.Equal(2, simulation.Tick);
    }

    [Fact]
    public void Pigeon_AlternatesBetweenWanderingAndResting()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        var intents = new HashSet<IntentKind>();

        for (var i = 0; i < 6_000; i++)
        {
            simulation.RunTicks(1);
            intents.Add(simulation.Pigeon.Intent);
            Assert.InRange(simulation.Pigeon.Fatigue, 0.0, 1.0);
        }

        Assert.Equal([IntentKind.Wander, IntentKind.Rest], intents.Order());
    }

    [Fact]
    public void Poke_InterruptsRest()
    {
        var simulation = new PigeonSimulation(Seed, Options);
        while (simulation.Pigeon.Intent != IntentKind.Rest)
        {
            simulation.RunTicks(1);
        }

        simulation.Submit(new UserPoked());
        simulation.RunTicks(1);

        Assert.Equal(IntentKind.Wander, simulation.Pigeon.Intent);
        Assert.Equal(simulation.Tick, simulation.Journal[^1].Tick);
    }

    [Fact]
    public void Replay_RejectsUnorderedJournal()
    {
        JournalEntry[] journal = [new(10, new UserPoked()), new(5, new UserPoked())];

        Assert.Throws<ArgumentException>(() => PigeonSimulation.Replay(Seed, Options, journal, 20));
    }

    private static PigeonSimulation Pump(TimeSpan total, Func<TimeSpan> nextFrame)
    {
        var simulation = new PigeonSimulation(Seed, Options);
        var elapsed = TimeSpan.Zero;
        while (elapsed < total)
        {
            var frame = nextFrame();
            if (elapsed + frame > total)
            {
                frame = total - elapsed;
            }

            simulation.Advance(frame);
            elapsed += frame;
        }

        return simulation;
    }
}
