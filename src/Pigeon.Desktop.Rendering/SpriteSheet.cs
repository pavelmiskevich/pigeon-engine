using System.Globalization;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace Pigeon.Desktop.Rendering;

/// <summary>
/// Кадр в листе: где лежит (<see cref="Source"/>) и где стоит внутри исходного, необрезанного
/// кадра (<see cref="Offset"/>). Пиксели — листа этого масштаба.
/// </summary>
public readonly record struct SpriteFrame(SKRectI Source, SKPointI Offset, int DurationMs);

/// <summary>Тень на земле в логических пикселях кадра: центр, радиус, сплющивание, непрозрачность.</summary>
public sealed record ShadowSpec(float X, float Y, float Radius, float Flatten, float Alpha);

/// <summary>
/// Геометрия голубя в логических пикселях (ADR-0023, <c>meta.pigeon</c>): размер кадра, точка
/// опоры — середина между стопами на земле — и длина шага за цикл walk.
/// </summary>
public sealed record PigeonMetrics(SKSize Frame, SKPoint Anchor, float Stride, ShadowSpec Shadow);

/// <summary>
/// Лист одного клипа в одном масштабе (формат Aseprite JSON, ADR-0023): кадры обрезаны по
/// содержимому и плотно уложены. Изображение декодируется сразу при загрузке.
/// </summary>
public sealed class SpriteSheet : IDisposable
{
    private SpriteSheet(string clip, int scale, bool loop, IReadOnlyList<SpriteFrame> frames, PigeonMetrics metrics, SKImage image)
    {
        Clip = clip;
        Scale = scale;
        Loop = loop;
        Frames = frames;
        Metrics = metrics;
        Image = image;
    }

    public string Clip { get; }

    /// <summary>Во сколько раз лист крупнее логических пикселей.</summary>
    public int Scale { get; }

    public bool Loop { get; }

    public IReadOnlyList<SpriteFrame> Frames { get; }

    public PigeonMetrics Metrics { get; }

    public SKImage Image { get; }

    /// <summary>Сколько памяти занимает декодированный лист.</summary>
    public long Bytes => (long)Image.Width * Image.Height * 4;

    /// <summary>Читает лист из JSON и PNG рядом с ним.</summary>
    public static SpriteSheet Load(string jsonPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(jsonPath);

        var root = JsonNode.Parse(File.ReadAllText(jsonPath)) ?? throw new InvalidDataException($"Пустой лист: {jsonPath}.");
        var meta = root["meta"] ?? throw new InvalidDataException($"В листе {jsonPath} нет meta.");
        var scale = int.Parse((string?)meta["scale"] ?? "1", CultureInfo.InvariantCulture);

        var frames = (root["frames"]?.AsArray() ?? throw new InvalidDataException($"В листе {jsonPath} нет кадров."))
            .Select(f => new SpriteFrame(
                SKRectI.Create((int)f!["frame"]!["x"]!, (int)f["frame"]!["y"]!, (int)f["frame"]!["w"]!, (int)f["frame"]!["h"]!),
                new SKPointI((int)f["spriteSourceSize"]!["x"]!, (int)f["spriteSourceSize"]!["y"]!),
                (int)f["duration"]!))
            .ToList();

        var tag = meta["frameTags"]?.AsArray().SingleOrDefault()
            ?? throw new InvalidDataException($"Лист {jsonPath} должен описывать ровно один клип.");

        var image = Path.Combine(Path.GetDirectoryName(jsonPath) ?? ".", (string)meta["image"]!);
        using var encoded = SKImage.FromEncodedData(image) ?? throw new InvalidDataException($"Не читается изображение {image}.");
        return new SpriteSheet(
            (string)tag["name"]!,
            scale,
            ((string?)tag["repeat"] ?? "0") == "0",
            frames,
            ParseMetrics(meta["pigeon"] ?? throw new InvalidDataException($"В листе {jsonPath} нет meta.pigeon."), scale),
            encoded.ToRasterImage(ensurePixelData: true));
    }

    public void Dispose() => Image.Dispose();

    private static PigeonMetrics ParseMetrics(JsonNode pigeon, int scale)
    {
        var frame = pigeon["logicalFrame"]!;
        var shadow = pigeon["shadow"] ?? throw new InvalidDataException("В meta.pigeon нет тени: лист собран старым генератором.");
        return new PigeonMetrics(
            new SKSize((float)frame["w"]!, (float)frame["h"]!),
            new SKPoint((float)pigeon["anchor"]!["x"]! / scale, (float)pigeon["anchor"]!["y"]! / scale),
            (float)pigeon["stride"]!,
            new ShadowSpec((float)shadow["x"]!, (float)shadow["y"]!, (float)shadow["radius"]!, (float)shadow["flatten"]!, (float)shadow["alpha"]!));
    }
}

/// <summary>
/// Листы голубя в каталоге: <c>{клип}@{масштаб}x.json</c>. Клип грузится при первом обращении и
/// выгружается, если долго не нужен, — в памяти только то, что голубь сейчас делает.
/// </summary>
public sealed class SpriteLibrary : IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<(string Clip, int Scale), (SpriteSheet Sheet, TimeSpan LastUsed)> _loaded = [];

    public SpriteLibrary(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!File.Exists(Path.Combine(directory, "idle@1x.json")))
        {
            throw new DirectoryNotFoundException(
                $"В «{directory}» нет листов голубя. Соберите их: dotnet run --project tools/Pigeon.SpriteBake -c Release");
        }

        _directory = directory;
    }

    /// <summary>Сколько листов сейчас в памяти.</summary>
    public int LoadedCount => _loaded.Count;

    public long LoadedBytes => _loaded.Values.Sum(l => l.Sheet.Bytes);

    /// <summary>
    /// Лист наименьшего масштаба, не меньшего нужного (ADR-0023): кадр при выводе только
    /// уменьшается, а лишняя память не тратится.
    /// </summary>
    public static int BucketFor(double displayScale) => displayScale <= 1.0 ? 1 : displayScale <= 2.0 ? 2 : 3;

    public SpriteSheet Get(string clip, int scale, TimeSpan now)
    {
        ArgumentException.ThrowIfNullOrEmpty(clip);

        if (!_loaded.TryGetValue((clip, scale), out var entry))
        {
            entry = (SpriteSheet.Load(Path.Combine(_directory, $"{clip}@{scale}x.json")), now);
        }

        _loaded[(clip, scale)] = (entry.Sheet, now);
        return entry.Sheet;
    }

    /// <summary>Выгружает листы, которые не запрашивались дольше <paramref name="idle"/>.</summary>
    public int ReleaseUnused(TimeSpan now, TimeSpan idle)
    {
        var stale = _loaded.Where(l => now - l.Value.LastUsed > idle).Select(l => l.Key).ToList();
        foreach (var key in stale)
        {
            _loaded[key].Sheet.Dispose();
            _loaded.Remove(key);
        }

        return stale.Count;
    }

    public void Dispose()
    {
        foreach (var (sheet, _) in _loaded.Values)
        {
            sheet.Dispose();
        }

        _loaded.Clear();
    }
}
