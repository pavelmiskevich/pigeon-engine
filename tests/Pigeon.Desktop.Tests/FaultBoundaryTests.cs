using Pigeon.Desktop;

namespace Pigeon.Desktop.Tests;

public sealed class FaultBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pigeon-faults-" + Guid.NewGuid().ToString("N"));
    private readonly HostLog _log;

    public FaultBoundaryTests() => _log = new HostLog(_directory);

    public void Dispose()
    {
        _log.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void FailedFrame_IsLogged_AndPigeonGoesOn()
    {
        var faults = new FaultBoundary(_log);

        var goOn = faults.Failed(HostLogTests.Throw(LocomotionStep));

        Assert.True(goOn);
        var text = Logged();
        Assert.Contains("WARN Кадр голубя упал, голубь продолжает жить.", text, StringComparison.Ordinal);
        Assert.Contains(nameof(LocomotionStep), text, StringComparison.Ordinal);
    }

    [Fact]
    public void GivesUp_AfterConsecutiveFailures_WithErrorRecord()
    {
        var faults = new FaultBoundary(_log, giveUpAfter: 3);
        var error = HostLogTests.Throw(LocomotionStep);

        Assert.True(faults.Failed(error));
        Assert.True(faults.Failed(error));
        Assert.False(faults.Failed(error));

        Assert.Contains("ERROR Кадр голубя упал 3 раз подряд — выход.", Logged(), StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulFrame_ResetsTheCount()
    {
        var faults = new FaultBoundary(_log, giveUpAfter: 3);
        var error = HostLogTests.Throw(LocomotionStep);

        for (var i = 0; i < 10; i++)
        {
            Assert.True(faults.Failed(error));
            Assert.True(faults.Failed(error));
            faults.Succeeded();
        }

        Assert.Equal(0, faults.Consecutive);
        Assert.Equal(TimeSpan.Zero, faults.Pause);
    }

    [Fact]
    public void Pause_GrowsAndIsCapped()
    {
        var faults = new FaultBoundary(_log, giveUpAfter: 100);
        var error = HostLogTests.Throw(LocomotionStep);
        var pauses = new List<TimeSpan>();

        for (var i = 0; i < 20; i++)
        {
            faults.Failed(error);
            pauses.Add(faults.Pause);
        }

        Assert.Equal(TimeSpan.FromMilliseconds(50), pauses[0]);
        Assert.Equal(TimeSpan.FromMilliseconds(100), pauses[1]);
        Assert.Equal(TimeSpan.FromSeconds(1), pauses[^1]);
        Assert.True(pauses.Zip(pauses.Skip(1)).All(p => p.Second >= p.First));
    }

    [Fact]
    public void RepeatedSameError_IsLoggedAtPowersOfTwo()
    {
        var faults = new FaultBoundary(_log, giveUpAfter: 2);
        var error = HostLogTests.Throw(LocomotionStep);

        // Кадр падает через раз: до выхода не доходит, а журнал растёт логарифмически.
        for (var i = 0; i < 100; i++)
        {
            faults.Failed(error);
            faults.Succeeded();
        }

        var warnings = Logged().Split(Environment.NewLine).Count(l => l.Contains(" WARN ", StringComparison.Ordinal));
        Assert.Equal(7, warnings); // 1, 2, 4, 8, 16, 32, 64
    }

    [Fact]
    public void SimulationFailure_IsNotRecoverable()
    {
        Assert.False(FaultBoundary.Recoverable(new SimulationFailedException(new InvalidOperationException())));
        Assert.True(FaultBoundary.Recoverable(new NullReferenceException()));
        Assert.True(FaultBoundary.Recoverable(new IOException()));
    }

    private static void LocomotionStep() => throw new NullReferenceException("монитор пропал во время перелёта");

    private string Logged() => string.Concat(_log.ListFiles().Select(HostLogTests.ReadShared));
}
