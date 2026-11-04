// Кандидат A (MVP-0, S0.2): прозрачное окно Avalonia 12 без рамки. Стили окна (no-activate,
// tool window) ставятся через Win32. Прозрачное окно Avalonia на Windows забирает клики всей
// площадью, поэтому click-through переключается флагом WS_EX_TRANSPARENT по попаданию курсора
// в голубя — запасной путь из матрицы возможностей.
//
//   Overlay.Avalonia                   голубь ходит и перелетает; закрыть — правый клик по голубю
//   Overlay.Avalonia --measure f.json  автозамер ~33 с, результат в JSON

using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Pigeon.Prototypes.Overlay;

if (!OperatingSystem.IsWindows())
{
    return 2;
}

OverlayApp.MeasurePath = args.Length >= 2 && args[0] == "--measure" ? args[1] : null;
return AppBuilder.Configure<OverlayApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);

[SupportedOSPlatform("windows")]
internal sealed class OverlayApp : Application
{
    public static string? MeasurePath { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new OverlayWindow(new MeasureSession("avalonia", MeasurePath));
        }

        base.OnFrameworkInitializationCompleted();
    }
}

[SupportedOSPlatform("windows")]
internal sealed class OverlayWindow : Window
{
    private readonly MeasureSession _measure;
    private readonly PigeonModel _model;
    private readonly PigeonView _view;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _clickThroughTimer;
    private TimeSpan _last;
    private TimeSpan _lastScreens;
    private TimeSpan _nextFrame;
    private IntPtr _handle;
    private bool _transparent;
    private bool _animating;

    public OverlayWindow(MeasureSession measure)
    {
        _measure = measure;
        _model = new PigeonModel(Win32.Screens());
        _view = new PigeonView(_model);

        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Width = PigeonModel.LogicalSize;
        Height = PigeonModel.LogicalSize;
        Content = _view;

        _idleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => Tick());
        _clickThroughTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => UpdateClickThrough(Win32.Cursor()));
        _measure.SimulateCursor = (x, y) => UpdateClickThrough((x, y));

        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                Close();
            }
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        Win32.SetExStyle(_handle, Win32.ExStyle(_handle) | Win32.WsExNoActivate | Win32.WsExToolWindow | Win32.WsExTopmost);
        _clickThroughTimer.Start();
        _animating = true;
        RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan _)
    {
        // Кадр вызывается с частотой экрана (120–144 Гц на ноутбуках); сценарий — 60 Гц.
        if (_model.Mode == PigeonMode.Moving && _clock.Elapsed >= _nextFrame - FramePacer.Tolerance)
        {
            _nextFrame = FramePacer.Next(_nextFrame, _clock.Elapsed);
            Tick();
        }

        // В покое цикл кадров останавливается, работает таймер на 10 кадров в секунду.
        _animating = _model.Mode == PigeonMode.Moving && !_measure.Finished;
        if (_animating)
        {
            RequestAnimationFrame(OnAnimationFrame);
        }
    }

    private void Tick()
    {
        var now = _clock.Elapsed;
        if (now - _lastScreens > TimeSpan.FromSeconds(2))
        {
            _lastScreens = now;
            _model.UpdateScreens(Win32.Screens());
        }

        if (_measure.Enabled)
        {
            _model.Mode = _measure.Mode;
        }

        if (_model.Mode == PigeonMode.Idle && !_idleTimer.IsEnabled)
        {
            _idleTimer.Start();
        }
        else if (_model.Mode == PigeonMode.Moving && _idleTimer.IsEnabled)
        {
            _idleTimer.Stop();
        }

        if (_model.Mode == PigeonMode.Moving && !_animating && !_measure.Finished)
        {
            _animating = true;
            RequestAnimationFrame(OnAnimationFrame);
        }

        _model.Update((now - _last).TotalSeconds);
        _last = now;

        var scale = _model.Screen.Scale;
        Position = new PixelPoint((int)(_model.X - (PigeonModel.LogicalSize * scale / 2)), (int)(_model.Y - (90 * scale)));
        _view.InvalidateVisual();
        _measure.OnFrame(_handle, _model);
        if (_measure.Finished)
        {
            Close();
        }
    }

    /// <summary>Окно пропускает клики, если курсор не над голубем.</summary>
    private void UpdateClickThrough((int X, int Y) cursor)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        Win32.GetWindowRect(_handle, out var rect);
        var scale = (rect.Right - rect.Left) / (double)PigeonModel.LogicalSize;
        var (bx, by) = PigeonSprite.BodyCenter;
        var dx = (cursor.X - (rect.Left + (bx * scale))) / (34 * scale);
        var dy = (cursor.Y - (rect.Top + (by * scale))) / (30 * scale);
        var transparent = (dx * dx) + (dy * dy) > 1;
        if (transparent == _transparent)
        {
            return;
        }

        _transparent = transparent;
        var style = Win32.ExStyle(_handle);
        Win32.SetExStyle(_handle, transparent ? style | Win32.WsExTransparent | Win32.WsExLayered : style & ~Win32.WsExTransparent);
    }
}

/// <summary>Отрисовка примитивов голубя средствами Avalonia.</summary>
internal sealed class PigeonView(PigeonModel model) : Control
{
    public override void Render(DrawingContext context)
    {
        foreach (var shape in PigeonSprite.Build(model))
        {
            var brush = new SolidColorBrush(Color.FromArgb(shape.Color.A, shape.Color.R, shape.Color.G, shape.Color.B));
            switch (shape)
            {
                case EllipseShape e:
                    context.DrawEllipse(brush, null, new Point(e.Cx, e.Cy), e.Rx, e.Ry);
                    break;
                case PolygonShape p:
                    var geometry = new PolylineGeometry([.. p.Points.Select(pt => new Point(pt.X, pt.Y))], true);
                    context.DrawGeometry(brush, null, geometry);
                    break;
                case LineShape l:
                    context.DrawLine(new Pen(brush, l.Width, lineCap: PenLineCap.Round), new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));
                    break;
            }
        }
    }
}
