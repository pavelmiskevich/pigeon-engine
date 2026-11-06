using System.Runtime.Versioning;
using Pigeon.Desktop.Rendering;
using Pigeon.Desktop.Windows.Overlay;
using SkiaSharp;

namespace Pigeon.Desktop;

/// <summary>
/// Выводит кадр сцены в два окна — голубь и тень под ним. Поверхности Skia живут вместе с буфером
/// окна, а тень перерисовывается, только когда меняется её размер или высота голубя: в покое
/// рисуется один кадр голубя за раз (ТЗ §37).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PigeonRenderer(OverlayWindow pigeon, OverlayWindow shadow) : IDisposable
{
    private readonly CachedSurface _pigeon = new(pigeon);
    private readonly CachedSurface _shadow = new(shadow);
    private (double Scale, double Height, bool FacingLeft)? _shadowDrawn;

    public void Draw(SpriteSheet sheet, int index, StageFrame frame)
    {
        var layout = PigeonPainter.PigeonLayout(sheet.Metrics, frame.Scale, frame.FacingLeft);
        PigeonPainter.DrawPigeon(_pigeon.Canvas(layout.Size), sheet, index, frame.Scale, frame.FacingLeft);
        _pigeon.Flush();

        var shadowLayout = PigeonPainter.ShadowLayout(sheet.Metrics, frame.Scale, frame.FacingLeft);
        var shadowKey = (frame.Scale, frame.Height, frame.FacingLeft);
        if (_shadowDrawn != shadowKey)
        {
            PigeonPainter.DrawShadow(_shadow.Canvas(shadowLayout.Size), sheet.Metrics, frame.Scale, frame.Height);
            _shadow.Flush();
            _shadowDrawn = shadowKey;
        }

        shadow.Present(frame.AnchorX + shadowLayout.Origin.X, frame.AnchorY + shadowLayout.Origin.Y);
        pigeon.Present(frame.AnchorX + layout.Origin.X, frame.AnchorY + layout.Origin.Y);
    }

    public void Dispose()
    {
        _pigeon.Dispose();
        _shadow.Dispose();
    }

    /// <summary>Поверхность Skia поверх буфера окна; пересоздаётся вместе с буфером при смене размера.</summary>
    private sealed class CachedSurface(OverlayWindow window) : IDisposable
    {
        private SKSurface? _surface;
        private IntPtr _bits;

        public SKCanvas Canvas(SKSizeI size)
        {
            window.EnsureSize(size.Width, size.Height);
            if (_surface is null || _bits != window.Bits)
            {
                _surface?.Dispose();
                _surface = SKSurface.Create(
                    new SKImageInfo(window.Width, window.Height, SKColorType.Bgra8888, SKAlphaType.Premul), window.Bits, window.Width * 4)
                    ?? throw new InvalidOperationException("Skia не создала поверхность поверх буфера окна.");
                _bits = window.Bits;
            }

            return _surface.Canvas;
        }

        public void Flush() => _surface?.Flush();

        public void Dispose() => _surface?.Dispose();
    }
}
