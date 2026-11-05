namespace Pigeon.World.Desktop.Perches;

/// <summary>Прямоугольник окна или монитора в физических пикселях; Right и Bottom не включаются.</summary>
public readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>Отрезок кромки [Start, End) по оси X.</summary>
public readonly record struct EdgeSpan(int Start, int End)
{
    public int Length => End - Start;
}

/// <summary>
/// Видимые участки верхних кромок окон с учётом z-order (ТЗ §11.4, MVP-0 S0.4). Участок кромки
/// пригоден для насеста, если над ним свободна полоса высотой <c>clearance</c> (там стоит голубь)
/// и он лежит на мониторе. Окна выше по z-order вырезают из кромки свои проекции на ось X.
/// </summary>
public static class EdgeOcclusion
{
    /// <param name="index">Номер окна в <paramref name="zOrderTopFirst"/>.</param>
    /// <param name="zOrderTopFirst">Видимые окна, верхнее первым.</param>
    /// <param name="screens">Мониторы.</param>
    /// <param name="clearance">Высота свободной полосы над кромкой.</param>
    /// <param name="minLength">Короче этого участок отбрасывается: голубь на нём не поместится.</param>
    public static IReadOnlyList<EdgeSpan> VisibleTopEdge(
        int index,
        IReadOnlyList<PixelBox> zOrderTopFirst,
        IReadOnlyList<PixelBox> screens,
        int clearance,
        int minLength)
    {
        ArgumentNullException.ThrowIfNull(zOrderTopFirst);
        ArgumentNullException.ThrowIfNull(screens);
        ArgumentOutOfRangeException.ThrowIfNegative(clearance);

        var window = zOrderTopFirst[index];
        var bandTop = window.Top - clearance;
        var bandBottom = window.Top + 1;

        // Кромка видна только там, где над ней монитор: полоса целиком на экране.
        var spans = new List<EdgeSpan>(4);
        foreach (var screen in screens)
        {
            if (screen.Top <= bandTop && screen.Bottom >= bandBottom)
            {
                var start = Math.Max(window.Left, screen.Left);
                var end = Math.Min(window.Right, screen.Right);
                if (end > start)
                {
                    spans.Add(new EdgeSpan(start, end));
                }
            }
        }

        spans = Merge(spans);
        for (var i = 0; i < index && spans.Count > 0; i++)
        {
            var above = zOrderTopFirst[i];
            if (above.Top < bandBottom && above.Bottom > bandTop)
            {
                spans = Subtract(spans, above.Left, above.Right);
            }
        }

        spans.RemoveAll(s => s.Length < minLength);
        return spans;
    }

    private static List<EdgeSpan> Subtract(List<EdgeSpan> spans, int cutStart, int cutEnd)
    {
        var result = new List<EdgeSpan>(spans.Count + 1);
        foreach (var span in spans)
        {
            if (cutEnd <= span.Start || cutStart >= span.End)
            {
                result.Add(span);
                continue;
            }

            if (cutStart > span.Start)
            {
                result.Add(new EdgeSpan(span.Start, cutStart));
            }

            if (cutEnd < span.End)
            {
                result.Add(new EdgeSpan(cutEnd, span.End));
            }
        }

        return result;
    }

    private static List<EdgeSpan> Merge(List<EdgeSpan> spans)
    {
        if (spans.Count < 2)
        {
            return spans;
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<EdgeSpan>(spans.Count) { spans[0] };
        for (var i = 1; i < spans.Count; i++)
        {
            var last = merged[^1];
            if (spans[i].Start <= last.End)
            {
                merged[^1] = last with { End = Math.Max(last.End, spans[i].End) };
            }
            else
            {
                merged.Add(spans[i]);
            }
        }

        return merged;
    }
}
