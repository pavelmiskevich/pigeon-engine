using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Pigeon.Common.Randomness;
using Pigeon.Common.Time;
using Pigeon.Desktop.Rendering;
using Pigeon.Desktop.Windows;
using Pigeon.Desktop.Windows.Overlay;
using Pigeon.Simulation;
using Pigeon.World.Desktop.Locomotion;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.Desktop;

/// <summary>Монотонные часы хоста для симуляции (ТЗ §3.3: в ядре — только через IClock).</summary>
internal sealed class StopwatchClock : IClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
}

/// <summary>
/// Голубь на Desktop Windows (M1.4, M1.5): проба, мир L2, симуляция, локомоция и окна оверлея в
/// одном потоке. Пока голубь стоит, окно перерисовывается только при смене кадра, и процесс
/// спит до следующего события; в движении кадры идут 60 раз в секунду (ТЗ §37).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DesktopHost(string spritesDirectory, Measurement? measurement)
{
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
        var settings = LocomotionSettings.Default;
        var locomotion = new DesktopLocomotion(world, new Pcg32(seed, 0x10C0), settings);

        using var sprites = new SpriteLibrary(spritesDirectory);
        using var shadow = new OverlayWindow("PigeonShadow", clickThrough: true);
        using var pigeon = new OverlayWindow("PigeonOverlay", clickThrough: false);
        using var keeper = new TopmostKeeper(shadow.Handle, pigeon.Handle);
        pigeon.RightClicked += (_, _) => MessageLoop.Quit();
        using var renderer = new PigeonRenderer(pigeon, shadow);

        var stage = new PigeonStage(settings.SizeOnScreen);
        var player = new ClipPlayer();
        var lastRefresh = clock.Elapsed;
        var lastLoop = clock.Elapsed;
        var measuredWalk = 0.0;
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
                var changes = world.Refresh();
                foreach (var displaced in changes.OfType<PigeonDisplaced>())
                {
                    simulation.Submit(new FootingLost(displaced.Reason));
                }

                Report(locomotion.OnWorldChanged(changes, now), simulation);
            }

            driver.Pump();
            var state = simulation.Pigeon;
            var body = locomotion.Body;
            var intent = state.Intent;
            if (measurement is { Resting: true })
            {
                // Замер покоя: голубь стоит и дышит — клип idle, локомоция не идёт.
                body = body.Gait == Gait.Hidden ? body : body with { Gait = Gait.Stand };
                intent = Pigeon.Core.IntentKind.Groom;
                locomotion.Update(now);
            }
            else
            {
                locomotion.Execute(state.Intent, state.IntentTarget, state.IntentStartedTick);
                Report(locomotion.Update(now), simulation);
                body = locomotion.Body;
            }

            var moving = measurement is { Moving: true };
            var frame = stage.Compose(world, body, intent, now);
            if (moving && frame is { } walking)
            {
                // Замер движения: шаг на месте со скоростью ходьбы, кадры по пройденному пути.
                measuredWalk += (now - lastLoop).TotalSeconds * settings.WalkSpeed / settings.SizeOnScreen;
                frame = walking with { Clip = "walk", Walked = measuredWalk };
            }

            lastLoop = now;
            var animating = moving || (measurement is not { Resting: true } && locomotion.IsMoving) || stage.IsAnimating(now);

            if (frame is not { } current)
            {
                pigeon.Hide();
                shadow.Hide();
                shown = null;
            }
            else
            {
                var sheet = sprites.Get(current.Clip, SpriteLibrary.BucketFor(current.Scale), now);
                int index;
                if (current.Clip == "walk")
                {
                    index = ClipPlayer.FrameByDistance(sheet, current.Walked);
                }
                else
                {
                    player.Play(current.Clip, current.ClipStarted);
                    index = player.FrameAt(sheet, now);
                }

                // В движении кадры идут по расписанию 60 Гц; на месте — только когда сменился
                // кадр клипа или сцена.
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

            if (animating)
            {
                // В движении кадры идут по композиции DWM: таймер ожидания слишком груб для 60 Гц.
                MessageLoop.WaitForComposition();
                continue;
            }

            // На месте — спать до смены кадра или опроса экрана. Отдельно ради тика симуляции не
            // просыпаемся: она досчитает пропущенные шаги при следующем пробуждении.
            var wake = Min(nextChange, lastRefresh + WorldRefresh, now + LongestSleep);
            MessageLoop.Wait(wake - clock.Elapsed);
        }

        return 0;
    }

    /// <summary>События локомоции — в симуляцию: результат исполнения и потеря опоры.</summary>
    private static void Report(IReadOnlyList<LocomotionEvent> events, PigeonSimulation simulation)
    {
        foreach (var e in events)
        {
            switch (e)
            {
                case ActionReported report:
                    simulation.Submit(new ActionFinished(report.Intent, report.IntentStartedTick, report.Outcome, report.Failure));
                    break;
                case FootingGone gone:
                    simulation.Submit(new FootingLost(gone.Reason));
                    break;
            }
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b, TimeSpan c) => a < b ? (a < c ? a : c) : (b < c ? b : c);
}
