using System.Globalization;
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

    private SqlitePetStore(string databasePath, StoreOpenReport report)
    {
        _databasePath = databasePath;
        OpenReport = report;
    }

    /// <summary>Открывает базу, при необходимости создаёт её и применяет миграции.</summary>
    public static ValueTask<SqlitePetStore> OpenAsync(string databasePath, CancellationToken cancellationToken = default) =>
        OpenAsync(databasePath, StoreOpenOptions.Default, cancellationToken);

    /// <summary>
    /// Открывает базу, при необходимости создаёт её и применяет миграции (#17). Временные ошибки
    /// (<c>SQLITE_IOERR</c>, <c>SQLITE_BUSY</c> — например, сразу после аварии, пока система
    /// освобождает файлы) повторяются с растущей паузой. Испорченная база откладывается рядом и
    /// восстанавливается из последней резервной копии, если каталог копий задан; иначе —
    /// <see cref="PetStoreCorruptedException"/>.
    /// </summary>
    public static ValueTask<SqlitePetStore> OpenAsync(string databasePath, StoreOpenOptions options, CancellationToken cancellationToken = default) =>
        OpenAsync(databasePath, options, Task.Delay, cancellationToken);

    internal static async ValueTask<SqlitePetStore> OpenAsync(
        string databasePath,
        StoreOpenOptions options,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentNullException.ThrowIfNull(options);

        var migrator = new SqliteSchemaMigrator(databasePath);
        async ValueTask<bool> Migrate()
        {
            await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        try
        {
            var (_, attempts, errors) = await TransientRetry.RunAsync(Migrate, options.RetryBudget, delay, cancellationToken).ConfigureAwait(false);
            return new SqlitePetStore(databasePath, new StoreOpenReport(attempts, errors));
        }
        catch (SqliteException corruption) when (TransientRetry.IsCorruption(corruption))
        {
            var backup = options.BackupDirectory is { } directory
                ? new SqliteBackupRotator(databasePath, directory).ListBackups().FirstOrDefault()
                : null;
            if (backup is null)
            {
                throw new PetStoreCorruptedException(
                    $"База питомца «{databasePath}» испорчена (код SQLite {corruption.SqliteExtendedErrorCode}), резервной копии нет.",
                    corruption);
            }

            var quarantine = Quarantine(databasePath);
            File.Copy(backup, databasePath);
            var (_, attempts, errors) = await TransientRetry.RunAsync(Migrate, options.RetryBudget, delay, cancellationToken).ConfigureAwait(false);
            return new SqlitePetStore(databasePath, new StoreOpenReport(attempts, errors, backup, quarantine));
        }
    }

    public string DatabasePath => _databasePath;

    /// <summary>Как прошло открытие: повторы и восстановление из копии — для журнала хоста.</summary>
    public StoreOpenReport OpenReport { get; }

    /// <summary>Самый старый хранимый снимок: с него воспроизводится жизнь, когда журнал до него усечён.</summary>
    public ValueTask<SimulationSnapshot?> LoadOldestAsync(PetId petId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM snapshots WHERE pet_id = $pet ORDER BY id LIMIT 1;";
        command.Parameters.AddWithValue("$pet", petId.ToString());

        var payload = command.ExecuteScalar() as string;
        return ValueTask.FromResult(payload is null ? null : SnapshotSerializer.Deserialize(payload));
    }

    /// <summary>
    /// Ограничивает журнал питомца на диске (ТЗ §31, §37; #17): удаляет снимки старше
    /// <see cref="JournalRetention.MaxAge"/> и самые старые снимки, пока журнал от самого старого
    /// оставшегося снимка больше <see cref="JournalRetention.MaxBytes"/>, а затем — записи журнала
    /// до этого снимка. Последний снимок остаётся всегда. Вызывать из фонового потока.
    /// </summary>
    public ValueTask<JournalTrimResult> TrimJournalAsync(PetId petId, JournalRetention retention, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retention);
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_databasePath);
        using var transaction = connection.BeginTransaction(deferred: false);
        var pet = petId.ToString();

        var snapshots = new List<(long Id, long Tick, long JournalLength)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT id, tick, journal_length FROM snapshots WHERE pet_id = $pet ORDER BY id DESC;";
            select.Parameters.AddWithValue("$pet", pet);
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                snapshots.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)));
            }
        }

        if (snapshots.Count == 0)
        {
            transaction.Commit();
            return ValueTask.FromResult(new JournalTrimResult(0, 0, null));
        }

        var latest = snapshots[0];
        var step = LoadLatestPayload(connection, transaction, pet).Options.Step;
        var maxAgeTicks = retention.MaxAge.Ticks / step.Ticks;
        var keep = snapshots.TakeWhile((s, i) => i == 0 || latest.Tick - s.Tick <= maxAgeTicks).Count();
        while (keep > 1 && JournalBytesFrom(connection, transaction, pet, snapshots[keep - 1].JournalLength) > retention.MaxBytes)
        {
            keep--;
        }

        var oldest = snapshots[keep - 1];
        using (var dropSnapshots = connection.CreateCommand())
        {
            dropSnapshots.Transaction = transaction;
            dropSnapshots.CommandText = "DELETE FROM snapshots WHERE pet_id = $pet AND id < $oldest;";
            dropSnapshots.Parameters.AddWithValue("$pet", pet);
            dropSnapshots.Parameters.AddWithValue("$oldest", oldest.Id);
            dropSnapshots.ExecuteNonQuery();
        }

        long entriesRemoved;
        using (var dropJournal = connection.CreateCommand())
        {
            dropJournal.Transaction = transaction;
            dropJournal.CommandText = "DELETE FROM journal WHERE pet_id = $pet AND position < $from;";
            dropJournal.Parameters.AddWithValue("$pet", pet);
            dropJournal.Parameters.AddWithValue("$from", oldest.JournalLength);
            entriesRemoved = dropJournal.ExecuteNonQuery();
        }

        transaction.Commit();
        return ValueTask.FromResult(new JournalTrimResult(snapshots.Count - keep, entriesRemoved, new ReplayStart(oldest.Tick, oldest.JournalLength)));
    }

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

        // BEGIN IMMEDIATE: блокировка записи берётся сразу, до чтения длины журнала (#61).
        using var transaction = connection.BeginTransaction(deferred: false);

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
        using var transaction = connection.BeginTransaction(deferred: false);
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
            "SELECT tick, type, payload FROM journal WHERE pet_id = $pet AND position >= $from ORDER BY position;";
        command.Parameters.AddWithValue("$pet", petId.ToString());
        command.Parameters.AddWithValue("$from", fromPosition);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var payload = reader.IsDBNull(2) ? null : reader.GetString(2);
            yield return new JournalEntry(reader.GetInt64(0), InputEventCodec.Decode(reader.GetString(1), payload));
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
            "INSERT INTO journal (pet_id, position, tick, type, payload) VALUES ($pet, $position, $tick, $type, $payload);";
        var pet = command.Parameters.AddWithValue("$pet", petId.ToString());
        var position = command.Parameters.Add("$position", SqliteType.Integer);
        var tick = command.Parameters.Add("$tick", SqliteType.Integer);
        var type = command.Parameters.Add("$type", SqliteType.Text);
        var payload = command.Parameters.Add("$payload", SqliteType.Text);

        for (var i = 0; i < entries.Count; i++)
        {
            position.Value = firstPosition + i;
            tick.Value = entries[i].Tick;
            var encoded = InputEventCodec.Encode(entries[i].Event);
            type.Value = encoded.Type;
            payload.Value = (object?)encoded.Payload ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Длина журнала с рождения. После усечения записей может не остаться вовсе — тогда длина
    /// берётся из последнего снимка: он сохранён вместе с журналом и не длиннее его.
    /// </summary>
    private static long JournalLength(SqliteConnection connection, SqliteTransaction? transaction, PetId petId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT MAX(
                COALESCE((SELECT MAX(position) + 1 FROM journal WHERE pet_id = $pet), 0),
                COALESCE((SELECT MAX(journal_length) FROM snapshots WHERE pet_id = $pet), 0));
            """;
        command.Parameters.AddWithValue("$pet", petId.ToString());
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>Объём журнала с позиции: данные записей и по 16 байт на ключ и тик.</summary>
    private static long JournalBytesFrom(SqliteConnection connection, SqliteTransaction transaction, string pet, long from)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COALESCE(SUM(length(type) + COALESCE(length(payload), 0) + 16), 0) FROM journal WHERE pet_id = $pet AND position >= $from;";
        command.Parameters.AddWithValue("$pet", pet);
        command.Parameters.AddWithValue("$from", from);
        return (long)command.ExecuteScalar()!;
    }

    private static SimulationSnapshot LoadLatestPayload(SqliteConnection connection, SqliteTransaction transaction, string pet)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT payload FROM snapshots WHERE pet_id = $pet ORDER BY id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$pet", pet);
        return SnapshotSerializer.Deserialize((string)command.ExecuteScalar()!);
    }

    /// <summary>Откладывает испорченную базу и её WAL рядом под именем с временем; возвращает путь.</summary>
    private static string Quarantine(string databasePath)
    {
        var quarantine = databasePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        File.Move(databasePath, quarantine);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(databasePath + suffix))
            {
                File.Move(databasePath + suffix, quarantine + suffix);
            }
        }

        return quarantine;
    }
}
