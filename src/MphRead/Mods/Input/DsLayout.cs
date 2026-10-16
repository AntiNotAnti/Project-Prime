using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input;

/// <summary>
/// One source of truth for two equal DS-style viewports and bottom-screen
/// pointer/radar geometry. The render-compositor that presents these viewports
/// is intentionally a separate slice; this pure mapping is safe without GL.
/// </summary>
internal static class DsLayout
{
    public static bool Enabled { get; set; }

    private static string _screenAspect = "4:3";
    public static string ScreenAspect
    {
        get => _screenAspect;
        set => _screenAspect = value == "5:3" ? "5:3" : "4:3";
    }
    public static int AspectWidth => ScreenAspect == "5:3" ? 5 : 4;
    public static float Aspect => AspectWidth / 3f;
    public static float HorizontalStretch => Active ? AspectWidth / 4f : 1f;
    public static bool Active => Enabled && PointerInput.StylusMode && !OperatingSystem.IsAndroid();

    private static float _radarSize = 1f, _radarX = .5f, _radarY = .5f;
    public static float RadarSize
    {
        get => _radarSize;
        set => _radarSize = float.IsFinite(value) ? Math.Clamp(value, .25f, 3f) : 1f;
    }
    public static float RadarX
    {
        get => _radarX;
        set => _radarX = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : .5f;
    }
    public static float RadarY
    {
        get => _radarY;
        set => _radarY = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : .5f;
    }
    // All coordinates are 768x576 *un-stretched* authoring units.
    // Prevent huge radar sizes from inverting travel bounds.
    public static Vector2 RadarCenter
    {
        get
        {
            float margin = 65f * RadarSize + 12;
            float insetX = MathF.Min(384, margin / HorizontalStretch);
            float insetY = MathF.Min(288, margin);
            return new(insetX + RadarX * (768 - 2 * insetX),
                insetY + RadarY * (576 - 2 * insetY));
        }
    }

    public static float HealthBlink(int hp, double seconds) =>
        hp > 99 ? 1f : .35f + .65f * (.5f + .5f * MathF.Cos((float)(seconds * Math.PI * 4)));

    public static (Vector4i Top, Vector4i Bottom) Bounds(int width, int height)
    {
        // Integral uniform scale, identical LCDs, centered letterboxing.
        int unit = Math.Max(1, Math.Min(Math.Max(width, 1) / AspectWidth, Math.Max(height, 1) / 6));
        int w = unit * AspectWidth, h = unit * 3;
        int x = (width - w) / 2, y = (height - h * 2) / 2;
        return (new Vector4i(x, y, w, h), new Vector4i(x, y + h, w, h));
    }

    public static Vector4 Bottom { get; private set; }
    public static Vector2i InputSize { get; private set; }

    public static void UpdateInputBounds(int width, int height)
    {
        width = Math.Max(4, width);
        height = Math.Max(6, height);
        InputSize = new Vector2i(width, height);
        Vector4i bottom = Bounds(width, height).Bottom;
        Bottom = new(bottom.X / (float)width, bottom.Y / (float)height,
            bottom.Z / (float)width, bottom.W / (float)height);
    }
}
