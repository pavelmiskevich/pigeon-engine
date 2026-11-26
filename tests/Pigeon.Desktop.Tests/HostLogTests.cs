using Pigeon.Desktop;

namespace Pigeon.Desktop.Tests;

public sealed class HostLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pigeon-hostlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Line_HasTimeLevelAndMessage_AndIsOnDiskImmediately()
    {
        var now = new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);
        using var log = new HostLog(_directory, now: () => now);

        log.Info("Запуск.");

        var file = Assert.Single(log.ListFiles());
        Assert.Equal("2026-11-26T10:00:00.000Z INFO Запуск." + Environment.NewLine, ReadShared(file));
    }

    [Fact]
    public void Exception_IsWrittenWithTypeAndStack_WithoutPathsOrLineNumbers()
    {
        using var log = new HostLog(_directory);
        var error = Throw(() => throw new IOException(@"Не открыть C:\Users\someone\AppData\Local\Pigeon\pigeon.db"));

        log.Error("Хранилище не открылось.", error);

        var text = ReadShared(Assert.Single(log.ListFiles()));
        Assert.Contains("ERROR Хранилище не открылось.", text, StringComparison.Ordinal);
        Assert.Contains("System.IO.IOException: Не открыть " + LogRedaction.PathMark + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("pigeon.db", text, StringComparison.Ordinal);
        Assert.Contains(nameof(Exception_IsWrittenWithTypeAndStack_WithoutPathsOrLineNumbers), text, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain(".cs:line", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InnerExceptions_AreDescribed()
    {
        var error = new InvalidOperationException("снаружи", new FormatException("внутри"));

        var text = LogRedaction.Describe(error);

        Assert.Contains("System.InvalidOperationException: снаружи", text, StringComparison.Ordinal);
        Assert.Contains("System.FormatException: внутри", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"файл C:\Users\me\x.db: занят", "файл <путь>: занят")]
    [InlineData(@"файл C:\Users\me\Мои документы\отчёт Иванова.docx", "файл <путь>")]
    [InlineData(@"файл ""C:\Program Files\Pigeon\x.dll"" не загружен", @"файл ""<путь>"" не загружен")]
    [InlineData(@"сеть \\server\share\pigeon.db", "сеть <путь>")]
    [InlineData("нет /home/me/.local/share/pigeon.db", "нет <путь>")]
    [InlineData("нет /Users/me/Library/My Pets/pigeon.db", "нет <путь>")]
    [InlineData("Error: тик 42 упал, a/b не путь", "Error: тик 42 упал, a/b не путь")]
    [InlineData("/homework не каталог", "/homework не каталог")]
    [InlineData("открыт file:///home/me/x.db", "открыт file://<путь>")]
    [InlineData("открыт //home/me/x.db", "открыт /<путь>")]
    [InlineData("нет /home/me/a:b/секрет.db", "нет <путь>")]
    [InlineData(@"нет \\?\C:\Users\Ivan Petrov\x.db", "нет <путь>")]
    [InlineData(@"нет \\.\pipe\Ivan Petrov", "нет <путь>")]
    [InlineData(@"нет \\server\share\a:b\секрет.db: занят", "нет <путь>: занят")]
    public void Scrub_ReplacesWholePaths(string message, string expected) =>
        Assert.Equal(expected, LogRedaction.Scrub(message, [], userName: null));

    [Theory]
    [InlineData(@"нет C:\Users\Ivan Petrov\AppData\Local\Pigeon\pigeon.db", "нет <путь>")]
    [InlineData(@"нет c:\users\ivan petrov\x.db: занят", "нет <путь>: занят")]
    [InlineData("данные в /opt/Моё Хранилище/Голубь/Секрет/pigeon.db", "данные в <путь>")]
    [InlineData("данные в /opt/Моё Хранилище/Голубь2/x", "данные в /opt/Моё Хранилище/Голубь2/x")]
    [InlineData("вошёл Ivan Petrov", "вошёл <пользователь>")]
    [InlineData("Ivan Petrovich не тот", "Ivan Petrovich не тот")]
    public void Scrub_RemovesKnownRootsWithSpaces_AndUserName(string message, string expected) =>
        Assert.Equal(expected, LogRedaction.Scrub(message, ["/opt/Моё Хранилище/Голубь", @"C:\Users\Ivan Petrov"], "Ivan Petrov"));

    [Fact]
    public void Scrub_RemovesThisMachineProfile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(string.IsNullOrEmpty(profile), "нет профиля пользователя");

        var text = LogRedaction.Scrub($"файл {Path.Combine(profile, "дальше с пробелом", "pigeon.db")} занят");

        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("файл " + LogRedaction.PathMark, text);
    }

    [Fact]
    public void Files_AreBoundedInSizeAndCount()
    {
        var limits = new HostLogLimits(MaxFileBytes: 2048, MaxFiles: 3);
        using var log = new HostLog(_directory, limits);
        var line = new string('ж', 100);

        for (var i = 0; i < 500; i++)
        {
            log.Info(line);
        }

        var files = log.ListFiles();
        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length < limits.MaxFileBytes + 1024));
    }

    [Fact]
    public void NewProcess_StartsNewFile_AndOldOnesStayWithinLimit()
    {
        var limits = new HostLogLimits(MaxFileBytes: 4096, MaxFiles: 2);
        for (var run = 0; run < 4; run++)
        {
            using var log = new HostLog(_directory, limits);
            log.Info($"запуск {run}");
        }

        using var last = new HostLog(_directory, limits);
        var files = last.ListFiles();
        Assert.Equal(2, files.Count);
        Assert.Contains("запуск 3", ReadShared(files[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void UnwritableDirectory_DoesNotThrow()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "logs");
        File.WriteAllText(blocker, "не каталог");
        using var log = new HostLog(blocker);

        log.Error("писать некуда", new InvalidOperationException());
        log.Info("и дальше тоже");

        Assert.Empty(log.ListFiles());
    }

    [Fact]
    public void ManyThreads_LinesAreNotInterleaved()
    {
        using var log = new HostLog(_directory);

        Parallel.For(0, 200, i => log.Info($"строка {i:D3}"));

        var lines = ReadShared(Assert.Single(log.ListFiles())).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(200, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^\S+ INFO строка \d{3}$", l));
    }

    internal static Exception Throw(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031
        catch (Exception error)
#pragma warning restore CA1031
        {
            return error;
        }

        throw new InvalidOperationException("Ожидалось исключение.");
    }

    internal static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
