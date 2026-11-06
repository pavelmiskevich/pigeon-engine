using Pigeon.World.Desktop.Capabilities;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.World.Desktop.Tests;

/// <summary>Фейковый экран для адаптерных тестов: топологию задаёт тест.</summary>
internal sealed class FakeDesktop : IDesktopEnvironment
{
    public static readonly MonitorKey Laptop = new("laptop");
    public static readonly MonitorKey External = new("external");

    public DesktopTopology Topology { get; set; } = DesktopTopology.Empty;

    public DesktopTopology ReadTopology() => Topology;

    /// <summary>Ноутбук 2560×1600 на 175 % с панелью задач снизу.</summary>
    public static MonitorState LaptopScreen(bool primary = true) =>
        new(Laptop, new PixelRect(0, 0, 2560, 1600), new PixelRect(0, 0, 2560, 1516), 1.75, primary);

    /// <summary>Внешний 1920×1080 на 100 % справа от ноутбука, без панели задач.</summary>
    public static MonitorState ExternalScreen(int x = 2560, int y = 0, double scale = 1.0) =>
        new(External, new PixelRect(x, y, 1920, 1080), new PixelRect(x, y, 1920, 1080), scale, false);

    public static TaskbarState LaptopTaskbar => new(Laptop, new PixelRect(0, 1516, 2560, 84), ScreenEdge.Bottom);

    public static DesktopTopology TwoMonitors(double externalScale = 1.0) =>
        new([LaptopScreen(), ExternalScreen(scale: externalScale)], [LaptopTaskbar], TaskbarsKnown: true);

    public static DesktopTopology LaptopOnly() => new([LaptopScreen()], [LaptopTaskbar], TaskbarsKnown: true);

    /// <summary>Проба, в которой есть всё для L2 (и L3).</summary>
    public static IReadOnlyList<CapabilityCheck> FullProbe() =>
        [.. Enum.GetValues<Capability>().Select(c => new CapabilityCheck(c, CapabilityStatus.Available, "test"))];
}
