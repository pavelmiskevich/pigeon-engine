using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Поверхности уровня L2 из топологии экрана (ТЗ §8.3, §10.1).</summary>
public sealed class ScreenSurfacesTests
{
    [Fact]
    public void BottomTaskbar_ReplacesTheFloor()
    {
        var surfaces = ScreenSurfaces.Compute(FakeDesktop.LaptopOnly());

        Assert.Equal([SurfaceKind.Taskbar, SurfaceKind.ScreenTop], surfaces.Select(s => s.Kind));
        Assert.Equal(1516, surfaces[0].Y);
        Assert.Equal("desktop/laptop/taskbar", surfaces[0].Key.Value);
        Assert.Equal(0, surfaces[1].Y);
    }

    [Fact]
    public void LogicalLength_DependsOnScale()
    {
        var surfaces = ScreenSurfaces.Compute(FakeDesktop.TwoMonitors(externalScale: 1.5));

        Assert.Equal(2560 / 1.75, surfaces.First(s => s.Monitor == FakeDesktop.Laptop).LogicalLength, 1e-9);
        Assert.Equal(1920 / 1.5, surfaces.First(s => s.Monitor == FakeDesktop.External).LogicalLength, 1e-9);
    }

    [Fact]
    public void MonitorWithoutTaskbar_HasAFloorAtTheBottomOfItsWorkArea()
    {
        var floor = ScreenSurfaces.Compute(FakeDesktop.TwoMonitors()).Single(s => s.Monitor == FakeDesktop.External && s.Kind == SurfaceKind.Floor);

        Assert.Equal(1080, floor.Y);
        Assert.Equal((2560, 4480), (floor.Left, floor.Right));
    }

    [Fact]
    public void TopTaskbar_ReplacesTheScreenTopAndSideTaskbarGivesNothing()
    {
        var monitor = FakeDesktop.ExternalScreen(x: 0);
        var top = new DesktopTopology(
            [monitor with { WorkArea = new PixelRect(0, 48, 1920, 1032) }],
            [new TaskbarState(FakeDesktop.External, new PixelRect(0, 0, 1920, 48), ScreenEdge.Top)],
            TaskbarsKnown: true);
        var side = new DesktopTopology(
            [monitor with { WorkArea = new PixelRect(62, 0, 1858, 1080) }],
            [new TaskbarState(FakeDesktop.External, new PixelRect(0, 0, 62, 1080), ScreenEdge.Left)],
            TaskbarsKnown: true);

        Assert.Equal([SurfaceKind.Floor, SurfaceKind.Taskbar], ScreenSurfaces.Compute(top).Select(s => s.Kind));
        Assert.Equal([SurfaceKind.Floor, SurfaceKind.ScreenTop], ScreenSurfaces.Compute(side).Select(s => s.Kind));
        Assert.Equal((62, 1920), (ScreenSurfaces.Compute(side)[0].Left, ScreenSurfaces.Compute(side)[0].Right));
    }

    [Fact]
    public void MonitorAbove_CutsTheScreenTopOfTheOneBelow()
    {
        // Внешний монитор стоит над ноутбуком со сдвигом вправо: верх ноутбука открыт только слева.
        var topology = new DesktopTopology(
            [FakeDesktop.LaptopScreen(), FakeDesktop.ExternalScreen(x: 1000, y: -1080)],
            [FakeDesktop.LaptopTaskbar],
            TaskbarsKnown: true);

        var laptopTop = ScreenSurfaces.Compute(topology).Where(s => s.Monitor == FakeDesktop.Laptop && s.Kind == SurfaceKind.ScreenTop).ToList();

        Assert.Equal([(0, 1000)], laptopTop.Select(s => (s.Left, s.Right)));
    }

    [Fact]
    public void TooShortPieces_AreDropped()
    {
        // Над ноутбуком почти целиком стоит другой монитор: слева остаётся 60 физических пикселей —
        // 34 логических на 175 %, голубю не хватит.
        var topology = new DesktopTopology(
            [FakeDesktop.LaptopScreen(), FakeDesktop.ExternalScreen(x: 60, y: -1080) with { Bounds = new PixelRect(60, -1080, 2500, 1080) }],
            [FakeDesktop.LaptopTaskbar],
            TaskbarsKnown: true);

        Assert.DoesNotContain(ScreenSurfaces.Compute(topology), s => s.Monitor == FakeDesktop.Laptop && s.Kind == SurfaceKind.ScreenTop);
    }
}
