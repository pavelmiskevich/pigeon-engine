using Microsoft.Data.Sqlite;

namespace Pigeon.Persistence.Sqlite;

/// <summary>Открытие соединений с едиными настройками надёжности.</summary>
internal static class SqliteConnections
{
    public static SqliteConnection Open(string databasePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Без пула: файл не остаётся открытым после Dispose, его можно копировать и удалять.
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        // WAL переживает аварийное завершение процесса без порчи файла; FULL — подтверждённая
        // транзакция не теряется и при отключении питания. Пишем редко, цена не важна.
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA synchronous = FULL;");
        Execute(connection, "PRAGMA busy_timeout = 5000;");
        return connection;
    }

    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }

    public static T Scalar<T>(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
