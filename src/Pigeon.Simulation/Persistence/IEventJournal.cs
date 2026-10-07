using Pigeon.Core;

namespace Pigeon.Simulation.Persistence;

/// <summary>
/// Append-only журнал входов питомца (ТЗ §31, ADR-0017). Позиция записи — её номер с начала
/// жизни питомца; журнал не допускает пропусков и повторов.
/// </summary>
public interface IEventJournal
{
    ValueTask<long> GetLengthAsync(PetId petId, CancellationToken cancellationToken);

    /// <param name="firstPosition">Позиция первой записи; должна совпадать с текущей длиной журнала.</param>
    ValueTask AppendAsync(
        PetId petId,
        long firstPosition,
        IReadOnlyList<JournalEntry> entries,
        CancellationToken cancellationToken);

    IAsyncEnumerable<JournalEntry> ReadAsync(PetId petId, long fromPosition, CancellationToken cancellationToken);
}
