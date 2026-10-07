using System.Xml.Linq;

namespace Pigeon.Architecture.Tests;

/// <summary>Проекты репозитория, прочитанные из .csproj и .slnx.</summary>
internal static class RepositoryLayout
{
    public const string SolutionFileName = "PigeonEngine.slnx";

    private static readonly Lazy<string> LazyRoot = new(FindRoot);
    private static readonly Lazy<IReadOnlyList<ProjectFile>> LazyProjects = new(LoadProjects);

    public static string Root => LazyRoot.Value;

    public static IReadOnlyList<ProjectFile> Projects => LazyProjects.Value;

    public static ProjectFile Project(string name) =>
        Projects.SingleOrDefault(p => p.Name == name)
        ?? throw new InvalidOperationException($"Проект {name} не найден в репозитории.");

    /// <summary>Пути проектов из .slnx относительно корня, с прямыми слэшами.</summary>
    public static IReadOnlyList<string> SolutionProjectPaths() =>
        XDocument.Load(Path.Combine(Root, SolutionFileName))
            .Descendants("Project")
            .Select(e => Normalize((string?)e.Attribute("Path") ?? string.Empty))
            .ToList();

    public static string RelativePath(string fullPath) =>
        Normalize(Path.GetRelativePath(Root, fullPath));

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Не найден {SolutionFileName} выше {AppContext.BaseDirectory}.");
    }

    private static IReadOnlyList<ProjectFile> LoadProjects() =>
        new[] { "src", "tools", "tests" }
            .Select(d => Path.Combine(Root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f))
            .Select(ProjectFile.Load)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj");
}

internal sealed record ProjectFile(
    string Name,
    string FullPath,
    bool IsCore,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences)
{
    public static ProjectFile Load(string path)
    {
        var doc = XDocument.Load(path);
        var isCore = doc.Descendants("PigeonCore")
            .Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
        var projectReferences = doc.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var packageReferences = doc.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToList();

        return new ProjectFile(Path.GetFileNameWithoutExtension(path), path, isCore, projectReferences, packageReferences);
    }
}
