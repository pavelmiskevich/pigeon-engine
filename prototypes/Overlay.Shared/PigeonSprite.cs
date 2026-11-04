namespace Pigeon.Prototypes.Overlay;

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);

public abstract record Shape(Rgba Color);

public sealed record EllipseShape(double Cx, double Cy, double Rx, double Ry, Rgba Color) : Shape(Color);

public sealed record PolygonShape(IReadOnlyList<(double X, double Y)> Points, Rgba Color) : Shape(Color);

public sealed record LineShape(double X1, double Y1, double X2, double Y2, double Width, Rgba Color) : Shape(Color);

/// <summary>
/// Рисунок голубя из примитивов в координатах 0..96 (логические пиксели окна). Оба кандидата
/// рисуют один и тот же список своим стеком. Ассетов нет: формат ассетов решается отдельно (S0.8).
/// </summary>
public static class PigeonSprite
{
    private static readonly Rgba Body = new(128, 132, 145);
    private static readonly Rgba Wing = new(96, 100, 114);
    private static readonly Rgba Neck = new(76, 140, 120);
    private static readonly Rgba Beak = new(225, 170, 60);
    private static readonly Rgba Eye = new(30, 30, 30);
    private static readonly Rgba Leg = new(210, 90, 80);

    /// <summary>Точка, по которой проверяется попадание в голубя (центр тела).</summary>
    public static (double X, double Y) BodyCenter => (48, 58);

    public static IReadOnlyList<Shape> Build(PigeonModel model)
    {
        var t = model.Time;
        var d = model.Direction;
        double Fx(double x) => d > 0 ? x : 96 - x;

        var shapes = new List<Shape>();
        var bob = model.Mode == PigeonMode.Idle ? Math.Sin(t * 2.0) * 1.0 : model.Flying ? 0 : Math.Abs(Math.Sin(t * 8)) * 2.5;

        if (!model.Flying)
        {
            var step = model.Mode == PigeonMode.Moving ? Math.Sin(t * 8) * 5 : 0;
            shapes.Add(new LineShape(Fx(44), 70, Fx(44 + step), 88, 2.5, Leg));
            shapes.Add(new LineShape(Fx(52), 70, Fx(52 - step), 88, 2.5, Leg));
        }

        shapes.Add(new EllipseShape(Fx(46), 60 - bob, 24, 15, Body));
        shapes.Add(new PolygonShape([(Fx(26), 56 - bob), (Fx(8), 50 - bob), (Fx(10), 62 - bob)], Wing));

        if (model.Flying)
        {
            var flap = Math.Sin(t * 18) * 22;
            shapes.Add(new PolygonShape([(Fx(36), 54), (Fx(54), 54), (Fx(44), 34 - flap)], Wing));
        }
        else
        {
            shapes.Add(new EllipseShape(Fx(42), 58 - bob, 15, 8, Wing));
        }

        var peck = model.Mode == PigeonMode.Moving && !model.Flying ? Math.Max(0, Math.Sin(t * 4)) * 4 : 0;
        shapes.Add(new EllipseShape(Fx(64), 46 - bob + peck, 9, 11, Neck));
        shapes.Add(new EllipseShape(Fx(68), 36 - bob + peck, 9, 8, Body));
        shapes.Add(new PolygonShape([(Fx(75), 34 - bob + peck), (Fx(84), 37 - bob + peck), (Fx(75), 39 - bob + peck)], Beak));

        var blink = model.Mode == PigeonMode.Idle && (t % 4) < 0.15;
        shapes.Add(new EllipseShape(Fx(70), 34 - bob + peck, 2, blink ? 0.4 : 2, Eye));
        return shapes;
    }
}
