// pigeon-probe: определение возможностей платформы C1–C20 и уровня L0–L3
// (MVP-0, S0.3; docs/platform/capability-matrix.md, §5).
//
//   pigeon-probe            текстовый отчёт
//   pigeon-probe --json     JSON (для заполнения матрицы и баг-репортов)
//   pigeon-probe --x11      в Wayland-сессии проверить XWayland
//
// Отчёт не содержит заголовков окон, имён приложений, путей, имени машины и пользователя.

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pigeon.Desktop.Linux;
using Pigeon.Desktop.MacOS;
using Pigeon.Desktop.Windows;
using Pigeon.World.Desktop.Capabilities;

Console.OutputEncoding = Encoding.UTF8;

ICapabilityProbe? probe =
    OperatingSystem.IsWindows() ? new WindowsCapabilityProbe()
    : OperatingSystem.IsMacOS() ? new MacCapabilityProbe()
    : OperatingSystem.IsLinux() ? new LinuxCapabilityProbe(forceX11: args.Contains("--x11"))
    : null;
if (probe is null)
{
    Console.Error.WriteLine($"pigeon-probe: проба для {Environment.OSVersion.Platform} ещё не реализована.");
    return 2;
}

var result = probe.Probe();

if (args.Contains("--json"))
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        result.Environment,
        Level = result.Level.Code(),
        MissingForNextLevel = DesktopLevels.MissingForNextLevel(result.Checks).Select(c => $"C{(int)c}"),
        result.Monitors,
        result.VisibleWindowCount,
        result.Protocols,
        Checks = result.Checks.Select(c => new { c.Id, c.Capability, c.Status, c.Verified, c.Mechanism, c.Note }),
    }, options));
    return 0;
}

var env = result.Environment;
Console.WriteLine($"pigeon-probe — {env.Os} {env.OsVersion} {env.Architecture}, сессия {env.Session}{(env.Compositor is null ? "" : $", {env.Compositor}")}");
Console.WriteLine();
Console.WriteLine($"Уровень: {result.Level.Code()} ({result.Level})");
var missing = DesktopLevels.MissingForNextLevel(result.Checks);
if (missing.Count > 0)
{
    Console.WriteLine($"До следующего уровня не хватает: {string.Join(", ", missing.Select(c => $"C{(int)c}"))}");
}

Console.WriteLine();
Console.WriteLine($"Мониторы ({result.Monitors.Count}):");
foreach (var m in result.Monitors)
{
    Console.WriteLine($"  {m.Bounds}, рабочая область {m.WorkArea}, масштаб {m.Scale:0.##}{(m.IsPrimary ? ", основной" : "")}");
}

Console.WriteLine($"Видимых окон: {result.VisibleWindowCount}");
if (result.Protocols.Count > 0)
{
    Console.WriteLine($"Протоколы композитора: {string.Join(", ", result.Protocols)}");
}
Console.WriteLine();
foreach (var c in result.Checks)
{
    var mark = c.Status switch
    {
        CapabilityStatus.Available => "✅",
        CapabilityStatus.Limited => "⚠️",
        CapabilityStatus.Unavailable => "❌",
        _ => "? ",
    };
    Console.WriteLine($"{c.Id,-4} {mark} {c.Capability,-20} {c.Mechanism}{(c.Verified ? "" : " [не проверено вызовом]")}");
    if (c.Note is not null)
    {
        Console.WriteLine($"          {c.Note}");
    }
}

return 0;
