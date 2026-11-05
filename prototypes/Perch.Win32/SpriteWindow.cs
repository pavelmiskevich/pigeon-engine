using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.Prototypes.Overlay;
using SkiaSharp;
using static Pigeon.Prototypes.Perch.Native;

namespace Pigeon.Prototypes.Perch;

/// <summary>Layered-окно голубя: кадр атласа ставится точкой опоры (лапами) в заданную точку экрана.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SpriteWindow
{
    /// <summary>Логический пиксель рисунка атласа на экране при масштабе 100 %.</summary>
    public const double DisplayScale = 0.62;

    private static WndProc? s_wndProc;
    private readonly string _spriteDir;
    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private IntPtr _bits;
    private SKSurface? _surface;
    private int _width;
    private int _height;
    private SKImage? _image;

    public SpriteWindow(string spriteDir)
    {
        _spriteDir = spriteDir;
        s_wndProc = Procedure;
        var instance = GetModuleHandleW(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            Instance = instance,
            ClassName = "PigeonPerch",
        };
        RegisterClassExW(ref windowClass);
        const long exStyle = WsExLayered | WsExTopmost | WsExToolWindow | WsExNoActivate;
        Handle = CreateWindowExW((uint)exStyle, "PigeonPerch", "Pigeon", WsPopup, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        ShowWindow(Handle, SwShowNoActivate);
    }

    public IntPtr Handle { get; }

    public SpriteAtlas? Atlas { get; private set; }

    /// <summary>Ширина голубя на экране в физических пикселях (по кадру атласа).</summary>
    public int PigeonWidth(double screenScale) => (int)(110 * DisplayScale * screenScale);

    /// <summary>Высота голубя над точкой опоры, физические пиксели.</summary>
    public int PigeonHeight(double screenScale) => (int)(115 * DisplayScale * screenScale);

    public void Show(int frameIndex, int anchorX, int anchorY, int direction, double screenScale)
    {
        var k = DisplayScale * screenScale;
        Load(k);
        var atlas = Atlas!;
        var frame = atlas.Frames[frameIndex];
        var width = (int)Math.Round(frame.W / (double)atlas.Scale * k);
        var height = (int)Math.Round(frame.H / (double)atlas.Scale * k);
        if (width != _width || height != _height)
        {
            Resize(width, height);
        }

        var canvas = _surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        if (direction < 0)
        {
            canvas.Scale(-1, 1, width / 2f, 0);
        }

        canvas.DrawImage(_image, new SKRect(frame.X, frame.Y, frame.X + frame.W, frame.Y + frame.H),
            new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Restore();
        canvas.Flush();

        var unit = k / atlas.Scale;
        var ax = atlas.AnchorX * unit;
        if (direction < 0)
        {
            ax = width - ax;
        }

        var destination = new Point { X = (int)(anchorX - ax), Y = (int)(anchorY - (atlas.AnchorY * unit)) };
        var extent = new Size { Width = width, Height = height };
        var source = new Point();
        var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref extent, _memoryDc, ref source, 0, ref blend, UlwAlpha);
    }

    private void Load(double k)
    {
        var bucket = k <= 1 ? 1 : k <= 2 ? 2 : 3;
        if (Atlas?.Scale == bucket)
        {
            return;
        }

        Atlas = SpriteAtlas.Parse(File.ReadAllText(Path.Combine(_spriteDir, $"atlas@{bucket}x.json")));
        _image?.Dispose();
        _image = SKImage.FromEncodedData(Path.Combine(_spriteDir, Atlas.Image)).ToRasterImage(ensurePixelData: true);
    }

    private void Resize(int width, int height)
    {
        _surface?.Dispose();
        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
        }

        if (_memoryDc == IntPtr.Zero)
        {
            _memoryDc = CreateCompatibleDC(IntPtr.Zero);
        }

        var header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        _bitmap = CreateDIBSection(_memoryDc, ref header, 0, out _bits, IntPtr.Zero, 0);
        SelectObject(_memoryDc, _bitmap);
        _surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), _bits, width * 4);
        _width = width;
        _height = height;
    }

    private static IntPtr Procedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmMouseActivate:
                return MaNoActivate;
            case WmRButtonUp:
                DestroyWindow(window);
                return IntPtr.Zero;
            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }
}
