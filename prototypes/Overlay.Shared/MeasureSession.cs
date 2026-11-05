using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Pigeon.Prototypes.Overlay;

/// <summary>
/// Автозамер прототипа (S0.2): разогрев 3 с, покой 15 с (голубь сидит, 10 кадров в секунду),
/// движение 15 с (ходит и перелетает, 60 кадров в секунду). Пишет CPU, память, время до первого
/// кадра, интервалы кадров и проверки окна в JSON.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MeasureSession
{
    private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PhaseLength = TimeSpan.FromSeconds(15);

    private readonly string _candidate;
    private readonly string? _outputPath;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly IntPtr _foregroundBefore = Win32.GetForegroundWindow();
    private readonly Dictionary<string, object> _results = [];
    private readonly List<double> _frameIntervals = [];
    private readonly List<long> _workingSet = [];
    private readonly SortedSet<string> _sizesByDpi = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _sizeMismatches = new(StringComparer.Ordinal);
    private TimeSpan _phaseCpuStart;
    private TimeSpan _phaseWallStart;
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    private TimeSpan _lastSample;
    private string _phase = "warmup";
    private bool _firstFrame = true;

    public MeasureSession(string candidate, string? outputPath)
    {
        _candidate = candidate;
        _outputPath = outputPath;
    }

    public bool Enabled => _outputPath is not null;

    /// <summary>
    /// Для кандидатов, где click-through переключается по положению курсора: перевести окно в
    /// состояние «курсор в этой точке» перед проверкой попадания.
    /// </summary>
    public Action<int, int>? SimulateCursor { get; set; }

    public bool Finished { get; private set; }

    /// <summary>Режим, которого требует текущая фаза замера.</summary>
    public PigeonMode Mode => _phase == "idle" ? PigeonMode.Idle : PigeonMode.Moving;

    /// <summary>Вызывается после того, как кадр отдан системе.</summary>
    public void OnFrame(IntPtr window, PigeonModel model)
    {
        var now = _clock.Elapsed;
        if (_firstFrame)
        {
            _firstFrame = false;
            _results["time_to_first_frame_ms"] = Math.Round((DateTime.Now - _process.StartTime).TotalMilliseconds);
        }

        if (!Enabled || Finished)
        {
            return;
        }

        if (_lastFrame != TimeSpan.MinValue && _phase != "warmup")
        {
            _frameIntervals.Add((now - _lastFrame).TotalMilliseconds);
        }

        _lastFrame = now;
        TrackSize(window, model);
        if (now - _lastSample >= TimeSpan.FromSeconds(1))
        {
            _lastSample = now;
            _process.Refresh();
            _workingSet.Add(_process.WorkingSet64);
        }

        switch (_phase)
        {
            case "warmup" when now >= Warmup:
                BeginPhase("idle", now);
                break;
            case "idle" when now >= Warmup + PhaseLength:
                CheckWindow(window, model);
                EndPhase("idle", now);
                BeginPhase("moving", now);
                break;
            case "moving" when now >= Warmup + PhaseLength + PhaseLength:
                EndPhase("moving", now);
                _results["monitor_transitions"] = model.MonitorTransitions;
                _results["window_px_by_dpi"] = _sizesByDpi.ToArray();
                _results["window_size_mismatches"] = _sizeMismatches.ToArray();
                Write();
                Finished = true;
                break;
        }
    }

    /// <summary>
    /// Размер окна должен соответствовать DPI монитора, на котором оно сейчас: 96 логических
    /// пикселей × масштаб. Расхождение — скачок размера при переходе между мониторами.
    /// </summary>
    private void TrackSize(IntPtr window, PigeonModel model)
    {
        if (!Win32.GetWindowRect(window, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var dpi = Win32.GetDpiForWindow(window);
        _sizesByDpi.Add($"{dpi} dpi: {width} px");
        var expected = (int)Math.Round(PigeonModel.LogicalSize * dpi / 96.0);
        if (Math.Abs(width - expected) > 2 && !model.Flying)
        {
            _sizeMismatches.Add($"{dpi} dpi: {width} px, ожидалось {expected}");
        }
    }

    private void BeginPhase(string phase, TimeSpan now)
    {
        _phase = phase;
        _process.Refresh();
        _phaseCpuStart = _process.TotalProcessorTime;
        _phaseWallStart = now;
        _frameIntervals.Clear();
        _workingSet.Clear();
    }

    private void EndPhase(string phase, TimeSpan now)
    {
        _process.Refresh();
        var cpu = (_process.TotalProcessorTime - _phaseCpuStart).TotalMilliseconds;
        var wall = (now - _phaseWallStart).TotalMilliseconds;
        var intervals = _frameIntervals.Order().ToList();
        _results[phase] = new Dictionary<string, object>
        {
            ["cpu_percent_of_one_core"] = Math.Round(100 * cpu / wall, 2),
            ["working_set_mb_avg"] = Math.Round(_workingSet.DefaultIfEmpty(_process.WorkingSet64).Average() / 1048576.0, 1),
            ["working_set_mb_max"] = Math.Round(_workingSet.DefaultIfEmpty(_process.WorkingSet64).Max() / 1048576.0, 1),
            ["private_mb"] = Math.Round(_process.PrivateMemorySize64 / 1048576.0, 1),
            ["fps"] = Math.Round(intervals.Count / (wall / 1000), 1),
            ["frame_ms_p50"] = Percentile(intervals, 50),
            ["frame_ms_p99"] = Percentile(intervals, 99),
            ["frames_over_25ms"] = phase == "moving" ? intervals.Count(i => i > 25) : intervals.Count(i => i > 150),
        };
    }

    /// <summary>
    /// Проверки окна, пока голубь сидит: клик в центр голубя попадает в наше окно, клик в
    /// прозрачный угол — в то, что под ним; стили и фокус.
    /// </summary>
    private void CheckWindow(IntPtr window, PigeonModel model)
    {
        Win32.GetWindowRect(window, out var rect);
        var scale = (rect.Right - rect.Left) / (double)PigeonModel.LogicalSize;
        var (bx, by) = PigeonSprite.BodyCenter;
        IntPtr HitAt(int x, int y)
        {
            SimulateCursor?.Invoke(x, y);
            return Win32.RootWindowAt(x, y);
        }

        var body = HitAt(rect.Left + (int)(bx * scale), rect.Top + (int)(by * scale));
        var corner = HitAt(rect.Left + 2, rect.Top + 2);
        var style = Win32.ExStyle(window);

        _results["checks"] = new Dictionary<string, object>
        {
            ["hit_on_pigeon_is_ours"] = body == window,
            ["hit_on_transparent_corner_passes_through"] = corner != window,
            ["topmost"] = (style & Win32.WsExTopmost) != 0,
            ["no_activate"] = (style & Win32.WsExNoActivate) != 0,
            ["tool_window_not_in_alt_tab"] = (style & Win32.WsExToolWindow) != 0,
            ["foreground_unchanged"] = Win32.GetForegroundWindow() == _foregroundBefore,
            ["window_px"] = rect.Right - rect.Left,
            ["dpi"] = Win32.GetDpiForWindow(window),
            ["monitors"] = Win32.Screens().Count,
        };
    }

    private void Write()
    {
        var report = new Dictionary<string, object>
        {
            ["candidate"] = _candidate,
            ["os_version"] = Environment.OSVersion.Version.ToString(),
            ["processor_count"] = Environment.ProcessorCount,
        };
        foreach (var (key, value) in _results)
        {
            report[key] = value;
        }

        File.WriteAllText(_outputPath!, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static double Percentile(List<double> sorted, int p) =>
        sorted.Count == 0 ? 0 : Math.Round(sorted[Math.Clamp((int)Math.Ceiling(p / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1)], 2);
}
