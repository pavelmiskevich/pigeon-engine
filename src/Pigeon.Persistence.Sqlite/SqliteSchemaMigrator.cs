using Pigeon.Simulation.Persistence;

namespace Pigeon.Persistence.Sqlite;

/// <summary>
/// Миграции схемы SQLite (ADR-0017). Версия хранится в <c>PRAGMA user_version</c>, каждая
/// миграция выполняется вместе с повышением версии в одной транзакции.
/// </summary>
public sealed class SqliteSchemaMigrator : ISchemaMigrator
{
    /// <summary>Миграции по порядку: индекс + 1 = версия схемы после миграции.</summary>
    private static readonly string[] Migrations =
    [
        // v1: снимки и журнал входов.
        """
        CREATE TABLE snapshots (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            pet_id TEXT NOT NULL,
            tick INTEGER NOT NULL,
            format_version INTEGER NOT NULL,
            payload TEXT NOT NULL
        );
        CREATE TABLE journal (
            pet_id TEXT NOT NULL,
            position INTEGER NOT NULL,
            tick INTEGER NOT NULL,
            type TEXT NOT NULL,
            PRIMARY KEY (pet_id, position)
        ) WITHOUT ROWID;
        """,

        // v2: позиция журнала у снимка — чтобы находить хвост журнала без разбора JSON.
        // Для существующих строк значение переносится из payload.
        """
        ALTER TABLE snapshots ADD COLUMN journal_length INTEGER NOT NULL DEFAULT 0;
        UPDATE snapshots SET journal_length = COALESCE(json_extract(payload, '$.journalLength'), 0);
        CREATE INDEX ix_snapshots_pet ON snapshots (pet_id, id);
        """,
    ];

    private readonly string _databasePath;

    public SqliteSchemaMigrator(string databasePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        _databasePath = databasePath;
    }

    public static int LatestVersion => Migrations.Length;

    public int CurrentVersion => LatestVersion;

    public ValueTask<int> GetVersionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = SqliteConnections.Open(_databasePath);
        return ValueTask.FromResult(SqliteConnections.Scalar<int>(connection, "PRAGMA user_version;"));
    }

    public ValueTask MigrateAsync(CancellationToken cancellationToken)
    {
        using var connection = SqliteConnections.Open(_databasePath);
        var version = SqliteConnections.Scalar<int>(connection, "PRAGMA user_version;");

        if (version > LatestVersion)
        {
            throw new NotSupportedException(
                $"База создана более новой версией приложения (схема {version}, поддерживается до {LatestVersion}).");
        }

        for (var next = version + 1; next <= LatestVersion; next++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var transaction = connection.BeginTransaction();
            SqliteConnections.Execute(connection, Migrations[next - 1], transaction);
            SqliteConnections.Execute(connection, $"PRAGMA user_version = {next};", transaction);
            transaction.Commit();
        }

        return ValueTask.CompletedTask;
    }
}
