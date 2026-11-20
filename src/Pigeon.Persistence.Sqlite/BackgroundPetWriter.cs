using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Pigeon.Core;
using Pigeon.Simulation;
using Pigeon.Simulation.Persistence;

namespace Pigeon.Persistence.Sqlite;

/// <summary>Сохранение не удалось; запись продолжится, если ошибка временная.</summary>
/// <param name="Attempt">Какая подряд неудачная попытка — с 1.</param>
/// <param name="WillRetry">Писатель повторит сохранение сам; <c>false</c> — запись остановлена.</param>
public sealed record PetWriteFailure(Exception Error, int Attempt, bool WillRetry);

/// <summary>Повтор неудачного сохранения и ограничение журнала.</summary>
/// <param name="FirstRetry">Пауза перед первым повтором; дальше она удваивается до <paramref name="LongestRetry"/>.</param>
/// <param name="Retention">Как ограничивать журнал на диске (#17); <c>null</c> — не ограничивать.</param>
/// <param name="TrimEvery">Журнал усекается после каждого такого числа удачных сохранений.</param>
public sealed record PetWriterOptions(TimeSpan FirstRetry, TimeSpan LongestRetry, JournalRetention? Retention = null, int TrimEvery = 60)
{
    public static PetWriterOptions Default { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), JournalRetention.Default);
}

/// <summary>
/// Пишет снимки вне потока симуляции (ТЗ §31): <see cref="Enqueue"/> не блокирует, запись идёт
/// в фоновой задаче. Если в очереди несколько снимков, пишется последний вместе со всеми
/// накопленными записями журнала.
/// </summary>
/// <remarks>
/// <para>
/// Записи журнала, которые ещё не подтверждены базой, писатель хранит у себя. Если сохранение
/// не удалось, он сразу сообщает об этом (<see cref="Failed"/>), сверяет длину журнала с базой
/// (<see cref="IEventJournal.GetLengthAsync"/>) и повторяет сохранение последнего снимка со всеми
/// неподтверждёнными записями, пока оно не удастся или не придёт новый снимок (#51).
/// </para>
/// <para>
/// Запись останавливается только на ошибке, которая сама не пройдёт: порча базы, разрыв в
/// журнале. Тогда ошибка всплывает в <see cref="FlushAsync"/> и <see cref="DisposeAsync"/>.
/// </para>
/// </remarks>
public sealed class BackgroundPetWriter : IAsyncDisposable
{
    private readonly IPetStore _store;
    private readonly IEventJournal _journal;
    private readonly PetId _petId;
    private readonly PetWriterOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<JournalRetention, ValueTask>? _trim;
    private readonly Channel<Request> _requests =
        Channel.CreateUnbounded<Request>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    private readonly List<JournalEntry> _unsaved = [];
    private long _unsavedFrom = -1;
    private SimulationSnapshot? _latest;
    private long _confirmed;
    private Exception? _stopped;
    private int _savesSinceTrim;

    /// <summary>Писатель в базу SQLite; журнал усекается по <see cref="PetWriterOptions.Retention"/>.</summary>
    public BackgroundPetWriter(SqlitePetStore store, PetId petId, PetWriterOptions? options = null)
        : this(store, store, petId, options, Task.Delay, async retention => await store.TrimJournalAsync(petId, retention, CancellationToken.None).ConfigureAwait(false))
    {
    }

    internal BackgroundPetWriter(
        IPetStore store,
        IEventJournal journal,
        PetId petId,
        PetWriterOptions? options,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<JournalRetention, ValueTask>? trim = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(delay);

        _store = store;
        _journal = journal;
        _petId = petId;
        _options = options ?? PetWriterOptions.Default;
        _delay = delay;
        _trim = trim;
        _worker = Task.Run(WorkAsync);
    }

    /// <summary>
    /// Сохранение не удалось. Вызывается из фонового потока: обработчик не должен блокировать и
    /// трогать симуляцию — только записать в журнал хоста и показать состояние.
    /// </summary>
    public event EventHandler<PetWriteFailure>? Failed;

    /// <summary>
    /// Длина журнала, подтверждённая базой: записи до этой позиции сохранены, и симуляция может
    /// их забыть (#50). Читается из любого потока.
    /// </summary>
    public long ConfirmedLength => Interlocked.Read(ref _confirmed);

    /// <summary>
    /// Ставит снимок и новые записи журнала в очередь на запись. Записи продолжают те, что были
    /// поставлены раньше: с позиции <c>snapshot.JournalLength - newJournalEntries.Count</c>.
    /// </summary>
    public void Enqueue(SimulationSnapshot snapshot, IReadOnlyList<JournalEntry> newJournalEntries)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(newJournalEntries);

        if (!_requests.Writer.TryWrite(new Request(snapshot, [.. newJournalEntries], null)))
        {
            throw new ObjectDisposedException(nameof(BackgroundPetWriter));
        }
    }

    /// <summary>
    /// Ждёт, пока запишется всё, что поставлено в очередь до вызова. Если последнее сохранение не
    /// удалось, не ждёт повтора, а бросает его ошибку.
    /// </summary>
    public async Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_requests.Writer.TryWrite(new Request(null, [], done)))
        {
            throw new ObjectDisposedException(nameof(BackgroundPetWriter));
        }

        await done.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        await _worker.ConfigureAwait(false);

        if (_stopped is not null)
        {
            throw new InvalidOperationException("Фоновая запись питомца остановлена ошибкой.", _stopped);
        }

        if (_latest is not null)
        {
            throw new InvalidOperationException("Фоновая запись питомца не сохранила последний снимок.");
        }
    }

    private async Task WorkAsync()
    {
        var reader = _requests.Reader;
        var failures = 0;
        Exception? lastFailure = null;
        while (true)
        {
            // Есть несохранённое — ждём нового запроса не дольше паузы до повтора.
            if (_latest is not null && _stopped is null && failures > 0)
            {
                var pause = Pause(failures);
                using var timeout = new CancellationTokenSource();
                var wait = reader.WaitToReadAsync(timeout.Token).AsTask();
                var delay = _delay(pause, timeout.Token);
                var first = await Task.WhenAny(wait, delay).ConfigureAwait(false);
                await timeout.CancelAsync().ConfigureAwait(false);
                if (first == delay)
                {
                    (failures, lastFailure) = await SaveAsync(failures).ConfigureAwait(false);
                    continue;
                }

                if (!await IgnoreCancellation(wait).ConfigureAwait(false))
                {
                    // Очередь закрыта: последняя попытка перед выходом.
                    await SaveAsync(failures).ConfigureAwait(false);
                    return;
                }
            }
            else if (!await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                return;
            }

            // Всё, что накопилось в очереди, — одно сохранение последнего снимка.
            var flushes = new List<TaskCompletionSource>();
            while (reader.TryRead(out var request))
            {
                if (request.Flushed is { } flushed)
                {
                    flushes.Add(flushed);
                }
                else if (_stopped is null)
                {
                    Accept(request);
                }
            }

            if (_latest is not null && _stopped is null)
            {
                (failures, lastFailure) = await SaveAsync(failures).ConfigureAwait(false);
            }

            foreach (var flushed in flushes)
            {
                var error = _stopped ?? (_latest is not null ? lastFailure : null);
                if (error is null)
                {
                    flushed.SetResult();
                }
                else
                {
                    flushed.SetException(new InvalidOperationException("Фоновая запись питомца завершилась с ошибкой.", error));
                }
            }
        }
    }

    /// <summary>Добавляет записи запроса к неподтверждённым и запоминает снимок.</summary>
    private void Accept(Request request)
    {
        var snapshot = request.Snapshot!;
        var from = snapshot.JournalLength - request.Entries.Count;
        if (_unsavedFrom < 0)
        {
            _unsavedFrom = from;
        }

        var end = _unsavedFrom + _unsaved.Count;
        if (from > end)
        {
            Stop(new InvalidOperationException($"Записи журнала с позиции {from} не продолжают поставленные раньше (до {end})."));
            return;
        }

        _unsaved.AddRange(request.Entries.Skip((int)(end - from)));
        _latest = snapshot;
    }

    /// <returns>Неудач подряд после этой попытки и последняя ошибка.</returns>
    private async Task<(int Failures, Exception? Last)> SaveAsync(int failures)
    {
        var snapshot = _latest!;
        try
        {
            var stored = await _journal.GetLengthAsync(_petId, CancellationToken.None).ConfigureAwait(false);
            if (stored < _unsavedFrom)
            {
                Stop(new InvalidOperationException($"В базе {stored} записей журнала, а несохранённые начинаются с позиции {_unsavedFrom}."));
                return (failures, _stopped);
            }

            // База могла подтвердить часть записей раньше, чем пришёл ответ: они уже сохранены.
            var skip = (int)Math.Min(stored - _unsavedFrom, _unsaved.Count);
            _unsaved.RemoveRange(0, skip);
            _unsavedFrom += skip;

            await _store.SaveAsync(_petId, snapshot, _unsaved, CancellationToken.None).ConfigureAwait(false);
            _unsavedFrom = snapshot.JournalLength;
            _unsaved.Clear();
            _latest = null;
            Interlocked.Exchange(ref _confirmed, snapshot.JournalLength);
            await TrimIfDueAsync().ConfigureAwait(false);
            return (0, null);
        }
#pragma warning disable CA1031 // Ошибка передаётся хосту через Failed и повторяется.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (IsTransient(exception))
            {
                Failed?.Invoke(this, new PetWriteFailure(exception, failures + 1, WillRetry: true));
            }
            else
            {
                Stop(exception, failures + 1);
            }

            return (failures + 1, exception);
        }
    }

    /// <summary>
    /// Ограничение журнала — отдельной транзакцией после сохранения, в том же фоновом потоке.
    /// Неудача не останавливает запись: усечение повторится через <see cref="PetWriterOptions.TrimEvery"/> сохранений.
    /// </summary>
    private async Task TrimIfDueAsync()
    {
        if (_trim is null || _options.Retention is not { } retention || ++_savesSinceTrim < _options.TrimEvery)
        {
            return;
        }

        _savesSinceTrim = 0;
        try
        {
            await _trim(retention).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Сохранение уже удалось; об ошибке усечения хост узнаёт через Failed.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Failed?.Invoke(this, new PetWriteFailure(exception, 1, WillRetry: true));
        }
    }

    /// <summary>Пройдёт само: файл занят, база временно недоступна. Порча и разрыв журнала — нет.</summary>
    private static bool IsTransient(Exception exception) => exception switch
    {
        SqliteException sqlite => !TransientRetry.IsCorruption(sqlite),
        IOException => true,
        _ => false,
    };

    /// <summary>Останавливает запись и сообщает хосту — один раз.</summary>
    private void Stop(Exception error, int attempt = 1)
    {
        if (_stopped is not null)
        {
            return;
        }

        _stopped = error;
        Failed?.Invoke(this, new PetWriteFailure(error, attempt, WillRetry: false));
    }

    private TimeSpan Pause(int failures)
    {
        var pause = _options.FirstRetry * Math.Pow(2, Math.Min(failures - 1, 16));
        return pause < _options.LongestRetry ? pause : _options.LongestRetry;
    }

    private static async Task<bool> IgnoreCancellation(Task<bool> wait)
    {
        try
        {
            return await wait.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private sealed record Request(SimulationSnapshot? Snapshot, IReadOnlyList<JournalEntry> Entries, TaskCompletionSource? Flushed);
}
