using System;

namespace MphRead.Hud.Radar;

public enum RadarStyle { Classic, Enhanced }
public enum RadarOrientation { Heading, North }
public enum RadarAnchor { TopRight, TopLeft, BottomRight, BottomLeft, Custom }

public static class RadarSettings
{
    public const float MinimumScale = .65f;
    public const float MaximumScale = 1.5f;
    public const float MinimumRange = 20;
    public const float MaximumRange = 80;
    public const float DefaultRange = 40;
    public const float MinimumOpacity = .35f;
    public const float MaximumOpacity = 1;
    private static float _range = DefaultRange;
    private static float _opacity = 1;

    public static RadarStyle Style { get; set; } = RadarStyle.Enhanced;
    public static RadarOrientation Orientation { get; set; } = RadarOrientation.Heading;
    public static RadarAnchor Anchor { get; set; } = RadarAnchor.TopRight;
    public static float Scale { get; set; } = 1;
    public static float OffsetX { get; set; }
    public static float OffsetY { get; set; }
    public static float Range
    {
        get => _range;
        set => _range = float.IsFinite(value)
            ? Math.Clamp(value, MinimumRange, MaximumRange) : DefaultRange;
    }
    public static float Opacity
    {
        get => _opacity;
        set => _opacity = float.IsFinite(value)
            ? Math.Clamp(value, MinimumOpacity, MaximumOpacity) : 1;
    }
    public static bool ElevationIndicators { get; set; } = true;
    public const float ElevationThreshold = 2;
}
