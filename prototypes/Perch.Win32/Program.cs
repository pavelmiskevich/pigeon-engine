// Прототип «голубь на окне» (MVP-0, S0.4). Голубь садится на видимый участок верхней кромки окна,
// следует за ним при перемещении и вспархивает, если кромку перекрыли, окно свернули или закрыли.
//
//   Perch.Win32 --sprite <dir>                      голубь садится на активное окно; закрыть — правый клик
//   Perch.Win32 --sprite <dir> --scenario out.json  автосценарий ~21 с с тестовыми окнами и отчёт
//
// Перемещение окна-насеста — по событиям (SetWinEventHook, EVENT_OBJECT_LOCATIONCHANGE),
// перекрытие — опросом z-order 10 раз в секунду и сразу по смене активного окна.

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Pigeon.Prototypes.Overlay;
using Pigeon.Prototypes.Perch;
using Pigeon.World.Desktop.Perches;
using static Pigeon.Prototypes.Perch.Native;

if (!OperatingSystem.IsWindows())
{
    return 2;
}

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
var sprite = Option("--sprite") ?? Path.Combine("assets", "sprites", "pigeon-black");
Win32.SetProcessDpiAwarenessContext(Win32.PerMonitorAwareV2);
return new PerchApp(sprite, Option("--scenario")).Run();

[SupportedOSPlatform("windows")]
internal sealed class PerchApp
{
    private const double FlightSeconds = 1.1;

    private readonly SpriteWindow _sprite;
    private readonly string? _reportPath;
    private readonly TestWindows? _test;
    private readonly WinEventProc _onEvent;
    private readonly WinEventProc _onLocation;
    private readonly List<double> _followLatencies = [];
    private readonly List<double> _snapshotMs = [];
    private readonly List<double> _occlusionMs = [];
    private readonly double _groundedSeconds;
    private readonly Dictionary<string, double> _reactions = [];
    private readonly List<string> _log = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IntPtr _locationHook;
    private IntPtr _target;
    private int _offset;
    private string _state = "grounded";
    private double _stateTime;
    private (double X, double Y) _from;
    private (double X, double Y) _to;
    private (double X, double Y) _pos;
    private int _direction = 1;
    private double _animTime;
    private TimeSpan _lastTick;
    private TimeSpan _lastOcclusion;

    public PerchApp(string spriteDir, string? reportPath)
    {
        _sprite = new SpriteWindow(spriteDir);
        _reportPath = reportPath;
        _onEvent = OnEvent;
        _onLocation = OnLocation;
        SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _onEvent, 0, 0, WinEventOutOfContext);
        SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeStart, IntPtr.Zero, _onEvent, 0, 0, WinEventOutOfContext);
        SetWinEventHook(EventObjectDestroy, EventObjectDestroy, IntPtr.Zero, _onEvent, 0, 0, WinEventOutOfContext);
        if (reportPath is not null)
        {
            _test = new TestWindows();
            _test.Start();
        }

        var work = Win32.Screens()[0].WorkArea;
        _pos = (work.X + (work.Width * 0.3), work.Bottom);

        // В сценарии голубь возвращается на окно быстрее, чтобы успеть к каждому испытанию.
        _groundedSeconds = reportPath is null ? 3.0 : 0.8;
        _stateTime = _groundedSeconds;
    }

    private double Scale => ScreenAt(_pos.X, _pos.Y).Scale;

    public int Run()
    {
        while (true)
        {
            var timeout = _state is "perched" or "grounded" ? 100u : 16u;
            MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, timeout, QsAllInput, MwmoInputAvailable);
            while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PmRemove))
            {
                if (message.Id == WmQuit)
                {
                    return 0;
                }

                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }

            Tick();
            if (_test is not null && Stopwatch.GetElapsedTime(_test.StartedAt).TotalSeconds > TestWindows.EndAt + 1)
            {
                WriteReport();
                return 0;
            }
        }
    }

    private void Tick()
    {
        var now = _clock.Elapsed;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        _stateTime += dt;
        _animTime += dt;

        switch (_state)
        {
            case "perched":
                if (now - _lastOcclusion > TimeSpan.FromMilliseconds(100))
                {
                    _lastOcclusion = now;
                    CheckPerch("poll");
                }

                break;
            case "flying":
                var t = Math.Min(1, _stateTime / FlightSeconds);
                _pos = (_from.X + ((_to.X - _from.X) * t), _from.Y + ((_to.Y - _from.Y) * t) - (160 * Scale * 4 * t * (1 - t)));
                if (t >= 1)
                {
                    if (_target != IntPtr.Zero && PerchBox() is { } box)
                    {
                        Enter("perched");
                        Place(box);
                    }
                    else
                    {
                        _target = IntPtr.Zero;
                        Enter("grounded");
                    }
                }

                break;
            case "grounded":
                if (_stateTime > _groundedSeconds)
                {
                    TryPerch(_test?.Target ?? GetForegroundWindow());
                }

                break;
        }

        Render();
    }

    /// <summary>Выбрать участок кромки окна и полететь туда.</summary>
    private void TryPerch(IntPtr window)
    {
        _stateTime = 0;
        if (window == IntPtr.Zero || window == _sprite.Handle || !IsWindow(window) || IsIconic(window))
        {
            return;
        }

        var windows = WindowSource.Snapshot(_sprite.Handle, window);
        var index = windows.FindIndex(w => w.Handle == window);
        if (index < 0)
        {
            return;
        }

        var box = windows[index].Box;
        var scale = ScreenAt(box.Left + (box.Width / 2.0), box.Top).Scale;
        var spans = EdgeOcclusion.VisibleTopEdge(index, [.. windows.Select(w => w.Box)], Screens(), _sprite.PigeonHeight(scale), _sprite.PigeonWidth(scale));
        if (spans.Count == 0)
        {
            return;
        }

        // Участок ближе к середине окна; голубь садится в его середину.
        var middle = box.Left + (box.Width / 2);
        var span = spans.MinBy(s => Math.Abs(((s.Start + s.End) / 2) - middle));
        _target = window;
        _offset = ((span.Start + span.End) / 2) - box.Left;
        Hook(window);
        FlyTo(box.Left + _offset, box.Top, "perch");
    }

    /// <summary>Проверка насеста: окно на месте, не свёрнуто, участок под голубем виден.</summary>
    private void CheckPerch(string trigger)
    {
        if (_state != "perched")
        {
            return;
        }

        if (!IsWindow(_target))
        {
            Flee("closed", trigger);
            return;
        }

        if (IsIconic(_target))
        {
            Flee("minimized", trigger);
            return;
        }

        // Окно в процессе закрытия: ещё существует, но уже невидимо.
        if (!IsWindowVisible(_target))
        {
            Flee("closed", trigger);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var windows = WindowSource.Snapshot(_sprite.Handle, _target);
        _snapshotMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        started = Stopwatch.GetTimestamp();
        var index = windows.FindIndex(w => w.Handle == _target);
        IReadOnlyList<EdgeSpan> spans = [];
        if (index >= 0)
        {
            var box = windows[index].Box;
            var scale = ScreenAt(box.Left + _offset, box.Top).Scale;
            spans = EdgeOcclusion.VisibleTopEdge(index, [.. windows.Select(w => w.Box)], Screens(), _sprite.PigeonHeight(scale), 1);
            var half = _sprite.PigeonWidth(scale) / 2;
            var x = box.Left + _offset;
            _occlusionMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (!spans.Any(s => s.Start <= x - half && s.End >= x + half))
            {
                Flee("occluded", trigger);
            }
        }
    }

    private void Flee(string reason, string trigger)
    {
        if (_test?.Events.TryGetValue(reason, out var at) == true && !_reactions.ContainsKey(reason))
        {
            _reactions[reason] = Stopwatch.GetElapsedTime(at).TotalMilliseconds;
        }

        _log.Add($"{_clock.Elapsed.TotalSeconds:0.00}s flee {reason} ({trigger})");
        Unhook();
        _target = IntPtr.Zero;
        var work = ScreenAt(_pos.X, _pos.Y).WorkArea;
        var x = Math.Clamp(_pos.X + (_direction * 260 * Scale), work.X + 80, work.Right - 80);
        FlyTo(x, work.Bottom, "flee");
    }

    private void FlyTo(double x, double y, string why)
    {
        _from = _pos;
        _to = (x, y);
        _direction = x >= _pos.X ? 1 : -1;
        _log.Add($"{_clock.Elapsed.TotalSeconds:0.00}s fly {why}");
        Enter("flying");
    }

    private void Enter(string state)
    {
        _state = state;
        _stateTime = 0;
        _animTime = 0;
    }

    private PixelBox? PerchBox() => _target != IntPtr.Zero && IsWindow(_target) && !IsIconic(_target) ? WindowSource.Bounds(_target) : null;

    private void Place(PixelBox box)
    {
        _pos = (box.Left + _offset, box.Top);
        Render();
    }

    private void Render()
    {
        var atlas = _sprite.Atlas;
        int frame;
        if (atlas is null)
        {
            frame = 0;
        }
        else
        {
            var clip = atlas.Clips[_state == "flying" ? "fly" : "idle"];
            frame = clip.From + ((int)(_animTime * 1000 / clip.FrameMs) % clip.Count);
        }

        _sprite.Show(frame, (int)_pos.X, (int)_pos.Y, _direction, Scale);
    }

    // ----- События -----

    private void Hook(IntPtr window)
    {
        Unhook();
        var thread = GetWindowThreadProcessId(window, out var process);
        _locationHook = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _onLocation, process, thread, WinEventOutOfContext);
    }

    private void Unhook()
    {
        if (_locationHook != IntPtr.Zero)
        {
            UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
    }

    /// <summary>Окно-насест сдвинулось: голубь переставляется сразу, в обработчике события.</summary>
    private void OnLocation(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (window != _target || objectId != ObjIdWindow || _state != "perched" || PerchBox() is not { } box)
        {
            return;
        }

        Place(box);
        if (_test is not null && GetWindowRect(window, out var r) && _test.Moves.TryGetValue((r.Left, r.Top), out var movedAt))
        {
            _followLatencies.Add(Stopwatch.GetElapsedTime(movedAt).TotalMilliseconds);
        }
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (_state != "perched")
        {
            return;
        }

        if (eventType == EventSystemForeground)
        {
            CheckPerch("foreground");
        }
        else if (window == _target && objectId == ObjIdWindow)
        {
            CheckPerch(eventType == EventObjectDestroy ? "destroy" : "minimize");
        }
    }

    // ----- Мониторы -----

    private static List<PixelBox> Screens() =>
        [.. Win32.Screens().Select(s => new PixelBox(s.WorkArea.X, s.WorkArea.Y, s.WorkArea.Right, s.WorkArea.Bottom))];

    private static ScreenInfo ScreenAt(double x, double y)
    {
        var screens = Win32.Screens();
        return screens.FirstOrDefault(s => x >= s.WorkArea.X && x < s.WorkArea.Right && y >= s.WorkArea.Y - 200 && y <= s.WorkArea.Bottom + 1)
            ?? screens.MinBy(s => Math.Abs(x - (s.WorkArea.X + (s.WorkArea.Width / 2.0))))!;
    }

    // ----- Отчёт -----

    private void WriteReport()
    {
        static double P(List<double> v, double p)
        {
            if (v.Count == 0)
            {
                return double.NaN;
            }

            var s = v.Order().ToList();
            return Math.Round(s[Math.Clamp((int)Math.Ceiling(p / 100 * s.Count) - 1, 0, s.Count - 1)], 3);
        }

        var report = new Dictionary<string, object>
        {
            ["os_version"] = Environment.OSVersion.Version.ToString(),
            ["follow_latency_ms"] = new Dictionary<string, object>
            {
                ["moves"] = _test?.Moves.Count ?? 0,
                ["matched"] = _followLatencies.Count,
                ["p50"] = P(_followLatencies, 50),
                ["p95"] = P(_followLatencies, 95),
                ["p99"] = P(_followLatencies, 99),
                ["max"] = P(_followLatencies, 100),
            },
            ["flee_reaction_ms"] = _reactions.ToDictionary(r => r.Key, r => (object)Math.Round(r.Value, 1)),
            ["window_snapshot_ms"] = new Dictionary<string, object>
            {
                ["checks"] = _snapshotMs.Count,
                ["p50"] = P(_snapshotMs, 50),
                ["p99"] = P(_snapshotMs, 99),
                ["max"] = P(_snapshotMs, 100),
                ["visible_windows"] = WindowSource.Snapshot(_sprite.Handle, IntPtr.Zero).Count,
            },
            ["occlusion_compute_ms"] = new Dictionary<string, object>
            {
                ["p50"] = P(_occlusionMs, 50),
                ["p99"] = P(_occlusionMs, 99),
                ["max"] = P(_occlusionMs, 100),
            },
            ["log"] = _log,
        };
        File.WriteAllText(_reportPath!, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    }
}
