using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Locomotion;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Локомоция Desktop L2 (ТЗ §8.1, §10.1, M1.5) на фейковом экране.</summary>
public sealed class LocomotionTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1.0 / 60);

    [Fact]
    public void Pigeon_AppearsStandingOnItsHomeSurface()
    {
        var (_, locomotion, _) = Create(FakeDesktop.LaptopOnly());

        locomotion.Update(TimeSpan.Zero);

        Assert.Equal(Gait.Stand, locomotion.Body.Gait);
        Assert.Equal(1516, locomotion.Body.Y);
        Assert.Equal(2560 * 0.78, locomotion.Body.X, 1e-9);
        Assert.Equal(FakeDesktop.Laptop, locomotion.Body.Monitor);
    }

    [Fact]
    public void Wandering_WalksAlongTheSurfaceAtTheStrideSpeed()
    {
        var (_, locomotion, _) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        locomotion.Execute(IntentKind.Wander, null, 1);

        run.For(TimeSpan.FromSeconds(120));

        Assert.Contains(Gait.Walk, run.Gaits);
        Assert.All(run.Bodies, b => Assert.InRange(b.X, 60 * 1.75, 2560 - (60 * 1.75)));
        Assert.All(run.Bodies, b => Assert.Equal(1516, b.Y));
        var walking = run.Bodies.Zip(run.Bodies.Skip(1)).Where(p => p.First.Gait == Gait.Walk && p.Second.Gait == Gait.Walk).ToList();
        Assert.NotEmpty(walking);
        Assert.All(walking, p => Assert.InRange(Math.Abs(p.Second.X - p.First.X) / Frame.TotalSeconds, 0, (20 * 1.75) + 0.01));
        var (first, last) = (walking[0].First, walking[^1].Second);
        Assert.True(last.Walked > first.Walked);
    }

    [Fact]
    public void Walked_CountsLogicalDrawingPixels()
    {
        var (_, locomotion, _) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        locomotion.Execute(IntentKind.Wander, null, 1);
        run.For(TimeSpan.FromSeconds(60));

        var travelled = run.Bodies.Zip(run.Bodies.Skip(1)).Sum(p => Math.Abs(p.Second.X - p.First.X));

        Assert.Equal(travelled / 1.75 / 0.62, run.Bodies[^1].Walked - run.Bodies[0].Walked, 1e-6);
    }

    [Fact]
    public void Exploring_TheSameSurface_WalksPecksAndReportsDone()
    {
        var (world, locomotion, _) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        var target = DesktopWorld.IdOf(world.HomeSurface(FakeDesktop.Laptop)!.Key);
        locomotion.Execute(IntentKind.Explore, target, 42);

        run.For(TimeSpan.FromSeconds(120));

        var report = Assert.Single(run.Events.OfType<ActionReported>());
        Assert.Equal(new ActionReported(IntentKind.Explore, 42, ActionOutcome.Completed), report);
        Assert.DoesNotContain(Gait.Fly, run.Gaits);
    }

    [Fact]
    public void ExploringAVanishedPlace_Fails()
    {
        var (_, locomotion, _) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        locomotion.Execute(IntentKind.Explore, new AffordanceId(12345), 7);

        run.For(Frame * 3);

        Assert.Equal(new ActionReported(IntentKind.Explore, 7, ActionOutcome.Failed, ActionFailure.TargetGone), Assert.Single(run.Events));
    }

    [Fact]
    public void Relocating_ToAnAdjacentMonitor_FliesInViewAndLands()
    {
        var (world, locomotion, _) = Create(FakeDesktop.TwoMonitors());
        var run = new Run(locomotion);
        locomotion.Execute(IntentKind.Relocate, Territory(world), 3);

        run.For(TimeSpan.FromSeconds(30));

        Assert.Equal([Gait.TakeOff, Gait.Fly, Gait.Land, Gait.Stand], Phases(run.Gaits));
        Assert.Contains(new LandedOnMonitor(new PigeonRelocated(FakeDesktop.Laptop, FakeDesktop.External)), run.Events);
        Assert.Contains(new ActionReported(IntentKind.Relocate, 3, ActionOutcome.Completed), run.Events);
        Assert.Equal(FakeDesktop.External, world.PigeonMonitor);
        Assert.Equal(1080, locomotion.Body.Y);
        Assert.InRange(locomotion.Body.X, 2560, 4480);
        Assert.All(run.Bodies.Where(b => b.Gait == Gait.Fly), b => Assert.NotNull(world.MonitorAt(b.X, b.Y - 1)));
    }

    [Fact]
    public void Flying_FacesWhereItGoesAndKeepsItsShadowOnTheGround()
    {
        var (world, locomotion, _) = Create(FakeDesktop.TwoMonitors());
        var run = new Run(locomotion);
        locomotion.Execute(IntentKind.Relocate, Territory(world), 3);

        run.For(TimeSpan.FromSeconds(30));

        var flying = run.Bodies.Where(b => b.Gait == Gait.Fly).ToList();
        Assert.All(flying, b => Assert.False(b.FacingLeft));
        Assert.Contains(flying, b => b.GroundY > b.Y + 50);
        Assert.All(flying, b => Assert.True(b.GroundY is 1516 or 1080, $"тень на {b.GroundY}"));
    }

    [Fact]
    public void Relocating_AcrossAGap_LeavesTheScreenAndComesBack()
    {
        var topology = FakeDesktop.TwoMonitors() with { Monitors = [FakeDesktop.LaptopScreen(), FakeDesktop.ExternalScreen(x: 3200)] };
        var (world, locomotion, _) = Create(topology);
        var run = new Run(locomotion);
        locomotion.Execute(IntentKind.Relocate, Territory(world), 3);

        run.For(TimeSpan.FromSeconds(40));

        Assert.Equal([Gait.TakeOff, Gait.Fly, Gait.Hidden, Gait.Fly, Gait.Land, Gait.Stand], Phases(run.Gaits));
        Assert.Equal(FakeDesktop.External, world.PigeonMonitor);
        Assert.Equal(1080, locomotion.Body.Y);
    }

    [Fact]
    public void NewIntentInTheAir_WaitsForTheLanding()
    {
        var (world, locomotion, _) = Create(FakeDesktop.TwoMonitors());
        var run = new Run(locomotion);
        locomotion.Execute(IntentKind.Relocate, Territory(world), 3);
        run.For(TimeSpan.FromSeconds(1));
        Assert.True(locomotion.Body.IsAirborne);

        locomotion.Execute(IntentKind.Rest, null, 20);
        run.For(TimeSpan.FromSeconds(30));

        Assert.Equal(FakeDesktop.External, world.PigeonMonitor);
        Assert.Equal(Gait.Stand, locomotion.Body.Gait);
        Assert.DoesNotContain(run.Events, e => e is ActionReported { Intent: IntentKind.Relocate, Outcome: ActionOutcome.Failed });
    }

    [Fact]
    public void UnpluggedMonitor_DropsThePigeonFromTheTopOfTheNextOne()
    {
        var (world, locomotion, desktop) = Create(FakeDesktop.TwoMonitors());
        var run = new Run(locomotion);
        world.MovePigeonTo(FakeDesktop.External);
        locomotion.OnWorldChanged([new PigeonDisplaced(FakeDesktop.Laptop, FakeDesktop.External, DisplacementReason.TerritoryGone)], run.Now);

        desktop.Topology = FakeDesktop.LaptopOnly();
        var changes = world.Refresh();
        locomotion.OnWorldChanged(changes, run.Now);
        run.For(TimeSpan.FromSeconds(10));

        Assert.Contains(changes, c => c is PigeonDisplaced);
        Assert.Equal(Gait.Fly, run.Bodies[0].Gait);
        Assert.True(run.Bodies[0].Y < 300, $"падает с {run.Bodies[0].Y}");
        Assert.Contains(Gait.Land, run.Gaits);
        Assert.Equal((Gait.Stand, 1516.0), (locomotion.Body.Gait, locomotion.Body.Y));
        Assert.Equal(FakeDesktop.Laptop, locomotion.Body.Monitor);
    }

    [Fact]
    public void VanishedTaskbar_DropsThePigeonOntoTheFloorWithAnEvent()
    {
        var (world, locomotion, desktop) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);

        desktop.Topology = new DesktopTopology([FakeDesktop.LaptopScreen() with { WorkArea = new(0, 0, 2560, 1600) }], [], TaskbarsKnown: false);
        var events = locomotion.OnWorldChanged(world.Refresh(), run.Now);
        run.For(TimeSpan.FromSeconds(5));

        Assert.Equal([new FootingGone(DisplacementReason.PlaceGone)], events);
        Assert.Equal((Gait.Stand, 1600.0), (locomotion.Body.Gait, locomotion.Body.Y));
    }

    [Fact]
    public void MovedSurface_CarriesThePigeon()
    {
        var (world, locomotion, desktop) = Create(FakeDesktop.LaptopOnly());
        var run = new Run(locomotion);
        run.For(Frame);

        desktop.Topology = FakeDesktop.LaptopOnly() with { Taskbars = [new(FakeDesktop.Laptop, new(0, 1480, 2560, 120), ScreenEdge.Bottom)] };
        var events = locomotion.OnWorldChanged(world.Refresh(), run.Now);

        Assert.Empty(events);
        Assert.Equal(1480, locomotion.Body.Y);
    }

    private static AffordanceId Territory(DesktopWorld world) =>
        world.Current().Single(a => a.Kind == AffordanceKind.TerritoryZone).Id;

    /// <summary>Подряд идущие одинаковые походки — одна фаза.</summary>
    private static List<Gait> Phases(IEnumerable<Gait> gaits)
    {
        var phases = new List<Gait>();
        foreach (var gait in gaits)
        {
            if (phases.Count == 0 || phases[^1] != gait)
            {
                phases.Add(gait);
            }
        }

        return phases;
    }

    private static (DesktopWorld World, DesktopLocomotion Locomotion, FakeDesktop Desktop) Create(DesktopTopology topology)
    {
        var desktop = new FakeDesktop { Topology = topology };
        var world = new DesktopWorld(desktop, FakeDesktop.FullProbe());
        world.Refresh();
        return (world, new DesktopLocomotion(world, new Pcg32(1, 2)), desktop);
    }

    /// <summary>Прогон локомоции кадрами по 1/60 с с записью тела и событий.</summary>
    private sealed class Run(DesktopLocomotion locomotion)
    {
        public TimeSpan Now { get; private set; }

        public List<PigeonBody> Bodies { get; } = [];

        public List<LocomotionEvent> Events { get; } = [];

        public IEnumerable<Gait> Gaits => Bodies.Select(b => b.Gait);

        public void For(TimeSpan duration)
        {
            var end = Now + duration;
            while (Now < end)
            {
                Now += Frame;
                Events.AddRange(locomotion.Update(Now));
                Bodies.Add(locomotion.Body);
            }
        }
    }
}
