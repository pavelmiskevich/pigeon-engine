namespace Pigeon.Desktop;

/// <summary>
/// Симуляция упала посреди тика: её состояние могло измениться наполовину, продолжать нельзя.
/// Граница восстановления (<see cref="FaultBoundary"/>) такие ошибки не ловит.
/// </summary>
internal sealed class SimulationFailedException(Exception inner)
    : Exception("Симуляция голубя остановлена ошибкой.", inner);

/// <summary>
/// Граница восстановления цикла хоста (#52): ошибка одного кадра — в локомоции, отрисовке,
/// опросе мира — не убивает голубя. Цикл ждёт с растущей паузой и пробует снова; после
/// <see cref="GiveUpAfter"/> неудач подряд — выход с записью в журнал.
/// </summary>
/// <remarks>
/// Одинаковые ошибки пишутся в журнал не каждый раз, а на 1, 2, 4, 8… повторе: если кадр
/// падает через раз годами, журнал всё равно растёт медленно.
/// </remarks>
internal sealed class FaultBoundary(HostLog log, int giveUpAfter = FaultBoundary.DefaultGiveUpAfter)
{
    public const int DefaultGiveUpAfter = 10;

    private static readonly TimeSpan FirstPause = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongestPause = TimeSpan.FromSeconds(1);

    private string? _signature;
    private long _repeats;

    public int GiveUpAfter { get; } = giveUpAfter > 0 ? giveUpAfter : throw new ArgumentOutOfRangeException(nameof(giveUpAfter));

    /// <summary>Неудач подряд — с последнего удачного кадра.</summary>
    public int Consecutive { get; private set; }

    /// <summary>Сколько ждать перед следующей попыткой после неудачи.</summary>
    public TimeSpan Pause
    {
        get
        {
            if (Consecutive == 0)
            {
                return TimeSpan.Zero;
            }

            var pause = FirstPause * Math.Pow(2, Math.Min(Consecutive - 1, 16));
            return pause < LongestPause ? pause : LongestPause;
        }
    }

    /// <summary>Ловит ли граница эту ошибку: всё, кроме падения симуляции.</summary>
    public static bool Recoverable(Exception error) => error is not SimulationFailedException and not OutOfMemoryException;

    /// <summary>Кадр прошёл: счёт неудач подряд сбрасывается.</summary>
    public void Succeeded() => Consecutive = 0;

    /// <summary>Кадр упал. Возвращает <c>false</c>, если пора сдаваться.</summary>
    public bool Failed(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Consecutive++;
        var signature = Signature(error);
        _repeats = signature == _signature ? _repeats + 1 : 1;
        _signature = signature;

        var giveUp = Consecutive >= GiveUpAfter;
        if (giveUp)
        {
            log.Error($"Кадр голубя упал {Consecutive} раз подряд — выход.", error);
        }
        else if ((_repeats & (_repeats - 1)) == 0)
        {
            log.Warn(
                _repeats == 1 ? "Кадр голубя упал, голубь продолжает жить." : $"Кадр голубя снова упал той же ошибкой (уже {_repeats} раз).",
                error);
        }

        return !giveUp;
    }

    /// <summary>Тип и место ошибки — чтобы узнавать повторы, не сравнивая сообщения с данными.</summary>
    private static string Signature(Exception error)
    {
        var top = new System.Diagnostics.StackTrace(error, fNeedFileInfo: false).GetFrame(0)?.GetMethod();
        return $"{error.GetType().FullName}@{top?.DeclaringType?.FullName}.{top?.Name}";
    }
}
