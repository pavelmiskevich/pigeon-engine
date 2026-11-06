using Pigeon.Core;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.World.Desktop.Screen;

/// <summary>Вид поверхности экрана уровня L2 (ТЗ §8.3, §10.1).</summary>
public enum SurfaceKind
{
    /// <summary>Нижняя кромка рабочей области — «пол» монитора.</summary>
    Floor = 0,

    /// <summary>Верхняя кромка панели задач у нижнего края монитора.</summary>
    Taskbar = 1,
}

/// <summary>
/// Горизонтальный отрезок, на котором голубь может стоять. Координаты — физические пиксели
/// виртуального экрана; длина для решений — в логических (ТЗ §10.1: голубь одного размера на
/// мониторах с разным масштабом).
/// </summary>
/// <param name="Y">Линия, на которой стоят лапы.</param>
/// <param name="Left">Левый край, включительно.</param>
/// <param name="Right">Правый край, не включительно.</param>
public sealed record Surface(PlaceKey Key, SurfaceKind Kind, MonitorKey Monitor, int Y, int Left, int Right, double Scale)
{
    public double LogicalLength => (Right - Left) / Scale;
}

/// <summary>Поверхности уровня L2 из топологии экрана (ТЗ §8.3, §11.5).</summary>
public static class ScreenSurfaces
{
    /// <summary>Отрезки короче этого (в логических пикселях) голубю не годятся.</summary>
    public const double MinLogicalLength = 48.0;

    /// <summary>
    /// По одной поверхности на монитор: верх панели задач, если она у нижнего края, иначе нижняя
    /// кромка рабочей области. Верхний край монитора и панель у верхнего края поверхностью не
    /// бывают: голубь, стоящий на них, оказался бы над экраном, где его не видно. Порядок —
    /// порядок мониторов в топологии.
    /// </summary>
    public static IReadOnlyList<Surface> Compute(DesktopTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var surfaces = new List<Surface>();
        foreach (var monitor in topology.Monitors)
        {
            var bottomBar = topology.Taskbars.FirstOrDefault(t => t.Monitor == monitor.Key && t.Edge == ScreenEdge.Bottom);
            if (bottomBar is not null)
            {
                Add(surfaces, monitor, SurfaceKind.Taskbar, "taskbar", bottomBar.Bounds.Y, Span(bottomBar.Bounds, monitor));
            }
            else
            {
                var work = monitor.WorkArea;
                Add(surfaces, monitor, SurfaceKind.Floor, "floor", work.Y + work.Height, (work.X, work.X + work.Width));
            }
        }

        return surfaces;
    }

    public static PlaceKey MonitorPlace(MonitorKey monitor) => new($"desktop/{monitor.Value}");

    private static void Add(List<Surface> surfaces, MonitorState monitor, SurfaceKind kind, string name, int y, (int Left, int Right) span)
    {
        var surface = new Surface(
            new PlaceKey($"{MonitorPlace(monitor.Key).Value}/{name}"), kind, monitor.Key, y, span.Left, span.Right, monitor.Scale);
        if (surface.LogicalLength >= MinLogicalLength)
        {
            surfaces.Add(surface);
        }
    }

    private static (int Left, int Right) Span(PixelRect rect, MonitorState monitor) =>
        (Math.Max(rect.X, monitor.Bounds.X), Math.Min(rect.X + rect.Width, monitor.Bounds.X + monitor.Bounds.Width));
}
