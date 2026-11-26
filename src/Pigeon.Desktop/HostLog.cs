using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pigeon.Desktop;

/// <summary>Сколько журнала хоста хранить (ТЗ §37: bounded).</summary>
/// <param name="MaxFileBytes">Файл больше этого закрывается, запись продолжается в новом.</param>
/// <param name="MaxFiles">Сколько последних файлов хранить; старые удаляются.</param>
internal sealed record HostLogLimits(long MaxFileBytes, int MaxFiles)
{
    /// <summary>Не больше 5 МБ на диске: пять файлов по мегабайту.</summary>
    public static HostLogLimits Default { get; } = new(1024 * 1024, 5);
}

/// <summary>
/// Журнал хоста (#52): почему голубь пропал, что случилось с хранилищем. Пишется в каталог
/// данных приложения, каждая строка сразу на диск — запись переживает аварию процесса. Без
/// заголовков окон, путей и имён приложений (ТЗ §14): сообщения и исключения проходят через
/// <see cref="LogRedaction"/>.
/// </summary>
/// <remarks>
/// Журнал никогда не бросает: если писать некуда, голубь живёт без журнала. Методы можно
/// вызывать из любого потока — фоновая запись питомца сообщает об ошибках из своего.
/// </remarks>
internal sealed class HostLog : IDisposable
{
    private const string Prefix = "pigeon-";
    private const string Extension = ".log";

    private readonly string _directory;
    private readonly HostLogLimits _limits;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private bool _broken;

    public HostLog(string directory, HostLogLimits? limits = null, Func<DateTimeOffset>? now = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        _directory = directory;
        _limits = limits ?? HostLogLimits.Default;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        ArgumentOutOfRangeException.ThrowIfLessThan(_limits.MaxFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(_limits.MaxFiles, 1);
    }

    /// <summary>Файлы журнала от старого к новому.</summary>
    public IReadOnlyList<string> ListFiles()
    {
        try
        {
            return Directory.Exists(_directory)
                ? [.. Directory.GetFiles(_directory, Prefix + "*" + Extension).Order(StringComparer.Ordinal)]
                : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message, Exception? error = null) => Write("WARN", message, error);

    public void Error(string message, Exception? error = null) => Write("ERROR", message, error);

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
            _broken = true;
        }
    }

    private void Write(string level, string message, Exception? error)
    {
        var now = _now().ToUniversalTime();
        var text = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ')
            .Append(LogRedaction.Scrub(message));
        if (error is not null)
        {
            text.AppendLine().Append(LogRedaction.Describe(error));
        }

        lock (_lock)
        {
            if (_broken)
            {
                return;
            }

            try
            {
                var writer = WriterFor(now);
                writer.WriteLine(text.ToString());
            }
            catch (IOException)
            {
                Break();
            }
            catch (UnauthorizedAccessException)
            {
                Break();
            }
        }
    }

    /// <summary>Текущий файл; если он перерос предел — новый, а лишние старые удаляются.</summary>
    private StreamWriter WriterFor(DateTimeOffset now)
    {
        if (_writer is { } current && current.BaseStream.Length < _limits.MaxFileBytes)
        {
            return current;
        }

        _writer?.Dispose();
        _writer = null;

        Directory.CreateDirectory(_directory);

        // Имя — сквозной номер и время UTC: номер продолжает самый новый файл, поэтому порядок имён
        // хронологический, даже если часы сдвинулись назад, а имя удалённого файла не переиспользуется.
        var sequence = NextSequence();
        string path;
        do
        {
            path = Path.Combine(
                _directory,
                $"{Prefix}{sequence++:D6}-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}{Extension}");
        }
        while (File.Exists(path));

        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        RemoveOld();
        return _writer;
    }

    private long NextSequence()
    {
        var files = ListFiles();
        if (files.Count == 0)
        {
            return 1;
        }

        var name = Path.GetFileName(files[^1]);
        var digits = name.AsSpan(Prefix.Length, Math.Min(6, name.Length - Prefix.Length));
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var last) ? last + 1 : files.Count + 1;
    }

    private void RemoveOld()
    {
        var files = ListFiles();
        for (var i = 0; i < files.Count - _limits.MaxFiles; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch (IOException)
            {
                // Файл держит кто-то ещё — удалится при следующей смене файла.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Break()
    {
        _broken = true;
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
        }

        _writer = null;
    }
}

/// <summary>
/// Что можно писать в журнал хоста (ТЗ §14): пути к файлам заменяются заглушкой — в них имя
/// пользователя, — стек исключения пишется без файлов и номеров строк.
/// </summary>
internal static partial class LogRedaction
{
    public const string PathMark = "<путь>";

    /// <summary>Сообщение без путей Windows (<c>C:\…</c>, <c>\\сервер\…</c>) и Unix (<c>/home/…</c>, <c>/Users/…</c>).</summary>
    public static string Scrub(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Paths().Replace(text, PathMark);

    /// <summary>Тип, сообщение и стек исключения и всех вложенных.</summary>
    public static string Describe(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var text = new StringBuilder();
        Append(text, error, depth: 0);
        return text.ToString().TrimEnd();
    }

    private static void Append(StringBuilder text, Exception error, int depth)
    {
        var indent = new string(' ', depth * 2);
        text.Append(indent).Append(error.GetType().FullName).Append(": ").AppendLine(Scrub(error.Message));

        // Без файлов: в путях исходников и сборок — каталоги машины, на которой собрано.
        var frames = new StackTrace(error, fNeedFileInfo: false).ToString();
        foreach (var line in frames.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            text.Append(indent).Append("  ").AppendLine(Scrub(line));
        }

        if (depth >= 4)
        {
            return;
        }

        var inner = error is AggregateException aggregate ? aggregate.InnerExceptions : (IEnumerable<Exception>)(error.InnerException is { } one ? [one] : []);
        foreach (var nested in inner)
        {
            text.Append(indent).AppendLine("  ---> ");
            Append(text, nested, depth + 1);
        }
    }

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|\\\\)[^\s""'<>|*?]*|/(?:home|Users|root|tmp|var|private|mnt|media)/[^\s""'<>|*?]*")]
    private static partial Regex Paths();
}
