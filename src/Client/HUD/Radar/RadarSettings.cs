namespace MphRead.Hud.Radar;

public enum RadarStyle { Classic, Enhanced }
public enum RadarOrientation { Heading, North }

public static class RadarSettings
{
    public static RadarStyle Style { get; set; } = RadarStyle.Classic;
    public static RadarOrientation Orientation { get; set; } = RadarOrientation.Heading;
    public const float Range = 40;
    public const float ElevationThreshold = 2;
}
