using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

public readonly record struct HudRadarStyleDefinition(int Rings, int Ticks, bool Segmented, bool Glow, bool Sweep);
public static class HudRadarStyles
{
    // Static direction templates are compiled once, shared by all runtime profiles.
    private static readonly Vector2[][] Ticks = BuildTicks();
    private static readonly Vector2[] Segments = BuildSegments();
    public static ReadOnlySpan<Vector2> TickDirections(HudRadarStyle style) => Ticks[(int)style];
    public static ReadOnlySpan<Vector2> SegmentEndpoints => Segments;
    private static Vector2[][] BuildTicks()
    {
        var result=new Vector2[8][];
        for(int style=0;style<result.Length;style++)
        {
            result[style]=new Vector2[Get((HudRadarStyle)style).Ticks];
            for(int i=0;i<result[style].Length;i++) result[style][i]=HudRadarGeometry.Polar(1,i*MathF.Tau/result[style].Length);
        }
        return result;
    }
    private static Vector2[] BuildSegments()
    {
        var result=new Vector2[72]; int count=0;
        for(int i=0;i<48;i++) if(i%4!=3)
        { result[count++]=HudRadarGeometry.Polar(1,i*MathF.Tau/48); result[count++]=HudRadarGeometry.Polar(1,(i+.8f)*MathF.Tau/48); }
        return result;
    }
    public static HudRadarStyleDefinition Get(HudRadarStyle style) => style switch
    {
        HudRadarStyle.Tactical => new(3, 16, false, false, false),
        HudRadarStyle.Holographic => new(2, 32, true, true, false),
        HudRadarStyle.Scanner => new(2, 8, true, false, true),
        HudRadarStyle.Competitive => new(0, 4, false, false, false),
        _ => default
    };
    // Radar-only presets deliberately leave layout, tint and visibility untouched.
    public static void Apply(HudProfile profile, HudRadarStyle style)
    {
        var p = new HudRadarProfile { Style = style };
        if (style >= HudRadarStyle.Tactical)
        {
            p.HunterFacing = true; p.Elevation = HudRadarElevationMode.Chevron;
            p.OutOfRange = HudRadarOutOfRangeMode.EdgeArrow; p.Objectives = true;
            p.BackgroundOpacity = style == HudRadarStyle.Competitive ? .25f : .55f;
            p.Cardinals = style == HudRadarStyle.Tactical;
            p.TrailSamples = style == HudRadarStyle.Holographic ? 3 : style == HudRadarStyle.Scanner ? 2 : 0;
            if (style == HudRadarStyle.Competitive) { p.BlipScale = 1.2f; p.OutlineThickness = 1.5f; }
        }
        profile.Radar = p;
        profile.RadarBackground = style >= HudRadarStyle.Tactical;
        profile.RadarOutlines = true;
    }
}
