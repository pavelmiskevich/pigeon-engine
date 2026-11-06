using Pigeon.Core;
using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Locomotion;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.Desktop.Rendering.Tests;

/// <summary>Сцена: клип по походке и намерению, положение, тень и масштаб (M1.4, M1.5).</summary>
public sealed class StageTests
{
    private static readonly MonitorKey Laptop = new("laptop");
    private static readonly MonitorKey External = new("external");

    [Theory]
    [InlineData(Gait.Walk, IntentKind.Rest, "walk")]
    [InlineData(Gait.Hop, IntentKind.Wander, "hop")]
    [InlineData(Gait.TakeOff, IntentKind.Relocate, "takeoff")]
    [InlineData(Gait.Fly, IntentKind.Relocate, "fly")]
    [InlineData(Gait.Land, IntentKind.Relocate, "land")]
    [InlineData(Gait.Stand, IntentKind.Sleep, "sleep")]
    [InlineData(Gait.Stand, IntentKind.Rest, "sit")]
    [InlineData(Gait.Stand, IntentKind.Explore, "peck")]
    [InlineData(Gait.Stand, IntentKind.Groom, "idle")]
    public void Clip_FollowsTheGaitAndOnTheSpotTheIntent(Gait gait, IntentKind intent, string clip)
    {
        Assert.Equal(clip, PigeonStage.ClipFor(gait, intent));
    }

    [Fact]
    public void Frame_TakesPositionDirectionAndWalkedPathFromTheBody()
    {
        var world = CreateWorld();
        var body = new PigeonBody(1200.4, 1516, 1516, Laptop, FacingLeft: false, Gait.Walk, Walked: 123.5, TimeSpan.FromSeconds(3));

        var frame = new PigeonStage(0.62).Compose(world, body, IntentKind.Wander, TimeSpan.FromSeconds(4))!.Value;

        Assert.Equal(("walk", 1200, 1516, 1516, false), (frame.Clip, frame.AnchorX, frame.AnchorY, frame.GroundY, frame.FacingLeft));
        Assert.Equal(123.5, frame.Walked);
        Assert.Equal(0.62 * 1.75, frame.Scale, 1e-12);
        Assert.Equal(0.0, frame.Height);
    }

    [Fact]
    public void FlyingPigeon_KnowsItsHeightForTheShadow()
    {
        var body = new PigeonBody(1000, 1516 - (217 * 2), 1516, Laptop, FacingLeft: true, Gait.Fly, 0, TimeSpan.Zero);

        var frame = new PigeonStage(0.62).Compose(CreateWorld(), body, IntentKind.Relocate, TimeSpan.Zero)!.Value;

        Assert.Equal(1516 - 434, frame.AnchorY);
        Assert.Equal(1516, frame.GroundY);
        Assert.Equal(434 / (1.75 * 0.62), frame.Height, 1e-9);
    }

    [Fact]
    public void Exploring_PecksAgainAndAgain()
    {
        var stage = new PigeonStage(0.62);
        var body = new PigeonBody(1000, 1516, 1516, Laptop, FacingLeft: true, Gait.Stand, 0, TimeSpan.FromSeconds(10));
        var later = TimeSpan.FromSeconds(10) + (PigeonStage.PeckEvery * 2) + TimeSpan.FromMilliseconds(100);

        var frame = stage.Compose(CreateWorld(), body, IntentKind.Explore, later)!.Value;

        Assert.Equal("peck", frame.Clip);
        Assert.Equal(TimeSpan.FromSeconds(10) + (PigeonStage.PeckEvery * 2), frame.ClipStarted);
    }

    [Fact]
    public void CrossingToAMonitorWithAnotherScale_ChangesTheSizeSmoothly()
    {
        var world = CreateWorld();
        var stage = new PigeonStage(0.62);
        stage.Compose(world, Standing(Laptop, 1000, 1516), IntentKind.Wander, TimeSpan.Zero);

        var start = TimeSpan.FromSeconds(10);
        var onExternal = Standing(External, 3000, 1080);
        var first = stage.Compose(world, onExternal, IntentKind.Wander, start)!.Value;
        var middle = stage.Compose(world, onExternal, IntentKind.Wander, start + (PigeonStage.ScaleTransitionTime / 2))!.Value;
        var end = stage.Compose(world, onExternal, IntentKind.Wander, start + PigeonStage.ScaleTransitionTime)!.Value;

        Assert.Equal(0.62 * 1.75, first.Scale, 1e-12);
        Assert.InRange(middle.Scale, 0.62 + 0.01, (0.62 * 1.75) - 0.01);
        Assert.Equal(0.62, end.Scale, 1e-12);
        Assert.False(stage.IsAnimating(start + PigeonStage.ScaleTransitionTime));
    }

    [Fact]
    public void HiddenPigeon_IsNotShown()
    {
        var hidden = new PigeonBody(5000, 500, 500, null, FacingLeft: true, Gait.Hidden, 0, TimeSpan.Zero);

        Assert.Null(new PigeonStage(0.62).Compose(CreateWorld(), hidden, IntentKind.Relocate, TimeSpan.Zero));
    }

    private static PigeonBody Standing(MonitorKey monitor, double x, double y) =>
        new(x, y, y, monitor, FacingLeft: true, Gait.Stand, 0, TimeSpan.Zero);

    private static DesktopWorld CreateWorld()
    {
        var world = new DesktopWorld(new Desktop(Topology()), Probe());
        world.Refresh();
        return world;
    }

    private static DesktopTopology Topology() => new(
        [
            new MonitorState(Laptop, new PixelRect(0, 0, 2560, 1600), new PixelRect(0, 0, 2560, 1516), 1.75, true),
            new MonitorState(External, new PixelRect(2560, 0, 1920, 1080), new PixelRect(2560, 0, 1920, 1080), 1.0, false),
        ],
        [new TaskbarState(Laptop, new PixelRect(0, 1516, 2560, 84), ScreenEdge.Bottom)],
        TaskbarsKnown: true);

    private static IReadOnlyList<CapabilityCheck> Probe() =>
        [.. Enum.GetValues<Capability>().Select(c => new CapabilityCheck(c, CapabilityStatus.Available, "test"))];

    private sealed class Desktop(DesktopTopology topology) : IDesktopEnvironment
    {
        public DesktopTopology ReadTopology() => topology;
    }
}
