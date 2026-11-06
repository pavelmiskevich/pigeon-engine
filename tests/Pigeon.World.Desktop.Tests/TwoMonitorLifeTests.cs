using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.Simulation;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Locomotion;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>
/// Голубь живёт на поверхностях двух мониторов с разным масштабом (M1.3, M1.5): симуляция,
/// Desktop World и локомоция на фейковом экране — так же, как их связывает хост.
/// </summary>
public sealed class TwoMonitorLifeTests
{
    private const long TicksPerHour = 36_000;

    [Fact]
    public void Pigeon_LivesOnBothMonitorsAndFliesBetweenThem()
    {
        var desktop = new FakeDesktop { Topology = FakeDesktop.TwoMonitors(externalScale: 1.25) };
        var host = new Host(desktop, seed: 21);

        host.Run(6 * TicksPerHour);

        Assert.Contains(host.Monitors, m => m == FakeDesktop.Laptop);
        Assert.Contains(host.Monitors, m => m == FakeDesktop.External);
        Assert.Contains(host.Landings, r => r.To == FakeDesktop.External);
        Assert.Contains(host.Landings, r => r.To == FakeDesktop.Laptop);
        Assert.Contains(host.Simulation.Journal, e => e.Event is ActionFinished { Intent: IntentKind.Relocate, Outcome: ActionOutcome.Completed });
        Assert.Contains(host.Simulation.Journal, e => e.Event is ActionFinished { Intent: IntentKind.Explore, Outcome: ActionOutcome.Completed });
        Assert.Contains(Gait.Walk, host.Gaits);
    }

    [Fact]
    public void UnpluggedMonitor_StartlesThePigeonAndDropsItOntoTheOtherOne()
    {
        var desktop = new FakeDesktop { Topology = FakeDesktop.TwoMonitors() };
        var host = new Host(desktop, seed: 21);
        host.Run(TicksPerHour / 4);
        var arousal = host.Simulation.Pigeon.Mood.Arousal;
        var unplugged = host.World.PigeonMonitor!.Value;
        var remaining = unplugged == FakeDesktop.Laptop ? FakeDesktop.External : FakeDesktop.Laptop;

        desktop.Topology = remaining == FakeDesktop.Laptop
            ? FakeDesktop.LaptopOnly()
            : new DesktopTopology([FakeDesktop.ExternalScreen() with { IsPrimary = true }], [], TaskbarsKnown: true);
        host.Run(Host.RefreshEveryTicks + 1);

        var lost = Assert.Single(host.Simulation.Journal, e => e.Event is FootingLost);
        Assert.Equal(new FootingLost(DisplacementReason.TerritoryGone), lost.Event);
        Assert.True(host.Simulation.Pigeon.Mood.Arousal > arousal + 0.1);
        Assert.Equal(remaining, host.World.PigeonMonitor);
        Assert.Contains(Gait.Fly, host.Gaits.TakeLast(Host.RefreshEveryTicks * Host.FramesPerTick));

        host.Run(100);
        Assert.Equal(remaining, host.Locomotion.Body.Monitor);
        Assert.False(host.Locomotion.Body.IsAirborne);
    }

    /// <summary>
    /// Связка хоста: экран перечитывается раз в 2 с, локомоция идёт кадрами по 1/60 с, её отчёты и
    /// потеря опоры уходят в симуляцию входами.
    /// </summary>
    private sealed class Host
    {
        public const int RefreshEveryTicks = 20;
        public const int FramesPerTick = 6;

        private TimeSpan _now;

        public Host(FakeDesktop desktop, ulong seed)
        {
            World = new DesktopWorld(desktop, FakeDesktop.FullProbe());
            World.Refresh();
            Simulation = new PigeonSimulation(seed, SimulationOptions.Desktop);
            Simulation.AttachWorld(World);
            Locomotion = new DesktopLocomotion(World, new Pcg32(seed, 3));
        }

        public DesktopWorld World { get; }

        public PigeonSimulation Simulation { get; }

        public DesktopLocomotion Locomotion { get; }

        public HashSet<MonitorKey> Monitors { get; } = [];

        public List<PigeonRelocated> Landings { get; } = [];

        public List<Gait> Gaits { get; } = [];

        public void Run(long ticks)
        {
            for (long i = 0; i < ticks; i++)
            {
                if (Simulation.Tick % RefreshEveryTicks == 0)
                {
                    var changes = World.Refresh();
                    foreach (var displaced in changes.OfType<PigeonDisplaced>())
                    {
                        Simulation.Submit(new FootingLost(displaced.Reason));
                    }

                    Report(Locomotion.OnWorldChanged(changes, _now));
                }

                Simulation.RunTicks(1);
                var pigeon = Simulation.Pigeon;
                Locomotion.Execute(pigeon.Intent, pigeon.IntentTarget, pigeon.IntentStartedTick);
                for (var f = 0; f < FramesPerTick; f++)
                {
                    _now += SimulationOptions.Desktop.Step / FramesPerTick;
                    Report(Locomotion.Update(_now));
                    Gaits.Add(Locomotion.Body.Gait);
                }

                if (Locomotion.Body.Monitor is { } monitor && !Locomotion.Body.IsAirborne)
                {
                    Monitors.Add(monitor);
                }
            }
        }

        private void Report(IReadOnlyList<LocomotionEvent> events)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case ActionReported report:
                        Simulation.Submit(new ActionFinished(report.Intent, report.IntentStartedTick, report.Outcome, report.Failure));
                        break;
                    case FootingGone gone:
                        Simulation.Submit(new FootingLost(gone.Reason));
                        break;
                    case LandedOnMonitor landed:
                        Landings.Add(landed.Change);
                        break;
                }
            }
        }
    }
}
