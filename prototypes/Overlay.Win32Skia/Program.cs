// Кандидат B (MVP-0, S0.2): layered-окно Win32 с попиксельной альфой, отрисовка SkiaSharp в
// DIB-секцию, вывод через UpdateLayeredWindow. Полностью прозрачные пиксели система сама
// пропускает для кликов — переключать click-through не нужно.
//
//   Overlay.Win32Skia                 голубь ходит и перелетает; закрыть — правый клик по голубю
//   Overlay.Win32Skia --measure f.json  автозамер ~33 с, результат в JSON
//   Overlay.Win32Skia --sprite <dir>    рисовать кадрами атласа (assets/sprites/pigeon-black)

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Pigeon.Prototypes.Overlay;
using SkiaSharp;

if (!OperatingSystem.IsWindows())
{
    return 2;
}

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
var spriteDir = Option("--sprite");
var measure = new MeasureSession(spriteDir is null ? "win32-skia" : "win32-skia-sprite", Option("--measure"));
return new LayeredOverlay(measure, spriteDir).Run();

[SupportedOSPlatform("windows")]
internal sealed class LayeredOverlay(MeasureSession measure, string? spriteDir)
{
    /// <summary>Логический пиксель рисунка атласа на экране при масштабе 100 %.</summary>
    private const double SpriteDisplayScale = 0.62;

    private const uint WsPopup = 0x80000000;
    private const uint WmDestroy = 0x0002;
    private const uint WmQuit = 0x0012;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmRButtonUp = 0x0205;
    private const int MaNoActivate = 3;
    private const int SwShowNoActivate = 4;
    private const uint PmRemove = 1;
    private const uint UlwAlpha = 2;

    private static WndProc? s_wndProc;

    private IntPtr _window;
    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private IntPtr _bits;
    private SKSurface? _surface;
    private int _width;
    private int _height;
    private SpriteAtlas? _atlas;
    private SKImage? _atlasImage;
    private SpriteAnimator? _animator;
    private double _lastX = double.NaN;
    private (int X, int Y) _bodyPoint;

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public int Run()
    {
        Win32.SetProcessDpiAwarenessContext(Win32.PerMonitorAwareV2);
        CreateWindow();

        var model = new PigeonModel(Win32.Screens());
        if (spriteDir is not null)
        {
            LoadAtlas(model.Screen.Scale);
            model.WalkSpeed = _atlas!.Stride * SpriteDisplayScale / 0.8;
            measure.LogicalWidth = _atlas.Frames[0].W / (double)_atlas.Scale * SpriteDisplayScale;
            measure.BodyPoint = () => _bodyPoint;
        }
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed;
        var lastScreens = last;
        var nextFrame = last;

        while (true)
        {
            while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
            {
                if (message.Id == WmQuit)
                {
                    return 0;
                }

                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }

            var now = clock.Elapsed;
            if (now - lastScreens > TimeSpan.FromSeconds(2))
            {
                lastScreens = now;
                model.UpdateScreens(Win32.Screens());
            }

            if (measure.Enabled)
            {
                model.Mode = measure.Mode;
            }

            var dt = (now - last).TotalSeconds;
            model.Update(dt);
            last = now;
            if (spriteDir is null)
            {
                Render(model);
            }
            else
            {
                RenderSprite(model, dt);
            }
            measure.OnFrame(_window, model);
            if (measure.Finished)
            {
                DestroyWindow(_window);
                continue;
            }

            if (model.Mode == PigeonMode.Moving)
            {
                // Кадры по расписанию 60 Гц с привязкой к композиции: на экранах 120–144 Гц
                // DwmFlush возвращается чаще, чем нужно.
                nextFrame = FramePacer.Next(nextFrame, clock.Elapsed);
                do
                {
                    Win32.DwmFlush();
                }
                while (clock.Elapsed < nextFrame - FramePacer.Tolerance);
            }
            else
            {
                Thread.Sleep(100);
            }
        }
    }

    private void CreateWindow()
    {
        s_wndProc = WindowProcedure;
        var instance = GetModuleHandleW(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
            Instance = instance,
            ClassName = "PigeonOverlay",
        };
        RegisterClassExW(ref windowClass);

        const long exStyle = Win32.WsExLayered | Win32.WsExTopmost | Win32.WsExToolWindow | Win32.WsExNoActivate;
        _window = CreateWindowExW((uint)exStyle, "PigeonOverlay", "Pigeon", WsPopup, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        ShowWindow(_window, SwShowNoActivate);
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
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

    private void Render(PigeonModel model)
    {
        var scale = model.Screen.Scale;
        var size = (int)Math.Round(PigeonModel.LogicalSize * scale);
        if (size != _width || size != _height)
        {
            Resize(size, size);
        }

        var canvas = _surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        canvas.Scale((float)scale);
        SkiaPainter.Draw(canvas, PigeonSprite.Build(model));
        canvas.Restore();
        canvas.Flush();

        var destination = new Win32.Point { X = (int)(model.X - (size / 2.0)), Y = (int)(model.Y - (90 * scale)) };
        var extent = new Size { Width = size, Height = size };
        var source = new Win32.Point();
        var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(_window, IntPtr.Zero, ref destination, ref extent, _memoryDc, ref source, 0, ref blend, UlwAlpha);
    }

    /// <summary>
    /// Атлас под масштаб: наименьший из 1×, 2×, 3×, не меньше нужного, чтобы кадр только
    /// уменьшался при выводе.
    /// </summary>
    private void LoadAtlas(double screenScale)
    {
        var need = SpriteDisplayScale * screenScale;
        var bucket = need <= 1 ? 1 : need <= 2 ? 2 : 3;
        if (_atlas?.Scale == bucket)
        {
            return;
        }

        _atlas = SpriteAtlas.Parse(File.ReadAllText(Path.Combine(spriteDir!, $"atlas@{bucket}x.json")));
        _atlasImage?.Dispose();
        _atlasImage = SKImage.FromEncodedData(Path.Combine(spriteDir!, _atlas.Image)).ToRasterImage(ensurePixelData: true);
        _animator ??= new SpriteAnimator(_atlas);
    }

    private void RenderSprite(PigeonModel model, double dt)
    {
        var screenScale = model.Screen.Scale;
        LoadAtlas(screenScale);
        var atlas = _atlas!;
        var k = SpriteDisplayScale * screenScale;
        var distance = double.IsNaN(_lastX) ? 0 : Math.Abs(model.X - _lastX) / k;
        _lastX = model.X;
        var frame = atlas.Frames[_animator!.Next(model, dt, distance)];

        var width = (int)Math.Round(frame.W / (double)atlas.Scale * k);
        var height = (int)Math.Round(frame.H / (double)atlas.Scale * k);
        if (width != _width || height != _height)
        {
            Resize(width, height);
        }

        var canvas = _surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        if (model.Direction < 0)
        {
            canvas.Scale(-1, 1, width / 2f, 0);
        }

        canvas.DrawImage(_atlasImage, new SKRect(frame.X, frame.Y, frame.X + frame.W, frame.Y + frame.H),
            new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Restore();
        canvas.Flush();

        var unit = k / atlas.Scale;
        var anchorX = atlas.AnchorX * unit;
        var anchorY = atlas.AnchorY * unit;
        if (model.Direction < 0)
        {
            anchorX = width - anchorX;
        }

        // Центр тела в кадре: точка рисунка (115, 100) со сдвигом кадра (−12, +8).
        var bodyX = 103 * k;
        _bodyPoint = ((int)(model.Direction < 0 ? width - bodyX : bodyX), (int)(108 * k));

        var destination = new Win32.Point { X = (int)(model.X - anchorX), Y = (int)(model.Y - anchorY) };
        var extent = new Size { Width = width, Height = height };
        var source = new Win32.Point();
        var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(_window, IntPtr.Zero, ref destination, ref extent, _memoryDc, ref source, 0, ref blend, UlwAlpha);
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

        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        _bitmap = CreateDIBSection(_memoryDc, ref header, 0, out _bits, IntPtr.Zero, 0);
        SelectObject(_memoryDc, _bitmap);
        _surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), _bits, width * 4);
        _width = width;
        _height = height;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Win32.Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(out Message message, IntPtr window, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc, ref Win32.Point destination, ref Size size,
        IntPtr sourceDc, ref Win32.Point source, uint colorKey, ref BlendFunction blend, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr gdiObject);
}

/// <summary>Отрисовка примитивов голубя через SkiaSharp.</summary>
internal static class SkiaPainter
{
    public static void Draw(SKCanvas canvas, IReadOnlyList<Shape> shapes)
    {
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var shape in shapes)
        {
            paint.Color = new SKColor(shape.Color.R, shape.Color.G, shape.Color.B, shape.Color.A);
            switch (shape)
            {
                case EllipseShape e:
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawOval((float)e.Cx, (float)e.Cy, (float)e.Rx, (float)e.Ry, paint);
                    break;
                case PolygonShape p:
                    paint.Style = SKPaintStyle.Fill;
                    using (var path = new SKPath())
                    {
                        path.AddPoly([.. p.Points.Select(pt => new SKPoint((float)pt.X, (float)pt.Y))]);
                        canvas.DrawPath(path, paint);
                    }

                    break;
                case LineShape l:
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = (float)l.Width;
                    paint.StrokeCap = SKStrokeCap.Round;
                    canvas.DrawLine((float)l.X1, (float)l.Y1, (float)l.X2, (float)l.Y2, paint);
                    break;
            }
        }
    }
}
