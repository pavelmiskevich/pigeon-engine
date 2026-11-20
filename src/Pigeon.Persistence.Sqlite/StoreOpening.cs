using Microsoft.Data.Sqlite;

namespace Pigeon.Persistence.Sqlite;

/// <summary>Как открывать базу питомца (#17).</summary>
/// <param name="BackupDirectory">
/// Каталог резервных копий <see cref="SqliteBackupRotator"/>: если база испорчена, она
/// откладывается рядом, а на её место встаёт последняя копия. <c>null</c> — не восстанавливать.
/// </param>
/// <param name="RetryFor">Сколько всего ждать, пока временная ошибка открытия пройдёт.</param>
public sealed record StoreOpenOptions(string? BackupDirectory = null, TimeSpan? RetryFor = null)
{
    public static StoreOpenOptions Default { get; } = new();

    internal TimeSpan RetryBudget => RetryFor ?? TimeSpan.FromSeconds(10);
}

/// <summary>Как прошло открытие базы — для журнала хоста и Developer Mode.</summary>
/// <param name="Attempts">Сколько попыток понадобилось; 1 — открылась сразу.</param>
/// <param name="TransientErrors">Расширенные коды SQLite временных ошибок, после которых был повтор.</param>
/// <param name="RestoredFrom">Из какой резервной копии восстановлена испорченная база.</param>
/// <param name="QuarantinedTo">Куда отложена испорченная база.</param>
public sealed record StoreOpenReport(
    int Attempts,
    IReadOnlyList<int> TransientErrors,
    string? RestoredFrom = null,
    string? QuarantinedTo = null);

/// <summary>База питомца испорчена, и восстановить её не из чего.</summary>
public sealed class PetStoreCorruptedException(string message, Exception inner) : IOException(message, inner);

/// <summary>Повтор открытия при временных ошибках SQLite (#17).</summary>
internal static class TransientRetry
{
    private const int Busy = 5;
    private const int Locked = 6;
    private const int IoError = 10;
    private const int Corrupt = 11;
    private const int NotADatabase = 26;

    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan LongestDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Временная ошибка: файл держит система или антивирус после аварии, другой процесс пишет.
    /// Такое проходит само; порча файла (<c>SQLITE_CORRUPT</c>, <c>SQLITE_NOTADB</c>) — нет.
    /// </summary>
    public static bool IsTransient(SqliteException exception) => exception.SqliteErrorCode is Busy or Locked or IoError;

    public static bool IsCorruption(SqliteException exception) => exception.SqliteErrorCode is Corrupt or NotADatabase;

    /// <summary>
    /// Выполняет <paramref name="attempt"/>, повторяя его при временных ошибках с растущей паузой,
    /// пока не выйдет <paramref name="budget"/>. Возвращает результат, число попыток и коды ошибок.
    /// </summary>
    public static async ValueTask<(T Result, int Attempts, IReadOnlyList<int> Errors)> RunAsync<T>(
        Func<ValueTask<T>> attempt,
        TimeSpan budget,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        var errors = new List<int>();
        var waited = TimeSpan.Zero;
        var next = FirstDelay;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return (await attempt().ConfigureAwait(false), errors.Count + 1, errors);
            }
            catch (SqliteException exception) when (IsTransient(exception) && waited + next <= budget)
            {
                errors.Add(exception.SqliteExtendedErrorCode);
            }

            await delay(next, cancellationToken).ConfigureAwait(false);
            waited += next;
            next = next * 2 < LongestDelay ? next * 2 : LongestDelay;
        }
    }
}

/// <summary>Сколько журнала хранить на диске (ТЗ §31, §37; #17).</summary>
/// <param name="MaxBytes">Предел объёма журнала питомца — данные записей, без служебных страниц SQLite.</param>
/// <param name="MaxAge">Снимки старше этого (по времени симуляции от последнего) удаляются, а с ними — журнал до них.</param>
/// <remarks>
/// Последний снимок не удаляется никогда, поэтому загрузка «последний снимок + хвост журнала»
/// после усечения даёт то же состояние. Воспроизвести жизнь можно с самого старого оставшегося
/// снимка (<see cref="SqlitePetStore.LoadOldestAsync"/>), а не с рождения.
/// </remarks>
public sealed record JournalRetention(long MaxBytes, TimeSpan MaxAge)
{
    /// <summary>ТЗ §37: журнал не больше 50 МБ. Возраст ТЗ не задаёт — 30 суток.</summary>
    public static JournalRetention Default { get; } = new(50L * 1024 * 1024, TimeSpan.FromDays(30));
}

/// <summary>Что сделало усечение журнала.</summary>
/// <param name="SnapshotsRemoved">Удалено снимков: старше предела возраста или сверх предела объёма.</param>
/// <param name="EntriesRemoved">Удалено записей журнала до самого старого оставшегося снимка.</param>
/// <param name="ReplayFrom">С какого снимка теперь доступно воспроизведение; <c>null</c> — снимков нет.</param>
public sealed record JournalTrimResult(int SnapshotsRemoved, long EntriesRemoved, ReplayStart? ReplayFrom);

/// <summary>Самый старый хранимый снимок: с него воспроизводится жизнь питомца.</summary>
/// <param name="JournalPosition">Позиция журнала, с которой читать хвост для воспроизведения.</param>
public sealed record ReplayStart(long Tick, long JournalPosition);
