using System.Reflection;

namespace Pigeon.Architecture.Tests;

/// <summary>
/// Правила зависимостей ядра (ТЗ §3.2, ADR-0013, ADR-0014): ядро не знает об ОС, UI, сети,
/// БД и DI-контейнере и зависит только от разрешённых проектов ядра.
/// </summary>
public sealed class CoreDependencyTests
{
    /// <summary>Разрешённые ссылки для каждого проекта ядра. Новый проект ядра добавляется сюда явно.</summary>
    private static readonly Dictionary<string, string[]> AllowedCoreReferences = new(StringComparer.Ordinal)
    {
        ["Pigeon.Common"] = [],
        ["Pigeon.Core"] = ["Pigeon.Common"],
        ["Pigeon.World.Abstractions"] = ["Pigeon.Common", "Pigeon.Core"],
        ["Pigeon.AI"] = ["Pigeon.Common", "Pigeon.Core", "Pigeon.World.Abstractions"],
        ["Pigeon.Simulation"] = ["Pigeon.Common", "Pigeon.Core", "Pigeon.AI", "Pigeon.World.Abstractions"],
        ["Pigeon.World.Desktop"] = ["Pigeon.Common", "Pigeon.Core", "Pigeon.World.Abstractions"],
        ["Pigeon.World.City.Generation"] = ["Pigeon.Common"],
        ["Pigeon.World.City"] = ["Pigeon.Common", "Pigeon.Core", "Pigeon.World.Abstractions", "Pigeon.World.City.Generation"],
    };

    /// <summary>Префиксы сборок, на которые ядро не может ссылаться даже транзитивно через свой код.</summary>
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "System.Net",
        "System.Diagnostics.Process",
        "System.IO.Pipes",
        "System.Threading.Thread",
        "System.Windows",
        "Microsoft.Win32",
        "Microsoft.Data",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.AspNetCore",
        "Avalonia",
        "SkiaSharp",
    ];

    public static TheoryData<string> CoreProjectNames() => new(AllowedCoreReferences.Keys);

    [Fact]
    public void CoreFlag_MatchesTheListOfCoreProjects()
    {
        var flagged = RepositoryLayout.Projects.Where(p => p.IsCore).Select(p => p.Name).Order(StringComparer.Ordinal);

        Assert.Equal(AllowedCoreReferences.Keys.Order(StringComparer.Ordinal), flagged);
    }

    [Theory]
    [MemberData(nameof(CoreProjectNames))]
    public void CoreProject_ReferencesOnlyAllowedProjects(string projectName)
    {
        var project = RepositoryLayout.Project(projectName);
        var forbidden = project.ProjectReferences.Except(AllowedCoreReferences[projectName], StringComparer.Ordinal).ToList();

        Assert.True(forbidden.Count == 0,
            $"{projectName} ссылается на запрещённые проекты: {string.Join(", ", forbidden)}. См. ТЗ §3.2.");
    }

    [Theory]
    [MemberData(nameof(CoreProjectNames))]
    public void CoreProject_HasNoPackageReferences(string projectName)
    {
        var project = RepositoryLayout.Project(projectName);

        Assert.True(project.PackageReferences.Count == 0,
            $"{projectName} подключает пакеты {string.Join(", ", project.PackageReferences)}. " +
            "Пакет в ядре — архитектурное решение: сначала ADR, затем исключение в этом тесте.");
    }

    [Theory]
    [MemberData(nameof(CoreProjectNames))]
    public void CoreAssembly_DoesNotReferenceForbiddenAssemblies(string projectName)
    {
        var references = Assembly.Load(new AssemblyName(projectName)).GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        var forbidden = references
            .Where(r => ForbiddenAssemblyPrefixes.Any(p => r.StartsWith(p, StringComparison.Ordinal))
                        || (r.StartsWith("Pigeon.", StringComparison.Ordinal) && !AllowedCoreReferences.ContainsKey(r)))
            .ToList();

        Assert.True(forbidden.Count == 0,
            $"{projectName} использует запрещённые сборки: {string.Join(", ", forbidden)}. См. ТЗ §3.2.");
    }

    [Theory]
    [MemberData(nameof(CoreProjectNames))]
    public void CoreAssembly_IsMarkedTrimmable(string projectName)
    {
        var isTrimmable = Assembly.Load(new AssemblyName(projectName))
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(a => a.Key == "IsTrimmable" && string.Equals(a.Value, "True", StringComparison.OrdinalIgnoreCase));

        Assert.True(isTrimmable, $"{projectName} должен собираться с IsTrimmable (ADR-0014).");
    }
}
