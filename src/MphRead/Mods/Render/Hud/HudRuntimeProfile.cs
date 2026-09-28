using System;
using System.Collections.Generic;
using System.Numerics;

namespace MphRead.Mods.Render.Hud;

public readonly record struct HudResolvedElement(bool Enabled, HudAnchor Anchor, Vector2 Offset, float Scale, float Opacity, int Layer, HudVisibility Visibility, Vector3 Color, HudContext Contexts);
public readonly record struct HudCrosshairVertex(float X, float Y);

/// <summary>All geometry is compiled at edit time. Rendering only reads immutable spans.</summary>
public sealed class CrosshairRuntime
{
    private readonly CrosshairRuntime[] _parts;
    public ReadOnlySpan<CrosshairRuntime> Parts => _parts;
    private readonly CrosshairBar[] _bars;
    private readonly HudCrosshairVertex[] _ring;
    private readonly HudCrosshairVertex[] _outlineRing;
    public ReadOnlySpan<CrosshairBar> Bars => _bars;
    public ReadOnlySpan<HudCrosshairVertex> Ring => _ring;
    public ReadOnlySpan<HudCrosshairVertex> OutlineRing => _outlineRing;
    private readonly HudCrosshairVertex[] _dot, _outlineDot;
    public ReadOnlySpan<HudCrosshairVertex> Dot => _dot;
    public ReadOnlySpan<HudCrosshairVertex> OutlineDot => _outlineDot;
    public readonly Vector3 OutlineColor;
    public readonly float OutlineOpacity;
    public readonly bool Enabled, HealthColor, Native;
    public readonly float NativeScale;
    public readonly float Opacity, Outline;
    public readonly Vector3 Color;
    public CrosshairRuntime(CrosshairProfile p) : this(p,true) { }
    private CrosshairRuntime(CrosshairProfile p,bool compileParts)
    {
        _parts=Array.Empty<CrosshairRuntime>();
        p.Validate(); Native=p.Native; NativeScale=p.Scale; Enabled = p.Enabled; HealthColor = p.HealthColor; Opacity = p.Opacity; Outline = p.Outline * p.Scale;
        OutlineColor = HudColor.Parse(p.OutlineColor); OutlineOpacity = p.OutlineOpacity;
        uint rgb = Convert.ToUInt32(p.Color[1..], 16);
        Color = new Vector3((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        var bars = new List<CrosshairBar>(17);
        void Bar(float x, float y, float w, float h) { if (w > 0 && h > 0) bars.Add(new(x * p.Scale, y * p.Scale, w * p.Scale, h * p.Scale)); }
        void Arms(float x, float y, float gap, bool t)
        {
            if (!t) Bar(0, gap + y / 2, p.Thickness, y);
            Bar(0, -gap - y / 2, p.Thickness, y);
            Bar(-gap - x / 2, 0, x, p.Thickness); Bar(gap + x / 2, 0, x, p.Thickness);
        }
        if (p.Inner) Arms(p.LengthX, p.LengthY, p.Gap, p.TStyle);
        if (p.Outer) Arms(p.OuterLength, p.OuterLength, p.OuterGap, false);
        if (p.Dot && p.DotShape == CrosshairDotShape.Square) Bar(0, 0, p.DotSize, p.DotSize);
        if (p.Dot && p.DotShape == CrosshairDotShape.Cross)
        { Bar(0,0,p.DotSize,p.DotSize/3); Bar(0,0,p.DotSize/3,p.DotSize); }
        _dot = MakeDot(p, 0); _outlineDot = p.Outline > 0 ? MakeDot(p,p.Outline) : Array.Empty<HudCrosshairVertex>();
        if (p.Brackets)
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sy = (i & 2) == 0 ? 1 : -1;
                Bar(sx * (p.BracketCorner - p.BracketLength / 2 + p.BracketThickness / 2), sy * (p.BracketCorner - p.BracketThickness / 2), p.BracketLength, p.BracketThickness);
                Bar(sx * (p.BracketCorner - p.BracketThickness / 2), sy * (p.BracketCorner - p.BracketLength / 2 - p.BracketThickness / 2), p.BracketThickness, p.BracketLength);
            }
        if(compileParts)
        {
            var parts=new List<CrosshairRuntime>(5);
            foreach(string name in CrosshairProperties.PartNames)
            {
                var part=System.Text.Json.JsonSerializer.Deserialize<CrosshairProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;
                part.Dot=p.Dot && name==nameof(p.DotStyle); part.Inner=p.Inner && name==nameof(p.InnerStyle);
                part.Outer=p.Outer && name==nameof(p.OuterStyle); part.Ring=p.Ring && name==nameof(p.RingStyle);
                part.Brackets=p.Brackets && name==nameof(p.BracketStyle);
                if(!part.Dot && !part.Inner && !part.Outer && !part.Ring && !part.Brackets) continue;
                var style=(CrosshairPartStyle)typeof(CrosshairProfile).GetProperty(name)!.GetValue(p)!;
                if(style.CustomColor) { part.Color=style.Color; part.HealthColor=false; }
                part.Opacity*=style.Opacity; if(style.Outline>=0) part.Outline=style.Outline;
                parts.Add(new(part,false));
            }
            _parts=parts.ToArray();
        }
        _bars = bars.ToArray();
        _ring = MakeRing(p, 0); _outlineRing = p.Outline > 0 ? MakeRing(p, p.Outline) : Array.Empty<HudCrosshairVertex>();
    }
    private static HudCrosshairVertex[] MakeDot(CrosshairProfile p, float outline)
    {
        if (!p.Dot || p.DotShape is CrosshairDotShape.Square or CrosshairDotShape.Cross) return Array.Empty<HudCrosshairVertex>();
        int segments = p.DotShape == CrosshairDotShape.Diamond ? 4 : p.Segments;
        float radius = (p.DotSize/2 + outline * (p.DotShape == CrosshairDotShape.Diamond ? MathF.Sqrt(2) : 1)) * p.Scale;
        var vertices = new HudCrosshairVertex[segments*3];
        for (int i=0; i<segments; i++)
        {
            float a = MathF.Tau*i/segments, b = MathF.Tau*(i+1)/segments;
            vertices[i*3] = new(0,0);
            vertices[i*3+1] = new(radius*MathF.Cos(a),radius*MathF.Sin(a));
            vertices[i*3+2] = new(radius*MathF.Cos(b),radius*MathF.Sin(b));
        }
        return vertices;
    }
    private static HudCrosshairVertex[] MakeRing(CrosshairProfile p, float outline)
    {
        if (!p.Ring) return Array.Empty<HudCrosshairVertex>();
        var vertices = new HudCrosshairVertex[(p.Segments + 1) * 2];
        float inner = MathF.Max(0, p.Radius - p.RingThickness / 2 - outline) * p.Scale;
        float outer = (p.Radius + p.RingThickness / 2 + outline) * p.Scale;
        for (int i = 0; i <= p.Segments; i++)
        {
            float angle = MathF.Tau * i / p.Segments, c = MathF.Cos(angle), s = MathF.Sin(angle);
            vertices[i * 2] = new(outer * c, outer * s); vertices[i * 2 + 1] = new(inner * c, inner * s);
        }
        return vertices;
    }
}

public sealed class HudRuntimeProfile
{
    private readonly HudResolvedElement[] _elements;
    public readonly HudMode Mode;
    public readonly float SafeArea, GlobalScale, GlobalOpacity, TextScale, IconScale;
    public readonly bool ReduceMotion, ReduceTransparency, IndependentGauges;
    public readonly HudNotificationRuntime Notifications;
    public readonly HudMeterRuntime Health, Ammo;
    public readonly HudKillFeedRuntime KillFeed;
    public readonly HudInventoryRuntime Inventory;
    public readonly HudHitMarkerRuntime HitMarker;
    public readonly HudRadarRuntime Radar;
    public readonly CrosshairRuntime Crosshair;
    private readonly CrosshairRuntime?[] _weapons = new CrosshairRuntime?[9];
    private readonly CrosshairRuntime? _zoom;
    public CrosshairRuntime CrosshairFor(int weapon, bool zoom) => zoom && _zoom != null ? _zoom : weapon >= 0 && weapon < 9 ? _weapons[weapon] ?? Crosshair : Crosshair;
    public HudResolvedElement this[int index] => _elements[index];
    public HudRuntimeProfile(HudProfile profile)
    {
        var p = profile.DeepClone(); p.Validate();
        Mode = p.Mode; SafeArea = p.SafeArea; GlobalScale = p.GlobalScale; GlobalOpacity = p.GlobalOpacity;
        IndependentGauges=p.IndependentGauges;
        TextScale=p.TextScale; IconScale=p.IconScale; ReduceMotion=p.ReduceMotion; ReduceTransparency=p.ReduceTransparency;
        Notifications=new(p.Notifications);
        Health=new(p.Health); Ammo=new(p.Ammo); KillFeed=new(p.KillFeed); Inventory=new(p.Inventory); HitMarker=new(p.HitMarker); Radar=new(p.Radar);
        Crosshair = new(p.Crosshair);
        for (int i = 0; i < 9; i++) if (p.WeaponCrosshairs[i] is {} weapon) _weapons[i] = new(CrosshairProperties.Resolve(p.Crosshair,weapon));
        if (p.ZoomCrosshair is {} zoom) _zoom = new(CrosshairProperties.Resolve(CrosshairProperties.Resolve(p.Crosshair,p.WeaponCrosshairs[4]),zoom));
        _elements = new HudResolvedElement[HudProfileDefaults.ElementIds.Length];
        for (int i = 0; i < _elements.Length; i++)
        {
            var e = p.Elements[HudProfileDefaults.ElementIds[i]];
            _elements[i] = new(e.Enabled, e.Anchor, new(e.OffsetX, e.OffsetY), p.GlobalScale * e.Scale, p.GlobalOpacity * e.Opacity, e.Layer, e.Visibility, HudColor.Parse(e.Color), e.Contexts);
        }
    }
}
