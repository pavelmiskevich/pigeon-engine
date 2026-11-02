using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Правило уровней из docs/platform/capability-matrix.md, §4.</summary>
public sealed class DesktopLevelTests
{
    private static List<CapabilityCheck> All(CapabilityStatus status) =>
        [.. Enum.GetValues<Capability>().Select(c => new CapabilityCheck(c, status, "test"))];

    private static List<CapabilityCheck> With(CapabilityStatus status, params Capability[] changed) =>
        [.. All(CapabilityStatus.Available).Select(c => changed.Contains(c.Capability) ? c with { Status = status } : c)];

    [Fact]
    public void Everything_IsFullDesktopWorld()
    {
        Assert.Equal(DesktopLevel.FullDesktopWorld, DesktopLevels.Compute(All(CapabilityStatus.Available)));
        Assert.Empty(DesktopLevels.MissingForNextLevel(All(CapabilityStatus.Available)));
    }

    [Fact]
    public void LimitedCapability_CountsAsUsable()
    {
        Assert.Equal(DesktopLevel.FullDesktopWorld, DesktopLevels.Compute(With(CapabilityStatus.Limited, Capability.ClickThrough)));
    }

    [Theory]
    [InlineData(Capability.GlobalCursor)]
    [InlineData(Capability.WindowGeometry)]
    [InlineData(Capability.WindowZOrder)]
    public void WithoutWindowsOrCursor_IsScreenWorld(Capability missing)
    {
        var checks = With(CapabilityStatus.Unavailable, missing);

        Assert.Equal(DesktopLevel.ScreenWorld, DesktopLevels.Compute(checks));
        Assert.Equal([missing], DesktopLevels.MissingForNextLevel(checks));
    }

    [Fact]
    public void WaylandKde_IsScreenWorld()
    {
        var checks = With(CapabilityStatus.Unavailable, Capability.GlobalCursor, Capability.WindowGeometry, Capability.WindowZOrder);

        Assert.Equal(DesktopLevel.ScreenWorld, DesktopLevels.Compute(checks));
    }

    [Theory]
    [InlineData(Capability.TransparentOverlay)]
    [InlineData(Capability.ClickThrough)]
    [InlineData(Capability.AlwaysOnTop)]
    [InlineData(Capability.NoFocusNoTaskbar)]
    [InlineData(Capability.MonitorEnumeration)]
    public void WithoutOverlayBasics_IsWindowMode(Capability missing)
    {
        Assert.Equal(DesktopLevel.WindowMode, DesktopLevels.Compute(With(CapabilityStatus.Unavailable, missing)));
    }

    [Fact]
    public void UnknownCapability_IsNotUsable()
    {
        Assert.Equal(DesktopLevel.WindowMode, DesktopLevels.Compute(With(CapabilityStatus.Unknown, Capability.AlwaysOnTop)));
    }

    [Fact]
    public void LevelCodes()
    {
        Assert.Equal("L0", DesktopLevel.Background.Code());
        Assert.Equal("L3", DesktopLevel.FullDesktopWorld.Code());
    }
}
