using Microsoft.Data.Sqlite;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;

namespace Pigeon.Persistence.Tests;

/// <summary>Конкурентный доступ к базе: несколько соединений одного и разных процессов (#60, #61).</summary>
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task OpeningWhileAnotherConnectionWrites_WaitsInsteadOfFailing()
    {
        // #60: переход в WAL требует блокировки файла; без busy_timeout до него — SQLITE_BUSY сразу.
        using var db = new TestDatabase();
        using var writer = Raw(db.Path);
        Execute(writer, "PRAGMA journal_mode = DELETE; CREATE TABLE t (x INTEGER);");
        using var transaction = writer.BeginTransaction();
        Execute(writer, "INSERT INTO t VALUES (1);", transaction);

        var release = Task.Run(async () =>
        {
            await Task.Delay(300, TestContext.Current.CancellationToken);
            transaction.Commit();
        }, TestContext.Current.CancellationToken);

        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
        await release;

        Assert.Equal(0, await store.GetLengthAsync(new PetId(Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.Equal("wal", TestDatabase.Query(db.Path, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task FailedOpen_DoesNotKeepTheFileOpen()
    {
        // #60: соединение, на котором упала прагма, закрывается — файл можно удалить или заменить.
        using var db = new TestDatabase();
        await File.WriteAllTextAsync(db.Path, new string('x', 4096), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<PetStoreCorruptedException>(async () => await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken));

        File.Delete(db.Path);
        Assert.False(File.Exists(db.Path));
    }

    [Fact]
    public void WriteTransactions_TakeTheWriteLockAtBegin()
    {
        // #61: транзакция хранилища — BEGIN IMMEDIATE: второй писатель ждёт, а не ловит
        // SQLITE_BUSY_SNAPSHOT после чтения. Проверяем, что драйвер так и открывает транзакцию.
        using var db = new TestDatabase();
        using var first = Raw(db.Path);
        Execute(first, "PRAGMA journal_mode = WAL; CREATE TABLE t (x INTEGER);");
        // Драйвер сам повторяет SQLITE_BUSY до DefaultTimeout (по умолчанию 30 с): здесь — 1 с.
        using var second = Raw(db.Path, timeoutSeconds: 1);
        Execute(second, "PRAGMA busy_timeout = 0;");

        using var transaction = first.BeginTransaction(deferred: false);

        var busy = Assert.Throws<SqliteException>(() => Execute(second, "INSERT INTO t VALUES (1);"));
        Assert.Equal(5, busy.SqliteErrorCode); // SQLITE_BUSY
    }

    [Fact]
    public async Task ParallelFirstOpens_MigrateOnce()
    {
        // #61: версия схемы читается внутри той же write-транзакции, что и миграция: два процесса,
        // открывшие новую базу одновременно, не применяют одну миграцию дважды.
        using var db = new TestDatabase();

        var opens = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(async () => await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken))
            .ToArray();
        await Task.WhenAll(opens);

        Assert.Equal(SqliteSchemaMigrator.LatestVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), TestDatabase.Query(db.Path, "PRAGMA user_version;"));
    }

    private static SqliteConnection Raw(string path, int timeoutSeconds = 30)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = timeoutSeconds }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }
}
