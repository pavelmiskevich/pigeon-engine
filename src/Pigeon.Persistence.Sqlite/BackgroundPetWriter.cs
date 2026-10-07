using System.Threading.Channels;
using Pigeon.Core;
using Pigeon.Simulation;
using Pigeon.Simulation.Persistence;

namespace Pigeon.Persistence.Sqlite;

/// <summary>
/// Пишет снимки вне потока симуляции (ТЗ §31): <see cref="Enqueue"/> не блокирует, запись идёт
/// по одной в фоновой задаче в порядке постановки. После первой ошибки запись останавливается —
/// продолжать нельзя, следующие порции журнала уже не стыкуются; ошибка всплывает в
/// <see cref="FlushAsync"/> и <see cref="DisposeAsync"/>.
/// </summary>
public sealed class BackgroundPetWriter : IAsyncDisposable
{
    private readonly IPetStore _store;
    private readonly PetId _petId;
    private readonly Channel<Request> _requests =
        Channel.CreateUnbounded<Request>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    private Exception? _failure;

    public BackgroundPetWriter(IPetStore store, PetId petId)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _petId = petId;
        _worker = Task.Run(WorkAsync);
    }

    /// <summary>Ставит снимок и новые записи журнала в очередь на запись.</summary>
    public void Enqueue(SimulationSnapshot snapshot, IReadOnlyList<JournalEntry> newJournalEntries)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(newJournalEntries);

        if (!_requests.Writer.TryWrite(new Request(snapshot, [.. newJournalEntries], null)))
        {
            throw new ObjectDisposedException(nameof(BackgroundPetWriter));
        }
    }

    /// <summary>Ждёт, пока запишется всё, что поставлено в очередь до вызова.</summary>
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

        if (_failure is not null)
        {
            throw new InvalidOperationException("Фоновая запись питомца завершилась с ошибкой.", _failure);
        }
    }

    private async Task WorkAsync()
    {
        await foreach (var request in _requests.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (request.Flushed is { } flushed)
            {
                if (_failure is null)
                {
                    flushed.SetResult();
                }
                else
                {
                    flushed.SetException(new InvalidOperationException("Фоновая запись питомца завершилась с ошибкой.", _failure));
                }

                continue;
            }

            if (_failure is not null)
            {
                continue;
            }

            try
            {
                await _store.SaveAsync(_petId, request.Snapshot!, request.Entries, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Ошибка сохраняется и всплывает в FlushAsync/DisposeAsync.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _failure = exception;
            }
        }
    }

    private sealed record Request(SimulationSnapshot? Snapshot, IReadOnlyList<JournalEntry> Entries, TaskCompletionSource? Flushed);
}
