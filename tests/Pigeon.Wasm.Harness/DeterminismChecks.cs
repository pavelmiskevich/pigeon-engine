using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using Pigeon.Testing;

namespace Pigeon.Wasm.Harness;

/// <summary>
/// Сверка эталонных хешей в браузере (ТЗ §3.3, ADR-0016): те же сценарии, что в .NET-тестах,
/// должны давать те же хеши в .NET WebAssembly.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class DeterminismChecks
{
    /// <summary>Первая строка отчёта — PASS или FAIL, далее по строке на сценарий.</summary>
    [JSExport]
    public static string Run()
    {
        var results = new[]
        {
            Check("simulation", GoldenSimulation.ExpectedHash, GoldenSimulation.Run()),
            Check("city", GoldenCity.ExpectedHash, GoldenCity.Run()),
        };

        var report = new StringBuilder();
        report.AppendLine(results.All(r => r.Passed) ? "PASS" : "FAIL");
        foreach (var result in results)
        {
            report.AppendLine(result.Line);
        }

        return report.ToString();
    }

    private static (bool Passed, string Line) Check(string name, ulong expected, ulong actual)
    {
        var passed = expected == actual;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: expected=0x{expected:X16} actual=0x{actual:X16} {(passed ? "ok" : "MISMATCH")}");
        return (passed, line);
    }
}
