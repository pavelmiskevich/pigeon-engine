using System.Globalization;

namespace Pigeon.Persistence.Sqlite;

/// <summary>
/// Ежедневные резервные копии базы через SQLite backup API (ТЗ §31): копия согласована даже
/// во время записи. Хранятся последние <see cref="Keep"/> копий.
/// </summary>
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

    public string CreateBackup(DateTimeOffset now)
    {
        Directory.CreateDirectory(_backupDirectory);
        var name = FilePrefix + now.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture) + FileExtension;
        var path = Path.Combine(_backupDirectory, name);
        var temporary = path + ".tmp";

        using (var source = SqliteConnections.Open(_databasePath))
        using (var destination = SqliteConnections.Open(temporary))
        {
            source.BackupDatabase(destination);
            // Копия — самостоятельный файл, без WAL рядом.
            SqliteConnections.Execute(destination, "PRAGMA journal_mode = DELETE;");
        }

        File.Move(temporary, path, overwrite: true);
        Prune();
        return path;
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
