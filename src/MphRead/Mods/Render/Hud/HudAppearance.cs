using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

public sealed class HudMeterProfile
{
    public bool Number { get; set; } = true;
    public bool Gauge { get; set; } = true;
    public bool Background { get; set; } = true;
    public bool Icon { get; set; } = true;
    public bool Vertical { get; set; }
    public float NumberScale { get; set; } = 1;
    public float GaugeScale { get; set; } = 1;
    public float GaugeThickness { get; set; } = 3;
    public float Warning { get; set; } = 60 / 99f;
    public float Danger { get; set; } = 33 / 99f;
    public string FullColor { get; set; } = "#3DD951";
    public string WarningColor { get; set; } = "#FFAD19";
    public string DangerColor { get; set; } = "#F22D2D";
    internal void Validate()
    {
        NumberScale = HudProfile.Clamp(NumberScale,.1f,8,1); GaugeScale = HudProfile.Clamp(GaugeScale,.1f,8,1);
        GaugeThickness = HudProfile.Clamp(GaugeThickness,.1f,20,3);
        Warning = HudProfile.Clamp(Warning,0,1,.6f); Danger = HudProfile.Clamp(Danger,0,Warning,.33f);
        FullColor = HudColor.Normalize(FullColor); WarningColor=HudColor.Normalize(WarningColor); DangerColor=HudColor.Normalize(DangerColor);
    }
}
public readonly record struct HudMeterRuntime(bool Number, bool Gauge, bool Background, bool Icon, bool Vertical,
    float NumberScale, float GaugeScale, float GaugeThickness, float Warning, float Danger,
    Vector3 FullColor, Vector3 WarningColor, Vector3 DangerColor)
{
    public HudMeterRuntime(HudMeterProfile p) : this(p.Number,p.Gauge,p.Background,p.Icon,p.Vertical,p.NumberScale,p.GaugeScale,p.GaugeThickness,p.Warning,p.Danger,
        HudColor.Parse(p.FullColor),HudColor.Parse(p.WarningColor),HudColor.Parse(p.DangerColor)) { }
    public Vector3 ColorFor(float fraction) => fraction > Warning ? FullColor : fraction > Danger ? WarningColor : DangerColor;
}
public sealed class HudKillFeedProfile
{
    public int Rows { get; set; } = 5;
    public float Lifetime { get; set; } = 5;
    public float Spacing { get; set; } = 8.8f;
    public bool Weapon { get; set; } = true;
    public bool Headshot { get; set; } = true;
    public bool TeamKill { get; set; } = true;
    internal void Validate() { Rows=Math.Clamp(Rows,1,10); Lifetime=HudProfile.Clamp(Lifetime,1,10,5); Spacing=HudProfile.Clamp(Spacing,8,32,8.8f); }
}
public readonly record struct HudKillFeedRuntime(int Rows, float Lifetime, float Spacing, bool Weapon, bool Headshot, bool TeamKill)
{
    public HudKillFeedRuntime(HudKillFeedProfile p) : this(p.Rows,p.Lifetime,p.Spacing,p.Weapon,p.Headshot,p.TeamKill) { }
}
public sealed class HudInventoryProfile
{
    public bool Horizontal { get; set; }
    public bool ShowUnowned { get; set; }
    public bool ShowAmmo { get; set; } = true;
    public bool SelectedOutline { get; set; }
    public float IconScale { get; set; } = 1;
    public float Spacing { get; set; }
    public float UnownedOpacity { get; set; } = .3f;
    public string SelectedColor { get; set; } = "#FFD700";
    internal void Validate()
    { IconScale=HudProfile.Clamp(IconScale,.1f,4,1); Spacing=HudProfile.Clamp(Spacing,0,16,0); UnownedOpacity=HudProfile.Clamp(UnownedOpacity,0,1,.3f); SelectedColor=HudColor.Normalize(SelectedColor); }
}
public readonly record struct HudInventoryRuntime(bool Horizontal, bool ShowUnowned, bool ShowAmmo, bool SelectedOutline,
    float IconScale, float Spacing, float UnownedOpacity, Vector3 SelectedColor)
{
    public HudInventoryRuntime(HudInventoryProfile p) : this(p.Horizontal,p.ShowUnowned,p.ShowAmmo,p.SelectedOutline,p.IconScale,p.Spacing,p.UnownedOpacity,HudColor.Parse(p.SelectedColor)) { }
}
public enum HudHitMarkerShape { X, Plus, Dot }
public sealed class HudHitMarkerProfile
{
    public bool Enabled { get; set; } = true;
    public HudHitMarkerShape Shape { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public float Scale { get; set; } = 1;
    public float Opacity { get; set; } = 1;
    public float Gap { get; set; } = 4;
    public float Length { get; set; } = 7;
    public float Thickness { get; set; } = 2;
    internal void Validate()
    {
        if(!Enum.IsDefined(Shape)) Shape=HudHitMarkerShape.X;
        Color=HudColor.Normalize(Color); Scale=HudProfile.Clamp(Scale,.1f,8,1); Opacity=HudProfile.Clamp(Opacity,0,1,1);
        Gap=HudProfile.Clamp(Gap,0,100,4); Length=HudProfile.Clamp(Length,.1f,100,7); Thickness=HudProfile.Clamp(Thickness,.1f,20,2);
    }
}
public readonly record struct HudHitMarkerRuntime(bool Enabled, HudHitMarkerShape Shape, Vector3 Color, float Scale, float Opacity, float Gap, float Length, float Thickness)
{
    public HudHitMarkerRuntime(HudHitMarkerProfile p) : this(p.Enabled,p.Shape,HudColor.Parse(p.Color),p.Scale,p.Opacity,p.Gap,p.Length,p.Thickness) { }
}
public enum HudNotificationQueue { Stack, Latest }
public sealed class HudNotificationProfile
{
    public float Spacing { get; set; } = 3;
    public int MaxVisible { get; set; } = 3;
    public HudNotificationQueue Queue { get; set; }
    public bool Fade { get; set; } = true;
    internal void Validate()
    { Spacing=HudProfile.Clamp(Spacing,0,32,3); MaxVisible=Math.Clamp(MaxVisible,1,5); if(!Enum.IsDefined(Queue)) Queue=HudNotificationQueue.Stack; }
}
public readonly record struct HudNotificationRuntime(float Spacing,int MaxVisible,HudNotificationQueue Queue,bool Fade)
{
    public HudNotificationRuntime(HudNotificationProfile p) : this(p.Spacing,p.MaxVisible,p.Queue,p.Fade) { }
}
