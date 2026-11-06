using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Поверхности уровня L2 из топологии экрана (ТЗ §8.3, §10.1).</summary>
public sealed class ScreenSurfacesTests
{
    [Fact]
    public void BottomTaskbar_IsThePlaceToStand()
    {
        var surface = Assert.Single(ScreenSurfaces.Compute(FakeDesktop.LaptopOnly()));

        Assert.Equal(SurfaceKind.Taskbar, surface.Kind);
        Assert.Equal(1516, surface.Y);
        Assert.Equal("desktop/laptop/taskbar", surface.Key.Value);
    }

    [Fact]
    public void MonitorWithoutTaskbar_HasAFloorAtTheBottomOfItsWorkArea()
    {
        var floor = ScreenSurfaces.Compute(FakeDesktop.TwoMonitors()).Single(s => s.Monitor == FakeDesktop.External);

        Assert.Equal(SurfaceKind.Floor, floor.Kind);
        Assert.Equal(1080, floor.Y);
        Assert.Equal((2560, 4480), (floor.Left, floor.Right));
    }

    [Fact]
    public void LogicalLength_DependsOnScale()
    {
        var surfaces = ScreenSurfaces.Compute(FakeDesktop.TwoMonitors(externalScale: 1.5));

        Assert.Equal(2560 / 1.75, surfaces.First(s => s.Monitor == FakeDesktop.Laptop).LogicalLength, 1e-9);
        Assert.Equal(1920 / 1.5, surfaces.First(s => s.Monitor == FakeDesktop.External).LogicalLength, 1e-9);
    }

    [Theory]
    [InlineData(ScreenEdge.Top)]
    [InlineData(ScreenEdge.Left)]
    public void TaskbarNotAtTheBottom_LeavesTheFloor(ScreenEdge edge)
    {
        // Стоять на верхней панели или на верхнем краю экрана нельзя — голубь оказался бы над экраном.
        var work = edge == ScreenEdge.Top ? new PixelRect(0, 48, 1920, 1032) : new PixelRect(62, 0, 1858, 1080);
        var bar = edge == ScreenEdge.Top ? new PixelRect(0, 0, 1920, 48) : new PixelRect(0, 0, 62, 1080);
        var topology = new DesktopTopology(
            [FakeDesktop.ExternalScreen(x: 0) with { WorkArea = work }],
            [new TaskbarState(FakeDesktop.External, bar, edge)],
            TaskbarsKnown: true);

        var surface = Assert.Single(ScreenSurfaces.Compute(topology));

        Assert.Equal(SurfaceKind.Floor, surface.Kind);
        Assert.Equal(1080, surface.Y);
        Assert.Equal((work.X, work.X + work.Width), (surface.Left, surface.Right));
    }

    [Fact]
    public void TooShortSurface_IsDropped()
    {
        // 80 физических пикселей на 200 % — 40 логических, голубю не хватит.
        var tiny = new MonitorState(FakeDesktop.External, new PixelRect(0, 0, 80, 600), new PixelRect(0, 0, 80, 600), 2.0, true);

        Assert.Empty(ScreenSurfaces.Compute(new DesktopTopology([tiny], [], TaskbarsKnown: true)));
    }
}
