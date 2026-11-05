using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Pigeon.Prototypes.Perch.Native;

namespace Pigeon.Prototypes.Perch;

/// <summary>
/// Автосценарий S0.4 в отдельном потоке со своим циклом сообщений: тестовое окно-насест движется
/// 60 раз в секунду, затем его кромку перекрывает другое окно, затем оно сворачивается и
/// закрывается. Отметки времени — Stopwatch (QPC), общие для всех потоков.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TestWindows
{
    public const double MoveStart = 1.5;
    public const double MoveEnd = 7.5;
    public const double OccludeAt = 9.0;
    public const double UnoccludeAt = 11.0;
    public const double MinimizeAt = 14.0;
    public const double RestoreAt = 16.0;
    public const double CloseAt = 19.0;
    public const double EndAt = 21.0;

    private static WndProc? s_proc;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;

    public TestWindows()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "test-windows" };
    }

    public IntPtr Target { get; private set; }

    /// <summary>Сдвиги окна: позиция GetWindowRect → момент вызова SetWindowPos.</summary>
    public ConcurrentDictionary<(int X, int Y), long> Moves { get; } = new();

    public ConcurrentDictionary<string, long> Events { get; } = new(StringComparer.Ordinal);

    public long StartedAt { get; private set; }

    public void Start()
    {
        _thread.Start();
        _ready.Wait();
    }

    private void Run()
    {
        s_proc = (w, m, wp, lp) => m == WmClose ? (DestroyWindow(w) ? IntPtr.Zero : IntPtr.Zero) : DefWindowProcW(w, m, wp, lp);
        var instance = GetModuleHandleW(null);
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WndProc = Marshal.GetFunctionPointerForDelegate(s_proc),
            Instance = instance,
            Background = 6,
            ClassName = "PigeonTestWindow",
        };
        RegisterClassExW(ref windowClass);
        Target = CreateWindowExW(0, "PigeonTestWindow", "S0.4 — окно-насест", WsOverlappedWindow | WsVisible, 300, 360, 720, 420, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        var occluder = CreateWindowExW(0, "PigeonTestWindow", "S0.4 — перекрывающее окно", WsOverlappedWindow, 0, 0, 360, 300, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        StartedAt = Stopwatch.GetTimestamp();
        _ready.Set();

        var stage = 0;
        while (true)
        {
            while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }

            var t = Stopwatch.GetElapsedTime(StartedAt).TotalSeconds;
            if (t >= MoveStart && t < MoveEnd)
            {
                // Фигура Лиссажу: окно ходит по экрану, как если бы его тащили мышью.
                var u = t - MoveStart;
                var x = 300 + (int)(260 * Math.Sin(u * 1.7));
                var y = 360 + (int)(140 * Math.Sin(u * 2.3));
                var at = Stopwatch.GetTimestamp();
                SetWindowPos(Target, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                GetWindowRect(Target, out var r);
                Moves.TryAdd((r.Left, r.Top), at);
            }
            else if (stage == 0 && t >= OccludeAt)
            {
                stage = 1;
                GetWindowRect(Target, out var r);
                SetWindowPos(occluder, HwndTop, r.Left + 80, r.Top - 120, 0, 0, SwpNoSize | SwpShowWindow | SwpNoActivate);
                Events["occluded"] = Stopwatch.GetTimestamp();
            }
            else if (stage == 1 && t >= UnoccludeAt)
            {
                stage = 2;
                ShowWindow(occluder, SwHide);
            }
            else if (stage == 2 && t >= MinimizeAt)
            {
                stage = 3;
                Events["minimized"] = Stopwatch.GetTimestamp();
                ShowWindow(Target, SwMinimize);
            }
            else if (stage == 3 && t >= RestoreAt)
            {
                stage = 4;
                ShowWindow(Target, SwRestore);
            }
            else if (stage == 4 && t >= CloseAt)
            {
                stage = 5;
                Events["closed"] = Stopwatch.GetTimestamp();
                DestroyWindow(Target);
                DestroyWindow(occluder);
            }
            else if (t >= EndAt)
            {
                return;
            }

            Thread.Sleep(t >= MoveStart && t < MoveEnd ? 16 : 5);
        }
    }
}
