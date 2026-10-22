#if !MPHREAD_SERVER
using System;
using System.Globalization;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Settings;

// The wheel is presentation only. The existing validated setting row owns its RGB draft.
internal static class NativeOutlineColorWheel
{
    internal static (float Hue, float Saturation, float Value) Read(string hex, float previousHue = 0)
    {
        uint rgb = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        float r = (rgb >> 16) / 255f, g = ((rgb >> 8) & 255) / 255f, b = (rgb & 255) / 255f;
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        float hue = delta == 0 ? previousHue : (max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) / 6;
        return (hue - MathF.Floor(hue), max == 0 ? 0 : delta / max, max);
    }

    internal static string Hex(float hue, float saturation, float value)
    {
        var (r, g, b) = Rgb(hue, saturation, value);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static (byte R, byte G, byte B) Rgb(float hue, float saturation, float value)
    {
        hue = (hue - MathF.Floor(hue)) * 6;
        saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        float chroma = saturation * value, x = chroma * (1 - MathF.Abs(hue % 2 - 1)), m = value - chroma;
        var (r, g, b) = (int)hue switch
        {
            0 => (chroma, x, 0f), 1 => (x, chroma, 0f), 2 => (0f, chroma, x),
            3 => (0f, x, chroma), 4 => (x, 0f, chroma), _ => (chroma, 0f, x)
        };
        return (RmlUiCanvasVertex.Quantize(Math.Clamp(r + m, 0, 1)), RmlUiCanvasVertex.Quantize(Math.Clamp(g + m, 0, 1)), RmlUiCanvasVertex.Quantize(Math.Clamp(b + m, 0, 1)));
    }

    internal static (float Hue, float Saturation) Pick(float x, float y, float width, float height)
    {
        float radius = Math.Min(width, height) / 2;
        x -= width / 2; y -= height / 2;
        float hue = MathF.Atan2(y, x) / (2 * MathF.PI);
        return (hue - MathF.Floor(hue), Math.Clamp(MathF.Sqrt(x * x + y * y) / radius, 0, 1));
    }

    internal static RmlUiCanvasFrame Frame(float value)
    {
        const int segments = 96;
        var vertices = new RmlUiCanvasVertex[segments + 2];
        var indices = new int[segments * 3];
        byte center = RmlUiCanvasVertex.Quantize(value);
        vertices[0] = new(100, 100, center, center, center, 255, 0, 0);
        for (int i = 0; i <= segments; i++)
        {
            float hue = i / (float)segments, angle = hue * 2 * MathF.PI;
            var (r, g, b) = Rgb(hue, 1, value);
            vertices[i + 1] = new(100 + 100 * MathF.Cos(angle), 100 + 100 * MathF.Sin(angle), r, g, b, 255, 0, 0);
            if (i == segments) continue;
            indices[i * 3] = 0; indices[i * 3 + 1] = i + 1; indices[i * 3 + 2] = i + 2;
        }
        return new(200, 200, vertices, indices, new[] { new RmlUiCanvasBatch(uint.MaxValue, 0, (uint)indices.Length) });
    }

    internal static RmlUiCanvasFrame BrightnessFrame(float hue, float saturation)
    {
        var (r,g,b)=Rgb(hue,saturation,1);
        RmlUiCanvasVertex[] vertices = [new(0,0,0,0,0,255,0,0),new(200,0,r,g,b,255,0,0),
            new(200,20,r,g,b,255,0,0),new(0,20,0,0,0,255,0,0)];
        return new(200,20,vertices,new[] {0,1,2,0,2,3},new[] {new RmlUiCanvasBatch(uint.MaxValue,0,6)});
    }
}

#endif
