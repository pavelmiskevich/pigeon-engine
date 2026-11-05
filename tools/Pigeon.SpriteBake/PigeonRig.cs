using SkiaSharp;

namespace Pigeon.SpriteBake;

/// <param name="Foot">Положение стопы; Swing — 0 на опоре, до 1 в середине переноса.</param>
internal readonly record struct Foot(float X, float Y, float Swing);

/// <summary>Поза одного кадра. Координаты — логические пиксели рисунка, голубь смотрит вправо.</summary>
internal sealed record Pose
{
    public float BodyY { get; init; }
    public float Pitch { get; init; }
    public float HeadX { get; init; }
    public float HeadY { get; init; }
    public float Tail { get; init; }
    public Foot[] Feet { get; init; } = [];
    public float Blink { get; init; }
    public float Puff { get; init; }

    /// <summary>Угол крыльев в полёте, градусы; null — крылья сложены.</summary>
    public float? WingAngle { get; init; }

    /// <summary>Лапы поджаты (полёт, сон).</summary>
    public bool Tucked { get; init; }
}

/// <summary>
/// Процедурный голубь: голова, шея и тело — один силуэт, шея деформируется вслед за головой.
/// Временный рисунок для MVP-1 (ADR-0023); формат кадров тот же, что у будущих кадров художника.
/// </summary>
internal static class PigeonRig
{
    public const float Ground = 146;
    public const float Stride = 26;
    public const float WalkCycle = 0.8f;

    private static readonly SKColor Body = new(0x2A, 0x2C, 0x32);
    private static readonly SKColor BodyDark = new(0x17, 0x18, 0x1C);
    private static readonly SKColor Wing = new(0x2E, 0x31, 0x38);
    private static readonly SKColor WingLight = new(0x36, 0x3A, 0x43);
    private static readonly SKColor Flight = new(0x1C, 0x1D, 0x21);
    private static readonly SKColor Edge = new(170, 180, 200, 41);
    private static readonly SKColor Eye = new(0xE2, 0x70, 0x2A);
    private static readonly SKColor Beak = new(0x2A, 0x2A, 0x2E);
    private static readonly SKColor Cere = new(0xE9, 0xE5, 0xDD);
    private static readonly SKColor Leg = new(0xC4, 0x54, 0x52);
    private static readonly SKColor LegFar = new(0x8E, 0x3C, 0x3B);
    private static readonly SKColor Claw = new(0x2B, 0x25, 0x26);

    private static readonly (float X, float Y, float R, float A)[] Scallops = MakeScallops();
    private static readonly (float X, float Y, float A)[] Coverts = MakeCoverts();
    private static readonly SKShader NoiseShader = MakeNoise();
    private static readonly SKPath WingPath = SKPath.ParseSvgPathData("M147,73 C132,68 106,70 88,78 C76,84 68,90 62,96 C80,103 108,104 130,98 C143,94 152,86 147,73 Z");

    // ----- Клипы -----

    public static Pose Walk(float p)
    {
        // Голова: рывок 30 % шага, затем 70 % стоит в мире — в системе тела едет назад со скоростью тела.
        var q = (p * 2) % 1;
        var amp = Stride * 0.35f;
        float hx, hy = 0;
        if (q < 0.3f)
        {
            hx = (-amp / 2) + (amp * Ease(q / 0.3f));
            hy = 1.6f * MathF.Sin(MathF.PI * q / 0.3f);
        }
        else
        {
            hx = (amp / 2) - (amp * ((q - 0.3f) / 0.7f));
        }

        return new Pose
        {
            BodyY = -0.9f * MathF.Abs(MathF.Sin(2 * MathF.PI * p)),
            HeadX = hx,
            HeadY = hy,
            Tail = MathF.Sin(4 * MathF.PI * p) * 0.6f,
            Feet = [StepFoot(p, 117, 6), StepFoot((p + 0.5f) % 1, 125, 6)],
        };
    }

    public static Pose Peck(float p)
    {
        var d = MathF.Sin(MathF.PI * MathF.Min(1, p / 0.9f));
        var down = Ease(MathF.Min(1, d * 1.15f));
        return new Pose
        {
            // Клюёт всем корпусом: тело наклоняется вокруг лап, хвост поднимается, голова идёт
            // вперёд и вниз и не проваливается под грудь.
            Pitch = 38 * down,
            HeadX = 16 * down,
            HeadY = 32 * down,
            Tail = -4 * down,
            Feet = Standing,
        };
    }

    public static Pose Idle(float p)
    {
        var breath = MathF.Sin(2 * MathF.PI * p);
        return new Pose
        {
            HeadX = 1.2f * breath,
            HeadY = -1 * breath,
            Feet = Standing,
            Blink = p is > 0.42f and < 0.56f ? MathF.Sin(MathF.PI * (p - 0.42f) / 0.14f) : 0,
        };
    }

    public static Pose Fly(float p)
    {
        // Взмах: быстрый удар вниз, медленный подъём; тело слегка качается в такт.
        var phase = p < 0.45f ? Ease(p / 0.45f) : 1 - Ease((p - 0.45f) / 0.55f);
        return new Pose
        {
            BodyY = -10 + (3 * phase),
            Pitch = -4,
            HeadX = 3,
            HeadY = -2 + phase,
            Tail = -3,
            WingAngle = 70 - (130 * phase),
            Tucked = true,
        };
    }

    public static Pose Sleep(float p)
    {
        var breath = MathF.Sin(2 * MathF.PI * p);
        return new Pose
        {
            BodyY = 17,
            HeadX = -9,
            HeadY = 13 + (0.6f * breath),
            Blink = 1,
            Puff = 0.05f + (0.012f * breath),
            Tucked = true,
        };
    }

    private static Foot[] Standing => [new(116, Ground, 0), new(126, Ground, 0)];

    private static Foot StepFoot(float phase, float baseX, float lift)
    {
        const float beta = 0.6f;
        var s = Stride * beta;
        if (phase < beta)
        {
            return new Foot(baseX + (s / 2) - (s * (phase / beta)), Ground, 0);
        }

        var u = (phase - beta) / (1 - beta);
        var swing = MathF.Sin(MathF.PI * u);
        return new Foot(baseX - (s / 2) + (s * Ease(u)), Ground - (lift * swing), swing);
    }

    // ----- Рисование -----

    public static void Draw(SKCanvas canvas, Pose pose)
    {
        var hx = 164 + pose.HeadX;
        var hy = 51 + pose.HeadY;
        DrawShadow(canvas, pose);

        const float cx = 118, cy = 112;
        var angle = pose.Pitch * MathF.PI / 180;
        SKPoint Hip(float x, float y)
        {
            var dx = x - cx;
            var dy = y - cy;
            return new SKPoint(cx + (dx * MathF.Cos(angle)) - (dy * MathF.Sin(angle)), cy + (dx * MathF.Sin(angle)) + (dy * MathF.Cos(angle)) + pose.BodyY);
        }

        if (!pose.Tucked)
        {
            DrawLeg(canvas, Hip(123, 112), pose.Feet[1], near: false);
        }

        canvas.Save();
        canvas.Translate(0, pose.BodyY);
        canvas.RotateDegrees(pose.Pitch, cx, cy);
        if (pose.Puff > 0)
        {
            canvas.Scale(1 + pose.Puff, 1 + pose.Puff, 115, 110);
        }

        if (pose.WingAngle is { } far)
        {
            DrawOpenWing(canvas, far - 8, near: false);
        }

        DrawTail(canvas, pose.Tail, spread: pose.WingAngle is not null);
        using var silhouette = Silhouette(hx, hy);
        DrawBody(canvas, silhouette, pose, hx, hy);

        if (pose.WingAngle is { } nearWing)
        {
            DrawOpenWing(canvas, nearWing, near: true);
        }
        else
        {
            DrawFoldedWing(canvas);
        }

        DrawHeadFeatures(canvas, hx, hy, pose.Blink);
        if (pose.Tucked && pose.WingAngle is not null)
        {
            DrawTuckedFeet(canvas);
        }

        canvas.Restore();

        if (!pose.Tucked)
        {
            DrawLeg(canvas, Hip(114, 112), pose.Feet[0], near: true);
        }
    }

    private static SKPath Silhouette(float hx, float hy)
    {
        float nx = hx - 11, ny = hy + 4;
        var path = new SKPath();
        path.MoveTo(66, 89);
        path.CubicTo(82, 85, 104, 81, 122, 76);
        path.CubicTo(132, 73, 139, 69, 142, 65);
        path.CubicTo(145, 61, nx - 2, ny + 7, nx, ny);
        path.CubicTo(nx - 1, hy - 9, hx - 3, hy - 13, hx + 4, hy - 11.5f);
        path.CubicTo(hx + 9.5f, hy - 10.5f, hx + 12.5f, hy - 5.5f, hx + 12.6f, hy - 1.5f);
        path.LineTo(hx + 11, hy + 5.5f);
        path.CubicTo(hx + 9.5f, hy + 14, 181, 76, 178, 90);
        path.CubicTo(176, 105, 166, 117, 147, 121.5f);
        path.CubicTo(131, 124.5f, 107, 122, 92, 116);
        path.CubicTo(82, 112, 73, 107, 66, 104);
        path.Close();
        return path;
    }

    private static void DrawShadow(SKCanvas canvas, Pose pose)
    {
        // Тень лежит на земле под голубем; в полёте земля далеко, тени в кадре нет.
        if (pose.WingAngle is not null)
        {
            return;
        }

        var lift = 0;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(new SKPoint(118, Ground + 2), 62,
                [new SKColor(0, 0, 0, (byte)(lift == 1 ? 40 : 82)), SKColors.Transparent], SKShaderTileMode.Clamp),
        };
        canvas.Save();
        canvas.Scale(1, 0.1f, 0, Ground + 2);
        canvas.DrawCircle(118, Ground + 2, 62, paint);
        canvas.Restore();
    }

    private static void DrawTail(SKCanvas canvas, float angle, bool spread)
    {
        canvas.Save();
        canvas.RotateDegrees(angle, 70, 92);
        using var paint = new SKPaint { IsAntialias = true };
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, Color = Edge };
        var fan = spread ? 5.2f : 3.9f;
        for (var t = 0; t < 7; t++)
        {
            float a0 = 86 + (t * 2.6f), a1 = 85 + (t * fan) - (spread ? 6 : 0);
            paint.Shader = SKShader.CreateLinearGradient(new SKPoint(70, 0), new SKPoint(28, 0),
                [Body, BodyDark, new SKColor(0x0F, 0x10, 0x13)], [0, 0.7f, 1], SKShaderTileMode.Clamp);
            using var path = new SKPath();
            path.MoveTo(72, a0);
            path.LineTo(31, a1);
            path.QuadTo(26, a1 + 3.1f, 31, a1 + 6.2f);
            path.LineTo(72, a0 + 6);
            path.Close();
            canvas.DrawPath(path, paint);
            canvas.DrawPath(path, edge);
        }

        canvas.Restore();
    }

    private static void DrawBody(SKCanvas canvas, SKPath silhouette, Pose pose, float hx, float hy)
    {
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 60), new SKPoint(0, 124), [Body, BodyDark], SKShaderTileMode.Clamp),
        };
        canvas.DrawPath(silhouette, fill);

        canvas.Save();
        canvas.ClipPath(silhouette, antialias: true);

        // Перелив шеи: оттенок зависит от наклона головы.
        var tilt = (pose.HeadY / 66) + (pose.HeadX / 20);
        var nx = Lerp(150, hx, 0.45f);
        var ny = Lerp(80, hy, 0.4f);
        using (var sheen = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
        {
            sheen.Shader = SKShader.CreateRadialGradient(new SKPoint(nx + 4, ny - 2), 20,
                [new SKColor(60, 190, 140, Alpha(0.55f - (0.2f * tilt))), new SKColor(60, 190, 140, 0)], SKShaderTileMode.Clamp);
            canvas.DrawRect(120, 30, 90, 90, sheen);
            sheen.Shader = SKShader.CreateRadialGradient(new SKPoint(nx + 10, ny + 12), 18,
                [new SKColor(150, 90, 210, Alpha(0.45f + (0.2f * tilt))), new SKColor(150, 90, 210, 0)], SKShaderTileMode.Clamp);
            canvas.DrawRect(120, 30, 90, 100, sheen);
        }

        using (var feathers = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f })
        {
            foreach (var (x, y, r, a) in Scallops)
            {
                feathers.Color = new SKColor(190, 200, 220, Alpha(a));
                canvas.DrawArc(new SKRect(x - r, y - r, x + r, y + r), 27, 126, false, feathers);
            }
        }

        using (var volume = new SKPaint { IsAntialias = true })
        {
            volume.Shader = SKShader.CreateTwoPointConicalGradient(new SKPoint(130, 68), 6, new SKPoint(120, 92), 70,
                [new SKColor(255, 255, 255, 41), new SKColor(255, 255, 255, 0), new SKColor(0, 0, 0, 89)], [0, 0.5f, 1], SKShaderTileMode.Clamp);
            canvas.DrawRect(40, 20, 170, 120, volume);
            volume.Shader = SKShader.CreateRadialGradient(new SKPoint(hx - 2, hy - 6), 14,
                [new SKColor(255, 255, 255, 46), new SKColor(255, 255, 255, 0)], SKShaderTileMode.Clamp);
            canvas.DrawRect(hx - 16, hy - 16, 32, 32, volume);
            volume.Shader = NoiseShader;
            volume.BlendMode = SKBlendMode.Overlay;
            volume.Color = new SKColor(255, 255, 255, 18);
            canvas.DrawRect(40, 20, 170, 120, volume);
        }

        canvas.Restore();
        using var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.9f, Color = new SKColor(150, 160, 180, 46) };
        canvas.DrawPath(silhouette, outline);
    }

    private static void DrawFoldedWing(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = Flight };
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.7f, Color = Edge };
        for (var f = 5; f >= 0; f--)
        {
            float bx = 95 - (f * 3), by = 90 + (f * 1.6f), tx = 41 + (f * 4), ty = 97 + (f * 1.4f);
            using var path = new SKPath();
            path.MoveTo(bx, by - 4);
            path.CubicTo(bx - 22, by - 3, tx + 14, ty - 3, tx, ty);
            path.CubicTo(tx + 16, ty + 2, bx - 20, by + 4, bx, by + 3);
            path.Close();
            canvas.DrawPath(path, paint);
            canvas.DrawPath(path, edge);
        }

        using var wing = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(150, 70), new SKPoint(70, 100), [WingLight, Wing], SKShaderTileMode.Clamp),
        };
        canvas.DrawPath(WingPath, wing);
        canvas.Save();
        canvas.ClipPath(WingPath, antialias: true);
        using (var coverts = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.9f })
        {
            foreach (var (x, y, a) in Coverts)
            {
                coverts.Color = new SKColor(200, 210, 230, Alpha(a));
                canvas.DrawArc(new SKRect(x, y - 3.7f, x + 7.4f, y + 3.7f), 18, 144, false, coverts);
            }
        }

        using (var light = new SKPaint { IsAntialias = true })
        {
            light.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 70), new SKPoint(0, 104),
                [new SKColor(255, 255, 255, 31), new SKColor(0, 0, 0, 64)], SKShaderTileMode.Clamp);
            canvas.DrawPath(WingPath, light);
        }

        canvas.Restore();
        using var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, Color = new SKColor(160, 170, 190, 51) };
        canvas.DrawPath(WingPath, outline);
    }

    /// <summary>
    /// Раскрытое крыло: кроющие у плеча и веер маховых, поворот вокруг плеча. Положительный угол —
    /// крыло поднято.
    /// </summary>
    private static void DrawOpenWing(SKCanvas canvas, float angleDegrees, bool near)
    {
        // Крыло в своих координатах направлено назад (−x) от плеча на спине: плечевая часть с
        // кроющими, второстепенные маховые по заднему краю, первостепенные веером на конце.
        canvas.Save();
        canvas.Translate(near ? 136 : 140, near ? 73 : 70);
        canvas.RotateDegrees(angleDegrees);
        var shade = near ? 1f : 0.62f;
        using var feather = new SKPaint { IsAntialias = true, Color = Mul(Flight, shade) };
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.7f, Color = Edge };

        for (var f = 0; f < 9; f++)
        {
            var x = -4 - (f * 4.2f);
            canvas.Save();
            canvas.Translate(x, 5);
            canvas.RotateDegrees(-100 + (f * 4));
            DrawFeather(canvas, 21 + (f * 0.5f), 6f, feather, edge);
            canvas.Restore();
        }

        for (var f = 0; f < 8; f++)
        {
            canvas.Save();
            canvas.Translate(-38 - (f * 0.6f), -2 + (f * 1.6f));
            canvas.RotateDegrees(-176 + (f * 7f));
            DrawFeather(canvas, 32 - (MathF.Abs(f - 2) * 1.4f), 6f, feather, edge);
            canvas.Restore();
        }

        using var arm = new SKPath();
        arm.MoveTo(5, -8);
        arm.CubicTo(-10, -14, -32, -13, -44, -4);
        arm.CubicTo(-47, 3, -42, 11, -32, 12);
        arm.CubicTo(-20, 14, -6, 13, 5, 9);
        arm.Close();
        using var coverts = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, -10), new SKPoint(-30, 10), [Mul(WingLight, shade), Mul(Wing, shade)], SKShaderTileMode.Clamp),
        };
        canvas.DrawPath(arm, coverts);
        canvas.Save();
        canvas.ClipPath(arm, antialias: true);
        using (var rows = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, Color = new SKColor(200, 210, 230, Alpha(0.14f * shade)) })
        {
            for (var y = -8f; y < 9; y += 3.6f)
            {
                for (var x = -40f + (y % 2); x < 4; x += 6)
                {
                    canvas.DrawArc(new SKRect(x, y - 3, x + 6, y + 3), 18, 144, false, rows);
                }
            }
        }

        canvas.Restore();
        using var outline = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, Color = new SKColor(160, 170, 190, 51) };
        canvas.DrawPath(arm, outline);
        canvas.Restore();
    }

    /// <summary>Перо вдоль оси +x от текущей точки: стержень у основания, закруглённый кончик.</summary>
    private static void DrawFeather(SKCanvas canvas, float length, float width, SKPaint fill, SKPaint edge)
    {
        using var path = new SKPath();
        path.MoveTo(0, -width * 0.4f);
        path.CubicTo(length * 0.4f, -width * 0.6f, length * 0.85f, -width * 0.55f, length, 0);
        path.CubicTo(length * 0.85f, width * 0.55f, length * 0.4f, width * 0.6f, 0, width * 0.4f);
        path.Close();
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, edge);
    }

    private static void DrawTuckedFeet(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeWidth = 3, Color = LegFar };
        canvas.DrawLine(112, 118, 100, 122, paint);
        paint.Color = Leg;
        canvas.DrawLine(120, 119, 108, 123, paint);
    }

    private static void DrawHeadFeatures(SKCanvas canvas, float hx, float hy, float blink)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = Beak };
        using (var beak = new SKPath())
        {
            beak.MoveTo(hx + 11.8f, hy - 2.6f);
            beak.QuadTo(hx + 20, hy - 1.4f, hx + 27.5f, hy + 3.6f);
            beak.QuadTo(hx + 20, hy + 5.2f, hx + 11.6f, hy + 4);
            beak.Close();
            canvas.DrawPath(beak, paint);
        }

        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.6f, Color = new SKColor(0, 0, 0, 128) };
        canvas.DrawLine(hx + 12.5f, hy + 1.2f, hx + 26, hy + 3.6f, line);

        canvas.Save();
        canvas.RotateDegrees(-8.6f, hx + 14.6f, hy - 1.9f);
        paint.Color = Cere;
        canvas.DrawOval(hx + 14.6f, hy - 1.9f, 2.7f, 1.7f, paint);
        canvas.Restore();

        float ex = hx + 3.6f, ey = hy - 2.6f;
        paint.Color = new SKColor(110, 115, 125, 204);
        canvas.DrawCircle(ex, ey, 4.6f, paint);
        paint.Color = Eye;
        canvas.DrawCircle(ex, ey, 3.4f, paint);
        paint.Color = new SKColor(0x0D, 0x0D, 0x0F);
        canvas.DrawCircle(ex, ey, 1.6f, paint);
        paint.Color = new SKColor(255, 255, 255, 230);
        canvas.DrawCircle(ex - 1, ey - 1, 0.8f, paint);
        if (blink > 0)
        {
            paint.Color = new SKColor(0x34, 0x36, 0x3D);
            canvas.DrawOval(ex, ey - 4.6f + (4.6f * blink), 4.8f, 4.6f * blink, paint);
        }
    }

    /// <summary>Двухзвенная ИК: бедро и голень спрятаны в оперении, цевка видна, «пятка» смотрит назад.</summary>
    private static void DrawLeg(SKCanvas canvas, SKPoint hip, Foot foot, bool near)
    {
        const float a = 12, b = 17;
        var dx = foot.X - hip.X;
        var dy = foot.Y - hip.Y;
        var d = MathF.Min(a + b - 0.01f, MathF.Sqrt((dx * dx) + (dy * dy)));
        var angle = MathF.Atan2(dy, dx);
        var k = MathF.Acos(((a * a) + (d * d) - (b * b)) / (2 * a * d));
        var heel = new SKPoint(hip.X + (a * MathF.Cos(angle + k)), hip.Y + (a * MathF.Sin(angle + k)));

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        paint.Color = near ? Body : BodyDark;
        paint.StrokeWidth = 8;
        canvas.DrawLine(hip.X, hip.Y - 3, heel.X, heel.Y, paint);
        paint.Color = near ? Leg : LegFar;
        paint.StrokeWidth = 3.4f;
        canvas.DrawLine(heel.X, heel.Y, foot.X, foot.Y, paint);

        paint.Color = new SKColor(0, 0, 0, 56);
        paint.StrokeWidth = 0.7f;
        for (var s = 1; s < 5; s++)
        {
            var t = s / 5f;
            var sx = Lerp(heel.X, foot.X, t);
            var sy = Lerp(heel.Y, foot.Y, t);
            canvas.DrawLine(sx - 1.6f, sy, sx + 1.6f, sy, paint);
        }

        var curl = foot.Swing;
        paint.Color = near ? Leg : LegFar;
        paint.StrokeWidth = 2.5f;
        var front = new SKPoint(foot.X + Lerp(9, 4, curl), foot.Y + Lerp(0.8f, 3, curl));
        canvas.DrawLine(foot.X, foot.Y, front.X, front.Y, paint);
        canvas.DrawLine(foot.X, foot.Y, foot.X + Lerp(6.5f, 3, curl), foot.Y + Lerp(2.6f, 4, curl), paint);
        canvas.DrawLine(foot.X, foot.Y, foot.X - Lerp(5, 2.5f, curl), foot.Y + Lerp(0.8f, 2.5f, curl), paint);
        paint.Color = Claw;
        paint.StrokeWidth = 1.3f;
        canvas.DrawLine(front.X, front.Y, foot.X + Lerp(11, 5.4f, curl), foot.Y + Lerp(1.4f, 3.8f, curl), paint);
    }

    // ----- Утилиты -----

    private static float Ease(float t) => t < 0.5f ? 2 * t * t : 1 - (MathF.Pow((-2 * t) + 2, 2) / 2);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static byte Alpha(float value) => (byte)Math.Clamp(value * 255, 0, 255);

    private static SKColor Mul(SKColor c, float k) => new((byte)(c.Red * k), (byte)(c.Green * k), (byte)(c.Blue * k), c.Alpha);

    private static Func<float> Random(uint seed) => () =>
    {
        seed = unchecked((seed * 1664525) + 1013904223);
        return seed / 4294967296f;
    };

    private static (float, float, float, float)[] MakeScallops()
    {
        var r = Random(5);
        return [.. Enumerable.Range(0, 420).Select(_ => (62 + (r() * 92), 70 + (r() * 54), 2.5f + (r() * 2.5f), 0.05f + (r() * 0.09f))).Where(s => s.Item1 <= 150)];
    }

    private static (float, float, float)[] MakeCoverts()
    {
        var r = Random(9);
        var list = new List<(float, float, float)>();
        for (float y = 74; y < 104; y += 4)
        {
            for (var x = 64 + (y / 4 % 2 * 4); x < 150; x += 7.5f)
            {
                list.Add((x + ((r() - 0.5f) * 2), y + ((r() - 0.5f) * 1.4f), 0.1f + (r() * 0.1f)));
            }
        }

        return [.. list];
    }

    private static SKShader MakeNoise()
    {
        var r = Random(3);
        using var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var v = (byte)(r() * 255);
                bitmap.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        return SKShader.CreateImage(SKImage.FromBitmap(bitmap), SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
    }
}
