using Pigeon.Common.Randomness;
using Pigeon.Core;
using Pigeon.World.Abstractions;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Locomotion;

/// <summary>Чем голубь сейчас перемещается (ТЗ §8.1, локомоция): от этого зависит клип.</summary>
public enum Gait
{
    Stand = 0,
    Walk = 1,
    Hop = 2,
    TakeOff = 3,
    Fly = 4,
    Land = 5,

    /// <summary>Улетел за край экрана и ещё не прилетел (ТЗ §10.1) или мониторов нет.</summary>
    Hidden = 6,
}

/// <summary>
/// Тело голубя на экране. Координаты — физические пиксели виртуального экрана, точка — середина
/// между стопами.
/// </summary>
/// <param name="GroundY">Где лежит тень: поверхность под голубем.</param>
/// <param name="Walked">Пройденный путь в логических пикселях рисунка — по нему идёт шаг (ADR-0023).</param>
/// <param name="GaitStarted">Когда началась текущая походка — для клипов без повтора.</param>
public readonly record struct PigeonBody(
    double X,
    double Y,
    double GroundY,
    MonitorKey? Monitor,
    bool FacingLeft,
    Gait Gait,
    double Walked,
    TimeSpan GaitStarted)
{
    public bool IsAirborne => Gait is Gait.Fly or Gait.Land or Gait.Hidden || (Gait == Gait.TakeOff);
}

/// <summary>Что локомоция сообщает хосту.</summary>
public abstract record LocomotionEvent;

/// <summary>Намерение исполнено или не исполнено (ТЗ §9.1) — хост передаёт это в симуляцию.</summary>
public sealed record ActionReported(IntentKind Intent, long IntentStartedTick, ActionOutcome Outcome, ActionFailure? Failure = null)
    : LocomotionEvent;

/// <summary>Голубь сел на другом мониторе.</summary>
public sealed record LandedOnMonitor(PigeonRelocated Change) : LocomotionEvent;

/// <summary>Поверхность под голубем исчезла, и он падает — хост сообщает симуляции потерю опоры.</summary>
public sealed record FootingGone(DisplacementReason Reason) : LocomotionEvent;

/// <summary>Скорости и времена локомоции в логических пикселях экрана (при масштабе 100 %).</summary>
/// <param name="WalkSpeed">Скорость шага: длина шага рисунка × размер на экране / цикл walk (ADR-0023).</param>
/// <param name="SizeOnScreen">Логический пиксель рисунка на экране при масштабе 100 %.</param>
public sealed record LocomotionSettings(
    double WalkSpeed = 20,
    double FlySpeed = 300,
    double SizeOnScreen = 0.62,
    double EdgeMargin = 60)
{
    public static LocomotionSettings Default { get; } = new();

    public TimeSpan TakeOff { get; init; } = TimeSpan.FromMilliseconds(330);

    public TimeSpan Land { get; init; } = TimeSpan.FromMilliseconds(330);

    public TimeSpan Hop { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Сколько голубь клюёт на месте исследования, прежде чем сказать «готово».</summary>
    public TimeSpan ExploreDwell { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Сколько голубь за краем экрана, перелетая на несмежный монитор.</summary>
    public TimeSpan Offscreen { get; init; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// Локомоция Desktop L2 (ТЗ §8.1, §10.1, M1.5): исполняет намерения на поверхностях экрана —
/// ходьба по полу, подскоки, перелёты между мониторами — и сообщает результат.
/// </summary>
/// <remarks>
/// <para>
/// Перелёт — квадратичная кривая Безье с фазами взлёта и посадки. Если дуга вышла бы за пределы
/// мониторов (зазор, несмежный монитор), голубь улетает за край своего монитора и прилетает с
/// края нужного (ТЗ §10.1). В воздухе намерение не меняется: новое исполняется после посадки.
/// </para>
/// <para>
/// Локомоция — часть мира, а не симуляции: её случайность (куда пойти при прогулке) не обязана
/// быть детерминированной, но берётся из <see cref="IRandomSource"/>, как и в ядре.
/// </para>
/// </remarks>
public sealed class DesktopLocomotion
{
    /// <summary>Короче этого голубь не идёт: стоит на месте.</summary>
    private const double MinWalk = 30;

    private const double HopChance = 0.15;

    private readonly DesktopWorld _world;
    private readonly IRandomSource _random;
    private readonly LocomotionSettings _settings;
    private readonly Queue<Step> _steps = new();
    private Step? _step;
    private TimeSpan _stepStarted;
    private TimeSpan _lastUpdate;
    private PlaceKey? _surface;
    private (IntentKind Intent, AffordanceId? Target, long Started)? _executing;
    private (IntentKind Intent, AffordanceId? Target, long Started)? _requested;

    public DesktopLocomotion(DesktopWorld world, IRandomSource random, LocomotionSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(random);

        _world = world;
        _random = random;
        _settings = settings ?? LocomotionSettings.Default;
        Body = new PigeonBody(0, 0, 0, null, FacingLeft: true, Gait.Hidden, 0, TimeSpan.Zero);
    }

    public PigeonBody Body { get; private set; }

    /// <summary>Голубь движется — кадры нужны с частотой экрана.</summary>
    public bool IsMoving => Body.Gait is not (Gait.Stand or Gait.Hidden);

    /// <summary>
    /// Текущее намерение симуляции; хост вызывает это каждый кадр. Новое намерение планируется
    /// сразу, а в воздухе — после посадки.
    /// </summary>
    public void Execute(IntentKind intent, AffordanceId? target, long intentStartedTick) =>
        _requested = (intent, target, intentStartedTick);

    /// <summary>Продвигает движение ко времени <paramref name="now"/>.</summary>
    public IReadOnlyList<LocomotionEvent> Update(TimeSpan now)
    {
        var events = new List<LocomotionEvent>();
        var dt = Math.Max(0, (now - _lastUpdate).TotalSeconds);
        _lastUpdate = now;

        if (Body.Gait == Gait.Hidden && _step is null && !EnsurePlaced(now))
        {
            return events;
        }

        if (!Body.IsAirborne && _requested != _executing && _requested is { } request)
        {
            Plan(request, now);
        }

        // Шагов может закончиться несколько за один вызов: отчёт и пауза мгновенны.
        for (var guard = 0; guard < 8; guard++)
        {
            if (_step is null)
            {
                if (_steps.Count == 0)
                {
                    ContinueIntent(now);
                    if (_steps.Count == 0)
                    {
                        SetGait(Gait.Stand, now);
                        break;
                    }
                }

                _step = _steps.Dequeue();
                _stepStarted = now;
            }

            if (!Advance(_step, now, dt, events))
            {
                break;
            }

            _step = null;
            dt = 0;
        }

        return events;
    }

    /// <summary>
    /// Экран изменился (<see cref="DesktopWorld.Refresh"/>): голубь, у которого пропал монитор,
    /// падает на новый; поверхность, которая сдвинулась, уносит голубя с собой; исчезнувшая — роняет.
    /// </summary>
    public IReadOnlyList<LocomotionEvent> OnWorldChanged(IReadOnlyList<DesktopWorldEvent> changes, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var events = new List<LocomotionEvent>();
        if (changes.OfType<PigeonDisplaced>().LastOrDefault() is { } displaced)
        {
            Abort();
            if (displaced.To is { } to && _world.HomeSurface(to) is { } home)
            {
                Fall(home, now, fromTop: true);
            }
            else
            {
                Body = Body with { Gait = Gait.Hidden, Monitor = null };
                _surface = null;
            }

            return events;
        }

        if (Body.IsAirborne || _surface is not { } key)
        {
            return events;
        }

        if (_world.Surfaces.FirstOrDefault(s => s.Key == key) is { } surface)
        {
            // Поверхность могла сдвинуться (смена разрешения, масштаба, высоты панели).
            Body = Body with { X = Math.Clamp(Body.X, surface.Left + Margin(surface), surface.Right - Margin(surface)), Y = surface.Y, GroundY = surface.Y };
            return events;
        }

        Abort();
        events.Add(new FootingGone(DisplacementReason.PlaceGone));
        if ((_world.SurfaceBelow(Body.X, Body.Y) ?? (Body.Monitor is { } monitor ? _world.HomeSurface(monitor) : null)) is { } below)
        {
            Fall(below, now, fromTop: false);
        }
        else
        {
            Body = Body with { Gait = Gait.Hidden };
            _surface = null;
        }

        return events;
    }

    private bool EnsurePlaced(TimeSpan now)
    {
        if (_world.PigeonMonitor is not { } monitor || _world.HomeSurface(monitor) is not { } home)
        {
            return false;
        }

        var x = home.Left + ((home.Right - home.Left) * 0.78);
        Body = new PigeonBody(x, home.Y, home.Y, monitor, FacingLeft: true, Gait.Stand, Body.Walked, now);
        _surface = home.Key;
        return true;
    }

    private void Plan((IntentKind Intent, AffordanceId? Target, long Started) request, TimeSpan now)
    {
        Abort();
        _executing = request;
        var (intent, target, started) = request;

        switch (intent)
        {
            case IntentKind.Wander:
                QueueWanderLeg();
                break;
            case IntentKind.Explore:
                if (target is not { } id || _world.SurfaceOf(id) is not { } place)
                {
                    Report(ActionOutcome.Failed, ActionFailure.TargetGone);
                    break;
                }

                if (place.Key == _surface)
                {
                    _steps.Enqueue(new WalkStep(RandomX(place)));
                }
                else
                {
                    QueueFlight(place, RandomX(place));
                }

                _steps.Enqueue(new PauseStep(_settings.ExploreDwell));
                Report(ActionOutcome.Completed, null);
                break;
            case IntentKind.Relocate:
                if (target is not { } territory || _world.MonitorOfTerritory(territory) is not { } monitor
                    || _world.HomeSurface(monitor) is not { } home)
                {
                    Report(ActionOutcome.Failed, ActionFailure.Unreachable);
                    break;
                }

                QueueFlight(home, home.Left + ((home.Right - home.Left) * Proportion()));
                Report(ActionOutcome.Completed, null);
                break;
            default:
                // Отдых, сон, чистка, наблюдение — на месте; игры с курсором — M1.6.
                break;
        }

        void Report(ActionOutcome outcome, ActionFailure? failure) =>
            _steps.Enqueue(new ReportStep(new ActionReported(intent, started, outcome, failure)));
    }

    /// <summary>Шаги кончились, а намерение то же: прогулка продолжается, остальное ждёт на месте.</summary>
    private void ContinueIntent(TimeSpan now)
    {
        if (_executing is { Intent: IntentKind.Wander } && _surface is not null)
        {
            QueueWanderLeg();
        }
    }

    private void QueueWanderLeg()
    {
        if (CurrentSurface() is not { } surface)
        {
            return;
        }

        if (_random.NextDouble() < HopChance)
        {
            _steps.Enqueue(new HopStep());
        }

        var x = RandomX(surface);
        if (Math.Abs(x - Body.X) / Scale() >= MinWalk)
        {
            _steps.Enqueue(new WalkStep(x));
        }

        _steps.Enqueue(new PauseStep(TimeSpan.FromSeconds(1.5 + (2.5 * _random.NextDouble()))));
    }

    private void QueueFlight(Surface landing, double x)
    {
        var start = (Body.X, Body.Y);
        var end = (x, (double)landing.Y);
        var scale = Scale();
        var legs = new List<Leg>();

        // Дуга повыше, если низкая проходит там, где нет монитора (мониторы разной высоты).
        if (new[] { 0.35, 0.7, 1.2 }.Select(lift => Arc(start, end, scale, lift)).FirstOrDefault(IsVisible) is { } arc)
        {
            legs.Add(arc);
        }
        else
        {
            // Через край: вылет за свой монитор, пауза за экраном, влёт с края нужного (ТЗ §10.1).
            var from = _world.Topology.Find(Body.Monitor ?? landing.Monitor)!;
            var to = _world.Topology.Find(landing.Monitor)!;
            var right = to.Bounds.X + (to.Bounds.Width / 2.0) > from.Bounds.X + (from.Bounds.Width / 2.0);
            var reach = 120 * scale;
            var exit = (right ? from.Bounds.X + from.Bounds.Width + reach : from.Bounds.X - reach, Math.Max(from.Bounds.Y + reach, Body.Y - (150 * scale)));
            var entry = (right ? to.Bounds.X - reach : to.Bounds.X + to.Bounds.Width + reach, Math.Max(to.Bounds.Y + reach, landing.Y - (150 * scale)));
            legs.Add(Arc(start, exit, scale, 0.35));
            legs.Add(new HiddenLeg(_settings.Offscreen, entry));
            legs.Add(Arc(entry, end, to.Scale, 0.35));
        }

        _steps.Enqueue(new FlightStep(legs, landing));
    }

    /// <summary>
    /// Голубь падает на <paramref name="landing"/>: с верха монитора, если его монитор пропал, или
    /// с места, где исчезла опора. Падение — полёт без взлёта, с посадкой.
    /// </summary>
    private void Fall(Surface landing, TimeSpan now, bool fromTop)
    {
        var monitor = _world.Topology.Find(landing.Monitor)!;
        var margin = Margin(landing);
        var x = fromTop
            ? landing.Left + ((landing.Right - landing.Left) * 0.78)
            : Math.Clamp(Body.X, landing.Left + margin, landing.Right - margin);
        var y = fromTop ? monitor.Bounds.Y + (80 * monitor.Scale) : Math.Min(Body.Y, landing.Y);
        Body = Body with { X = x, Y = y, Monitor = landing.Monitor, Gait = Gait.Fly, GaitStarted = now };
        _surface = null;
        var drop = Math.Max(1, landing.Y - y);
        _steps.Enqueue(new FlightStep([new ArcLeg((x, y), (x, (y + landing.Y) / 2), (x, landing.Y), FlightTime(drop, monitor.Scale))], landing, SkipTakeOff: true));
    }

    private void Abort()
    {
        _steps.Clear();
        _step = null;
        _executing = null;
        if (!Body.IsAirborne && Body.Gait != Gait.Hidden)
        {
            Body = Body with { Gait = Gait.Stand };
        }
    }

    /// <summary>Исполняет шаг; true — шаг закончен.</summary>
    private bool Advance(Step step, TimeSpan now, double dt, List<LocomotionEvent> events)
    {
        var elapsed = now - _stepStarted;
        switch (step)
        {
            case ReportStep report:
                events.Add(report.Report);
                return true;
            case PauseStep pause:
                SetGait(Gait.Stand, now);
                return elapsed >= pause.Duration;
            case HopStep:
                SetGait(Gait.Hop, now);
                return elapsed >= _settings.Hop;
            case WalkStep walk:
                return Walk(walk.TargetX, now, dt);
            case FlightStep flight:
                return Fly(flight, now, elapsed, events);
            default:
                throw new InvalidOperationException($"Неизвестный шаг локомоции: {step}.");
        }
    }

    private bool Walk(double target, TimeSpan now, double dt)
    {
        var distance = target - Body.X;
        if (Math.Abs(distance) < 0.5)
        {
            return true;
        }

        SetGait(Gait.Walk, now);
        var scale = Scale();
        var step = Math.Min(Math.Abs(distance), _settings.WalkSpeed * scale * dt);
        Body = Body with
        {
            X = Body.X + (Math.Sign(distance) * step),
            FacingLeft = distance < 0,
            Walked = Body.Walked + (step / scale / _settings.SizeOnScreen),
        };
        return Math.Abs(target - Body.X) < 0.5;
    }

    private bool Fly(FlightStep flight, TimeSpan now, TimeSpan elapsed, List<LocomotionEvent> events)
    {
        var takeOff = flight.SkipTakeOff ? TimeSpan.Zero : _settings.TakeOff;
        if (elapsed < takeOff)
        {
            SetGait(Gait.TakeOff, now);
            _surface = null;
            return false;
        }

        var t = elapsed - takeOff;
        foreach (var leg in flight.Legs)
        {
            var duration = Duration(leg);
            if (t < duration)
            {
                Follow(leg, t / duration, now);
                return false;
            }

            t -= duration;
        }

        var landing = flight.Landing;
        Body = Body with { X = flight.Legs[^1].End.X, Y = landing.Y, GroundY = landing.Y, Monitor = landing.Monitor };
        if (t < _settings.Land)
        {
            SetGait(Gait.Land, now);
            return false;
        }

        _surface = landing.Key;
        SetGait(Gait.Stand, now);
        if (_world.MovePigeonTo(landing.Monitor) is { } moved)
        {
            events.Add(new LandedOnMonitor(moved));
        }

        return true;
    }

    private void Follow(Leg leg, double progress, TimeSpan now)
    {
        if (leg is HiddenLeg hidden)
        {
            SetGait(Gait.Hidden, now);
            Body = Body with { X = hidden.End.X, Y = hidden.End.Y };
            return;
        }

        SetGait(Gait.Fly, now);
        var arc = (ArcLeg)leg;
        var (x, y) = arc.At(progress);
        var ground = _world.SurfaceBelow(x, y)?.Y ?? Body.GroundY;
        Body = Body with
        {
            FacingLeft = arc.End.X < arc.Start.X || (arc.End.X == arc.Start.X && Body.FacingLeft),
            X = x,
            Y = y,
            GroundY = ground,
            Monitor = _world.MonitorAt(x, y - 1)?.Key ?? Body.Monitor,
        };
    }

    private static TimeSpan Duration(Leg leg) => leg.Duration;

    /// <summary>Время перелёта считается при планировании: в пути масштаб под голубем меняется.</summary>
    private TimeSpan FlightTime(double distance, double scale) =>
        TimeSpan.FromSeconds(Math.Max(0.3, distance / (_settings.FlySpeed * scale)));

    private ArcLeg Arc((double X, double Y) from, (double X, double Y) to, double scale, double liftShare)
    {
        var distance = Math.Sqrt(((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)));
        var lift = Math.Clamp(liftShare * distance, 50 * scale, 600 * scale);
        var middle = ((from.X + to.X) / 2, Math.Min(from.Y, to.Y) - lift);

        // Вершина дуги не выше верха монитора под ней: голубь не вылетает за верх экрана.
        if (_world.MonitorAt(middle.Item1, Math.Max(from.Y, to.Y) - 1) is { } below)
        {
            middle = (middle.Item1, Math.Max(middle.Item2, below.Bounds.Y + (90 * scale)));
        }

        return new ArcLeg(from, middle, to, FlightTime(Math.Max(distance, 1), scale));
    }

    private bool IsVisible(ArcLeg arc)
    {
        for (var i = 0; i <= 24; i++)
        {
            // Точка лап на полу лежит на нижней границе монитора: проверяем пиксель над ней.
            var (x, y) = arc.At(i / 24.0);
            if (_world.MonitorAt(x, y - 1) is null)
            {
                return false;
            }
        }

        return true;
    }

    private void SetGait(Gait gait, TimeSpan now)
    {
        if (Body.Gait != gait)
        {
            Body = Body with { Gait = gait, GaitStarted = now };
        }
    }

    private Surface? CurrentSurface() => _surface is { } key ? _world.Surfaces.FirstOrDefault(s => s.Key == key) : null;

    private double Scale() => (Body.Monitor is { } key ? _world.Topology.Find(key)?.Scale : null) ?? 1.0;

    private double Margin(Surface surface) => Math.Min(_settings.EdgeMargin * surface.Scale, (surface.Right - surface.Left) / 2.0);

    private double RandomX(Surface surface)
    {
        var margin = Margin(surface);
        return surface.Left + margin + ((surface.Right - surface.Left - (2 * margin)) * _random.NextDouble());
    }

    /// <summary>Где голубь на своей поверхности: доля длины — сохраняется при перелёте на другой монитор.</summary>
    private double Proportion() =>
        CurrentSurface() is { } surface && surface.Right > surface.Left ? Math.Clamp((Body.X - surface.Left) / (surface.Right - surface.Left), 0.1, 0.9) : 0.78;

    private abstract record Step;

    private sealed record WalkStep(double TargetX) : Step;

    private sealed record PauseStep(TimeSpan Duration) : Step;

    private sealed record HopStep : Step;

    private sealed record ReportStep(ActionReported Report) : Step;

    private sealed record FlightStep(IReadOnlyList<Leg> Legs, Surface Landing, bool SkipTakeOff = false) : Step;

    private abstract record Leg((double X, double Y) End, TimeSpan Duration);

    /// <summary>Квадратичная кривая Безье.</summary>
    private sealed record ArcLeg((double X, double Y) Start, (double X, double Y) Control, (double X, double Y) End, TimeSpan Duration)
        : Leg(End, Duration)
    {
        public (double X, double Y) At(double t)
        {
            var u = 1 - t;
            return ((u * u * Start.X) + (2 * u * t * Control.X) + (t * t * End.X), (u * u * Start.Y) + (2 * u * t * Control.Y) + (t * t * End.Y));
        }
    }

    private sealed record HiddenLeg(TimeSpan Duration, (double X, double Y) End) : Leg(End, Duration);
}
