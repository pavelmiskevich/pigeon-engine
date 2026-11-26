// Хост Desktop (MVP-1): голубь живёт на поверхностях экрана Windows (M1.3) и рисуется кадрами
// атласа в окне-оверлее (M1.4, ADR-0021, ADR-0023).
//
//   Pigeon.Desktop [--sprites <каталог>] [--data <каталог>] [--measure <файл.json>]
//
// Листы спрайтов собирает tools/Pigeon.SpriteBake; по умолчанию ищутся рядом с программой
// (sprites/pigeon-black) и в assets/sprites/pigeon-black выше по дереву каталогов.
// Данные приложения — по умолчанию %LOCALAPPDATA%\Pigeon; журнал хоста — в logs внутри (#52).
// Мышь (M1.6): клик — тычок, потереть курсором — погладить, потянуть — взять в руки; правый
// клик — меню (скрыть на час, выход).
//
// Коды выхода: 0 — пользователь закрыл; 2 — не хватает файлов или платформа не та; 3 — голубь
// остановлен ошибкой, причина — в журнале хоста.

using System.Runtime.InteropServices;
using System.Text;
using Pigeon.Desktop;

Console.OutputEncoding = Encoding.UTF8;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Pigeon.Desktop: пока есть только для Windows (MVP-1).");
    return 2;
}

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

var data = Option("--data")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pigeon");
using var log = new HostLog(Path.Combine(data, "logs"));

// Исключения фоновых потоков: процесс всё равно завершится, но причина останется в журнале.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    log.Error("Необработанное исключение — процесс завершается.", e.ExceptionObject as Exception);
TaskScheduler.UnobservedTaskException += (_, e) =>
    log.Warn("Исключение фоновой задачи никто не обработал.", e.Exception);

log.Info($"Запуск: {typeof(DesktopHost).Assembly.GetName().Version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}.");

var sprites = Option("--sprites") ?? FindSprites();
if (sprites is null)
{
    log.Error("Не найдены листы спрайтов голубя.");
    Console.Error.WriteLine("Не найдены листы голубя. Соберите их: dotnet run --project tools/Pigeon.SpriteBake -c Release");
    return 2;
}

var measure = Option("--measure");
try
{
    var code = new DesktopHost(sprites, measure is null ? null : new Measurement(measure), log).Run();
    log.Info($"Выход, код {code}.");
    return code;
}
#pragma warning disable CA1031 // Верхняя граница: любая ошибка — в журнал, процесс завершается кодом 3.
catch (Exception error)
#pragma warning restore CA1031
{
    log.Error("Голубь остановлен ошибкой.", error);
    Console.Error.WriteLine($"Голубь остановлен ошибкой: {error.GetType().Name}. Подробности — в журнале {Path.Combine(data, "logs")}.");
    return 3;
}

static string? FindSprites()
{
    var nextToProgram = Path.Combine(AppContext.BaseDirectory, "sprites", "pigeon-black");
    if (File.Exists(Path.Combine(nextToProgram, "idle@1x.json")))
    {
        return nextToProgram;
    }

    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, "assets", "sprites", "pigeon-black");
        if (File.Exists(Path.Combine(candidate, "idle@1x.json")))
        {
            return candidate;
        }
    }

    return null;
}
