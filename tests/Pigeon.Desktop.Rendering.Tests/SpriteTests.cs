using SkiaSharp;

namespace Pigeon.Desktop.Rendering.Tests;

/// <summary>Листы, библиотека клипов и выбор кадра (ADR-0023, M1.4).</summary>
public sealed class SpriteTests : IDisposable
{
    private readonly TestSheets _sheets = new();

    public void Dispose() => _sheets.Dispose();

    [Fact]
    public void Sheet_ReadsTrimmedFramesAndMetricsInLogicalPixels()
    {
        using var sheet = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@2x.json"));

        Assert.Equal("idle", sheet.Clip);
        Assert.Equal(2, sheet.Scale);
        Assert.True(sheet.Loop);
        Assert.Equal(new SpriteFrame(SKRectI.Create(9, 0, 8, 6), new SKPointI(12, 12), 100), sheet.Frames[1]);
        Assert.Equal(new SKPoint(8, 10), sheet.Metrics.Anchor);
        Assert.Equal(new SKSize(16, 12), sheet.Metrics.Frame);
        Assert.Equal(17 * 6 * 4, sheet.Bytes);
    }

    [Fact]
    public void Library_LoadsClipsOnDemandAndReleasesUnusedOnes()
    {
        using var library = new SpriteLibrary(_sheets.Directory);

        Assert.Equal(0, library.LoadedCount);
        var idle = library.Get("idle", 1, TimeSpan.Zero);
        Assert.Same(idle, library.Get("idle", 1, TimeSpan.FromSeconds(5)));
        library.Get("peck", 2, TimeSpan.FromSeconds(1));
        Assert.Equal(2, library.LoadedCount);

        Assert.Equal(1, library.ReleaseUnused(TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(36)));
        Assert.Equal(1, library.LoadedCount);
        Assert.Equal("idle", library.Get("idle", 1, TimeSpan.FromSeconds(40)).Clip);
    }

    [Fact]
    public void Library_RefusesADirectoryWithoutSheets()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new SpriteLibrary(Path.GetTempPath()));
    }

    [Theory]
    [InlineData(0.62, 1)]
    [InlineData(1.0, 1)]
    [InlineData(1.085, 2)]
    [InlineData(2.0, 2)]
    [InlineData(2.2, 3)]
    public void Bucket_IsTheSmallestNotBelowTheDisplayScale(double scale, int bucket)
    {
        Assert.Equal(bucket, SpriteLibrary.BucketFor(scale));
    }

    [Fact]
    public void LoopingClip_CyclesByFrameDurations()
    {
        using var sheet = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@1x.json"));
        var player = new ClipPlayer();
        player.Play("idle", TimeSpan.FromSeconds(10));

        Assert.Equal(0, player.FrameAt(sheet, TimeSpan.FromSeconds(10.05)));
        Assert.Equal(1, player.FrameAt(sheet, TimeSpan.FromSeconds(10.15)));
        Assert.Equal(0, player.FrameAt(sheet, TimeSpan.FromSeconds(10.25)));
        Assert.Equal(TimeSpan.FromSeconds(10.3), player.NextChange(sheet, TimeSpan.FromSeconds(10.25)));

        player.Play("idle", TimeSpan.FromSeconds(11));
        Assert.Equal(1, player.FrameAt(sheet, TimeSpan.FromSeconds(11.15)));
    }

    [Fact]
    public void OneShotClip_StopsOnItsLastFrame()
    {
        using var sheet = SpriteSheet.Load(Path.Combine(_sheets.Directory, "peck@1x.json"));
        var player = new ClipPlayer();
        player.Play("peck", TimeSpan.Zero);

        Assert.Equal(1, player.FrameAt(sheet, TimeSpan.FromMilliseconds(60)));
        Assert.Equal(1, player.FrameAt(sheet, TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.MaxValue, player.NextChange(sheet, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ScaleTransition_SnapsFirstThenEasesToTheTarget()
    {
        var scale = new ScaleTransition(TimeSpan.FromMilliseconds(400));

        scale.SetTarget(1.75, TimeSpan.Zero);
        Assert.Equal(1.75, scale.Value(TimeSpan.Zero));
        Assert.False(scale.IsAnimating(TimeSpan.Zero));

        scale.SetTarget(1.0, TimeSpan.FromSeconds(1));
        Assert.Equal(1.375, scale.Value(TimeSpan.FromSeconds(1.2)), 1e-9);
        Assert.True(scale.IsAnimating(TimeSpan.FromSeconds(1.2)));
        Assert.Equal(1.0, scale.Value(TimeSpan.FromSeconds(1.4)));
        Assert.False(scale.IsAnimating(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ScaleTransition_RetargetsSmoothlyFromWhereItIs()
    {
        var scale = new ScaleTransition(TimeSpan.FromMilliseconds(400));
        scale.SetTarget(2.0, TimeSpan.Zero);
        scale.SetTarget(1.0, TimeSpan.FromSeconds(1));
        var midway = scale.Value(TimeSpan.FromSeconds(1.2));

        scale.SetTarget(1.5, TimeSpan.FromSeconds(1.2));

        Assert.Equal(midway, scale.Value(TimeSpan.FromSeconds(1.2)), 1e-12);
        Assert.Equal(1.5, scale.Value(TimeSpan.FromSeconds(1.6)));
    }
}
