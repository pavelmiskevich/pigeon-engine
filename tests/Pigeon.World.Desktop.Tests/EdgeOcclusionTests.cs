using System.Diagnostics;
using Pigeon.World.Desktop.Perches;

namespace Pigeon.World.Desktop.Tests;

public sealed class EdgeOcclusionTests
{
    private static readonly PixelBox[] OneScreen = [new(0, 0, 1920, 1040)];

    [Fact]
    public void LoneWindow_WholeEdgeIsVisible()
    {
        var spans = EdgeOcclusion.VisibleTopEdge(0, [new(100, 300, 900, 800)], OneScreen, clearance: 60, minLength: 50);

        Assert.Equal([new EdgeSpan(100, 900)], spans);
    }

    [Fact]
    public void WindowAbove_CutsItsProjection()
    {
        PixelBox[] windows = [new(400, 200, 600, 700), new(100, 300, 900, 800)];

        var spans = EdgeOcclusion.VisibleTopEdge(1, windows, OneScreen, clearance: 60, minLength: 50);

        Assert.Equal([new EdgeSpan(100, 400), new EdgeSpan(600, 900)], spans);
    }

    [Fact]
    public void WindowAboveButFarBelowTheEdge_DoesNotCut()
    {
        PixelBox[] windows = [new(400, 500, 600, 700), new(100, 300, 900, 800)];

        var spans = EdgeOcclusion.VisibleTopEdge(1, windows, OneScreen, clearance: 60, minLength: 50);

        Assert.Equal([new EdgeSpan(100, 900)], spans);
    }

    [Fact]
    public void WindowInTheClearanceBand_Cuts()
    {
        // Окно выше по z-order заканчивается прямо над кромкой: голубю там негде стоять.
        PixelBox[] windows = [new(400, 100, 600, 280), new(100, 300, 900, 800)];

        var spans = EdgeOcclusion.VisibleTopEdge(1, windows, OneScreen, clearance: 60, minLength: 50);

        Assert.Equal([new EdgeSpan(100, 400), new EdgeSpan(600, 900)], spans);
    }

    [Fact]
    public void WindowBelowInZOrder_DoesNotCut()
    {
        PixelBox[] windows = [new(100, 300, 900, 800), new(400, 200, 600, 700)];

        var spans = EdgeOcclusion.VisibleTopEdge(0, windows, OneScreen, clearance: 60, minLength: 50);

        Assert.Equal([new EdgeSpan(100, 900)], spans);
    }

    [Fact]
    public void ShortPieces_AreDropped()
    {
        PixelBox[] windows = [new(130, 200, 870, 700), new(100, 300, 900, 800)];

        var spans = EdgeOcclusion.VisibleTopEdge(1, windows, OneScreen, clearance: 60, minLength: 50);

        Assert.Empty(spans);
    }

    [Fact]
    public void EdgeTooCloseToScreenTop_IsNotAPerch()
    {
        var spans = EdgeOcclusion.VisibleTopEdge(0, [new(100, 20, 900, 800)], OneScreen, clearance: 60, minLength: 50);

        Assert.Empty(spans);
    }

    [Fact]
    public void EdgeAcrossTwoMonitors_IsOneSpan()
    {
        PixelBox[] screens = [new(0, 0, 1920, 1040), new(1920, 0, 5360, 1392)];

        var spans = EdgeOcclusion.VisibleTopEdge(0, [new(1500, 300, 2500, 800)], screens, clearance: 60, minLength: 50);

        Assert.Equal([new EdgeSpan(1500, 2500)], spans);
    }

    [Fact]
    public void EdgeOffScreen_IsClipped()
    {
        var spans = EdgeOcclusion.VisibleTopEdge(0, [new(-300, 300, 500, 800)], OneScreen, clearance: 60, minLength: 50);

        Assert.Equal([new EdgeSpan(0, 500)], spans);
    }

    /// <summary>Бюджет S0.4: все кромки 50 окон — не дольше 1 мс.</summary>
    [Fact]
    public void FiftyWindows_AllEdgesUnderOneMillisecond()
    {
        var random = new Random(1);
        var windows = Enumerable.Range(0, 50).Select(_ =>
        {
            var x = random.Next(0, 1700);
            var y = random.Next(80, 900);
            return new PixelBox(x, y, x + random.Next(200, 900), y + random.Next(150, 600));
        }).ToArray();

        for (var warm = 0; warm < 200; warm++)
        {
            AllEdges(windows);
        }

        const int runs = 200;
        var started = Stopwatch.GetTimestamp();
        for (var run = 0; run < runs; run++)
        {
            AllEdges(windows);
        }

        var perRun = Stopwatch.GetElapsedTime(started) / runs;
        Assert.True(perRun < TimeSpan.FromMilliseconds(1), $"Все кромки 50 окон: {perRun.TotalMilliseconds:0.000} мс.");
    }

    private static int AllEdges(PixelBox[] windows)
    {
        var total = 0;
        for (var i = 0; i < windows.Length; i++)
        {
            total += EdgeOcclusion.VisibleTopEdge(i, windows, OneScreen, clearance: 60, minLength: 50).Count;
        }

        return total;
    }
}
