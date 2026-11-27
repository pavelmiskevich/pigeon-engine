using Pigeon.Desktop;
using Pigeon.Desktop.Windows;

namespace Pigeon.Desktop.Tests;

/// <summary>Режим тишины в хосте (#31): ручная тишина по сроку и автоматическая при полном экране.</summary>
public sealed class QuietModeTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    [Fact]
    public void ManualQuiet_EndsOnTime()
    {
        var quiet = new QuietMode();

        Assert.True(quiet.QuietFor(Hour, TimeSpan.FromMinutes(5)));
        Assert.True(quiet.IsQuiet);
        Assert.Equal(QuietReason.Manual, quiet.Reason);

        Assert.False(quiet.Update(TimeSpan.FromMinutes(64), fullscreen: false));
        Assert.True(quiet.Update(TimeSpan.FromMinutes(65), fullscreen: false));
        Assert.False(quiet.IsQuiet);
        Assert.Equal(QuietReason.None, quiet.Reason);
    }

    [Fact]
    public void Fullscreen_TurnsQuietOnAndOff_OnlyOnChange()
    {
        var quiet = new QuietMode();

        Assert.False(quiet.Update(TimeSpan.Zero, fullscreen: false));
        Assert.True(quiet.Update(TimeSpan.FromSeconds(2), fullscreen: true));
        Assert.Equal(QuietReason.Fullscreen, quiet.Reason);
        Assert.False(quiet.Update(TimeSpan.FromSeconds(4), fullscreen: true));
        Assert.True(quiet.Update(TimeSpan.FromSeconds(6), fullscreen: false));
        Assert.False(quiet.IsQuiet);
    }

    [Fact]
    public void ManualQuiet_OutlastsTheFullscreenApp()
    {
        var quiet = new QuietMode();
        quiet.Update(TimeSpan.Zero, fullscreen: true);

        Assert.False(quiet.QuietFor(Hour, TimeSpan.FromMinutes(1)));
        Assert.False(quiet.Update(TimeSpan.FromMinutes(10), fullscreen: false));
        Assert.True(quiet.IsQuiet);
        Assert.Equal(QuietReason.Manual, quiet.Reason);
        Assert.True(quiet.Update(TimeSpan.FromMinutes(61), fullscreen: false));
    }

    [Theory]
    [InlineData(10, 0, 20 * 60)]
    [InlineData(23, 0, 7 * 60)]
    [InlineData(2, 0, 4 * 60)]
    [InlineData(6, 0, 24 * 60)]
    [InlineData(5, 59, 1)]
    public void UntilMorning_IsTheNextSixOClock(int hour, int minute, int minutes)
    {
        var now = new DateTime(2026, 11, 27, hour, minute, 0, DateTimeKind.Local);

        Assert.Equal(TimeSpan.FromMinutes(minutes), QuietMode.UntilMorning(now));
    }

    [Theory]
    [InlineData(1, false)] // QUNS_NOT_PRESENT: экран заблокирован или заставка
    [InlineData(2, true)] // QUNS_BUSY: полноэкранное приложение
    [InlineData(3, true)] // QUNS_RUNNING_D3D_FULL_SCREEN
    [InlineData(4, true)] // QUNS_PRESENTATION_MODE
    [InlineData(5, false)] // QUNS_ACCEPTS_NOTIFICATIONS
    [InlineData(6, false)] // QUNS_QUIET_TIME
    [InlineData(7, true)] // QUNS_APP: приложение Store во весь экран
    public void FullscreenStates_ThatMeanQuiet(int state, bool quiet)
    {
        Assert.Equal(quiet, FullscreenState.IsQuiet(state));
    }
}
