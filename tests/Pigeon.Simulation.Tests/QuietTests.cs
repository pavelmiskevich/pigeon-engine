using Pigeon.AI;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.Simulation.Tests;

/// <summary>Режим тишины (#31, ТЗ §11.7): голубь затихает и просыпается на том же месте.</summary>
public sealed class QuietTests
{
    private const long Hour = 36_000;
    private static readonly SimulationOptions Options = SimulationOptions.Desktop;
    private static readonly IntentKind[] QuietIntents = [IntentKind.Rest, IntentKind.Sleep, IntentKind.Groom];

    private static readonly Affordance Taskbar =
        new(new AffordanceId(1), new PlaceKey("desktop/A/taskbar"), AffordanceKind.PerchSpot, 0.5, 0.0, TimeSpan.FromSeconds(5));

    private static readonly Affordance Cursor =
        new(new AffordanceId(2), new PlaceKey("desktop/cursor"), AffordanceKind.AttentionTarget, 0.7, 0.2, TimeSpan.FromSeconds(8));

    private static readonly Affordance MonitorB =
        new(new AffordanceId(3), new PlaceKey("desktop/B"), AffordanceKind.TerritoryZone, 0.6, 0.1, TimeSpan.FromSeconds(30));

    [Fact]
    public void Quiet_MakesThePigeonSettle()
    {
        var simulation = Lively();
        simulation.RunTicks(600);

        simulation.Submit(new QuietChanged(true));
        simulation.RunTicks(1);

        Assert.True(simulation.IsQuiet);
        Assert.Equal(DecisionTrigger.Interrupted, simulation.LastDecision!.Trigger);
        for (var tick = 0; tick < Hour; tick++)
        {
            Assert.Contains(simulation.Pigeon.Intent, QuietIntents);
            simulation.RunTicks(1);
        }
    }

    [Fact]
    public void QuietEnd_WakesThePigeonUp()
    {
        var simulation = Lively();
        simulation.Submit(new QuietChanged(true));
        simulation.RunTicks(Hour);

        // Пробуждение — не в тот тик, когда тихое намерение кончается само.
        while (simulation.Pigeon.IntentEndsTick <= simulation.Tick + 1)
        {
            simulation.RunTicks(1);
        }

        var asleep = simulation.Pigeon.Mood.Arousal;
        var decisions = simulation.LastDecision;

        simulation.Submit(new QuietChanged(false));
        simulation.RunTicks(1);

        Assert.False(simulation.IsQuiet);
        Assert.InRange(simulation.Pigeon.Mood.Arousal, asleep + 0.09, asleep + 0.11);
        Assert.NotSame(decisions, simulation.LastDecision);
        Assert.Equal(DecisionTrigger.Interrupted, simulation.LastDecision!.Trigger);
    }

    [Fact]
    public void RepeatedQuietState_ChangesNothing()
    {
        var repeated = Lively();
        var plain = Lively();
        repeated.RunTicks(600);
        plain.RunTicks(600);

        repeated.Submit(new QuietChanged(false));
        repeated.RunTicks(Hour);
        plain.RunTicks(Hour);

        Assert.Equal(plain.Pigeon.ToSnapshot(), repeated.Pigeon.ToSnapshot());
        Assert.Equal(plain.JournalLength + 1, repeated.JournalLength);
    }

    [Fact]
    public void Quiet_SurvivesSnapshotRestoreAndReplay()
    {
        var live = Lively();
        live.RunTicks(1_000);
        live.Submit(new QuietChanged(true));
        live.RunTicks(4_000);
        var middle = live.CreateSnapshot();
        live.RunTicks(3_000);
        live.Submit(new QuietChanged(false));
        live.RunTicks(2_000);

        var restored = PigeonSimulation.Restore(middle);
        Assert.True(restored.IsQuiet);
        Assert.True(middle.Quiet);

        var fromMiddle = PigeonSimulation.ReplayFrom(middle, live.GetJournalFrom(middle.JournalLength), live.Tick);
        var replayed = PigeonSimulation.Replay(9, Options, live.Journal, live.Tick, Curious);
        Assert.Equal(live.ComputeStateHash(), fromMiddle.ComputeStateHash());
        Assert.Equal(live.ComputeStateHash(), replayed.ComputeStateHash());
        Assert.False(fromMiddle.IsQuiet);
    }

    private static readonly Personality Curious = Personality.Average with { Curiosity = 90, Energy = 90 };

    /// <summary>Любопытный и бодрый голубь, которому есть куда пойти и полететь.</summary>
    private static PigeonSimulation Lively()
    {
        var simulation = new PigeonSimulation(9, Options, Curious);
        simulation.AttachWorld(new FixedWorld(Taskbar, Cursor, MonitorB));
        return simulation;
    }

    private sealed class FixedWorld(params Affordance[] items) : IAffordanceSource
    {
        public IReadOnlyList<Affordance> Current() => items;
    }
}
