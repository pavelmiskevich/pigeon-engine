namespace Pigeon.Architecture.Tests;

public sealed class SolutionTests
{
    [Fact]
    public void EveryProject_IsIncludedInSolution()
    {
        var inSolution = RepositoryLayout.SolutionProjectPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = RepositoryLayout.Projects
            .Select(p => RepositoryLayout.RelativePath(p.FullPath))
            .Where(path => !inSolution.Contains(path))
            .ToList();

        Assert.True(missing.Count == 0,
            $"Проекты не добавлены в {RepositoryLayout.SolutionFileName}: {string.Join(", ", missing)}.");
    }
}
