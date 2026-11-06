using Pigeon.Core;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.Desktop.Rendering;

/// <summary>Что показать на экране: клип, точка опоры в физических пикселях, направление и масштаб.</summary>
/// <param name="Scale">Физических пикселей экрана на логический пиксель рисунка.</param>
/// <param name="Height">Высота над землёй, логические пиксели рисунка (для тени).</param>
public readonly record struct StageFrame(string Clip, int AnchorX, int AnchorY, bool FacingLeft, double Scale, double Height);

/// <summary>
/// Сцена голубя на Desktop L2 (M1.4): на какой поверхности он стоит, какой клип играет, какого он
/// размера. Пока нет локомоции (M1.5), голубь стоит на панели задач или полу своего монитора, а
/// клип выбирается по намерению; переход на другой монитор меняет размер плавно.
/// </summary>
public sealed class PigeonStage(double sizeOnScreen)
{
    /// <summary>За сколько размер голубя подстраивается под DPI нового монитора.</summary>
    public static readonly TimeSpan ScaleTransitionTime = TimeSpan.FromMilliseconds(350);

    /// <summary>Где на поверхности стоит голубь: доля длины от левого края. Сохраняется при смене монитора.</summary>
    private const double Position = 0.78;

    private readonly ScaleTransition _scale = new(ScaleTransitionTime);

    /// <summary>
    /// Кадр сцены; <c>null</c> — голубю негде стоять (уровень ниже L2), окна прячутся.
    /// </summary>
    public StageFrame? Compose(DesktopWorld world, PigeonState pigeon, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(pigeon);

        if (world.PigeonMonitor is not { } here || world.Topology.Find(here) is not { } monitor)
        {
            return null;
        }

        var surface = HomeSurface(world.Surfaces, here);
        if (surface is null)
        {
            return null;
        }

        _scale.SetTarget(sizeOnScreen * monitor.Scale, now);
        var x = surface.Left + (int)Math.Round((surface.Right - surface.Left) * Position);
        return new StageFrame(ClipFor(pigeon.Intent), x, surface.Y, FacingLeft: true, _scale.Value(now), Height: 0);
    }

    public bool IsAnimating(TimeSpan now) => _scale.IsAnimating(now);

    /// <summary>Клип по намерению (ADR-0023: словарь анимаций → временный набор клипов MVP-1).</summary>
    public static string ClipFor(IntentKind intent) => intent switch
    {
        IntentKind.Sleep or IntentKind.Rest => "sleep",
        IntentKind.Explore => "peck",
        _ => "idle",
    };

    /// <summary>Панель задач, если есть; иначе пол; иначе любая поверхность монитора.</summary>
    private static Surface? HomeSurface(IReadOnlyList<Surface> surfaces, MonitorKey monitor) =>
        surfaces.Where(s => s.Monitor == monitor)
            .OrderBy(s => s.Kind switch { SurfaceKind.Taskbar => 0, SurfaceKind.Floor => 1, _ => 2 })
            .FirstOrDefault();
}
