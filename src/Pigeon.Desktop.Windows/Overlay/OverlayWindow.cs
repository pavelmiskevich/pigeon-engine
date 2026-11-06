using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Pigeon.Desktop.Windows.Native;

namespace Pigeon.Desktop.Windows.Overlay;

/// <summary>
/// Окно-оверлей Windows (ТЗ §11.2, ADR-0021): layered-окно с попиксельной альфой поверх всех, без
/// фокуса, без кнопки на панели задач и в Alt-Tab. Пиксели рисует вызывающий код в
/// <see cref="Bits"/> (BGRA, premultiplied), <see cref="Present"/> выводит их в нужное место.
/// </summary>
/// <remarks>
/// Полностью прозрачные пиксели система сама пропускает для кликов. Окно с
/// <c>clickThrough</c> пропускает клики целиком — для тени, которая полупрозрачна везде.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class OverlayWindow : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WmDestroy = 0x0002;
    private const uint WmMouseActivate = 0x0021;
    private const uint WmRButtonUp = 0x0205;
    private const int MaNoActivate = 3;
    private const int SwShowNoActivate = 4;
    private const uint UlwAlpha = 2;
    private const byte AcSrcAlpha = 1;
    private static readonly IntPtr IdcArrow = 32512;

    // Делегат оконной процедуры живёт столько же, сколько класс окна: сборщик мусора не должен его забрать.
    private static readonly WindowProc Procedure = WindowProcedure;
    private static readonly Dictionary<IntPtr, OverlayWindow> Windows = [];
    private static readonly HashSet<string> RegisteredClasses = new(StringComparer.Ordinal);

    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private bool _disposed;

    /// <param name="className">Класс окна: по нему окно узнаётся в пробах и при отладке.</param>
    public OverlayWindow(string className, bool clickThrough)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);

        var instance = GetModuleHandleW(null);
        if (RegisteredClasses.Add(className))
        {
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = instance,
                Cursor = LoadCursorW(IntPtr.Zero, IdcArrow),
                ClassName = className,
            };
            if (RegisterClassExW(ref windowClass) == 0)
            {
                throw new InvalidOperationException($"Не удалось зарегистрировать класс окна {className}: {Marshal.GetLastPInvokeError()}.");
            }
        }

        var exStyle = WsExLayered | WsExTopmost | WsExToolWindow | WsExNoActivate | (clickThrough ? WsExTransparent : 0);
        Handle = CreateWindowExW(exStyle, className, null, WsPopup, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Не удалось создать окно {className}: {Marshal.GetLastPInvokeError()}.");
        }

        Windows[Handle] = this;
        _memoryDc = CreateCompatibleDC(IntPtr.Zero);
    }

    /// <summary>Правый клик по непрозрачной части окна.</summary>
    public event EventHandler? RightClicked;

    public IntPtr Handle { get; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Пиксели окна: BGRA, premultiplied alpha, строка — <c>Width × 4</c> байт.</summary>
    public IntPtr Bits { get; private set; }

    public bool IsShown { get; private set; }

    /// <summary>Меняет размер буфера, если нужно; содержимое после смены размера не определено.</summary>
    public void EnsureSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (width == Width && height == Height)
        {
            return;
        }

        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        var bitmap = CreateDIBSection(_memoryDc, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Не удалось создать буфер {width}×{height}.");
        }

        SelectObject(_memoryDc, bitmap);
        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
        }

        _bitmap = bitmap;
        Bits = bits;
        Width = width;
        Height = height;
    }

    /// <summary>Выводит буфер так, что его левый верхний угол — в (<paramref name="x"/>, <paramref name="y"/>) физических пикселей экрана.</summary>
    public void Present(int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Bits == IntPtr.Zero)
        {
            return;
        }

        var destination = new Point { X = x, Y = y };
        var size = new Size { Width = Width, Height = Height };
        var source = new Point();
        var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = AcSrcAlpha };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size, _memoryDc, ref source, 0, ref blend, UlwAlpha);
        if (!IsShown)
        {
            ShowWindow(Handle, SwShowNoActivate);
            IsShown = true;
        }
    }

    public void Hide()
    {
        if (IsShown)
        {
            ShowWindow(Handle, 0);
            IsShown = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Windows.Remove(Handle);
        DestroyWindow(Handle);
        if (_bitmap != IntPtr.Zero)
        {
            DeleteObject(_bitmap);
        }

        DeleteDC(_memoryDc);
        _memoryDc = IntPtr.Zero;
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmMouseActivate:
                return MaNoActivate;
            case WmRButtonUp when Windows.TryGetValue(window, out var overlay):
                overlay.RightClicked?.Invoke(overlay, EventArgs.Empty);
                return IntPtr.Zero;
            case WmDestroy:
                return IntPtr.Zero;
            default:
                return DefWindowProcW(window, message, wParam, lParam);
        }
    }
}

/// <summary>
/// Держит окна голубя над другими окнами «поверх всех» (находка пробы #44): Windows ставит выше
/// то topmost-окно, что показано последним, и всплывающее уведомление накрывало голубя. Когда
/// показывается чужое topmost-окно, наши окна поднимаются наверх — без кражи фокуса.
/// </summary>
/// <remarks>
/// Окна оболочки (системные уведомления, «Пуск») живут в отдельном слое выше любых topmost-окон,
/// туда обычному приложению не попасть. Хук работает вне контекста процесса: события приходят
/// через очередь сообщений потока, создавшего хук, поэтому поток должен её прокачивать.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class TopmostKeeper : IDisposable
{
    private const uint EventSystemForegroundId = 0x0003;
    private const uint EventObjectShow = 0x8002;
    private const int ObjIdWindow = 0;
    private const uint GaRoot = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly IntPtr HwndTopmost = -1;

    private readonly IntPtr[] _windows;
    private readonly WinEventProc _callback;
    private readonly IntPtr _showHook;
    private readonly IntPtr _foregroundHook;

    /// <param name="bottomToTop">Наши окна снизу вверх: тень, затем голубь.</param>
    public TopmostKeeper(params IntPtr[] bottomToTop)
    {
        _windows = bottomToTop;
        _callback = OnEvent;
        _showHook = SetWinEventHook(EventObjectShow, EventObjectShow, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
        _foregroundHook = SetWinEventHook(EventSystemForegroundId, EventSystemForegroundId, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext);
    }

    /// <summary>Сколько раз окна поднимались — для замера.</summary>
    public int Raises { get; private set; }

    public void Raise()
    {
        foreach (var window in _windows)
        {
            SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }

        Raises++;
    }

    public void Dispose()
    {
        UnhookWinEvent(_showHook);
        UnhookWinEvent(_foregroundHook);
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (objectId != ObjIdWindow || window == IntPtr.Zero || _windows.Contains(window) || GetAncestor(window, GaRoot) != window)
        {
            return;
        }

        if ((GetWindowLongPtrW(window, GwlExStyle).ToInt64() & WsExTopmost) != 0 || eventType == EventSystemForegroundId)
        {
            Raise();
        }
    }
}

/// <summary>Очередь сообщений потока окон: прокачка и ожидание без опроса.</summary>
[SupportedOSPlatform("windows")]
public static class MessageLoop
{
    private const uint PmRemove = 1;
    private const uint WmQuit = 0x0012;
    private const uint QsAllInput = 0x04FF;
    private const uint MwmoInputAvailable = 0x0004;

    /// <summary>Режим DPI процесса — Per-Monitor V2: координаты окон в физических пикселях каждого монитора.</summary>
    public static void EnablePerMonitorDpi() => SetProcessDpiAwarenessContext(-4);

    /// <summary>Обрабатывает все сообщения в очереди; false — пришёл WM_QUIT.</summary>
    public static bool Pump()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
        {
            if (message.Id == WmQuit)
            {
                return false;
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        return true;
    }

    /// <summary>Спит до <paramref name="timeout"/> или до первого сообщения — что раньше.</summary>
    public static void Wait(TimeSpan timeout)
    {
        var milliseconds = (uint)Math.Clamp(timeout.TotalMilliseconds, 0, 10_000);
        MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, milliseconds, QsAllInput, MwmoInputAvailable);
    }

    public static void Quit() => PostQuitMessage(0);

    /// <summary>Ждёт следующей композиции DWM — темп кадров анимации по частоте экрана.</summary>
    public static void WaitForComposition() => DwmFlush();

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
}
