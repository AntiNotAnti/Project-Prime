using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input;

internal static class DsLayout
{
    public static bool Enabled { get; set; }
    // Stored independently of the enable switch; old preferences remain 4:3.
    private static string _screenAspect = "4:3";
    public static string ScreenAspect { get => _screenAspect; set => _screenAspect = value == "5:3" ? "5:3" : "4:3"; }
    public static int AspectWidth => ScreenAspect == "5:3" ? 5 : 4;
    public static float Aspect => AspectWidth / 3f;
    public static float HorizontalStretch => Active ? AspectWidth / 4f : 1f;
    private static float _radarSize = 1;
    public static float RadarSize { get => _radarSize; set => _radarSize = float.IsFinite(value) ? Math.Clamp(value, .25f, 3f) : 1; }
    private static float _radarX = .5f, _radarY = .5f;
    public static float RadarX { get => _radarX; set => _radarX = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : .5f; }
    public static float RadarY { get => _radarY; set => _radarY = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : .5f; }
    // Position is relative to the available travel area, keeping the full ring
    // inside the border at every size. 50% remains the exact screen center.
    public static Vector2 RadarCenter
    {
        get { float margin = 65 * RadarSize + 12; return new(margin / HorizontalStretch + RadarX * (768 - 2 * margin / HorizontalStretch), margin + RadarY * (576 - 2 * margin)); }
    }
    public static float HealthBlink(int health, double seconds) => health > 99 ? 1f : .35f + .65f * (.5f + .5f * MathF.Cos((float)(seconds * Math.PI * 4)));
    public static bool Active => Enabled && PointerInput.StylusMode && !OperatingSystem.IsAndroid();
    // Normalized input rectangle; refreshed from client coordinates, so display
    // scaling does not mix framebuffer pixels with tablet coordinates.
    public static Vector4 Bottom { get; private set; }
    public static Vector2i InputSize { get; private set; }
    public static (Vector4i Top, Vector4i Bottom) Bounds(int width, int height)
    {
        int unit = Math.Max(1, Math.Min(width / AspectWidth, height / 6));
        int w = unit * AspectWidth, h = unit * 3;
        int x = (width - w) / 2, y = (height - h * 2) / 2;
        return (new Vector4i(x, y, w, h), new Vector4i(x, y + h, w, h));
    }
    public static void UpdateInputBounds(int width, int height)
    {
        width = Math.Max(4, width); height = Math.Max(6, height);
        InputSize = new Vector2i(width, height);
        var b = Bounds(width, height).Bottom;
        Bottom = new Vector4(b.X / (float)width, b.Y / (float)height,
            b.Z / (float)width, b.W / (float)height);
    }
}
