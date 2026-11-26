using Pigeon.Desktop;
using Pigeon.Simulation;

namespace Pigeon.Desktop.Tests;

/// <summary>Голубь в хранилище хоста (#33): рождение, перезапуск, авария, порча базы.</summary>
public sealed class PetSessionTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pigeon-session-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock _clock = new(Start);
    private readonly HostLog _log;

    public PetSessionTests() => _log = new HostLog(Path.Combine(_directory, "logs"));

    private string Data => Path.Combine(_directory, "data");

    public void Dispose()
    {
        _log.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог; если файл ещё занят, его уберёт система.
        }
    }

    [Fact]
    public async Task FirstStart_BirthsAPet_AndTheNextStartFindsIt()
    {
        var first = await Open();
        var identity = first.Identity;
        await first.CloseAsync(saveFinal: true);

        var second = await Open();

        Assert.Null(first.Absence);
        Assert.Equal(PetSession.DefaultName, identity.Name);
        Assert.Equal(Start, identity.BornAt);
        Assert.Equal(identity, second.Identity);
        Assert.Equal(identity.Seed, second.Simulation.Seed);
        Assert.Contains("INFO Родился новый голубь.", Logged(), StringComparison.Ordinal);
        await second.CloseAsync(saveFinal: false);
    }

    [Fact]
    public async Task Restart_ContinuesTheSameLife_AndCountsTheAbsence()
    {
        var first = await Open();
        Live(first.Simulation, ticks: 700);
        var tick = first.Simulation.Tick;
        var hash = first.Simulation.ComputeStateHash();
        await first.CloseAsync(saveFinal: true);

        _clock.Now = _clock.Now.AddHours(3);
        var second = await Open();

        Assert.Equal(tick, second.Simulation.Tick);
        Assert.Equal(hash, second.Simulation.ComputeStateHash());
        Assert.Equal(TimeSpan.FromHours(3), second.Absence);
        var absence = Assert.IsType<AbsenceEnded>(Assert.Single(second.Simulation.CreateSnapshot().Pending).Event);
        Assert.Equal(TimeSpan.FromHours(3), absence.Duration);
        Assert.Contains("приложение не работало 180 мин.", Logged(), StringComparison.Ordinal);
        await second.CloseAsync(saveFinal: false);
    }

    [Fact]
    public async Task Crash_LosesNoMoreThanTheTimeSinceTheLastPeriodicSave()
    {
        var session = await Open();
        Live(session.Simulation, ticks: 300);
        session.Tick(); // минута ещё не прошла — не сохраняет
        _clock.Now = _clock.Now.AddSeconds(61);
        session.Tick();
        var savedTick = session.Simulation.Tick;
        await session.FlushAsync();
        Live(session.Simulation, ticks: 400);

        // Авария: последнего сохранения при выходе нет.
        await session.CloseAsync(saveFinal: false);
        var after = await Open();

        Assert.Equal(savedTick, after.Simulation.Tick);
        await after.CloseAsync(saveFinal: false);
    }

    [Fact]
    public async Task ConfirmedJournal_IsForgottenInMemory()
    {
        var session = await Open();
        Live(session.Simulation, ticks: 700);
        Assert.NotEmpty(session.Simulation.Journal);

        session.SaveNow();
        await session.FlushAsync();
        session.Tick();

        Assert.Empty(session.Simulation.Journal);
        Assert.Equal(session.Simulation.JournalLength, session.Simulation.JournalOffset);
        await session.CloseAsync(saveFinal: true);
    }

    [Fact]
    public async Task CorruptedDatabase_IsRestoredFromTheDailyBackup()
    {
        var first = await Open();
        var identity = first.Identity;
        await first.CloseAsync(saveFinal: true);
        Assert.Contains("Сделана суточная резервная копия базы.", Logged(), StringComparison.Ordinal);

        var database = Path.Combine(Data, PetSession.DatabaseName);
        foreach (var sidecar in new[] { "-wal", "-shm" })
        {
            File.Delete(database + sidecar);
        }

        await File.WriteAllBytesAsync(database, [.. Enumerable.Repeat((byte)0x5A, 8192)], TestContext.Current.CancellationToken);

        var restored = await Open();

        Assert.Equal(identity, restored.Identity);
        Assert.Contains("восстановлена из резервной копии", Logged(), StringComparison.Ordinal);
        await restored.CloseAsync(saveFinal: false);
    }

    [Fact]
    public async Task ClosingTwice_DoesNothing()
    {
        var session = await Open();

        await session.CloseAsync(saveFinal: true);
        await session.CloseAsync(saveFinal: true);

        Assert.Throws<ObjectDisposedException>(session.Tick);
    }

    private Task<PetSession> Open() => PetSession.OpenAsync(Data, _log, _clock, cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>Голубь живёт: время идёт, иногда его тычут — в журнале появляются записи.</summary>
    private static void Live(PigeonSimulation simulation, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            if ((simulation.Tick + 1) % 250 == 0)
            {
                simulation.Submit(new UserPoked());
            }

            simulation.RunTicks(1);
        }
    }

    private string Logged() => string.Concat(_log.ListFiles().Select(HostLogTests.ReadShared));
}

/// <summary>Настенные часы, которые идут только по команде теста.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
