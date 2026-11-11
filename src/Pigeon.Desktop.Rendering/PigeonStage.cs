using Pigeon.Core;
using Pigeon.World.Desktop.Locomotion;
using Pigeon.World.Desktop.Screen;

namespace Pigeon.Desktop.Rendering;

/// <summary>Что показать на экране: клип, где лапы и тень, направление и масштаб.</summary>
/// <param name="AnchorX">Середина между стопами, физические пиксели экрана.</param>
/// <param name="GroundY">Где лежит тень — поверхность под голубем.</param>
/// <param name="Scale">Физических пикселей экрана на логический пиксель рисунка.</param>
/// <param name="Height">Высота над землёй, логические пиксели рисунка (для тени).</param>
/// <param name="ClipStarted">С какого момента играет клип — для клипов без повтора.</param>
/// <param name="Walked">Пройденный путь, логические пиксели рисунка — шаг идёт по нему (ADR-0023).</param>
public readonly record struct StageFrame(
    string Clip,
    int AnchorX,
    int AnchorY,
    int GroundY,
    bool FacingLeft,
    double Scale,
    double Height,
    TimeSpan ClipStarted,
    double Walked);

/// <summary>
/// Сцена голубя на Desktop (M1.4, M1.5): клип по походке и намерению, положение из локомоции,
/// размер по монитору под голубем — при переходе на монитор с другим DPI он меняется плавно.
/// </summary>
public sealed class PigeonStage(double sizeOnScreen)
{
    /// <summary>За сколько размер голубя подстраивается под DPI нового монитора.</summary>
    public static readonly TimeSpan ScaleTransitionTime = TimeSpan.FromMilliseconds(350);

    /// <summary>Исследуя место, голубь клюёт с такой паузой.</summary>
    public static readonly TimeSpan PeckEvery = TimeSpan.FromMilliseconds(1400);

    private readonly ScaleTransition _scale = new(ScaleTransitionTime);

    /// <summary>Кадр сцены; <c>null</c> — голубя не видно: он за краем экрана или мониторов нет.</summary>
    public StageFrame? Compose(DesktopWorld world, PigeonBody body, IntentKind intent, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (body.Gait == Gait.Hidden)
        {
            return null;
        }

        var monitor = world.MonitorAt(body.X, body.Y - 1) ?? (body.Monitor is { } key ? world.Topology.Find(key) : null);
        if (monitor is null)
        {
            return null;
        }

        _scale.SetTarget(sizeOnScreen * monitor.Scale, now);
        var clip = ClipFor(body.Gait, intent);
        var started = body.GaitStarted;
        if (clip == "peck" && now > started)
        {
            started += PeckEvery * Math.Floor((now - started) / PeckEvery);
        }

        return new StageFrame(
            clip,
            (int)Math.Round(body.X),
            (int)Math.Round(body.Y),
            (int)Math.Round(body.GroundY),
            body.FacingLeft,
            _scale.Value(now),
            Math.Max(0, body.GroundY - body.Y) / (monitor.Scale * sizeOnScreen),
            started,
            body.Walked);
    }

    public bool IsAnimating(TimeSpan now) => _scale.IsAnimating(now);

    /// <summary>
    /// Клип по походке, а на месте — по намерению (ADR-0023: словарь анимаций → набор клипов MVP-1).
    /// </summary>
    public static string ClipFor(Gait gait, IntentKind intent) => gait switch
    {
        Gait.Walk => "walk",
        Gait.Hop => "hop",
        Gait.TakeOff => "takeoff",
        Gait.Fly or Gait.Held => "fly",
        Gait.Land => "land",
        _ => intent switch
        {
            IntentKind.Sleep => "sleep",
            IntentKind.Rest => "sit",
            IntentKind.Explore => "peck",
            _ => "idle",
        },
    };
}
