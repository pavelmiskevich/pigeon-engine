using SkiaSharp;

namespace Pigeon.Desktop.Rendering;

/// <summary>
/// Какой кадр клипа показывать: по длительностям кадров из листа, с повтором или с остановкой на
/// последнем кадре. Знает, когда кадр сменится, — чтобы не перерисовывать окно зря (ТЗ §37).
/// </summary>
public sealed class ClipPlayer
{
    private TimeSpan _started;

    public string? Clip { get; private set; }

    /// <summary>
    /// Клип, играющий с момента <paramref name="started"/>: другой клип или новый старт начинают его
    /// заново, тот же — продолжают.
    /// </summary>
    public void Play(string clip, TimeSpan started)
    {
        Clip = clip;
        _started = started;
    }

    public int FrameAt(SpriteSheet sheet, TimeSpan now) => Locate(sheet, now).Index;

    /// <summary>
    /// Кадр шага по пройденному пути (ADR-0023): за длину шага проходит один цикл, поэтому опорная
    /// лапа не скользит при любой скорости.
    /// </summary>
    /// <param name="walked">Путь, логические пиксели рисунка.</param>
    public static int FrameByDistance(SpriteSheet sheet, double walked)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var count = sheet.Frames.Count;
        var cycle = walked / sheet.Metrics.Stride;
        return (int)Math.Floor((cycle - Math.Floor(cycle)) * count) % count;
    }

    /// <summary>Когда сменится кадр; <see cref="TimeSpan.MaxValue"/> — не сменится (клип без повтора доиграл).</summary>
    public TimeSpan NextChange(SpriteSheet sheet, TimeSpan now) => Locate(sheet, now).Until;

    private (int Index, TimeSpan Until) Locate(SpriteSheet sheet, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var total = sheet.Frames.Sum(f => (long)f.DurationMs);
        var elapsed = (long)Math.Max(0, (now - _started).TotalMilliseconds);
        var cycles = 0L;
        if (sheet.Loop)
        {
            cycles = elapsed / total;
            elapsed %= total;
        }
        else if (elapsed >= total)
        {
            return (sheet.Frames.Count - 1, TimeSpan.MaxValue);
        }

        long end = 0;
        for (var i = 0; i < sheet.Frames.Count; i++)
        {
            end += sheet.Frames[i].DurationMs;
            if (elapsed < end)
            {
                return (i, _started + TimeSpan.FromMilliseconds((cycles * total) + end));
            }
        }

        return (sheet.Frames.Count - 1, TimeSpan.MaxValue);
    }
}

/// <summary>
/// Плавная смена масштаба голубя (M1.4): при переходе на монитор с другим DPI размер меняется
/// за <paramref name="duration"/>, а не рывком. Первое значение ставится сразу.
/// </summary>
public sealed class ScaleTransition(TimeSpan duration)
{
    private double _from;
    private double _to;
    private TimeSpan _start;
    private bool _initialized;

    public double Target => _to;

    public void SetTarget(double target, TimeSpan now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(target);

        if (!_initialized)
        {
            _from = _to = target;
            _initialized = true;
            return;
        }

        if (target == _to)
        {
            return;
        }

        _from = Value(now);
        _to = target;
        _start = now;
    }

    public double Value(TimeSpan now)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Масштаб ещё не задан.");
        }

        var t = duration <= TimeSpan.Zero ? 1.0 : Math.Clamp((now - _start) / duration, 0.0, 1.0);
        var eased = t * t * (3.0 - 2.0 * t);
        return _from + (_to - _from) * eased;
    }

    public bool IsAnimating(TimeSpan now) => _initialized && Value(now) != _to;
}

/// <summary>Где окно относительно точки опоры и какого оно размера, в физических пикселях.</summary>
/// <param name="Origin">Сдвиг левого верхнего угла окна от точки опоры голубя на экране.</param>
public readonly record struct SpriteLayout(SKSizeI Size, SKPointI Origin);

/// <summary>
/// Рисует голубя и его тень (ADR-0023). Голубь и тень — в разных окнах: тень полупрозрачная, и
/// в одном окне с голубем она перехватывала бы клики по тому, что под ней (значки панели задач).
/// </summary>
/// <remarks>
/// Масштаб <c>k</c> — физических пикселей экрана на логический пиксель рисунка: размер голубя
/// на экране (доля от рисунка) × масштаб монитора. Кадр листа при выводе только уменьшается.
/// </remarks>
public static class PigeonPainter
{
    /// <summary>На какой высоте над землёй тень исчезает совсем, логические пиксели рисунка.</summary>
    public const float ShadowFadeHeight = 150;

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public static SpriteLayout PigeonLayout(PigeonMetrics metrics, double k, bool facingLeft)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var size = new SKSizeI((int)Math.Ceiling(metrics.Frame.Width * k), (int)Math.Ceiling(metrics.Frame.Height * k));
        var anchorX = facingLeft ? metrics.Frame.Width - metrics.Anchor.X : metrics.Anchor.X;
        return new SpriteLayout(size, new SKPointI(-(int)Math.Round(anchorX * k), -(int)Math.Round(metrics.Anchor.Y * k)));
    }

    /// <summary>Очищает холст и рисует кадр так, что точка опоры попадает в <see cref="PigeonLayout"/>.</summary>
    public static void DrawPigeon(SKCanvas canvas, SpriteSheet sheet, int frameIndex, double k, bool facingLeft)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(sheet);

        var frame = sheet.Frames[frameIndex];
        var unit = (float)(k / sheet.Scale);
        var destination = SKRect.Create(frame.Offset.X * unit, frame.Offset.Y * unit, frame.Source.Width * unit, frame.Source.Height * unit);

        canvas.Clear(SKColors.Transparent);
        canvas.Save();
        if (facingLeft)
        {
            var width = (float)(sheet.Metrics.Frame.Width * k);
            canvas.Scale(-1, 1, width / 2, 0);
        }

        canvas.DrawImage(sheet.Image, SKRect.Create(frame.Source.Left, frame.Source.Top, frame.Source.Width, frame.Source.Height), destination, Sampling);
        canvas.Restore();
    }

    /// <summary>Окно тени: эллипс на земле под голубем; чем выше голубь, тем тень меньше и бледнее.</summary>
    public static SpriteLayout ShadowLayout(PigeonMetrics metrics, double k, bool facingLeft)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var shadow = metrics.Shadow;
        var width = (int)Math.Ceiling(2 * shadow.Radius * k) + 2;
        var height = (int)Math.Ceiling(2 * shadow.Radius * shadow.Flatten * k) + 2;
        var dx = (facingLeft ? -1 : 1) * (shadow.X - metrics.Anchor.X) * k;
        var dy = (shadow.Y - metrics.Anchor.Y) * k;
        return new SpriteLayout(new SKSizeI(width, height), new SKPointI((int)Math.Round(dx - width / 2.0), (int)Math.Round(dy - height / 2.0)));
    }

    /// <param name="height">Высота голубя над землёй, логические пиксели рисунка.</param>
    public static void DrawShadow(SKCanvas canvas, PigeonMetrics metrics, double k, double height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(metrics);

        canvas.Clear(SKColors.Transparent);
        var lift = (float)Math.Clamp(height / ShadowFadeHeight, 0.0, 1.0);
        if (lift >= 1)
        {
            return;
        }

        var shadow = metrics.Shadow;
        var radius = (float)(shadow.Radius * k) * (1 - (0.5f * lift));
        var center = new SKPoint(canvas.LocalClipBounds.MidX, canvas.LocalClipBounds.MidY);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(center, radius,
                [new SKColor(0, 0, 0, (byte)Math.Round(255 * shadow.Alpha * (1 - lift))), SKColors.Transparent], SKShaderTileMode.Clamp),
        };
        canvas.Save();
        canvas.Scale(1, shadow.Flatten, center.X, center.Y);
        canvas.DrawCircle(center, radius, paint);
        canvas.Restore();
    }
}
