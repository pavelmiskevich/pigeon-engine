using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Pigeon.Common.Time;
using Pigeon.Desktop.Rendering;
using Pigeon.Desktop.Windows;
using Pigeon.Desktop.Windows.Overlay;
using Pigeon.Simulation;
using Pigeon.World.Desktop.Screen;
using SkiaSharp;

namespace Pigeon.Desktop;

/// <summary>Монотонные часы хоста для симуляции (ТЗ §3.3: в ядре — только через IClock).</summary>
internal sealed class StopwatchClock : IClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
}

/// <summary>
/// Голубь на Desktop Windows (M1.4): проба, мир L2, симуляция и окна оверлея в одном потоке.
/// Окно перерисовывается, только когда меняется кадр, масштаб или место, — в покое процесс спит
/// до следующего события (ТЗ §37).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DesktopHost(string spritesDirectory, Measurement? measurement)
{
    /// <summary>Логический пиксель рисунка на экране при масштабе 100 % — как в прототипах S0.8.</summary>
    private const double SizeOnScreen = 0.62;

    private static readonly TimeSpan WorldRefresh = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UnusedClip = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MovingFrame = TimeSpan.FromMilliseconds(1000.0 / 60);

    /// <summary>Дольше этого цикл не спит, даже если кадр не меняется: симуляция догонит время.</summary>
    private static readonly TimeSpan LongestSleep = TimeSpan.FromMilliseconds(500);

    public int Run()
    {
        MessageLoop.EnablePerMonitorDpi();

        var probe = new WindowsCapabilityProbe().Probe();
        var world = new DesktopWorld(new WindowsDesktopEnvironment(), probe.Checks);
        world.Refresh();

        var clock = new StopwatchClock();
        // Хранения в продукте ещё нет (M1.9): каждый запуск — новый голубь со случайным seed.
        var seed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
        var simulation = new PigeonSimulation(seed, SimulationOptions.Desktop);
        simulation.AttachWorld(world);
        var driver = new SimulationDriver(clock, simulation);

        using var sprites = new SpriteLibrary(spritesDirectory);
        using var shadow = new OverlayWindow("PigeonShadow", clickThrough: true);
        using var pigeon = new OverlayWindow("PigeonOverlay", clickThrough: false);
        using var keeper = new TopmostKeeper(shadow.Handle, pigeon.Handle);
        pigeon.RightClicked += (_, _) => MessageLoop.Quit();
        using var renderer = new PigeonRenderer(pigeon, shadow);

        var stage = new PigeonStage(SizeOnScreen);
        var player = new ClipPlayer();
        var lastRefresh = clock.Elapsed;
        StageFrame? shown = null;
        var shownFrame = -1;
        var nextChange = TimeSpan.Zero;
        var firstFrame = true;

        Console.WriteLine($"Голубь на мониторе {world.PigeonMonitor}, уровень {world.Level}. Закрыть — правый клик по голубю.");

        while (MessageLoop.Pump())
        {
            var now = clock.Elapsed;
            if (now - lastRefresh >= WorldRefresh)
            {
                lastRefresh = now;
                foreach (var change in world.Refresh())
                {
                    if (change is PigeonDisplaced displaced)
                    {
                        simulation.Submit(new FootingLost(displaced.Reason));
                    }
                }
            }

            driver.Pump();
            world.TrackIntent(simulation.Pigeon.Intent, simulation.Pigeon.IntentTarget);

            var frame = stage.Compose(world, simulation.Pigeon, now);
            var moving = measurement is { Moving: true };
            if (moving && frame is { } walking)
            {
                frame = walking with { Clip = "walk" };
            }

            if (frame is not { } current)
            {
                pigeon.Hide();
                shadow.Hide();
                shown = null;
            }
            else
            {
                var bucket = SpriteLibrary.BucketFor(current.Scale);
                var sheet = sprites.Get(current.Clip, bucket, now);
                player.Play(current.Clip, now);
                var index = player.FrameAt(sheet, now);

                // В движении и при смене масштаба кадры идут по расписанию 60 Гц; в покое —
                // только когда сменился кадр клипа или сцена.
                var animating = moving || stage.IsAnimating(now);
                var due = animating ? now >= nextChange : current != shown || index != shownFrame;
                if (due)
                {
                    renderer.Draw(sheet, index, current);
                    shown = current;
                    shownFrame = index;
                    measurement?.OnFrame(now);
                    if (firstFrame)
                    {
                        firstFrame = false;
                        measurement?.OnFirstFrame();
                    }
                }

                nextChange = !animating ? player.NextChange(sheet, now)
                    : !due ? nextChange
                    : nextChange + MovingFrame > now ? nextChange + MovingFrame
                    : now + MovingFrame;
            }

            sprites.ReleaseUnused(now, UnusedClip);

            if (measurement is not null)
            {
                measurement.Tick(now, () => new Dictionary<string, object>
                {
                    ["level"] = world.Level.ToString(),
                    ["monitors"] = world.Topology.Monitors.Select(m => $"{m.Bounds.Width}×{m.Bounds.Height} @ {m.Scale:0.##}").ToArray(),
                    ["sprite_sheets_loaded"] = sprites.LoadedCount,
                    ["sprite_sheets_mb"] = Math.Round(sprites.LoadedBytes / 1048576.0, 1),
                    ["topmost_raises"] = keeper.Raises,
                });
                if (measurement.Finished)
                {
                    break;
                }
            }

            if (moving || stage.IsAnimating(now))
            {
                // В движении кадры идут по композиции DWM: таймер ожидания слишком груб для 60 Гц.
                MessageLoop.WaitForComposition();
                continue;
            }

            // В покое — спать до смены кадра или опроса экрана. Отдельно ради тика симуляции не
            // просыпаемся: она досчитает пропущенные шаги при следующем пробуждении.
            var wake = Min(nextChange, lastRefresh + WorldRefresh, now + LongestSleep);
            MessageLoop.Wait(wake - clock.Elapsed);
        }

        return 0;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b, TimeSpan c) => a < b ? (a < c ? a : c) : (b < c ? b : c);
}
