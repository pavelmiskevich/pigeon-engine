using Microsoft.Data.Sqlite;
using Pigeon.Simulation;

namespace Pigeon.Persistence.Tests;

/// <summary>Временный каталог с базой; удаляется после теста.</summary>
internal sealed class TestDatabase : IDisposable
{
    public TestDatabase()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pigeon-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        Path = System.IO.Path.Combine(Directory, "pigeon.db");
    }

    public string Directory { get; }

    public string Path { get; }

    public static PigeonSimulation RunSimulation(ulong seed, int ticks)
    {
        var simulation = new PigeonSimulation(seed, SimulationOptions.Desktop);
        for (var i = 0; i < ticks; i++)
        {
            if ((simulation.Tick + 1) % 333 == 0)
            {
                simulation.Submit(new UserPoked());
            }

            simulation.RunTicks(1);
        }

        return simulation;
    }

    public static string Query(string databasePath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог; если файл ещё занят, его уберёт система.
        }
    }
}
