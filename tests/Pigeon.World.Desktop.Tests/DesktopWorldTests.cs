using Pigeon.Core;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Desktop World L2 на фейковом экране (ТЗ §11, §12, §32).</summary>
public sealed class DesktopWorldTests
{
    [Fact]
    public void FirstLook_PutsThePigeonOnThePrimaryMonitor()
    {
        var (world, _) = Create(FakeDesktop.TwoMonitors());

        Assert.Equal(DesktopLevel.ScreenWorld, world.Level);
        Assert.Equal(FakeDesktop.Laptop, world.PigeonMonitor);
        Assert.Equal(
            ["desktop/external", "desktop/laptop/taskbar", "desktop/laptop/top"],
            world.Current().Select(a => a.PlaceKey.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OtherMonitor_IsATerritoryAndItsSurfacesWaitUntilThePigeonFliesThere()
    {
        var (world, _) = Create(FakeDesktop.TwoMonitors());

        var territory = Assert.Single(world.Current(), a => a.Kind == AffordanceKind.TerritoryZone);
        Assert.Equal(DesktopWorld.AdjacentFlight, territory.TravelCost);
        Assert.DoesNotContain(world.Current(), a => a.PlaceKey.Value.StartsWith("desktop/external/", StringComparison.Ordinal));
    }

    [Fact]
    public void FarMonitor_TakesLongerToReach()
    {
        var topology = FakeDesktop.TwoMonitors() with { Monitors = [FakeDesktop.LaptopScreen(), FakeDesktop.ExternalScreen(x: 3000)] };
        var (world, _) = Create(topology);

        Assert.Equal(DesktopWorld.GapFlight, world.Current().Single(a => a.Kind == AffordanceKind.TerritoryZone).TravelCost);
    }

    [Fact]
    public void AffordanceIds_AreStableAcrossRefreshesAndRuns()
    {
        var (first, _) = Create(FakeDesktop.TwoMonitors());
        var (second, _) = Create(FakeDesktop.TwoMonitors());
        var ids = first.Current().Select(a => a.Id).ToList();

        first.Refresh();

        Assert.Equal(ids, first.Current().Select(a => a.Id));
        Assert.Equal(ids, second.Current().Select(a => a.Id));
        Assert.Equal(DesktopWorld.IdOf(new PlaceKey("desktop/laptop/top")), first.Current().Single(a => a.PlaceKey.Value == "desktop/laptop/top").Id);
    }

    [Fact]
    public void ConnectingAMonitor_AddsATerritory()
    {
        var (world, desktop) = Create(FakeDesktop.LaptopOnly());

        desktop.Topology = FakeDesktop.TwoMonitors();
        var events = world.Refresh();

        Assert.Equal(FakeDesktop.External, Assert.IsType<MonitorConnected>(Assert.Single(events)).Monitor.Key);
        Assert.Contains(world.Current(), a => a.Kind == AffordanceKind.TerritoryZone);
    }

    [Fact]
    public void ScaleChange_IsReportedAndSurfacesFollowIt()
    {
        var (world, desktop) = Create(FakeDesktop.TwoMonitors());

        desktop.Topology = FakeDesktop.TwoMonitors(externalScale: 1.25);
        var change = Assert.IsType<MonitorChanged>(Assert.Single(world.Refresh()));

        Assert.True(change.ScaleChanged);
        Assert.Equal(1920 / 1.25, world.Surfaces.First(s => s.Monitor == FakeDesktop.External).LogicalLength, 1e-9);
    }

    [Fact]
    public void UnpluggingThePigeonsMonitor_DropsItOnTheNearestOneWithAnEvent()
    {
        var desktop = new FakeDesktop { Topology = new DesktopTopology(
            [FakeDesktop.ExternalScreen() with { IsPrimary = true }, FakeDesktop.LaptopScreen(primary: false)], [FakeDesktop.LaptopTaskbar], true) };
        var world = new DesktopWorld(desktop, FakeDesktop.FullProbe());
        world.Refresh();
        Assert.Equal(FakeDesktop.External, world.PigeonMonitor);

        desktop.Topology = FakeDesktop.LaptopOnly() with { Monitors = [FakeDesktop.LaptopScreen(primary: true)] };
        var events = world.Refresh();

        Assert.Contains(events, e => e is MonitorDisconnected d && d.Monitor.Key == FakeDesktop.External);
        Assert.Contains(new PigeonDisplaced(FakeDesktop.External, FakeDesktop.Laptop, DisplacementReason.TerritoryGone), events);
        Assert.Equal(FakeDesktop.Laptop, world.PigeonMonitor);
        Assert.All(world.Current(), a => Assert.StartsWith("desktop/laptop/", a.PlaceKey.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void LosingMonitorEnumeration_FallsBackToWindowModeAndBack()
    {
        var (world, desktop) = Create(FakeDesktop.LaptopOnly());

        desktop.Topology = DesktopTopology.Empty with { TaskbarsKnown = true };
        var lost = world.Refresh();

        Assert.Contains(new CapabilityChanged(Capability.MonitorEnumeration, false), lost);
        Assert.Contains(new LevelChanged(DesktopLevel.ScreenWorld, DesktopLevel.WindowMode), lost);
        Assert.Contains(new PigeonDisplaced(FakeDesktop.Laptop, null, DisplacementReason.CapabilityLost), lost);
        Assert.Empty(world.Current());

        desktop.Topology = FakeDesktop.LaptopOnly();
        var back = world.Refresh();

        Assert.Contains(new CapabilityChanged(Capability.MonitorEnumeration, true), back);
        Assert.Contains(new LevelChanged(DesktopLevel.WindowMode, DesktopLevel.ScreenWorld), back);
        Assert.Equal(FakeDesktop.Laptop, world.PigeonMonitor);
        Assert.NotEmpty(world.Current());
    }

    [Fact]
    public void LosingTaskbarGeometry_TurnsTheTaskbarIntoAFloor()
    {
        var (world, desktop) = Create(FakeDesktop.LaptopOnly());

        desktop.Topology = new DesktopTopology([FakeDesktop.LaptopScreen()], [], TaskbarsKnown: false);
        var events = world.Refresh();

        Assert.Equal([new CapabilityChanged(Capability.TaskbarGeometry, false)], events);
        Assert.Contains(world.Current(), a => a.PlaceKey.Value == "desktop/laptop/floor" && a.Kind == AffordanceKind.RestSpot);
        Assert.DoesNotContain(world.Current(), a => a.PlaceKey.Value == "desktop/laptop/taskbar");
    }

    [Fact]
    public void WithoutAnOverlay_ThereIsNoScreenWorld()
    {
        var probe = FakeDesktop.FullProbe()
            .Select(c => c.Capability == Capability.ClickThrough ? c with { Status = CapabilityStatus.Unavailable } : c)
            .ToList();
        var world = new DesktopWorld(new FakeDesktop { Topology = FakeDesktop.LaptopOnly() }, probe);

        world.Refresh();

        Assert.Equal(DesktopLevel.WindowMode, world.Level);
    }

    [Fact]
    public void FinishedRelocation_MovesThePigeonToTheOtherMonitor()
    {
        var (world, _) = Create(FakeDesktop.TwoMonitors());
        var territory = world.Current().Single(a => a.Kind == AffordanceKind.TerritoryZone).Id;

        Assert.Null(world.TrackIntent(IntentKind.Relocate, territory));
        Assert.Equal(FakeDesktop.Laptop, world.PigeonMonitor);
        var moved = world.TrackIntent(IntentKind.Rest, null);

        Assert.Equal(new PigeonRelocated(FakeDesktop.Laptop, FakeDesktop.External), moved);
        Assert.Equal(FakeDesktop.External, world.PigeonMonitor);
        Assert.Contains(world.Current(), a => a.PlaceKey.Value == "desktop/external/floor");
        Assert.Contains(world.Current(), a => a.PlaceKey.Value == "desktop/laptop");
        Assert.Null(world.TrackIntent(IntentKind.Wander, null));
    }

    private static (DesktopWorld World, FakeDesktop Desktop) Create(DesktopTopology topology)
    {
        var desktop = new FakeDesktop { Topology = topology };
        var world = new DesktopWorld(desktop, FakeDesktop.FullProbe());
        world.Refresh();
        return (world, desktop);
    }
}
