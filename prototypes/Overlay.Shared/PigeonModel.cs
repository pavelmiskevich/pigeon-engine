namespace Pigeon.Prototypes.Overlay;

/// <summary>Прямоугольник в физических пикселях виртуального экрана.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <param name="WorkArea">Рабочая область без панели задач, физические пиксели.</param>
/// <param name="Scale">Масштаб монитора: 1.0 = 96 DPI.</param>
public sealed record ScreenInfo(ScreenRect WorkArea, double Scale);

public enum PigeonMode
{
    /// <summary>Сидит: дыхание и моргание, 10 кадров в секунду.</summary>
    Idle,

    /// <summary>Ходит и перелетает: 60 кадров в секунду.</summary>
    Moving,
}

/// <summary>
/// Поведение голубя для прототипа: ходит по нижней кромке рабочей области, время от времени
/// перелетает дугой в другую точку — на другом мониторе, если он есть. Позиция — точка опоры
/// (лапы) в физических пикселях.
/// </summary>
public sealed class PigeonModel
{
    /// <summary>Логический размер окна голубя при масштабе 1.0.</summary>
    public const int LogicalSize = 96;

    private const double FlightDuration = 1.6;
    private const double WalkDuration = 6.0;

    private readonly Random _random = new(42);
    private IReadOnlyList<ScreenInfo> _screens;
    private int _screen;
    private double _walkTimer;
    private double _flightTime = -1;
    private (double X, double Y) _flightFrom;
    private (double X, double Y) _flightTo;
    private int _targetScreen;

    public PigeonModel(IReadOnlyList<ScreenInfo> screens)
    {
        _screens = screens;
        var work = screens[0].WorkArea;
        X = work.X + (work.Width / 3.0);
        Y = work.Bottom;
    }

    public PigeonMode Mode { get; set; } = PigeonMode.Moving;

    /// <summary>Скорость ходьбы, логические пиксели в секунду.</summary>
    public double WalkSpeed { get; set; } = 70;

    public double X { get; private set; }

    public double Y { get; private set; }

    public int Direction { get; private set; } = 1;

    public bool Flying => _flightTime >= 0;

    /// <summary>Время анимации, секунды.</summary>
    public double Time { get; private set; }

    public ScreenInfo Screen => _screens[_screen];

    /// <summary>Сколько раз голубь перелетел на другой монитор.</summary>
    public int MonitorTransitions { get; private set; }

    public void UpdateScreens(IReadOnlyList<ScreenInfo> screens)
    {
        _screens = screens;
        _screen = Math.Min(_screen, screens.Count - 1);
        _targetScreen = Math.Min(_targetScreen, screens.Count - 1);
    }

    public void Update(double dt)
    {
        Time += dt;
        if (Mode == PigeonMode.Idle)
        {
            return;
        }

        if (Flying)
        {
            Fly(dt);
            return;
        }

        var work = Screen.WorkArea;
        var margin = LogicalSize * Screen.Scale / 2;
        X += Direction * WalkSpeed * Screen.Scale * dt;
        if (X > work.Right - margin || X < work.X + margin)
        {
            Direction = -Direction;
            X = Math.Clamp(X, work.X + margin, work.Right - margin);
        }

        Y = work.Bottom;
        _walkTimer += dt;
        if (_walkTimer >= WalkDuration)
        {
            _walkTimer = 0;
            StartFlight();
        }
    }

    private void StartFlight()
    {
        _targetScreen = _screens.Count > 1 ? (_screen + 1) % _screens.Count : _screen;
        var work = _screens[_targetScreen].WorkArea;
        var margin = LogicalSize * _screens[_targetScreen].Scale;
        _flightFrom = (X, Y);
        _flightTo = (work.X + margin + (_random.NextDouble() * Math.Max(1, work.Width - (2 * margin))), work.Bottom);
        Direction = _flightTo.X >= X ? 1 : -1;
        _flightTime = 0;
    }

    private void Fly(double dt)
    {
        _flightTime += dt;
        var t = Math.Min(1, _flightTime / FlightDuration);
        var height = 220 * Screen.Scale;
        X = _flightFrom.X + ((_flightTo.X - _flightFrom.X) * t);
        Y = _flightFrom.Y + ((_flightTo.Y - _flightFrom.Y) * t) - (height * 4 * t * (1 - t));

        var now = IndexOf(X, Y);
        if (now >= 0 && now != _screen)
        {
            _screen = now;
            MonitorTransitions++;
        }

        if (t >= 1)
        {
            _screen = _targetScreen;
            _flightTime = -1;
        }
    }

    private int IndexOf(double x, double y)
    {
        for (var i = 0; i < _screens.Count; i++)
        {
            var w = _screens[i].WorkArea;
            if (x >= w.X && x < w.Right && y >= w.Y - (400 * _screens[i].Scale) && y <= w.Bottom + 1)
            {
                return i;
            }
        }

        return -1;
    }
}
