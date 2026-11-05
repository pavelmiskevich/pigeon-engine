// pigeon-spritebake: запекает атлас анимаций голубя (MVP-0, S0.8; ADR-0023).
//
//   dotnet run --project tools/Pigeon.SpriteBake -c Release -- [--out assets/sprites/pigeon-black]
//
// На выходе для каждого масштаба (1×, 2×, 3×) — атлас PNG и JSON в формате Aseprite (массив
// кадров, frameTags для клипов). Тот же формат экспортирует Aseprite, поэтому кадры художника
// заменят процедурные без изменений в движке.

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
const int Columns = 8;
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

var total = clips.Sum(c => c.Frames);
var rows = (total + Columns - 1) / Columns;

foreach (var scale in scales)
{
    var info = new SKImageInfo(Columns * FrameWidth * scale, rows * FrameHeight * scale, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    var frames = new JsonArray();
    var tags = new JsonArray();
    var index = 0;
    foreach (var clip in clips)
    {
        tags.Add(new JsonObject
        {
            ["name"] = clip.Name,
            ["from"] = index,
            ["to"] = index + clip.Frames - 1,
            ["direction"] = "forward",
            ["repeat"] = clip.Loop ? "0" : "1",
        });

        for (var f = 0; f < clip.Frames; f++, index++)
        {
            var x = index % Columns * FrameWidth * scale;
            var y = index / Columns * FrameHeight * scale;
            canvas.Save();
            canvas.ClipRect(new SKRect(x, y, x + (FrameWidth * scale), y + (FrameHeight * scale)));
            canvas.Translate(x, y);
            canvas.Scale(scale);
            canvas.Translate(OffsetX, OffsetY);
            PigeonRig.Draw(canvas, clip.Pose(f / (float)clip.Frames));
            canvas.Restore();

            frames.Add(new JsonObject
            {
                ["filename"] = $"{clip.Name} {f}",
                ["frame"] = Rect(x, y, FrameWidth * scale, FrameHeight * scale),
                ["rotated"] = false,
                ["trimmed"] = false,
                ["spriteSourceSize"] = Rect(0, 0, FrameWidth * scale, FrameHeight * scale),
                ["sourceSize"] = new JsonObject { ["w"] = FrameWidth * scale, ["h"] = FrameHeight * scale },
                ["duration"] = (int)Math.Round(1000.0 / clip.Fps),
            });
        }
    }

    var imageName = $"atlas@{scale}x.png";
    using (var image = surface.Snapshot())
    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
    using (var file = File.Create(Path.Combine(output, imageName)))
    {
        data.SaveTo(file);
    }

    var document = new JsonObject
    {
        ["frames"] = frames,
        ["meta"] = new JsonObject
        {
            ["app"] = "pigeon-spritebake",
            ["version"] = "1",
            ["image"] = imageName,
            ["format"] = "RGBA8888",
            ["size"] = new JsonObject { ["w"] = info.Width, ["h"] = info.Height },
            ["scale"] = scale.ToString(CultureInfo.InvariantCulture),
            ["frameTags"] = tags,
            // Точка опоры — середина между стопами на земле, в пикселях кадра этого масштаба.
            // stride — длина шага за цикл walk в логических пикселях: скорость голубя = stride / цикл.
            ["pigeon"] = new JsonObject
            {
                ["logicalFrame"] = new JsonObject { ["w"] = FrameWidth, ["h"] = FrameHeight },
                ["anchor"] = new JsonObject { ["x"] = (118 - OffsetX) * scale, ["y"] = (PigeonRig.Ground - OffsetY) * scale },
                ["stride"] = PigeonRig.Stride,
            },
        },
    };
    File.WriteAllText(Path.Combine(output, $"atlas@{scale}x.json"),
        document.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
    Console.WriteLine($"{imageName}: {info.Width}×{info.Height}, кадров {total}");
}

return 0;

static JsonObject Rect(int x, int y, int w, int h) => new() { ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h };
