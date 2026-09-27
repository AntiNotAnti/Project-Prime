using System;
namespace MphRead.Mods.Render.Hud;

public enum HudRadarStyle { Basic = 0, Minimal = 1, Ring = 2, Square = 3, Tactical = 4, Holographic = 5, Scanner = 6, Competitive = 7 }
public enum HudRadarOrientation { HeadingUp, NorthUp }
public enum HudRadarOutOfRangeMode { Clamp, EdgeArrow, Hide }
public enum HudRadarElevationMode { Off, Chevron, Color }
public sealed class HudRadarProfile
{
    public HudRadarStyle Style { get; set; }
    public bool Hunters { get; set; } = true;
    public bool Weapons { get; set; } = true;
    public bool Powerups { get; set; } = true;
    public bool Cardinals { get; set; }
    public float RadiusScale { get; set; } = 1;
    public float BackgroundOpacity { get; set; } = 1;
    public float OutlineThickness { get; set; } = 1;
    public float BlipScale { get; set; } = 1;
    public float BlipOpacity { get; set; } = 1;
    public bool Objectives { get; set; } = false;
    public bool HunterFacing { get; set; } = false;
    public HudRadarOrientation Orientation { get; set; } = HudRadarOrientation.HeadingUp;
    public HudRadarOutOfRangeMode OutOfRange { get; set; } = HudRadarOutOfRangeMode.Clamp;
    public HudRadarElevationMode Elevation { get; set; } = HudRadarElevationMode.Off;
    public float ElevationThreshold { get; set; } = 2f;
    public int TrailSamples { get; set; } = 0;
    public float RangeScale { get; set; } = 1f;
    public float SweepSpeed { get; set; } = .55f;
    public float SweepOpacity { get; set; } = .6f;
    public bool RangeRings { get; set; } = true;
    internal void Validate()
    {
        if(!Enum.IsDefined(Orientation)) Orientation=HudRadarOrientation.HeadingUp;
        if(!Enum.IsDefined(OutOfRange)) OutOfRange=HudRadarOutOfRangeMode.Clamp;
        if(!Enum.IsDefined(Elevation)) Elevation=HudRadarElevationMode.Off;
        RangeScale=HudProfile.Clamp(RangeScale,.5f,1,1);
        ElevationThreshold=HudProfile.Clamp(ElevationThreshold,.1f,20,2);
        TrailSamples=Math.Clamp(TrailSamples,0,4);
        SweepSpeed=HudProfile.Clamp(SweepSpeed,.1f,2,.55f);
        SweepOpacity=HudProfile.Clamp(SweepOpacity,0,1,.6f);
        if(!Enum.IsDefined(Style)) Style=HudRadarStyle.Basic;
        RadiusScale=HudProfile.Clamp(RadiusScale,.1f,4,1); BackgroundOpacity=HudProfile.Clamp(BackgroundOpacity,0,1,1);
        OutlineThickness=HudProfile.Clamp(OutlineThickness,.1f,8,1); BlipScale=HudProfile.Clamp(BlipScale,.1f,8,1); BlipOpacity=HudProfile.Clamp(BlipOpacity,0,1,1);
    }
}
public readonly record struct HudRadarRuntime(HudRadarStyle Style,float RadiusScale,float BackgroundOpacity,float OutlineThickness,float BlipScale,float BlipOpacity,bool Hunters,bool Weapons,bool Powerups,bool Cardinals,bool Objectives,bool HunterFacing,HudRadarOrientation Orientation,HudRadarOutOfRangeMode OutOfRange,HudRadarElevationMode Elevation,float ElevationThreshold,int TrailSamples,float RangeScale,float SweepSpeed,float SweepOpacity,bool RangeRings)
{
    public HudRadarRuntime(HudRadarProfile p) : this(p.Style,p.RadiusScale,p.BackgroundOpacity,p.OutlineThickness,p.BlipScale,p.BlipOpacity,p.Hunters,p.Weapons,p.Powerups,p.Cardinals,p.Objectives,p.HunterFacing,p.Orientation,p.OutOfRange,p.Elevation,p.ElevationThreshold,p.TrailSamples,p.RangeScale,p.SweepSpeed,p.SweepOpacity,p.RangeRings) { _ = HudRadarStyles.TickDirections(Style).Length; }
}

