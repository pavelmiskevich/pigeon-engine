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
    public const string UserMark = "<пользователь>";

    private static readonly Lock RootsLock = new();
    private static string[] _roots = KnownRoots();

    /// <summary>
    /// Сообщение без путей и имени пользователя. Путь вычищается целиком, со всеми сегментами после
    /// корня — в именах файлов и каталогов бывают имена людей и проектов. Сегмент тянется до
    /// разделителя, кавычки, двоеточия или конца строки и может содержать пробелы, поэтому иногда
    /// вместе с путём уходит и конец сообщения: лучше потерять пару слов, чем имя файла.
    /// Сначала — известные корни (профиль, AppData, временный каталог, каталоги из
    /// <see cref="Protect"/>), затем всё, что похоже на путь Windows (<c>C:\…</c>, <c>\\сервер\…</c>)
    /// или Unix (<c>/home/…</c>, <c>/Users/…</c>), затем имя пользователя отдельным словом.
    /// </summary>
    public static string Scrub(string? text) => Scrub(text, Volatile.Read(ref _roots), Environment.UserName);

    /// <summary>Каталог, путь к которому не должен попасть в журнал: данные приложения, листы спрайтов.</summary>
    public static void Protect(string? path)
    {
        if (Normalize(path) is not { } root)
        {
            return;
        }

        lock (RootsLock)
        {
            _roots = [.. _roots.Append(root).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(r => r.Length)];
        }
    }

    internal static string Scrub(string? text, IReadOnlyList<string> roots, string? userName)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        foreach (var root in roots)
        {
            // Корень — только целым сегментом: «C:\Users\Ivan» не совпадает с началом «C:\Users\Ivanov».
            text = Regex.Replace(
                text,
                Regex.Escape(root) + @"(?![^\\/\s""'<>|*?:])" + Segments,
                PathMark,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        text = Paths().Replace(text, PathMark);

        // Короткое имя совпало бы с обычными словами; имя из профиля уже заменено вместе с путём.
        if (userName is { Length: >= 3 })
        {
            text = Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(userName)}(?![\p{{L}}\p{{N}}])", UserMark, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        return text;
    }

    private static string[] KnownRoots() =>
    [
        .. new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath(),
        }
        .Select(Normalize)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(r => r.Length),
    ];

    /// <summary>Полный путь без завершающего разделителя; корень диска и пустое — не защищаются.</summary>
    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full.Length > 3 ? full : null;
    }

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

    /// <summary>
    /// Символ имени в пути. Двоеточие — тоже, кроме «путь: сообщение» (за ним пробел или конец
    /// строки): в путях Unix оно допустимо, а в <c>\\?\C:\…</c> стоит посреди пути.
    /// </summary>
    private const string NameChar = @"(?:[^\\/""'<>|*?\r\n:]|:(?!\s|$))";

    /// <summary>Сегменты пути после корня: разделитель и символы имени до следующего разделителя.</summary>
    private const string Segments = @"(?:[\\/]" + NameChar + "*)*";

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])[A-Za-z]:[\\/]" + NameChar + "*" + Segments +
        @"|\\\\(?:[?.][\\/])?(?:[^\\/\s""'<>|*?\r\n:]|:(?!\s|$))+" + Segments +
        @"|(?<![\p{L}\p{N}])/(?:home|Users|root|tmp|var|private|mnt|media)(?![^/\s""'<>|*?:])" + Segments,
        RegexOptions.CultureInvariant)]
    private static partial Regex Paths();
}
