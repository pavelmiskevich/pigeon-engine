using Pigeon.Common.Time;

namespace Pigeon.Common.Tests;

public sealed class ManualClockTests
{
    [Fact]
    public void Advance_AccumulatesElapsedTime()
    {
        var clock = new ManualClock();

        clock.Advance(TimeSpan.FromMilliseconds(16));
        clock.Advance(TimeSpan.FromMilliseconds(17));

        Assert.Equal(TimeSpan.FromMilliseconds(33), clock.Elapsed);
    }

    [Fact]
    public void Advance_RejectsNegativeDelta()
    {
        var clock = new ManualClock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromTicks(-1)));
    }
}
