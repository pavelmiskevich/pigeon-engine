using Pigeon.AI.Behavior;
using Pigeon.Common.Numerics;
using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.Simulation.Tests;

/// <summary>Симуляция ведёт память мест и хронику жизни (#33) по подтверждениям мира.</summary>
public sealed class MemoryLifeTests
{
    private const long Day = 86_400;
    private static readonly SimulationOptions Options = SimulationOptions.Desktop;

    private static readonly Affordance Taskbar =
        new(new AffordanceId(1), new PlaceKey("desktop/A/taskbar"), AffordanceKind.PerchSpot, 0.5, 0.0, TimeSpan.FromSeconds(5));

    private static readonly Affordance Cursor =
        new(new AffordanceId(2), new PlaceKey("desktop/cursor"), AffordanceKind.AttentionTarget, 0.7, 0.2, TimeSpan.FromSeconds(8));

    private static readonly Affordance MonitorB =
        new(new AffordanceId(3), new PlaceKey("desktop/B"), AffordanceKind.TerritoryZone, 0.6, 0.1, TimeSpan.FromSeconds(30));

    [Fact]
    public void Relocation_CountsAVisitEvenWhenTheTargetVanishes()
    {
        var simulation = StartedOn(IntentKind.Relocate, "relocate", MonitorB, visitsTarget: true);
        var started = simulation.Pigeon.IntentStartedTick;

        // Долетел: мир сразу перестаёт предлагать территорию, на которой голубь теперь сидит, и
        // это наблюдение применяется раньше отчёта о завершении.
        simulation.AttachWorld(null);
        simulation.Submit(new AffordancesObserved([]));
        simulation.Submit(new ActionFinished(IntentKind.Relocate, started, ActionOutcome.Completed));
        simulation.RunTicks(1);

        Assert.True(simulation.Pigeon.Memory.FamiliarityOf(MonitorB.PlaceKey, simulation.LifeSeconds) > 0.3);
        Assert.True(simulation.Pigeon.Memory.Entries.Single(e => e.Place == MonitorB.PlaceKey).Territory);
        Assert.Contains(simulation.LifeStory, e => e.Key == "first-multi-monitor-transition");
        Assert.Contains(simulation.LifeStory, e => e.Key == "first-visit:desktop/B" && e.Place == MonitorB.PlaceKey);
    }

    [Fact]
    public void ReturnToTheBirthMonitor_IsNotAFirstVisit()
    {
        var monitorA = MonitorB with { Id = new AffordanceId(4), PlaceKey = new PlaceKey("desktop/A") };
        var simulation = new PigeonSimulation(5, Options, behavior: OnlyRelocation());
        simulation.AttachWorld(new FixedWorld(Taskbar, monitorA));
        simulation.RunTicks(1);
        Assert.Equal(monitorA.PlaceKey, simulation.Pigeon.IntentPlace);

        simulation.Submit(new ActionFinished(IntentKind.Relocate, simulation.Pigeon.IntentStartedTick, ActionOutcome.Completed));
        simulation.RunTicks(1);

        Assert.Contains(simulation.LifeStory, e => e.Kind == LifeEventKind.FirstMultiMonitorTransition);
        Assert.DoesNotContain(simulation.LifeStory, e => e.Kind == LifeEventKind.FirstVisitToMonitor);
        Assert.True(simulation.Pigeon.Memory.FamiliarityOf(monitorA.PlaceKey, simulation.LifeSeconds) > 0.35);
    }

    [Fact]
    public void FailedOrStaleReports_DoNotCountAVisit()
    {
        var simulation = StartedOn(IntentKind.Relocate, "relocate", MonitorB, visitsTarget: true);
        var started = simulation.Pigeon.IntentStartedTick;

        simulation.Submit(new ActionFinished(IntentKind.Relocate, started - 1, ActionOutcome.Completed));
        simulation.Submit(new ActionFinished(IntentKind.Explore, started, ActionOutcome.Completed));
        simulation.Submit(new ActionFinished(IntentKind.Relocate, started, ActionOutcome.Failed, ActionFailure.Unreachable));
        simulation.RunTicks(1);

        Assert.Equal(0.0, simulation.Pigeon.Memory.FamiliarityOf(MonitorB.PlaceKey, simulation.LifeSeconds));
        Assert.DoesNotContain(simulation.LifeStory, e => e.Kind == LifeEventKind.FirstMultiMonitorTransition);
    }

    [Fact]
    public void Flee_DoesNotCountAVisit()
    {
        var simulation = StartedOn(IntentKind.Flee, "flee", Cursor, visitsTarget: false);

        simulation.Submit(new ActionFinished(IntentKind.Flee, simulation.Pigeon.IntentStartedTick, ActionOutcome.Completed));
        simulation.RunTicks(1);

        Assert.Equal(0.0, simulation.Pigeon.Memory.FamiliarityOf(Cursor.PlaceKey, simulation.LifeSeconds));
    }

    [Fact]
    public void FirstObservation_RemembersTheBirthPlace()
    {
        var simulation = new PigeonSimulation(3, Options);
        simulation.AttachWorld(new FixedWorld(Taskbar, Cursor));

        simulation.RunTicks(1);

        var memory = simulation.Pigeon.Memory;
        Assert.Equal(0.35, memory.FamiliarityOf(Taskbar.PlaceKey, simulation.LifeSeconds));
        Assert.Equal(0.1, memory.FamiliarityOf(new PlaceKey("desktop/A"), simulation.LifeSeconds));
        Assert.Equal(0.0, memory.FamiliarityOf(Cursor.PlaceKey, simulation.LifeSeconds));
    }

    [Fact]
    public void Absence_IsCountedInFull()
    {
        var simulation = new PigeonSimulation(3, Options);
        simulation.AttachWorld(new FixedWorld(Taskbar));
        simulation.RunTicks(50);
        var before = simulation.LifeSeconds;
        Assert.Equal(5, before);

        simulation.Submit(new AbsenceEnded(TimeSpan.FromDays(14)));
        simulation.RunTicks(1);

        Assert.Equal(before + 14 * Day, simulation.LifeSeconds);
        var entry = simulation.Pigeon.Memory.Entries.Single(e => e.Place == Taskbar.PlaceKey);
        var expected = entry.Familiarity * DeterministicMath.PowInt(1.0 - 1.0 / Day, simulation.LifeSeconds - entry.LastSeen);
        Assert.Equal(expected, simulation.Pigeon.Memory.FamiliarityOf(Taskbar.PlaceKey, simulation.LifeSeconds));
        Assert.InRange(expected, 0.0, 1e-6);
    }

    [Fact]
    public void HugeAbsence_DoesNotOverflow()
    {
        var simulation = new PigeonSimulation(3, Options);
        simulation.AttachWorld(new FixedWorld(Taskbar));
        simulation.RunTicks(10);

        simulation.Submit(new AbsenceEnded(TimeSpan.MaxValue));
        simulation.Submit(new AbsenceEnded(TimeSpan.MaxValue));
        simulation.RunTicks(10);

        Assert.Equal(2 + 2 * (TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond), simulation.LifeSeconds);
        var familiarity = simulation.Pigeon.Memory.FamiliarityOf(Taskbar.PlaceKey, simulation.LifeSeconds);
        Assert.True(double.IsFinite(familiarity) && familiarity >= 0.0);
    }

    [Fact]
    public void FirstEvents_AreRecordedOnce()
    {
        var simulation = new PigeonSimulation(3, Options, behavior: OnlyIntents("sleep", "wander"));
        for (var i = 0; i < 4 * 60 * 10; i++)
        {
            if (i % 600 == 300)
            {
                simulation.Submit(new UserPoked());
            }

            simulation.RunTicks(1);
        }

        var keys = simulation.LifeStory.Select(e => e.Key).ToList();
        Assert.Equal(keys.Distinct(), keys);
        Assert.Contains("first-interaction", keys);
        Assert.Contains("first-sleep", keys);
        Assert.Contains("first-walk", keys);
        Assert.Equal(1, simulation.LifeStory.Count(e => e.Kind == LifeEventKind.FirstInteraction));
        Assert.True(simulation.LifeStory.Single(e => e.Key == "first-interaction").Tick > 300);
    }

    [Fact]
    public void Explore_IsTheFirstAdventure()
    {
        var simulation = StartedOn(IntentKind.Explore, "explore", Taskbar, visitsTarget: true);

        simulation.Submit(new ActionFinished(IntentKind.Explore, simulation.Pigeon.IntentStartedTick, ActionOutcome.Completed));
        simulation.RunTicks(1);

        Assert.Contains(simulation.LifeStory, e => e.Kind == LifeEventKind.FirstAdventure);
        // Первый визит — рождение рядом с панелью задач, второй — исследование.
        Assert.Equal(2, simulation.Pigeon.Memory.Entries.Single(e => e.Place == Taskbar.PlaceKey).Visits);
    }

    [Fact]
    public void LifeStory_SurvivesRestoreAndReplayWithoutDuplicates()
    {
        var live = new PigeonSimulation(7, Options, Personality.Average with { Curiosity = 90 });
        live.AttachWorld(new FixedWorld(Taskbar, Cursor, MonitorB));
        SimulationSnapshot? middle = null;
        for (var tick = 1; tick <= 40_000; tick++)
        {
            if (tick % 2_000 == 0)
            {
                live.Submit(new UserPoked());
            }

            // Мир подтверждает каждое намерение с целью через 5 секунд после начала.
            if (live.Pigeon.IntentTarget is not null && live.Tick - live.Pigeon.IntentStartedTick == 50)
            {
                live.Submit(new ActionFinished(live.Pigeon.Intent, live.Pigeon.IntentStartedTick, ActionOutcome.Completed));
            }

            live.RunTicks(1);
            if (tick == 20_000)
            {
                middle = live.CreateSnapshot();
            }
        }

        var restored = PigeonSimulation.Restore(live.CreateSnapshot());
        var replayed = PigeonSimulation.Replay(7, Options, live.Journal, live.Tick, Personality.Average with { Curiosity = 90 });
        var fromMiddle = PigeonSimulation.ReplayFrom(middle!, live.GetJournalFrom(middle!.JournalLength), live.Tick);

        var keys = live.LifeStory.Select(e => e.Key).ToList();
        Assert.Equal(keys.Distinct(), keys);
        Assert.Contains("first-adventure", keys);
        Assert.Contains("first-interaction", keys);
        foreach (var other in new[] { restored, replayed, fromMiddle })
        {
            Assert.Equal(live.ComputeStateHash(), other.ComputeStateHash());
            Assert.Equal(live.LifeStory, other.LifeStory);
        }

        restored.RunTicks(1_000);
        Assert.Equal(restored.LifeStory.Select(e => e.Key).Distinct().Count(), restored.LifeStory.Count);
    }

    /// <summary>Голубь с единственным намерением <paramref name="intent"/> начинает его к цели <paramref name="target"/>.</summary>
    private static PigeonSimulation StartedOn(IntentKind intent, string name, Affordance target, bool visitsTarget)
    {
        var targets = target.Kind switch
        {
            AffordanceKind.TerritoryZone => "territoryZone",
            AffordanceKind.AttentionTarget => "attentionTarget",
            _ => "perchSpot",
        };
        var json = $$"""
            { "selection": { "topK": 1, "delta": 0, "momentum": 0, "travelCostScaleSeconds": 60 },
              "intents": { "{{name}}": {
                "targets": ["{{targets}}"],
                "visitsTarget": {{(visitsTarget ? "true" : "false")}},
                "considerations": [ { "curve": "constant", "value": 0.5 } ],
                "durationSeconds": [600, 600] } } }
            """;
        var simulation = new PigeonSimulation(5, Options, behavior: BehaviorConfigJson.Parse(json));
        simulation.AttachWorld(new FixedWorld(target));
        simulation.RunTicks(1);
        Assert.Equal(intent, simulation.Pigeon.Intent);
        Assert.Equal(target.Id, simulation.Pigeon.IntentTarget);
        Assert.Equal(target.PlaceKey, simulation.Pigeon.IntentPlace);
        return simulation;
    }

    private static BehaviorConfig OnlyRelocation() => BehaviorConfigJson.Parse("""
        { "selection": { "topK": 1, "delta": 0, "momentum": 0, "travelCostScaleSeconds": 60 },
          "intents": { "relocate": { "targets": ["territoryZone"], "visitsTarget": true,
            "considerations": [ { "curve": "constant", "value": 0.5 } ], "durationSeconds": [600, 600] } } }
        """);

    private static BehaviorConfig OnlyIntents(params string[] names)
    {
        var intents = string.Join(",", names.Select(n => $$"""
            "{{n}}": { "considerations": [ { "curve": "constant", "value": 0.5 } ], "durationSeconds": [20, 20] }
            """));
        return BehaviorConfigJson.Parse($$"""
            { "selection": { "topK": 2, "delta": 0.5, "momentum": 0, "travelCostScaleSeconds": 60 },
              "intents": { {{intents}} } }
            """);
    }

    private sealed class FixedWorld(params Affordance[] items) : IAffordanceSource
    {
        public IReadOnlyList<Affordance> Current() => items;
    }
}
