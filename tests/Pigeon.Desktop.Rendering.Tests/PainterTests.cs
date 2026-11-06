using SkiaSharp;

namespace Pigeon.Desktop.Rendering.Tests;

/// <summary>Голубь и тень попадают туда, где должны быть (M1.4).</summary>
public sealed class PainterTests : IDisposable
{
    private readonly TestSheets _sheets = new();

    public void Dispose() => _sheets.Dispose();

    [Fact]
    public void TrimmedFrame_IsDrawnAtItsPlaceInTheFullFrame()
    {
        using var sheet = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@1x.json"));
        using var bitmap = Draw(sheet, frame: 0, k: 1.0, facingLeft: false);

        Assert.Equal(TestSheets.First, Opaque(bitmap.GetPixel(5, 6)));
        Assert.Equal(TestSheets.First, Opaque(bitmap.GetPixel(8, 8)));
        Assert.Equal(0, bitmap.GetPixel(4, 6).Alpha);
        Assert.Equal(0, bitmap.GetPixel(9, 6).Alpha);
    }

    [Fact]
    public void SheetOfAnyScale_DrawsTheSameLogicalPicture()
    {
        using var small = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@1x.json"));
        using var large = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@2x.json"));
        using var fromSmall = Draw(small, frame: 1, k: 1.0, facingLeft: false);
        using var fromLarge = Draw(large, frame: 1, k: 1.0, facingLeft: false);

        Assert.Equal(Opaque(fromSmall.GetPixel(7, 7)), Opaque(fromLarge.GetPixel(7, 7)));
        Assert.Equal(TestSheets.Second, Opaque(fromLarge.GetPixel(7, 7)));
        Assert.Equal(0, fromLarge.GetPixel(5, 7).Alpha);
    }

    [Fact]
    public void FacingLeft_MirrorsThePictureAndTheAnchor()
    {
        using var sheet = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@1x.json"));
        using var mirrored = Draw(sheet, frame: 0, k: 1.0, facingLeft: true);

        // Кадр 5..8 по X в кадре шириной 16 отражается в 7..10.
        Assert.Equal(TestSheets.First, Opaque(mirrored.GetPixel(10, 6)));
        Assert.Equal(0, mirrored.GetPixel(6, 6).Alpha);
        Assert.Equal(new SpriteLayout(new SKSizeI(16, 12), new SKPointI(-8, -10)), PigeonPainter.PigeonLayout(sheet.Metrics, 1.0, false));
        Assert.Equal(new SKPointI(-8, -10), PigeonPainter.PigeonLayout(sheet.Metrics, 1.0, true).Origin);
    }

    [Fact]
    public void Layout_ScalesWithTheMonitor()
    {
        using var sheet = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@2x.json"));

        var layout = PigeonPainter.PigeonLayout(sheet.Metrics, 1.75, facingLeft: false);

        Assert.Equal(new SKSizeI(28, 21), layout.Size);
        Assert.Equal(new SKPointI(-14, -18), layout.Origin);
    }

    [Fact]
    public void Shadow_IsCenteredUnderTheAnchorAndFadesWithHeight()
    {
        using var sheet = SpriteSheet.Load(Path.Combine(_sheets.Directory, "idle@1x.json"));
        var layout = PigeonPainter.ShadowLayout(sheet.Metrics, 2.0, facingLeft: false);

        Assert.Equal(new SKSizeI(26, 14), layout.Size);
        Assert.Equal(new SKPointI(-13, -7), layout.Origin);

        using var ground = DrawShadow(sheet, layout, height: 0);
        using var high = DrawShadow(sheet, layout, height: PigeonPainter.ShadowFadeHeight / 2);
        using var gone = DrawShadow(sheet, layout, height: PigeonPainter.ShadowFadeHeight);

        var center = ground.GetPixel(13, 7);
        Assert.InRange(center.Alpha, 100, 140);
        Assert.True(high.GetPixel(13, 7).Alpha < center.Alpha);
        Assert.Equal(0, gone.GetPixel(13, 7).Alpha);
        Assert.Equal(0, ground.GetPixel(0, 0).Alpha);
    }

    private static SKBitmap Draw(SpriteSheet sheet, int frame, double k, bool facingLeft)
    {
        var size = PigeonPainter.PigeonLayout(sheet.Metrics, k, facingLeft).Size;
        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        PigeonPainter.DrawPigeon(canvas, sheet, frame, k, facingLeft);
        return bitmap;
    }

    private static SKBitmap DrawShadow(SpriteSheet sheet, SpriteLayout layout, double height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(layout.Size.Width, layout.Size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        PigeonPainter.DrawShadow(canvas, sheet.Metrics, 2.0, height);
        return bitmap;
    }

    private static SKColor Opaque(SKColor color) => color.WithAlpha(255);
}
