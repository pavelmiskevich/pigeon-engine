using Pigeon.Core;
using Pigeon.World.Desktop.Capabilities;

namespace Pigeon.World.Desktop.Screen;

/// <summary>Вид поверхности экрана уровня L2 (ТЗ §8.3, §10.1).</summary>
public enum SurfaceKind
{
    /// <summary>Нижняя кромка рабочей области — «пол» монитора.</summary>
    Floor = 0,

    /// <summary>Верхняя кромка панели задач.</summary>
    Taskbar = 1,

    /// <summary>Верхний край монитора там, где выше нет другого монитора.</summary>
    ScreenTop = 2,
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
    /// Для каждого монитора: пол или верх панели задач у нижнего края, верхний край монитора —
    /// кроме участков, над которыми стоит другой монитор. Панель у верхнего края заменяет
    /// верхний край; боковая панель поверхности не даёт. Порядок — порядок мониторов в топологии.
    /// </summary>
    public static IReadOnlyList<Surface> Compute(DesktopTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var surfaces = new List<Surface>();
        foreach (var monitor in topology.Monitors)
        {
            var taskbars = topology.Taskbars.Where(t => t.Monitor == monitor.Key).ToList();
            var bottomBar = taskbars.FirstOrDefault(t => t.Edge == ScreenEdge.Bottom);
            var topBar = taskbars.FirstOrDefault(t => t.Edge == ScreenEdge.Top);

            if (bottomBar is not null)
            {
                Add(surfaces, monitor, SurfaceKind.Taskbar, "taskbar", bottomBar.Bounds.Y, Span(bottomBar.Bounds, monitor));
            }
            else
            {
                var work = monitor.WorkArea;
                Add(surfaces, monitor, SurfaceKind.Floor, "floor", work.Y + work.Height, (work.X, work.X + work.Width));
            }

            if (topBar is not null)
            {
                Add(surfaces, monitor, SurfaceKind.Taskbar, "taskbar-top", topBar.Bounds.Y, Span(topBar.Bounds, monitor));
                continue;
            }

            var top = monitor.Bounds.Y;
            var open = Subtract(
                (monitor.Bounds.X, monitor.Bounds.X + monitor.Bounds.Width),
                topology.Monitors
                    .Where(other => other.Key != monitor.Key && other.Bounds.Y + other.Bounds.Height == top)
                    .Select(other => (other.Bounds.X, other.Bounds.X + other.Bounds.Width)));

            for (var i = 0; i < open.Count; i++)
            {
                Add(surfaces, monitor, SurfaceKind.ScreenTop, i == 0 ? "top" : $"top-{i + 1}", top, open[i]);
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

    /// <summary>Отрезок минус перекрывающие его отрезки; результат — по возрастанию X.</summary>
    private static List<(int Left, int Right)> Subtract((int Left, int Right) span, IEnumerable<(int Left, int Right)> holes)
    {
        var parts = new List<(int Left, int Right)> { span };
        foreach (var (holeLeft, holeRight) in holes.OrderBy(h => h.Left))
        {
            parts = [.. parts.SelectMany(p => Cut(p, holeLeft, holeRight))];
        }

        return parts;
    }

    private static IEnumerable<(int Left, int Right)> Cut((int Left, int Right) part, int holeLeft, int holeRight)
    {
        if (holeRight <= part.Left || holeLeft >= part.Right)
        {
            yield return part;
            yield break;
        }

        if (holeLeft > part.Left)
        {
            yield return (part.Left, holeLeft);
        }

        if (holeRight < part.Right)
        {
            yield return (holeRight, part.Right);
        }
    }
}
