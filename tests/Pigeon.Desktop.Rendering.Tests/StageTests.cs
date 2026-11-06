using Pigeon.Core;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.Desktop.Rendering.Tests;

/// <summary>Где стоит голубь и какой клип играет, пока нет локомоции (M1.4).</summary>
public sealed class StageTests
{
    private static readonly MonitorKey Laptop = new("laptop");
    private static readonly MonitorKey External = new("external");

    [Fact]
    public void Pigeon_StandsOnTheTaskbarOfItsMonitor()
    {
        var world = CreateWorld();
        var stage = new PigeonStage(0.62);

        var frame = stage.Compose(world, Pigeon(IntentKind.Wander), TimeSpan.Zero)!.Value;

        Assert.Equal(1516, frame.AnchorY);
        Assert.Equal((int)Math.Round(2560 * 0.78), frame.AnchorX);
        Assert.Equal(0.62 * 1.75, frame.Scale, 1e-12);
        Assert.Equal("idle", frame.Clip);
    }

    [Theory]
    [InlineData(IntentKind.Sleep, "sleep")]
    [InlineData(IntentKind.Rest, "sleep")]
    [InlineData(IntentKind.Explore, "peck")]
    [InlineData(IntentKind.Groom, "idle")]
    [InlineData(IntentKind.SeekAttention, "idle")]
    public void Clip_FollowsTheIntent(IntentKind intent, string clip)
    {
        Assert.Equal(clip, PigeonStage.ClipFor(intent));
    }

    [Fact]
    public void MovingToAMonitorWithAnotherScale_ChangesTheSizeSmoothly()
    {
        var stage = new PigeonStage(0.62);
        var desktop = new Desktop(Topology());
        var world = new DesktopWorld(desktop, Probe());
        world.Refresh();
        stage.Compose(world, Pigeon(IntentKind.Wander), TimeSpan.Zero);

        var territory = world.Current().Single(a => a.Kind == AffordanceKind.TerritoryZone).Id;
        world.TrackIntent(IntentKind.Relocate, territory);
        world.TrackIntent(IntentKind.Wander, null);
        var start = TimeSpan.FromSeconds(10);
        var first = stage.Compose(world, Pigeon(IntentKind.Wander), start)!.Value;
        var middle = stage.Compose(world, Pigeon(IntentKind.Wander), start + PigeonStage.ScaleTransitionTime / 2)!.Value;
        var end = stage.Compose(world, Pigeon(IntentKind.Wander), start + PigeonStage.ScaleTransitionTime)!.Value;

        Assert.Equal(0.62 * 1.75, first.Scale, 1e-12);
        Assert.InRange(middle.Scale, 0.62 * 1.0 + 0.01, 0.62 * 1.75 - 0.01);
        Assert.Equal(0.62, end.Scale, 1e-12);
        Assert.Equal(1080, end.AnchorY);
        Assert.False(stage.IsAnimating(start + PigeonStage.ScaleTransitionTime));
    }

    [Fact]
    public void WithoutMonitors_ThereIsNothingToShow()
    {
        var world = new DesktopWorld(new Desktop(DesktopTopology.Empty), Probe());
        world.Refresh();

        Assert.Null(new PigeonStage(0.62).Compose(world, Pigeon(IntentKind.Wander), TimeSpan.Zero));
    }

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

    private static PigeonState Pigeon(IntentKind intent)
    {
        var pigeon = new PigeonState(Personality.Average, MoodModel.Default.Baseline(Personality.Average));
        pigeon.StartIntent(intent, null, 0, 100);
        return pigeon;
    }

    private sealed class Desktop(DesktopTopology topology) : IDesktopEnvironment
    {
        public DesktopTopology ReadTopology() => topology;
    }
}
