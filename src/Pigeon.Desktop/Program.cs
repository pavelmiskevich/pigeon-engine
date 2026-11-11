// Хост Desktop (MVP-1): голубь живёт на поверхностях экрана Windows (M1.3) и рисуется кадрами
// атласа в окне-оверлее (M1.4, ADR-0021, ADR-0023).
//
//   Pigeon.Desktop [--sprites <каталог>] [--measure <файл.json>]
//
// Листы спрайтов собирает tools/Pigeon.SpriteBake; по умолчанию ищутся рядом с программой
// (sprites/pigeon-black) и в assets/sprites/pigeon-black выше по дереву каталогов.
// Мышь (M1.6): клик — тычок, потереть курсором — погладить, потянуть — взять в руки; правый
// клик — меню (скрыть на час, выход).

using System.Text;
using Pigeon.Desktop;

Console.OutputEncoding = Encoding.UTF8;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Pigeon.Desktop: пока есть только для Windows (MVP-1).");
    return 2;
}

string? Option(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

var sprites = Option("--sprites") ?? FindSprites();
if (sprites is null)
{
    Console.Error.WriteLine("Не найдены листы голубя. Соберите их: dotnet run --project tools/Pigeon.SpriteBake -c Release");
    return 2;
}

var measure = Option("--measure");
return new DesktopHost(sprites, measure is null ? null : new Measurement(measure)).Run();

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
