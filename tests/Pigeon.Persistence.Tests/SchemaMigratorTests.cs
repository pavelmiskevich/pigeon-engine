using Microsoft.Data.Sqlite;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;

namespace Pigeon.Persistence.Tests;

public sealed class SchemaMigratorTests
{
    private static readonly PetId FixturePet = new(Guid.Parse("6f1c2a5e-0b7d-4c1e-9a3b-2d4e6f8a0c11"));

    [Fact]
    public async Task EmptyDatabase_IsMigratedToTheLatestVersionInWalMode()
    {
        using var db = new TestDatabase();
        var migrator = new SqliteSchemaMigrator(db.Path);

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SqliteSchemaMigrator.LatestVersion, await migrator.GetVersionAsync(TestContext.Current.CancellationToken));
        Assert.Equal("wal", TestDatabase.Query(db.Path, "PRAGMA journal_mode;"));
        Assert.Equal("1", TestDatabase.Query(db.Path, "SELECT COUNT(*) FROM pragma_table_info('snapshots') WHERE name = 'journal_length';"));
    }

    [Fact]
    public async Task VersionOneFixture_IsMigratedWithoutLosingData()
    {
        using var db = new TestDatabase();
        CreateFromFixture(db.Path, "schema-v1.sql");

        var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);

        Assert.Equal(SqliteSchemaMigrator.LatestVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            TestDatabase.Query(db.Path, "PRAGMA user_version;"));
        Assert.Equal("0,2", TestDatabase.Query(db.Path, "SELECT group_concat(journal_length) FROM (SELECT journal_length FROM snapshots ORDER BY id);"));
        Assert.Equal(2, await store.GetLengthAsync(FixturePet, TestContext.Current.CancellationToken));

        var latest = await store.LoadLatestAsync(FixturePet, TestContext.Current.CancellationToken);
        Assert.NotNull(latest);
        Assert.Equal(30, latest.Tick);
        Assert.Equal(TimeSpan.FromMilliseconds(50), latest.Accumulated);
        Assert.Equal(30, Simulation.PigeonSimulation.Restore(latest).Tick);
    }

    [Fact]
    public async Task Migration_IsIdempotent()
    {
        using var db = new TestDatabase();
        var migrator = new SqliteSchemaMigrator(db.Path);

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SqliteSchemaMigrator.LatestVersion, await migrator.GetVersionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DatabaseFromANewerVersion_IsRefused()
    {
        using var db = new TestDatabase();
        TestDatabase.Query(db.Path, $"PRAGMA user_version = {SqliteSchemaMigrator.LatestVersion + 1};");

        await Assert.ThrowsAsync<NotSupportedException>(
            () => new SqliteSchemaMigrator(db.Path).MigrateAsync(TestContext.Current.CancellationToken).AsTask());
    }

    private static void CreateFromFixture(string databasePath, string fixture)
    {
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
