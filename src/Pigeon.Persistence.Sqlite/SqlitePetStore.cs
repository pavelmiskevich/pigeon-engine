using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite.Serialization;
using Pigeon.Simulation;
using Pigeon.Simulation.Persistence;

namespace Pigeon.Persistence.Sqlite;

/// <summary>
/// Снимки и журнал питомца в SQLite (ADR-0017). Каждый метод открывает своё соединение, поэтому
/// экземпляр можно использовать из любого потока; запись — из фонового (<see cref="BackgroundPetWriter"/>).
/// </summary>
public sealed class SqlitePetStore : IPetStore, IEventJournal
{
    /// <summary>Сколько последних снимков хранится на питомца.</summary>
    public const int SnapshotsToKeep = 10;

    private readonly string _databasePath;

    private SqlitePetStore(string databasePath)
    {
        _databasePath = databasePath;
    }

    /// <summary>Открывает базу, при необходимости создаёт её и применяет миграции.</summary>
    public static async ValueTask<SqlitePetStore> OpenAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await new SqliteSchemaMigrator(databasePath).MigrateAsync(cancellationToken).ConfigureAwait(false);
        return new SqlitePetStore(databasePath);
    }

    public string DatabasePath => _databasePath;

    public ValueTask<SimulationSnapshot?> LoadLatestAsync(PetId petId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM snapshots WHERE pet_id = $pet ORDER BY id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$pet", petId.ToString());

        var payload = command.ExecuteScalar() as string;
        return ValueTask.FromResult(payload is null ? null : SnapshotSerializer.Deserialize(payload));
    }

    public ValueTask SaveAsync(
        PetId petId,
        SimulationSnapshot snapshot,
        IReadOnlyList<JournalEntry> newJournalEntries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(newJournalEntries);
        cancellationToken.ThrowIfCancellationRequested();
        snapshot.Validate();

        var firstPosition = snapshot.JournalLength - newJournalEntries.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(firstPosition, nameof(newJournalEntries));
        var payload = SnapshotSerializer.Serialize(snapshot);

        using var connection = SqliteConnections.Open(_databasePath);
        using var transaction = connection.BeginTransaction();

        AppendJournal(connection, transaction, petId, firstPosition, newJournalEntries);

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO snapshots (pet_id, tick, format_version, journal_length, payload)
                VALUES ($pet, $tick, $format, $journal, $payload);
                """;
            insert.Parameters.AddWithValue("$pet", petId.ToString());
            insert.Parameters.AddWithValue("$tick", snapshot.Tick);
            insert.Parameters.AddWithValue("$format", snapshot.FormatVersion);
            insert.Parameters.AddWithValue("$journal", snapshot.JournalLength);
            insert.Parameters.AddWithValue("$payload", payload);
            insert.ExecuteNonQuery();
        }

        using (var prune = connection.CreateCommand())
        {
            prune.Transaction = transaction;
            prune.CommandText =
                """
                DELETE FROM snapshots
                WHERE pet_id = $pet
                  AND id NOT IN (SELECT id FROM snapshots WHERE pet_id = $pet ORDER BY id DESC LIMIT $keep);
                """;
            prune.Parameters.AddWithValue("$pet", petId.ToString());
            prune.Parameters.AddWithValue("$keep", SnapshotsToKeep);
            prune.ExecuteNonQuery();
        }

        transaction.Commit();
        return ValueTask.CompletedTask;
    }

    public ValueTask<long> GetLengthAsync(PetId petId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_databasePath);
        return ValueTask.FromResult(JournalLength(connection, null, petId));
    }

    public ValueTask AppendAsync(
        PetId petId,
        long firstPosition,
        IReadOnlyList<JournalEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_databasePath);
        using var transaction = connection.BeginTransaction();
        AppendJournal(connection, transaction, petId, firstPosition, entries);
        transaction.Commit();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<JournalEntry> ReadAsync(
        PetId petId,
        long fromPosition,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromPosition);

        using var connection = SqliteConnections.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tick, type FROM journal WHERE pet_id = $pet AND position >= $from ORDER BY position;";
        command.Parameters.AddWithValue("$pet", petId.ToString());
        command.Parameters.AddWithValue("$from", fromPosition);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new JournalEntry(reader.GetInt64(0), InputEventCodec.Decode(reader.GetString(1)));
        }
    }

    private static void AppendJournal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PetId petId,
        long firstPosition,
        IReadOnlyList<JournalEntry> entries)
    {
        var length = JournalLength(connection, transaction, petId);
        if (firstPosition != length)
        {
            throw new InvalidOperationException(
                $"Журнал питомца {petId} содержит {length} записей, а новые начинаются с позиции {firstPosition}.");
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO journal (pet_id, position, tick, type) VALUES ($pet, $position, $tick, $type);";
        var pet = command.Parameters.AddWithValue("$pet", petId.ToString());
        var position = command.Parameters.Add("$position", SqliteType.Integer);
        var tick = command.Parameters.Add("$tick", SqliteType.Integer);
        var type = command.Parameters.Add("$type", SqliteType.Text);

        for (var i = 0; i < entries.Count; i++)
        {
            position.Value = firstPosition + i;
            tick.Value = entries[i].Tick;
            type.Value = InputEventCodec.Encode(entries[i].Event);
            command.ExecuteNonQuery();
        }
    }

    private static long JournalLength(SqliteConnection connection, SqliteTransaction? transaction, PetId petId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(position) + 1, 0) FROM journal WHERE pet_id = $pet;";
        command.Parameters.AddWithValue("$pet", petId.ToString());
        return (long)command.ExecuteScalar()!;
    }
}
