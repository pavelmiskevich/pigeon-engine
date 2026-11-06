// pigeon-spritebake: запекает анимации голубя (MVP-0, S0.8; MVP-1, M1.4; ADR-0023).
//
//   dotnet run --project tools/Pigeon.SpriteBake -c Release -- [--out assets/sprites/pigeon-black]
//
// Для каждого масштаба (1×, 2×, 3×) и каждого клипа — свой лист PNG и JSON в формате Aseprite:
// кадры обрезаны по содержимому (trimmed) и плотно уложены, поэтому продукт грузит только нужные
// клипы и не держит в памяти пустые поля. Тени в кадрах нет — её рисует движок по meta.pigeon.shadow.
// Тот же формат экспортирует Aseprite, поэтому кадры художника заменят процедурные без изменений
// в движке.
//
// Дополнительно пишется старый атлас atlas@Nx (все клипы в одном листе, кадры без обрезки, тень в
// кадре) — его читают прототипы MVP-0 (Overlay.Win32Skia, Perch.Win32).

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pigeon.SpriteBake;
using SkiaSharp;

const int FrameWidth = 204;
const int FrameHeight = 160;
const float OffsetX = -12;
const float OffsetY = 8;
const int LegacyColumns = 8;
const int Padding = 1;
int[] scales = [1, 2, 3];

var output = args.Length >= 2 && args[0] == "--out" ? args[1] : Path.Combine("assets", "sprites", "pigeon-black");
Directory.CreateDirectory(output);

// Клипы: имя, число кадров, частота, зациклен ли, поза.
var clips = new (string Name, int Frames, int Fps, bool Loop, Func<float, Pose> Pose)[]
{
    ("idle", 16, 10, true, PigeonRig.Idle),
    ("walk", 24, 30, true, PigeonRig.Walk),
    ("peck", 20, 30, false, PigeonRig.Peck),
    ("fly", 12, 40, true, PigeonRig.Fly),
    ("sleep", 8, 4, true, PigeonRig.Sleep),
};

var json = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

foreach (var scale in scales)
{
    var frameW = FrameWidth * scale;
    var frameH = FrameHeight * scale;
    long fullBytes = 0, packedBytes = 0;

    foreach (var clip in clips)
    {
        var frames = Enumerable.Range(0, clip.Frames)
            .Select(f => Trim(Render(clip.Pose(f / (float)clip.Frames), scale, withShadow: false)))
            .ToList();
        var placements = Pack(frames.Select(f => (f.Image.Width, f.Image.Height)).ToList(), out var sheetW, out var sheetH);

        using var sheet = SKSurface.Create(new SKImageInfo(sheetW, sheetH, SKColorType.Rgba8888, SKAlphaType.Premul));
        sheet.Canvas.Clear(SKColors.Transparent);
        var jsonFrames = new JsonArray();
        for (var i = 0; i < frames.Count; i++)
        {
            var (image, source) = frames[i];
            var (x, y) = placements[i];
            sheet.Canvas.DrawImage(image, x, y);
            jsonFrames.Add(new JsonObject
            {
                ["filename"] = $"{clip.Name} {i}",
                ["frame"] = Rect(x, y, image.Width, image.Height),
                ["rotated"] = false,
                ["trimmed"] = true,
                ["spriteSourceSize"] = Rect(source.X, source.Y, image.Width, image.Height),
                ["sourceSize"] = new JsonObject { ["w"] = frameW, ["h"] = frameH },
                ["duration"] = (int)Math.Round(1000.0 / clip.Fps),
            });
            image.Dispose();
        }

        var name = $"{clip.Name}@{scale}x";
        Save(sheet, Path.Combine(output, name + ".png"));
        var document = Document(jsonFrames, name + ".png", sheetW, sheetH, scale,
            [Tag(clip.Name, 0, clip.Frames - 1, clip.Loop)], withShadow: false);
        File.WriteAllText(Path.Combine(output, name + ".json"), document.ToJsonString(json) + "\n");

        fullBytes += (long)frameW * frameH * 4 * clip.Frames;
        packedBytes += (long)sheetW * sheetH * 4;
    }

    WriteLegacyAtlas(scale);
    Console.WriteLine($"@{scale}x: клипов {clips.Length}, в памяти {packedBytes / 1048576.0:0.0} МБ вместо {fullBytes / 1048576.0:0.0} МБ без обрезки");
}

return 0;

SKImage Render(Pose pose, int scale, bool withShadow)
{
    using var surface = SKSurface.Create(new SKImageInfo(FrameWidth * scale, FrameHeight * scale, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.Scale(scale);
    surface.Canvas.Translate(OffsetX, OffsetY);
    PigeonRig.Draw(surface.Canvas, pose, withShadow);
    return surface.Snapshot().ToRasterImage(ensurePixelData: true);
}

// Обрезка по непрозрачным пикселям с полем в 1 пиксель — чтобы сглаживание при масштабировании
// не тянуло соседний кадр листа.
(SKImage Image, SKPointI Offset) Trim(SKImage frame)
{
    using var pixels = frame.PeekPixels();
    int left = frame.Width, top = frame.Height, right = -1, bottom = -1;
    for (var y = 0; y < frame.Height; y++)
    {
        for (var x = 0; x < frame.Width; x++)
        {
            if (pixels.GetPixelColor(x, y).Alpha != 0)
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
    }

    if (right < 0)
    {
        left = top = 0;
        right = bottom = 0;
    }

    left = Math.Max(0, left - Padding);
    top = Math.Max(0, top - Padding);
    right = Math.Min(frame.Width - 1, right + Padding);
    bottom = Math.Min(frame.Height - 1, bottom + Padding);
    var trimmed = frame.Subset(SKRectI.Create(left, top, right - left + 1, bottom - top + 1))!.ToRasterImage(ensurePixelData: true);
    frame.Dispose();
    return (trimmed, new SKPointI(left, top));
}

// Укладка полками: кадры по убыванию высоты, ширина листа — около квадрата суммарной площади.
static List<(int X, int Y)> Pack(List<(int W, int H)> sizes, out int width, out int height)
{
    var area = sizes.Sum(s => (long)(s.W + Padding) * (s.H + Padding));
    width = Math.Max(sizes.Max(s => s.W), (int)Math.Ceiling(Math.Sqrt(area) * 1.1));
    var order = Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i].H).ThenBy(i => i).ToList();
    var placements = new (int X, int Y)[sizes.Count];
    int x = 0, y = 0, shelf = 0, used = 0;
    foreach (var i in order)
    {
        var (w, h) = sizes[i];
        if (x + w > width)
        {
            x = 0;
            y += shelf + Padding;
            shelf = 0;
        }

        placements[i] = (x, y);
        x += w + Padding;
        shelf = Math.Max(shelf, h);
        used = Math.Max(used, x - Padding);
    }

    width = used;
    height = y + shelf;
    return [.. placements];
}

void WriteLegacyAtlas(int scale)
{
    var frameW = FrameWidth * scale;
    var frameH = FrameHeight * scale;
    var total = clips.Sum(c => c.Frames);
    var rows = (total + LegacyColumns - 1) / LegacyColumns;
    using var surface = SKSurface.Create(new SKImageInfo(LegacyColumns * frameW, rows * frameH, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);

    var frames = new JsonArray();
    var tags = new List<JsonObject>();
    var index = 0;
    foreach (var clip in clips)
    {
        tags.Add(Tag(clip.Name, index, index + clip.Frames - 1, clip.Loop));
        for (var f = 0; f < clip.Frames; f++, index++)
        {
            var x = index % LegacyColumns * frameW;
            var y = index / LegacyColumns * frameH;
            using (var image = Render(clip.Pose(f / (float)clip.Frames), scale, withShadow: true))
            {
                surface.Canvas.DrawImage(image, x, y);
            }

            frames.Add(new JsonObject
            {
                ["filename"] = $"{clip.Name} {f}",
                ["frame"] = Rect(x, y, frameW, frameH),
                ["rotated"] = false,
                ["trimmed"] = false,
                ["spriteSourceSize"] = Rect(0, 0, frameW, frameH),
                ["sourceSize"] = new JsonObject { ["w"] = frameW, ["h"] = frameH },
                ["duration"] = (int)Math.Round(1000.0 / clip.Fps),
            });
        }
    }

    var imageName = $"atlas@{scale}x.png";
    Save(surface, Path.Combine(output, imageName));
    var document = Document(frames, imageName, LegacyColumns * frameW, rows * frameH, scale, tags, withShadow: true);
    File.WriteAllText(Path.Combine(output, $"atlas@{scale}x.json"), document.ToJsonString(json) + "\n");
}

static void Save(SKSurface surface, string path)
{
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var file = File.Create(path);
    data.SaveTo(file);
}

static JsonObject Document(JsonArray frames, string image, int width, int height, int scale, IEnumerable<JsonObject> tags, bool withShadow)
{
    var pigeon = new JsonObject
    {
        ["logicalFrame"] = new JsonObject { ["w"] = FrameWidth, ["h"] = FrameHeight },
        // Точка опоры — середина между стопами на земле, в пикселях исходного кадра этого масштаба.
        ["anchor"] = new JsonObject { ["x"] = (118 + OffsetX) * scale, ["y"] = (PigeonRig.Ground + OffsetY) * scale },
        // Длина шага за цикл walk в логических пикселях: скорость голубя = stride / цикл.
        ["stride"] = PigeonRig.Stride,
    };

    if (!withShadow)
    {
        // Тень для движка — в логических пикселях кадра: центр, радиус, сплющивание, непрозрачность.
        pigeon["shadow"] = new JsonObject
        {
            ["x"] = PigeonRig.ShadowX + OffsetX,
            ["y"] = PigeonRig.Ground + 2 + OffsetY,
            ["radius"] = PigeonRig.ShadowRadius,
            ["flatten"] = PigeonRig.ShadowFlatten,
            ["alpha"] = Math.Round(PigeonRig.ShadowAlpha / 255.0, 3),
        };
    }

    return new JsonObject
    {
        ["frames"] = frames,
        ["meta"] = new JsonObject
        {
            ["app"] = "pigeon-spritebake",
            ["version"] = withShadow ? "1" : "2",
            ["image"] = image,
            ["format"] = "RGBA8888",
            ["size"] = new JsonObject { ["w"] = width, ["h"] = height },
            ["scale"] = scale.ToString(CultureInfo.InvariantCulture),
            ["frameTags"] = new JsonArray([.. tags]),
            ["pigeon"] = pigeon,
        },
    };
}

static JsonObject Tag(string name, int from, int to, bool loop) => new()
{
    ["name"] = name,
    ["from"] = from,
    ["to"] = to,
    ["direction"] = "forward",
    ["repeat"] = loop ? "0" : "1",
};

static JsonObject Rect(int x, int y, int w, int h) => new() { ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h };
