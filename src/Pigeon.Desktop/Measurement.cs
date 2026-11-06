using System.Diagnostics;
using System.Text.Json;

namespace Pigeon.Desktop;

/// <summary>
/// Замер бюджетов ТЗ §37 в продукте (M1.4) — так же, как в прототипах S0.2 и S0.8: разогрев 3 с,
/// покой 15 с (голубь сидит, кадры по клипу), движение 15 с (клип walk, 60 кадров в секунду).
/// CPU — в процентах одного ядра и всего процессора.
/// </summary>
internal sealed class Measurement(string outputPath)
{
    private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PhaseLength = TimeSpan.FromSeconds(15);

    private readonly Process _process = Process.GetCurrentProcess();
    private readonly Dictionary<string, object> _results = [];
    private readonly List<long> _workingSet = [];
    private readonly List<double> _frameIntervals = [];
    private TimeSpan _phaseStart;
    private TimeSpan _cpuStart;
    private TimeSpan _lastSample;
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    private string _phase = "warmup";

    /// <summary>Фаза «движение»: голубь ходит на месте с частотой 60 кадров в секунду.</summary>
    public bool Moving => _phase == "moving";

    /// <summary>Разогрев и покой: голубь стоит на месте, что бы ни выбрал ИИ.</summary>
    public bool Resting => _phase is "warmup" or "idle";

    public bool Finished { get; private set; }

    public void OnFirstFrame() =>
        _results["time_to_first_frame_ms"] = Math.Round((DateTime.Now - _process.StartTime).TotalMilliseconds);

    public void OnFrame(TimeSpan now)
    {
        if (_lastFrame != TimeSpan.MinValue && _phase != "warmup")
        {
            _frameIntervals.Add((now - _lastFrame).TotalMilliseconds);
        }

        _lastFrame = now;
    }

    /// <summary>Вызывается на каждом обороте цикла; пишет результат, когда замер закончен.</summary>
    public void Tick(TimeSpan now, Func<IReadOnlyDictionary<string, object>> details)
    {
        if (now - _lastSample >= TimeSpan.FromSeconds(1))
        {
            _lastSample = now;
            _process.Refresh();
            _workingSet.Add(_process.WorkingSet64);
        }

        switch (_phase)
        {
            case "warmup" when now >= Warmup:
                Begin("idle", now);
                break;
            case "idle" when now >= Warmup + PhaseLength:
                End("idle", now);
                Begin("moving", now);
                break;
            case "moving" when now >= Warmup + PhaseLength + PhaseLength:
                End("moving", now);
                foreach (var (key, value) in details())
                {
                    _results[key] = value;
                }

                File.WriteAllText(outputPath, JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                Finished = true;
                break;
        }
    }

    private void Begin(string phase, TimeSpan now)
    {
        _phase = phase;
        _process.Refresh();
        _cpuStart = _process.TotalProcessorTime;
        _phaseStart = now;
        _workingSet.Clear();
        _frameIntervals.Clear();
    }

    private void End(string phase, TimeSpan now)
    {
        _process.Refresh();
        var cpu = (_process.TotalProcessorTime - _cpuStart).TotalMilliseconds;
        var wall = (now - _phaseStart).TotalMilliseconds;
        var oneCore = 100 * cpu / wall;
        _results[phase] = new Dictionary<string, object>
        {
            ["cpu_percent_of_one_core"] = Math.Round(oneCore, 2),
            ["cpu_percent_total"] = Math.Round(oneCore / Environment.ProcessorCount, 2),
            ["working_set_mb_avg"] = Math.Round(_workingSet.DefaultIfEmpty(_process.WorkingSet64).Average() / 1048576.0, 1),
            ["working_set_mb_max"] = Math.Round(_workingSet.DefaultIfEmpty(_process.WorkingSet64).Max() / 1048576.0, 1),
            ["private_mb"] = Math.Round(_process.PrivateMemorySize64 / 1048576.0, 1),
            ["managed_heap_mb"] = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
            ["fps"] = Math.Round(_frameIntervals.Count / (wall / 1000), 1),
        };
    }
}
