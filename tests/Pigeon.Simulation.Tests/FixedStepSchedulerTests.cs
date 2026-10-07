namespace Pigeon.Simulation.Tests;

public sealed class FixedStepSchedulerTests
{
    private static readonly SimulationOptions Options = new(TimeSpan.FromMilliseconds(100), 5, 10);

    [Fact]
    public void Advance_RunsWholeStepsAndKeepsTheRemainder()
    {
        var scheduler = new FixedStepScheduler(Options);

        var first = scheduler.Advance(TimeSpan.FromMilliseconds(250));
        var second = scheduler.Advance(TimeSpan.FromMilliseconds(60));

        Assert.Equal(new AdvanceResult(2, 0, 0.5), first);
        Assert.Equal(1, second.TicksToRun);
        Assert.Equal(0.1, second.Alpha, 9);
    }

    [Fact]
    public void Advance_ClampsLongPausesAndReportsDroppedTicks()
    {
        var scheduler = new FixedStepScheduler(Options);

        var result = scheduler.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(5, result.TicksToRun);
        Assert.Equal(15, result.TicksDropped);
        Assert.Equal(0.0, result.Alpha);
    }

    [Fact]
    public void Advance_RejectsNegativeDelta()
    {
        var scheduler = new FixedStepScheduler(Options);

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void Options_AreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepScheduler(Options with { Step = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepScheduler(Options with { MaxTicksPerAdvance = 0 }));
    }
}
