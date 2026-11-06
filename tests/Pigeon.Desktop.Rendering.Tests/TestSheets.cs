using System.Globalization;
using SkiaSharp;

namespace Pigeon.Desktop.Rendering.Tests;

/// <summary>
/// Маленькие листы в формате генератора, создаваемые в тесте: кадр 16×12 логических пикселей,
/// точка опоры (8, 10), обрезанные кадры 4×3 на сдвигах (5, 6) и (6, 6).
/// </summary>
internal sealed class TestSheets : IDisposable
{
    public static readonly SKColor First = SKColors.Red;
    public static readonly SKColor Second = SKColors.Lime;

    public TestSheets()
    {
        Directory = Path.Combine(Path.GetTempPath(), "pigeon-sheets", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        foreach (var scale in new[] { 1, 2 })
        {
            Write("idle", scale, loop: true, durationMs: 100);
            Write("peck", scale, loop: false, durationMs: 50);
        }
    }

    public string Directory { get; }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Временный каталог: если файл ещё занят, его уберёт система.
        }
    }

    private void Write(string clip, int scale, bool loop, int durationMs)
    {
        var w = 4 * scale;
        var h = 3 * scale;
        using (var surface = SKSurface.Create(new SKImageInfo((2 * w) + 1, h, SKColorType.Rgba8888, SKAlphaType.Premul)))
        {
            surface.Canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint();
            paint.Color = First;
            surface.Canvas.DrawRect(0, 0, w, h, paint);
            paint.Color = Second;
            surface.Canvas.DrawRect(w + 1, 0, w, h, paint);
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(Directory, $"{clip}@{scale}x.png"), data.ToArray());
        }

        string Frame(int x, int ox) => string.Create(CultureInfo.InvariantCulture,
            $$"""{ "frame": { "x": {{x}}, "y": 0, "w": {{w}}, "h": {{h}} }, "trimmed": true, "spriteSourceSize": { "x": {{ox * scale}}, "y": {{6 * scale}}, "w": {{w}}, "h": {{h}} }, "sourceSize": { "w": {{16 * scale}}, "h": {{12 * scale}} }, "duration": {{durationMs}} }""");

        var json = string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "frames": [ {{Frame(0, 5)}}, {{Frame(w + 1, 6)}} ],
              "meta": {
                "image": "{{clip}}@{{scale}}x.png",
                "scale": "{{scale}}",
                "frameTags": [ { "name": "{{clip}}", "from": 0, "to": 1, "repeat": "{{(loop ? "0" : "1")}}" } ],
                "pigeon": {
                  "logicalFrame": { "w": 16, "h": 12 },
                  "anchor": { "x": {{8 * scale}}, "y": {{10 * scale}} },
                  "stride": 4,
                  "shadow": { "x": 8, "y": 10, "radius": 6, "flatten": 0.5, "alpha": 0.5 }
                }
              }
            }
            """);
        File.WriteAllText(Path.Combine(Directory, $"{clip}@{scale}x.json"), json);
    }
}
