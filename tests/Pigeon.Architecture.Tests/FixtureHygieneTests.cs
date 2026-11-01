using System.Text.RegularExpressions;

namespace Pigeon.Architecture.Tests;

/// <summary>
/// Записанные ответы внешних API (ТЗ §17.4) лежат в публичном репозитории: в них не должно быть
/// ни ключей, ни идентификаторов аккаунта. Ответ OpenRouter с ошибкой содержит <c>user_id</c>
/// владельца ключа, поэтому фикстуры после перезаписи проверяются здесь.
/// </summary>
public sealed partial class FixtureHygieneTests
{
    public static TheoryData<string> FixtureFiles()
    {
        var data = new TheoryData<string>();
        var directory = Path.Combine(RepositoryLayout.Root, "tests", "Fixtures", "Jev");
        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            data.Add(RepositoryLayout.RelativePath(file));
        }

        return data;
    }

    [Fact]
    public void JevFixtures_Exist()
    {
        Assert.NotEmpty(FixtureFiles());
    }

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Fixture_HasNoSecretsOrAccountIds(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryLayout.Root, relativePath));

        Assert.DoesNotMatch(ApiKey(), text);
        Assert.DoesNotMatch(AccountId(), text);
        Assert.DoesNotContain("\"user_id\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer ", text, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"sk-or-[A-Za-z0-9-]{16,}")]
    private static partial Regex ApiKey();

    [GeneratedRegex(@"\buser_[A-Za-z0-9]{20,}\b")]
    private static partial Regex AccountId();
}
