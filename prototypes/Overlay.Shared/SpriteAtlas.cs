using System.Globalization;
using System.Text.Json.Nodes;

namespace Pigeon.Prototypes.Overlay;

public readonly record struct AtlasRect(int X, int Y, int W, int H);

/// <param name="From">Первый кадр клипа в массиве кадров атласа.</param>
public sealed record AtlasClip(string Name, int From, int To, bool Loop, int FrameMs)
{
    public int Count => To - From + 1;
}

/// <summary>
/// Атлас в формате Aseprite JSON (массив кадров + meta.frameTags) с секцией meta.pigeon: точка
/// опоры и длина шага (ADR-0023).
/// </summary>
public sealed class SpriteAtlas
{
    private SpriteAtlas(IReadOnlyList<AtlasRect> frames, IReadOnlyDictionary<string, AtlasClip> clips,
        string image, int scale, float anchorX, float anchorY, float stride)
    {
        Frames = frames;
        Clips = clips;
        Image = image;
        Scale = scale;
        AnchorX = anchorX;
        AnchorY = anchorY;
        Stride = stride;
    }

    public IReadOnlyList<AtlasRect> Frames { get; }

    public IReadOnlyDictionary<string, AtlasClip> Clips { get; }

    /// <summary>Имя файла атласа рядом с JSON.</summary>
    public string Image { get; }

    /// <summary>Во сколько раз атлас крупнее логических пикселей.</summary>
    public int Scale { get; }

    /// <summary>Точка опоры (середина между стопами на земле) в пикселях кадра атласа.</summary>
    public float AnchorX { get; }

    public float AnchorY { get; }

    /// <summary>Длина шага за цикл walk, логические пиксели.</summary>
    public float Stride { get; }

    public static SpriteAtlas Parse(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new FormatException("Пустой атлас.");
        var frames = root["frames"]!.AsArray().Select(f =>
        {
            var r = f!["frame"]!;
            return (Rect: new AtlasRect((int)r["x"]!, (int)r["y"]!, (int)r["w"]!, (int)r["h"]!), Duration: (int)f["duration"]!);
        }).ToList();

        var meta = root["meta"]!;
        var clips = meta["frameTags"]!.AsArray().Select(t =>
        {
            int from = (int)t!["from"]!, to = (int)t["to"]!;
            var repeat = (string?)t["repeat"] ?? "0";
            return new AtlasClip((string)t["name"]!, from, to, repeat == "0", frames[from].Duration);
        }).ToDictionary(c => c.Name, StringComparer.Ordinal);

        var pigeon = meta["pigeon"]!;
        return new SpriteAtlas(
            [.. frames.Select(f => f.Rect)],
            clips,
            (string)meta["image"]!,
            int.Parse((string)meta["scale"]!, CultureInfo.InvariantCulture),
            (float)pigeon["anchor"]!["x"]!,
            (float)pigeon["anchor"]!["y"]!,
            (float)pigeon["stride"]!);
    }
}

/// <summary>
/// Выбор клипа и кадра по состоянию голубя. Кадр шага считается от пройденного пути: за длину
/// шага проходит один цикл, поэтому опорная лапа не скользит при любой скорости.
/// </summary>
public sealed class SpriteAnimator(SpriteAtlas atlas)
{
    private string _clip = "idle";
    private double _clipTime;
    private double _walked;

    public string Clip => _clip;

    /// <param name="distance">Путь по земле за кадр, логические пиксели рисунка атласа.</param>
    public int Next(PigeonModel model, double dt, double distance)
    {
        var clip = model.Flying ? "fly" : model.Mode == PigeonMode.Moving ? "walk" : "idle";
        if (clip != _clip)
        {
            _clip = clip;
            _clipTime = 0;
        }

        _clipTime += dt;
        var c = atlas.Clips[_clip];
        int index;
        if (_clip == "walk")
        {
            _walked += distance;
            index = (int)(_walked / atlas.Stride * c.Count) % c.Count;
        }
        else
        {
            var frame = (int)(_clipTime * 1000 / c.FrameMs);
            index = c.Loop ? frame % c.Count : Math.Min(frame, c.Count - 1);
        }

        return c.From + index;
    }
}
