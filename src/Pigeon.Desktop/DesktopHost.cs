using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Pigeon.Common.Randomness;
using Pigeon.Common.Time;
using Pigeon.Desktop.Rendering;
using Pigeon.Desktop.Windows;
using Pigeon.Desktop.Windows.Overlay;
using Pigeon.Simulation;
using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Interaction;
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
/// Голубь на Desktop Windows (M1.4–M1.6): проба, мир L2, симуляция, локомоция, окна оверлея и
/// мышь в одном потоке. Пока голубь стоит, окно перерисовывается только при смене кадра, и процесс
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

    /// <summary>Пока курсор рядом, он опрашивается чаще: голубь следит за ним без задержки (ТЗ §8.2).</summary>
    private static readonly TimeSpan CursorNearSleep = TimeSpan.FromMilliseconds(100);

    /// <summary>Сколько поглаженный голубь сидит довольный.</summary>
    private static readonly TimeSpan PettedFor = TimeSpan.FromSeconds(2.5);

    private static readonly TimeSpan HideFor = TimeSpan.FromHours(1);

    /// <summary>Середина голубя над лапами, логические пиксели экрана: до неё считается расстояние до курсора.</summary>
    private const double BodyMiddle = 20;

    private const int MenuHide = 1;
    private const int MenuQuit = 2;

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
        using var pigeon = new OverlayWindow("PigeonOverlay", clickThrough: false, handCursor: true);
        using var keeper = new TopmostKeeper(shadow.Handle, pigeon.Handle);
        var pointer = new Queue<PointerEventArgs>();
        pigeon.Pointer += (_, e) => pointer.Enqueue(e);
        using var renderer = new PigeonRenderer(pigeon, shadow);

        // Курсор вдали от голубя — только при C7 (ТЗ §8.2); над голубем жесты работают всегда.
        var cursorKnown = probe.Checks.Any(c => c.Capability == Capability.GlobalCursor && c.IsUsable);
        var gestures = new PointerGestures();
        var petted = (Since: TimeSpan.Zero, Until: TimeSpan.Zero);
        var hiddenUntil = TimeSpan.Zero;

        var stage = new PigeonStage(settings.SizeOnScreen);
        var player = new ClipPlayer();
        var lastRefresh = clock.Elapsed;
        var lastLoop = clock.Elapsed;
        var measuredWalk = 0.0;
        StageFrame? shown = null;
        var shownFrame = -1;
        var nextChange = TimeSpan.Zero;
        var firstFrame = true;

        Console.WriteLine($"Голубь на мониторе {world.PigeonMonitor}, уровень {world.Level}. Меню — правый клик по голубю.");

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

            while (pointer.TryDequeue(out var input))
            {
                foreach (var gesture in Recognize(gestures, input, now))
                {
                    Interact(gesture, locomotion, simulation, now, ref petted);
                }

                if (input.Action == PointerAction.RightUp)
                {
                    MenuItem?[] items = [new MenuItem(MenuHide, "Скрыть на час"), null, new MenuItem(MenuQuit, "Выход")];
                    switch (PopupMenu.Show(pigeon.Handle, input.X, input.Y, items))
                    {
                        case MenuHide:
                            hiddenUntil = clock.Elapsed + HideFor;
                            break;
                        case MenuQuit:
                            MessageLoop.Quit();
                            break;
                    }
                }
            }

            var hidden = now < hiddenUntil;
            if (cursorKnown && measurement is null)
            {
                var seen = locomotion.Body;
                var scale = world.MonitorAt(seen.X, seen.Y - 1)?.Scale ?? 1.0;
                (double X, double Y)? middle = shown is not null && !hidden && !locomotion.IsHeld
                    ? (seen.X, seen.Y - (BodyMiddle * scale))
                    : null;
                (double X, double Y)? cursor = CursorPosition.Read() is { } point ? (point.X, point.Y) : null;
                if (world.ObserveCursor(cursor, middle, now))
                {
                    simulation.Submit(new CursorApproached());
                }
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
            var frame = hidden ? null : stage.Compose(world, body, intent, now);
            if (now < petted.Until && body.Gait == Gait.Stand && frame is { } content)
            {
                // Поглаженный голубь сидит довольный, пока не кончится пауза.
                frame = content with { Clip = "sit", ClipStarted = petted.Since };
            }

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
            var wake = Min(nextChange, lastRefresh + WorldRefresh, now + (world.Cursor.IsNear ? CursorNearSleep : LongestSleep));
            MessageLoop.Wait(wake - clock.Elapsed);
        }

        return 0;
    }

    /// <summary>Событие мыши над голубем — в жесты (ТЗ §11.8).</summary>
    private static IReadOnlyList<Gesture> Recognize(PointerGestures gestures, PointerEventArgs input, TimeSpan now) => input.Action switch
    {
        PointerAction.Hover or PointerAction.Drag => gestures.Move(input.X, input.Y, now),
        PointerAction.LeftDown => gestures.Press(input.X, input.Y, now),
        PointerAction.LeftUp => gestures.Release(input.X, input.Y, now),
        PointerAction.CaptureLost => gestures.Cancel(input.X, input.Y),
        _ => [],
    };

    /// <summary>
    /// Жест — в мир и в симуляцию: мир сразу показывает реакцию тела, симуляция записывает
    /// взаимодействие в журнал и меняет потребности и настроение (ТЗ §11.8).
    /// </summary>
    private static void Interact(
        Gesture gesture,
        DesktopLocomotion locomotion,
        PigeonSimulation simulation,
        TimeSpan now,
        ref (TimeSpan Since, TimeSpan Until) petted)
    {
        switch (gesture.Kind)
        {
            case GestureKind.Poke when !locomotion.IsHeld:
                locomotion.Flinch();
                simulation.Submit(new UserPoked());
                break;
            case GestureKind.Pet when !locomotion.IsHeld:
                locomotion.Settle(PettedFor);
                petted = (now < petted.Until ? petted.Since : now, now + PettedFor);
                simulation.Submit(new UserPetted());
                break;
            case GestureKind.PickUp:
                locomotion.PickUp(gesture.X, gesture.Y, now);
                if (locomotion.IsHeld)
                {
                    simulation.Submit(new UserPickedUp());
                }

                break;
            case GestureKind.Carry:
                locomotion.Carry(gesture.X, gesture.Y);
                break;
            case GestureKind.Drop when locomotion.IsHeld:
                locomotion.Drop(now);
                simulation.Submit(new UserDropped());
                break;
        }
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
