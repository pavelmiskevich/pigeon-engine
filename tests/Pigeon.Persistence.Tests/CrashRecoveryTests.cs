using System.Diagnostics;
using System.Globalization;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Simulation;

namespace Pigeon.Persistence.Tests;

/// <summary>
/// Восстановление после аварийного завершения (ADR-0017): дочерний процесс непрерывно пишет
/// снимки и журнал, тест убивает его посреди записи и проверяет базу.
/// </summary>
public sealed class CrashRecoveryTests
{
    private const ulong Seed = 0x5EED_00C4UL;
    private const int CommitsBeforeKill = 40;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task KilledWriter_LeavesAnIntactDatabaseWithTheLastCommittedSnapshot()
    {
        using var db = new TestDatabase();

        // Несколько убийств подряд в одну базу: разные моменты обрыва, накопленный WAL.
        for (var round = 0; round < 3; round++)
        {
            var pet = new PetId(Guid.NewGuid());
            var lastReportedTick = await RunAndKillWriterAsync(db.Path, pet, CommitsBeforeKill + (round * 17));

            Assert.Equal("ok", TestDatabase.Query(db.Path, "PRAGMA integrity_check;"));

            var store = await SqlitePetStore.OpenAsync(db.Path, TestContext.Current.CancellationToken);
            var snapshot = await store.LoadLatestAsync(pet, TestContext.Current.CancellationToken);
            Assert.NotNull(snapshot);
            Assert.True(snapshot.Tick >= lastReportedTick,
                $"Последний снимок на тике {snapshot.Tick}, а процесс подтвердил тик {lastReportedTick}.");

            // Снимок и журнал согласованы: повтор журнала с нуля даёт то же состояние.
            var journal = await store.ReadAsync(pet, 0, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(snapshot.JournalLength, journal.Count);
            var replayed = PigeonSimulation.Replay(Seed, SimulationOptions.Desktop, journal, snapshot.Tick);
            Assert.Equal(replayed.ComputeStateHash(), PigeonSimulation.Restore(snapshot).ComputeStateHash());
        }
    }

    private static async Task<long> RunAndKillWriterAsync(string databasePath, PetId pet, int commitsBeforeKill)
    {
        var writer = WriterPath();
        var start = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(writer);
        start.ArgumentList.Add(databasePath);
        start.ArgumentList.Add(pet.ToString());
        start.ArgumentList.Add(Seed.ToString(CultureInfo.InvariantCulture));

        using var process = new Process { StartInfo = start };
        var enoughCommits = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderr = new System.Text.StringBuilder();
        long lastTick = 0;
        var commits = 0;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line && line.StartsWith("committed ", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref lastTick, long.Parse(line["committed ".Length..], CultureInfo.InvariantCulture));
                if (Interlocked.Increment(ref commits) >= commitsBeforeKill)
                {
                    enoughCommits.TrySetResult();
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderr)
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };
        process.Exited += (_, _) => enoughCommits.TrySetException(
            new InvalidOperationException($"Писатель завершился сам (код {process.ExitCode}): {stderr}"));
        process.EnableRaisingEvents = true;

        Assert.True(process.Start(), "Не удалось запустить писатель.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await enoughCommits.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            // Убиваем без предупреждения: процесс в этот момент, как правило, внутри транзакции.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        // Подтверждённый тик читаем после остановки: позже строк уже не будет.
        return Interlocked.Read(ref lastTick);
    }

    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
            ? host
            : "dotnet";

    /// <summary>Сборка писателя лежит рядом: та же конфигурация и TFM, соседний проект.</summary>
    private static string WriterPath()
    {
        const string testsProject = "Pigeon.Persistence.Tests";
        const string writerProject = "Pigeon.Persistence.CrashWriter";
        var baseDirectory = AppContext.BaseDirectory;
        var separator = Path.DirectorySeparatorChar;
        var marker = $"{separator}{testsProject}{separator}";
        var index = baseDirectory.LastIndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Не удалось вычислить путь к писателю из {baseDirectory}.");

        var path = Path.Combine(
            baseDirectory[..index] + $"{separator}{writerProject}{separator}" + baseDirectory[(index + marker.Length)..],
            writerProject + ".dll");
        Assert.True(File.Exists(path), $"Писатель не собран: {path}.");
        return path;
    }
}
