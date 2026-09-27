using System;
using System.Linq;
namespace MphRead.Mods.Render.Hud;

public enum CrosshairDotShape { Square, Circle, Diamond, Cross }

public sealed class CrosshairProfile
{
    // Null preserves old whole-profile overrides; an empty list inherits every property.
    public string[]? OverrideProperties { get; set; }
    public CrosshairPartStyle DotStyle { get; set; } = new();
    public CrosshairPartStyle InnerStyle { get; set; } = new();
    public CrosshairPartStyle OuterStyle { get; set; } = new();
    public CrosshairPartStyle RingStyle { get; set; } = new();
    public CrosshairPartStyle BracketStyle { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool HealthColor { get; set; } = true;
    public string Color { get; set; } = "#FFFFFF";
    public float Opacity { get; set; } = 1;
    public float Scale { get; set; } = 1;
    public float Outline { get; set; }
    public string OutlineColor { get; set; } = "#000000";
    public float OutlineOpacity { get; set; } = 1;
    public CrosshairDotShape DotShape { get; set; }
    public bool Dot { get; set; }
    public float DotSize { get; set; } = 4;
    public bool Inner { get; set; } = true;
    public float LengthX { get; set; } = 9;
    public float LengthY { get; set; } = 9;
    public float Thickness { get; set; } = 3;
    public float Gap { get; set; } = 3;
    public bool TStyle { get; set; }
    public bool Outer { get; set; }
    public float OuterLength { get; set; } = 6;
    public float OuterGap { get; set; } = 16;
    public bool Ring { get; set; }
    public float Radius { get; set; } = 8;
    public float RingThickness { get; set; } = 2;
    public int Segments { get; set; } = 40;
    public bool Brackets { get; set; }
    public float BracketCorner { get; set; } = 10;
    public float BracketLength { get; set; } = 6;
    public float BracketThickness { get; set; } = 2;
    public void Validate()
    {
        DotStyle ??= new(); InnerStyle ??= new(); OuterStyle ??= new(); RingStyle ??= new(); BracketStyle ??= new();
        DotStyle.Validate(); InnerStyle.Validate(); OuterStyle.Validate(); RingStyle.Validate(); BracketStyle.Validate();
        if (OverrideProperties != null)
            OverrideProperties = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Where(OverrideProperties,
                id => Array.Exists(CrosshairProperties.All,d => d.Id == id) || CrosshairProperties.PartNames.Contains(id))));
        Scale = HudProfile.Clamp(Scale, .1f, 8, 1); Opacity = HudProfile.Clamp(Opacity, 0, 1, 1);
        if (!Enum.IsDefined(DotShape)) DotShape = CrosshairDotShape.Square;
        OutlineColor = HudColor.Normalize(OutlineColor);
        OutlineOpacity = HudProfile.Clamp(OutlineOpacity, 0, 1, 1);
        Outline = HudProfile.Clamp(Outline, 0, 20, 0);
        DotSize = HudProfile.Clamp(DotSize, .1f, 100, 4);
        LengthX = HudProfile.Clamp(LengthX, 0, 100, 9); LengthY = HudProfile.Clamp(LengthY, 0, 100, 9);
        Thickness = HudProfile.Clamp(Thickness, .1f, 20, 3); Gap = HudProfile.Clamp(Gap, 0, 100, 3);
        OuterLength = HudProfile.Clamp(OuterLength, 0, 100, 6); OuterGap = HudProfile.Clamp(OuterGap, 0, 100, 16);
        Radius = HudProfile.Clamp(Radius, 0, 100, 8); RingThickness = HudProfile.Clamp(RingThickness, .1f, 20, 2);
        Segments = Math.Clamp(Segments, 12, 128);
        BracketCorner = HudProfile.Clamp(BracketCorner, 0, 100, 10);
        BracketLength = HudProfile.Clamp(BracketLength, 0, 100, 6);
        BracketThickness = HudProfile.Clamp(BracketThickness, .1f, 20, 2);
        if (Color == null || Color.Length != 7 || Color[0] != '#' || !uint.TryParse(Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _)) Color = "#FFFFFF";
    }
    public static CrosshairProfile FromLegacy(CrosshairStyle style, CrosshairSize size)
    {
        var p = new CrosshairProfile { Scale = Crosshair.ScaleOf(size), Inner = style is CrosshairStyle.Cross or CrosshairStyle.CrossDot,
            Dot = style is CrosshairStyle.Dot or CrosshairStyle.CrossDot, Ring = style == CrosshairStyle.Circle, Brackets = style == CrosshairStyle.Brackets };
        if (style == CrosshairStyle.CrossDot) { p.LengthX = p.LengthY = 8; p.Gap = 5; p.DotSize = 3; }
        return p;
    }
}
