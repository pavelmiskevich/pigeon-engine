using Pigeon.Core;
using Pigeon.Simulation;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>
/// Голубь живёт на поверхностях двух мониторов с разным масштабом (M1.3, «готово, когда»):
/// симуляция с Desktop World на фейковом экране — так же, как их свяжет хост.
/// </summary>
public sealed class TwoMonitorLifeTests
{
    private const long TicksPerHour = 36_000;

    [Fact]
    public void Pigeon_PerchesOnBothMonitorsAndFliesBetweenThem()
    {
        var desktop = new FakeDesktop { Topology = FakeDesktop.TwoMonitors(externalScale: 1.25) };
        var host = new Host(desktop, seed: 21);

        host.Run(8 * TicksPerHour);

        Assert.Contains(host.Targets, t => t.StartsWith("desktop/laptop/", StringComparison.Ordinal));
        Assert.Contains(host.Targets, t => t.StartsWith("desktop/external/", StringComparison.Ordinal));
        Assert.True(host.Relocations.Count >= 2, $"перелётов: {host.Relocations.Count}");
        Assert.Contains(host.Relocations, r => r.To == FakeDesktop.External);
        Assert.Contains(host.Relocations, r => r.To == FakeDesktop.Laptop);
    }

    [Fact]
    public void UnpluggedMonitor_StartlesThePigeonInsteadOfTeleportingIt()
    {
        var desktop = new FakeDesktop { Topology = FakeDesktop.TwoMonitors() };
        var host = new Host(desktop, seed: 21);
        host.Run(TicksPerHour / 2);
        var arousal = host.Simulation.Pigeon.Mood.Arousal;
        var unplugged = host.World.PigeonMonitor!.Value;
        var remaining = unplugged == FakeDesktop.Laptop ? FakeDesktop.External : FakeDesktop.Laptop;

        desktop.Topology = remaining == FakeDesktop.Laptop
            ? FakeDesktop.LaptopOnly()
            : new DesktopTopology([FakeDesktop.ExternalScreen() with { IsPrimary = true }], [], TaskbarsKnown: true);
        host.Run(Host.RefreshEveryTicks);

        var lost = Assert.Single(host.Simulation.Journal, e => e.Event is FootingLost);
        Assert.Equal(new FootingLost(DisplacementReason.TerritoryGone), lost.Event);
        Assert.True(host.Simulation.Pigeon.Mood.Arousal > arousal + 0.1);
        Assert.Equal(remaining, host.World.PigeonMonitor);
        Assert.All(host.Simulation.Affordances, a => Assert.StartsWith($"desktop/{remaining}", a.PlaceKey.Value, StringComparison.Ordinal));
    }

    /// <summary>Связка хоста: экран перечитывается раз в 2 с, потеря опоры уходит в симуляцию входом.</summary>
    private sealed class Host
    {
        public const int RefreshEveryTicks = 20;

        public Host(FakeDesktop desktop, ulong seed)
        {
            World = new DesktopWorld(desktop, FakeDesktop.FullProbe());
            World.Refresh();
            Simulation = new PigeonSimulation(seed, SimulationOptions.Desktop);
            Simulation.AttachWorld(World);
        }

        public DesktopWorld World { get; }

        public PigeonSimulation Simulation { get; }

        public HashSet<string> Targets { get; } = [];

        public List<PigeonRelocated> Relocations { get; } = [];

        public void Run(long ticks)
        {
            for (long i = 0; i < ticks; i++)
            {
                if (Simulation.Tick % RefreshEveryTicks == 0)
                {
                    foreach (var displaced in World.Refresh().OfType<PigeonDisplaced>())
                    {
                        Simulation.Submit(new FootingLost(displaced.Reason));
                    }
                }

                Simulation.RunTicks(1);

                var pigeon = Simulation.Pigeon;
                if (World.TrackIntent(pigeon.Intent, pigeon.IntentTarget) is { } moved)
                {
                    Relocations.Add(moved);
                }

                if (pigeon.IntentTarget is { } target && Simulation.Affordances.FirstOrDefault(a => a.Id == target) is { } affordance)
                {
                    Targets.Add(affordance.PlaceKey.Value);
                }
            }
        }
    }
}
