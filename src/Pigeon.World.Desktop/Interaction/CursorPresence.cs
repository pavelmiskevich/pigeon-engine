using Pigeon.Core;
using Pigeon.World.Abstractions;

namespace Pigeon.World.Desktop.Interaction;

/// <summary>Расстояния и времена, по которым голубь замечает курсор (ТЗ §8.2), в логических пикселях экрана.</summary>
/// <param name="NoticeRadius">Ближе этого курсор замечается.</param>
/// <param name="LeaveRadius">Дальше этого курсор считается ушедшим — с запасом, чтобы не мигать на границе.</param>
/// <param name="ScarySpeed">Скорость курсора, при которой он пугает сильнее всего, логических пикселей в секунду.</param>
public sealed record CursorSettings(double NoticeRadius = 220, double LeaveRadius = 340, double ScarySpeed = 1500)
{
    public static CursorSettings Default { get; } = new();

    /// <summary>Сколько ушедший курсор ещё интересен: голубь не бросает реакцию, если курсор отошёл на миг.</summary>
    public TimeSpan Linger { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Курсор, вернувшийся раньше, не замечается заново: решение не пересматривается на каждое касание.</summary>
    public TimeSpan RenoticeAfter { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Курсор, двигавшийся за это время, — «живой» и привлекательнее неподвижного.</summary>
    public TimeSpan Lively { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Курсор как объект Desktop World (ТЗ §8.2, C7): где он, близко ли к голубю и насколько резко
/// двигается. Пока курсор рядом, мир предлагает его как цель внимания
/// (<see cref="AffordanceKind.AttentionTarget"/>): быстрый курсор — рискованнее, движущийся —
/// привлекательнее. Ядро решает, что с ним делать: наблюдать, подойти, поиграть или отойти.
/// </summary>
/// <remarks>
/// Оценки округляются ступенями: ответ мира пишется в журнал при каждом изменении (ADR-0016), и
/// дрожание курсора не должно превращаться в поток записей.
/// </remarks>
public sealed class CursorPresence(CursorSettings? settings = null)
{
    public static readonly PlaceKey Place = new("desktop/cursor");

    private const double Calm = 0.5;
    private const double Lively = 0.8;
    private const double BaseRisk = 0.15;
    private const double SpeedRisk = 0.45;
    private const double RiskStep = 0.05;
    private const double TravelStepSeconds = 2;
    private const double WalkSpeed = 20;

    /// <summary>За сколько скорость курсора сглаживается.</summary>
    private static readonly TimeSpan SpeedSmoothing = TimeSpan.FromMilliseconds(300);

    private readonly CursorSettings _settings = settings ?? CursorSettings.Default;
    private TimeSpan? _lastSeen;
    private TimeSpan? _lastMoved;
    private TimeSpan? _lastNoticed;
    private TimeSpan? _farSince;
    private double _speed;
    private double _distance;

    public static AffordanceId Id { get; } = Screen.DesktopWorld.IdOf(Place);

    /// <summary>Где курсор, физические пиксели виртуального экрана; <c>null</c> — неизвестно.</summary>
    public (double X, double Y)? Position { get; private set; }

    /// <summary>Курсор рядом с голубем — он цель внимания.</summary>
    public bool IsNear { get; private set; }

    /// <summary>Курсор как цель внимания, пока он рядом.</summary>
    public Affordance? Affordance => IsNear
        ? new Affordance(Id, Place, AffordanceKind.AttentionTarget, Attractiveness(), Risk(), Travel())
        : null;

    /// <summary>
    /// Новое положение курсора и голубя. Возвращает <c>true</c>, когда курсор только что подошёл
    /// и это стоит заметить: голубь пересмотрит решение (вход <c>CursorApproached</c>).
    /// </summary>
    /// <param name="cursor">Курсор; <c>null</c> — платформа его сейчас не отдаёт.</param>
    /// <param name="pigeon">Голубь; <c>null</c> — его не видно или его несут.</param>
    /// <param name="scale">Масштаб монитора под голубем: расстояния считаются в логических пикселях.</param>
    public bool Observe((double X, double Y)? cursor, (double X, double Y)? pigeon, double scale, TimeSpan now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        if (cursor is not { } point)
        {
            Position = null;
            _lastSeen = null;
            IsNear = false;
            return false;
        }

        if (Position is { } previous && _lastSeen is { } seen && now > seen)
        {
            var moved = Distance(previous, point) / scale;
            var dt = (now - seen).TotalSeconds;
            var k = Math.Min(1.0, (now - seen) / SpeedSmoothing);
            _speed += ((moved / dt) - _speed) * k;
            if (moved >= 1.0)
            {
                _lastMoved = now;
            }
        }

        Position = point;
        _lastSeen = now;

        if (pigeon is not { } body)
        {
            IsNear = false;
            _farSince = null;
            return false;
        }

        _distance = Distance(point, body) / scale;
        if (!IsNear)
        {
            if (_distance > _settings.NoticeRadius)
            {
                return false;
            }

            IsNear = true;
            _farSince = null;
            var notice = _lastNoticed is not { } noticed || now - noticed >= _settings.RenoticeAfter;
            _lastNoticed = now;
            return notice;
        }

        if (_distance <= _settings.LeaveRadius)
        {
            _farSince = null;
            _lastNoticed = now;
            return false;
        }

        _farSince ??= now;
        if (now - _farSince.Value >= _settings.Linger)
        {
            IsNear = false;
            _farSince = null;
        }

        return false;
    }

    private double Attractiveness() =>
        _lastSeen is { } seen && _lastMoved is { } moved && seen - moved <= _settings.Lively ? Lively : Calm;

    private double Risk()
    {
        var risk = BaseRisk + (SpeedRisk * Math.Min(1.0, _speed / _settings.ScarySpeed));
        return Math.Round(risk / RiskStep) * RiskStep;
    }

    private TimeSpan Travel() =>
        TimeSpan.FromSeconds(Math.Round(_distance / WalkSpeed / TravelStepSeconds) * TravelStepSeconds);

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
