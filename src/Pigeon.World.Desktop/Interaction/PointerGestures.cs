namespace Pigeon.World.Desktop.Interaction;

/// <summary>Что сделал пользователь мышью над голубем (ТЗ §11.8).</summary>
public enum GestureKind
{
    /// <summary>Короткий клик — тычок.</summary>
    Poke = 0,

    /// <summary>Курсор потёр голубя туда-сюда — погладил.</summary>
    Pet = 1,

    /// <summary>Нажал и потянул — взял в руки; точка — где нажал.</summary>
    PickUp = 2,

    /// <summary>Несёт; точка — где курсор.</summary>
    Carry = 3,

    /// <summary>Отпустил; точка — где отпустил.</summary>
    Drop = 4,
}

/// <param name="X">Физические пиксели экрана.</param>
public readonly record struct Gesture(GestureKind Kind, double X, double Y);

/// <summary>Пороги жестов; расстояния — физические пиксели экрана.</summary>
/// <param name="DragDistance">Дальше этого от точки нажатия — уже не клик, а перетаскивание.</param>
/// <param name="StrokeDistance">Столько надо провести в одну сторону, чтобы движение считалось мазком.</param>
/// <param name="StrokesToPet">Сколько смен направления подряд — это «погладил».</param>
public sealed record GestureSettings(double DragDistance = 8, double StrokeDistance = 6, int StrokesToPet = 3)
{
    public static GestureSettings Default { get; } = new();

    /// <summary>Смены направления дальше друг от друга во времени — уже не одно поглаживание.</summary>
    public TimeSpan StrokeWindow { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Не чаще одного «погладил» за это время: непрерывное поглаживание — серия, а не поток.</summary>
    public TimeSpan PetEvery { get; init; } = TimeSpan.FromSeconds(1.5);
}

/// <summary>
/// Жесты мышью над голубем (ТЗ §11.8) из событий указателя: клик — тычок, движение туда-сюда без
/// кнопки — поглаживание, нажать и потянуть — взять в руки. Платформа сообщает события только
/// над непрозрачной частью голубя, а во время перетаскивания — везде (захват мыши).
/// </summary>
public sealed class PointerGestures(GestureSettings? settings = null)
{
    private readonly GestureSettings _settings = settings ?? GestureSettings.Default;
    private (double X, double Y)? _pressedAt;
    private bool _carrying;
    private double? _strokeFrom;
    private int _direction;
    private int _strokes;
    private TimeSpan _lastTurn;
    private TimeSpan? _lastPet;

    /// <summary>Голубя несут.</summary>
    public bool IsCarrying => _carrying;

    public IReadOnlyList<Gesture> Press(double x, double y, TimeSpan now)
    {
        _pressedAt = (x, y);
        _carrying = false;
        ResetStrokes();
        return [];
    }

    /// <summary>Курсор сдвинулся: с нажатой кнопкой — тащит, без неё — может гладить.</summary>
    public IReadOnlyList<Gesture> Move(double x, double y, TimeSpan now)
    {
        if (_pressedAt is not { } pressed)
        {
            return Stroke(x, y, now);
        }

        if (_carrying)
        {
            return [new Gesture(GestureKind.Carry, x, y)];
        }

        var dx = x - pressed.X;
        var dy = y - pressed.Y;
        if ((dx * dx) + (dy * dy) <= _settings.DragDistance * _settings.DragDistance)
        {
            return [];
        }

        _carrying = true;
        return [new Gesture(GestureKind.PickUp, pressed.X, pressed.Y), new Gesture(GestureKind.Carry, x, y)];
    }

    public IReadOnlyList<Gesture> Release(double x, double y, TimeSpan now)
    {
        if (_pressedAt is null)
        {
            return [];
        }

        var carried = _carrying;
        _pressedAt = null;
        _carrying = false;
        return [new Gesture(carried ? GestureKind.Drop : GestureKind.Poke, x, y)];
    }

    /// <summary>Платформа отняла мышь (захват потерян): несомого голубя отпускают, клик не засчитывается.</summary>
    public IReadOnlyList<Gesture> Cancel(double x, double y)
    {
        var carried = _carrying;
        _pressedAt = null;
        _carrying = false;
        return carried ? [new Gesture(GestureKind.Drop, x, y)] : [];
    }

    private List<Gesture> Stroke(double x, double y, TimeSpan now)
    {
        if (_strokeFrom is not { } from)
        {
            _strokeFrom = x;
            return [];
        }

        var shift = x - from;
        if (Math.Abs(shift) < _settings.StrokeDistance)
        {
            return [];
        }

        var direction = Math.Sign(shift);
        _strokeFrom = x;
        if (direction == _direction)
        {
            return [];
        }

        _strokes = _direction != 0 && now - _lastTurn <= _settings.StrokeWindow ? _strokes + 1 : 0;
        _direction = direction;
        _lastTurn = now;

        if (_strokes < _settings.StrokesToPet || (_lastPet is { } pet && now - pet < _settings.PetEvery))
        {
            return [];
        }

        _lastPet = now;
        _strokes = 0;
        return [new Gesture(GestureKind.Pet, x, y)];
    }

    private void ResetStrokes()
    {
        _strokeFrom = null;
        _direction = 0;
        _strokes = 0;
    }
}
