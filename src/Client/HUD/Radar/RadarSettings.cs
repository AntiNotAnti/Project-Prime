namespace MphRead.Hud.Radar;

public enum RadarStyle { Classic, Enhanced }
public enum RadarOrientation { Heading, North }
public enum RadarAnchor { TopRight, TopLeft, BottomRight, BottomLeft, Custom }

public static class RadarSettings
{
    public const float MinimumScale = .65f;
    public const float MaximumScale = 1.5f;

    public static RadarStyle Style { get; set; } = RadarStyle.Enhanced;
    public static RadarOrientation Orientation { get; set; } = RadarOrientation.Heading;
    public static RadarAnchor Anchor { get; set; } = RadarAnchor.TopRight;
    public static float Scale { get; set; } = 1;
    public static float OffsetX { get; set; }
    public static float OffsetY { get; set; }
    public const float Range = 40;
    public const float ElevationThreshold = 2;
}
