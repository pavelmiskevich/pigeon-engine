namespace Pigeon.Desktop.Rendering.Tests;

/// <summary>Темп цикла хоста: когда просыпаться и когда идти за композицией DWM (ТЗ §37, #48).</summary>
public sealed class PacingTests
{
    private static readonly TimeSpan Now = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan Refresh = Now + TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Poll = Now + TimeSpan.FromMilliseconds(500);

    [Fact]
    public void WithoutAFrame_StaleFrameChangeDoesNotWakeTheLoopAtOnce()
    {
        // Окно спрятали: смена кадра так и осталась в прошлом.
        var pace = FramePacing.Next(hasFrame: false, animating: false, nextChange: Now - TimeSpan.FromSeconds(30), Refresh, Poll, Now);

        Assert.False(pace.AwaitComposition);
        Assert.Equal(Poll, pace.Wake);
    }

    [Fact]
    public void WithoutAFrame_MovementDoesNotRunAt60Hz()
    {
        // Голубь идёт, но окна нет (режим тишины) — кадры рисовать некуда.
        var pace = FramePacing.Next(hasFrame: false, animating: true, nextChange: Now, Refresh, Poll, Now);

        Assert.False(pace.AwaitComposition);
        Assert.Equal(Poll, pace.Wake);
    }

    [Fact]
    public void WithoutAFrame_WorldRefreshComesFirstWhenItIsSooner()
    {
        var refresh = Now + TimeSpan.FromMilliseconds(200);

        var pace = FramePacing.Next(hasFrame: false, animating: false, nextChange: Now - TimeSpan.FromSeconds(1), refresh, Poll, Now);

        Assert.Equal(refresh, pace.Wake);
    }

    [Fact]
    public void Moving_FollowsComposition()
    {
        var pace = FramePacing.Next(hasFrame: true, animating: true, nextChange: Now, Refresh, Poll, Now);

        Assert.True(pace.AwaitComposition);
    }

    [Fact]
    public void Standing_SleepsUntilTheNextFrameOfTheClip()
    {
        var change = Now + TimeSpan.FromMilliseconds(120);

        var pace = FramePacing.Next(hasFrame: true, animating: false, change, Refresh, Poll, Now);

        Assert.False(pace.AwaitComposition);
        Assert.Equal(change, pace.Wake);
    }

    [Fact]
    public void Standing_NeverWakesInThePast()
    {
        var pace = FramePacing.Next(hasFrame: true, animating: false, nextChange: Now - TimeSpan.FromSeconds(1), Refresh, Poll, Now);

        Assert.Equal(Now, pace.Wake);
    }

    [Fact]
    public void Standing_PollIsTheLongestSleep()
    {
        var pace = FramePacing.Next(hasFrame: true, animating: false, nextChange: Now + TimeSpan.FromSeconds(10), Refresh, Poll, Now);

        Assert.Equal(Poll, pace.Wake);
    }
}
