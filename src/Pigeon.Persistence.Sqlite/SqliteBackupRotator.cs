using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pigeon.Persistence.Sqlite;

/// <summary>
/// Ежедневные резервные копии базы через SQLite backup API (ТЗ §31): копия согласована даже
/// во время записи. Хранятся последние <see cref="Keep"/> копий.
/// </summary>
/// <remarks>
/// Копируется только существующая база питомца со схемой: иначе ротация вытеснила бы настоящие
/// копии пустыми (#59). Старые копии удаляются только после того, как новая создана и проверена.
/// </remarks>
public sealed class SqliteBackupRotator
{
    public const int DefaultKeep = 7;

    private const string FilePrefix = "pigeon-";
    private const string FileExtension = ".db";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public SqliteBackupRotator(string databasePath, string backupDirectory, int keep = DefaultKeep)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        ArgumentException.ThrowIfNullOrEmpty(backupDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keep);

        _databasePath = databasePath;
        _backupDirectory = backupDirectory;
        Keep = keep;
    }

    public int Keep { get; }

    /// <summary>Копии от новой к старой. Имя содержит время UTC, поэтому порядок имён — хронологический.</summary>
    public IReadOnlyList<string> ListBackups() =>
        Directory.Exists(_backupDirectory)
            ? [.. Directory.EnumerateFiles(_backupDirectory, FilePrefix + "*" + FileExtension)
                .Where(f => TryParseTimestamp(f, out _))
                .OrderDescending(StringComparer.Ordinal)]
            : [];

    /// <summary>Делает копию, если за текущие сутки (UTC) её ещё нет. Возвращает путь новой копии.</summary>
    public string? BackupIfDue(DateTimeOffset now)
    {
        var today = now.UtcDateTime.Date;
        var latest = ListBackups().FirstOrDefault();
        if (latest is not null && TryParseTimestamp(latest, out var latestTime) && latestTime.Date >= today)
        {
            return null;
        }

        return CreateBackup(now);
    }

    /// <exception cref="IOException">Базы нет, она пустая или копия не прошла проверку.</exception>
    public string CreateBackup(DateTimeOffset now)
    {
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException("Базы питомца нет — копировать нечего.", _databasePath);
        }

        Directory.CreateDirectory(_backupDirectory);
        RemoveLeftovers();
        var name = FilePrefix + now.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture) + FileExtension;
        var path = Path.Combine(_backupDirectory, name);
        var temporary = path + ".tmp";

        try
        {
            // Источник — только для чтения: так SQLite не создаст пустую базу на месте пропавшей.
            using (var source = OpenReadOnly(_databasePath))
            {
                var schema = SqliteConnections.Scalar<int>(source, "PRAGMA user_version;");
                if (schema == 0)
                {
                    throw new IOException($"«{_databasePath}» — не база питомца: схемы нет.");
                }

                using var destination = SqliteConnections.Open(temporary);
                source.BackupDatabase(destination);
                // Копия — самостоятельный файл, без WAL рядом.
                SqliteConnections.Execute(destination, "PRAGMA journal_mode = DELETE;");
                if (SqliteConnections.Scalar<int>(destination, "PRAGMA user_version;") != schema)
                {
                    throw new IOException($"Копия «{temporary}» не совпала с базой по версии схемы.");
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }

        Prune();
        return path;
    }

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch (SqliteException e)
        {
            connection.Dispose();
            throw new IOException($"Базу «{databasePath}» не открыть для копирования: {e.Message}", e);
        }
    }

    /// <summary>Временные файлы прошлых копий, оборванных аварией.</summary>
    private void RemoveLeftovers()
    {
        foreach (var leftover in Directory.EnumerateFiles(_backupDirectory, FilePrefix + "*" + FileExtension + ".tmp"))
        {
            File.Delete(leftover);
        }
    }

    private void Prune()
    {
        foreach (var stale in ListBackups().Skip(Keep))
        {
            File.Delete(stale);
        }
    }

    private static bool TryParseTimestamp(string path, out DateTime timestamp)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        timestamp = default;
        return name.StartsWith(FilePrefix, StringComparison.Ordinal)
            && DateTime.TryParseExact(
                name[FilePrefix.Length..],
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out timestamp);
    }
}
