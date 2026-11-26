using Pigeon.Core;
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
    public async Task NewLifeEvent_IsLogged_AndSavedAtOnce()
    {
        var session = await Open();
        session.Simulation.Submit(new UserPoked());
        session.Simulation.RunTicks(1);

        session.Tick(); // минута не прошла: сохраняет только новая запись хроники
        await session.FlushAsync();
        await session.CloseAsync(saveFinal: false); // авария
        var after = await Open();

        Assert.Contains("INFO В хронике: впервые встретил внимание человека.", Logged(), StringComparison.Ordinal);
        Assert.Contains(after.Simulation.LifeStory, e => e.Kind == LifeEventKind.FirstInteraction);
        await after.CloseAsync(saveFinal: false);
    }

    [Fact]
    public async Task RestoredStory_IsNotLoggedAgain()
    {
        var first = await Open();
        first.Simulation.Submit(new UserPoked());
        first.Simulation.RunTicks(1);
        first.Tick();
        await first.CloseAsync(saveFinal: true);

        var second = await Open();
        second.Simulation.RunTicks(10);
        second.Tick();
        await second.CloseAsync(saveFinal: true);

        Assert.Equal(1, CountOf(Logged(), "В хронике:"));
        Assert.DoesNotContain("В хронике: родился.", Logged(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LifeEventLines_HaveNoPlaceKeys()
    {
        var session = await Open();
        var simulation = session.Simulation;
        simulation.Pigeon.LifeStory.Record(LifeEventKind.FirstVisitToMonitor, new PlaceKey(@"desktop/\.\DISPLAY2"), simulation.Tick, simulation.LifeSeconds);

        session.Tick();
        await session.CloseAsync(saveFinal: false);

        var logged = Logged();
        Assert.Contains("INFO В хронике: впервые побывал на этом мониторе.", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("desktop/", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("DISPLAY2", logged, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLifeEventKind_HasAPhrase()
    {
        var phrases = Enum.GetValues<LifeEventKind>().Select(PetSession.Phrase).ToList();

        Assert.DoesNotContain("новое событие", phrases);
        Assert.Equal(phrases.Count, phrases.Distinct().Count());
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

    private static int CountOf(string text, string fragment)
    {
        var count = 0;
        for (var at = text.IndexOf(fragment, StringComparison.Ordinal); at >= 0; at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

/// <summary>Настенные часы, которые идут только по команде теста.</summary>
internal sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
