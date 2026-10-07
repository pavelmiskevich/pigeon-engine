using Pigeon.Core;

namespace Pigeon.Simulation.Persistence;

/// <summary>
/// Хранилище снимков питомца (ТЗ §31, ADR-0017). Загрузка — последний снимок; журнал после него
/// читается через <see cref="IEventJournal"/>.
/// </summary>
public interface IPetStore
{
    ValueTask<SimulationSnapshot?> LoadLatestAsync(PetId petId, CancellationToken cancellationToken);

    /// <summary>
    /// Сохраняет снимок и записи журнала, появившиеся после предыдущего сохранения, одной
    /// транзакцией: после аварии в хранилище остаётся либо всё, либо ничего.
    /// </summary>
    /// <param name="newJournalEntries">
    /// Записи с позиции <c>snapshot.JournalLength - newJournalEntries.Count</c>; эта позиция
    /// должна совпадать с текущей длиной журнала в хранилище.
    /// </param>
    ValueTask SaveAsync(
        PetId petId,
        SimulationSnapshot snapshot,
        IReadOnlyList<JournalEntry> newJournalEntries,
        CancellationToken cancellationToken);
}
