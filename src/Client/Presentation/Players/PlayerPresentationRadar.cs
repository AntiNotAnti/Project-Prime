using System;
using System.Collections.Generic;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Hud.Radar;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerPresentation
{
    private readonly RadarFrame _radarFrame = new();
    private readonly List<HudGeometryVertex> _radarGeometry = new(1024);

    private void DrawEnhancedRadar()
    {
        float aspectFix = HudAspectFix;
        RadarLayout layout = RadarLayoutCalculator.Calculate(RadarSettings.Anchor, RadarSettings.Scale,
            RadarSettings.OffsetX, RadarSettings.OffsetY, aspectFix);
        Presentation.DrawHudFlatBox(layout.Left, layout.Top,
            layout.Left + layout.Width, layout.Top + layout.Height, RadarColor(RadarPalette.Background));

        RadarMapPresentation map = Presentation.RadarMapPresentation;
        if (map.Geometry is RadarMapGeometry geometry)
            DrawRadarMap(layout, geometry, map.FloorTextures);

        _radarGeometry.Clear();
        DrawRadarFrame(layout, aspectFix);
        DrawRadarPlayer(layout, aspectFix);
        DrawRadarContacts(layout, aspectFix);
        if (_radarGeometry.Count >= 3) Presentation.DrawHudGeometry(_radarGeometry);
        DrawRadarObjectiveLabels(layout, aspectFix);
    }

    private void DrawRadarMap(RadarLayout layout, RadarMapGeometry geometry,
        IReadOnlyList<TextureIdentity> textures)
    {
        if (textures.Count == 0) return;
        Vector2 center = geometry.WorldToMap(_radarFrame.Origin);
        Vector2 worldSize = geometry.WorldSize;
        float du = worldSize.X > .0001f ? RadarSettings.Range / worldSize.X : .5f;
        float dv = worldSize.Y > .0001f ? RadarSettings.Range / worldSize.Y : .5f;
        Vector4 uv = new(center.X - du, center.Y - dv, center.X + du, center.Y + dv);
        float rotation = RadarSettings.Orientation == RadarOrientation.Heading
            ? RadarWidget.HeadingAngle(_radarFrame.Facing) : 0;
        int current = geometry.FindCurrentFloor(_radarFrame.Origin.Y);
        for (int offset = -1; offset <= 1; offset += 2)
        {
            int adjacent = current + offset;
            if ((uint)adjacent < (uint)textures.Count)
                Presentation.DrawHudTexture(textures[adjacent], layout.Left, layout.Top,
                    layout.Left + layout.Width, layout.Top + layout.Height,
                    Vector4.One, uv, rotation, .24f * RadarOpacity);
        }
        if ((uint)current < (uint)textures.Count)
            Presentation.DrawHudTexture(textures[current], layout.Left, layout.Top,
                layout.Left + layout.Width, layout.Top + layout.Height,
                Vector4.One, uv, rotation, .9f * RadarOpacity);
    }

    private void DrawRadarFrame(RadarLayout layout, float aspectFix)
    {
        float thicknessX = aspectFix, thicknessY = 1;
        AddQuad(layout.Left, layout.Top, layout.Left + layout.Width, layout.Top + thicknessY, RadarColor(RadarPalette.Border));
        AddQuad(layout.Left, layout.Top + layout.Height - thicknessY,
            layout.Left + layout.Width, layout.Top + layout.Height, RadarColor(RadarPalette.Border));
        AddQuad(layout.Left, layout.Top, layout.Left + thicknessX, layout.Top + layout.Height, RadarColor(RadarPalette.Border));
        AddQuad(layout.Left + layout.Width - thicknessX, layout.Top,
            layout.Left + layout.Width, layout.Top + layout.Height, RadarColor(RadarPalette.Border));
        const int segments = 28;
        float inner = layout.Radius - .65f, outer = layout.Radius + .15f;
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathHelper.TwoPi * i / segments;
            float a1 = MathHelper.TwoPi * (i + 1) / segments;
            AddQuadPoints(
                Point(layout, MathF.Cos(a0) * outer, MathF.Sin(a0) * outer, aspectFix),
                Point(layout, MathF.Cos(a0) * inner, MathF.Sin(a0) * inner, aspectFix),
                Point(layout, MathF.Cos(a1) * outer, MathF.Sin(a1) * outer, aspectFix),
                Point(layout, MathF.Cos(a1) * inner, MathF.Sin(a1) * inner, aspectFix),
                RadarColor(RadarPalette.Ring));
        }
        if (RadarSettings.Orientation == RadarOrientation.North)
            AddTriangle(Point(layout, 0, -layout.Radius + 1, aspectFix),
                Point(layout, -2, -layout.Radius + 5, aspectFix),
                Point(layout, 2, -layout.Radius + 5, aspectFix), RadarColor(RadarPalette.Border));
    }

    private void DrawRadarPlayer(RadarLayout layout, float aspectFix)
    {
        float angle = RadarSettings.Orientation == RadarOrientation.Heading
            ? 0 : RadarWidget.HeadingAngle(_radarFrame.Facing);
        Vector2 Rotate(float x, float y)
        {
            float cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
            return Point(layout, x * cosine - y * sine, x * sine + y * cosine, aspectFix);
        }
        AddTriangle(Rotate(0, -4), Rotate(-2.8f, 3), Rotate(2.8f, 3), RadarColor(RadarPalette.Player));
    }

    private void DrawRadarContacts(RadarLayout layout, float aspectFix)
    {
        foreach (RadarContact contact in _radarFrame.Contacts)
        {
            RadarPoint point = RadarWidget.Project(contact, _radarFrame.Origin,
                _radarFrame.Facing, RadarSettings.Orientation);
            Vector2 center = Point(layout, point.RelativePosition.X * layout.Radius,
                point.RelativePosition.Y * layout.Radius, aspectFix);
            float alpha = Math.Clamp(contact.Visibility, 0, 1) * (contact.AgeTicks > 0 ? .5f : 1);
            Vector4 baseColor = ContactColor(contact);
            Vector4 color = new(baseColor.X, baseColor.Y, baseColor.Z,
                baseColor.W * alpha * RadarOpacity);
            if (point.Clamped)
            {
                Vector2 direction = point.RelativePosition.LengthSquared > .0001f
                    ? point.RelativePosition.Normalized() : -Vector2.UnitY;
                Vector2 tangent = new(-direction.Y, direction.X);
                Vector2 tip = center + Scaled(direction * 2, aspectFix);
                Vector2 back = center - Scaled(direction * 3, aspectFix);
                AddTriangle(tip, back + Scaled(tangent * 2.2f, aspectFix),
                    back - Scaled(tangent * 2.2f, aspectFix), color);
            }
            else if (contact.Type == RadarContactType.Teammate || contact.Objective is RadarObjective.Node or RadarObjective.Defender)
                AddDiamond(center, 2.4f, aspectFix, color);
            else if (contact.Type == RadarContactType.PrimeHunter)
                AddDiamond(center, 3.2f, aspectFix, color);
            else
                AddDiamond(center, 1.8f, aspectFix, color);
            if (RadarSettings.ElevationIndicators && point.Elevation == RadarElevation.Above)
                AddQuad(center.X + 2.8f * aspectFix, center.Y - 3,
                    center.X + 3.8f * aspectFix, center.Y, color);
            else if (RadarSettings.ElevationIndicators && point.Elevation == RadarElevation.Below)
                AddQuad(center.X + 2.8f * aspectFix, center.Y,
                    center.X + 3.8f * aspectFix, center.Y + 3, color);
        }
    }

    private void DrawRadarObjectiveLabels(RadarLayout layout, float aspectFix)
    {
        foreach (RadarContact contact in _radarFrame.Contacts)
        {
            if (contact.Type != RadarContactType.Objective) continue;
            RadarPoint point = RadarWidget.Project(contact, _radarFrame.Origin,
                _radarFrame.Facing, RadarSettings.Orientation);
            if (point.Clamped) continue;
            Vector2 center = Point(layout, point.RelativePosition.X * layout.Radius,
                point.RelativePosition.Y * layout.Radius, aspectFix);
            Vector4 color = ContactColor(contact);
            DrawText2D(center.X, center.Y - 2.2f, Align.Center, 0,
                RadarWidget.Symbol(contact), ToTextColor(color),
                alpha: Math.Clamp(contact.Visibility, 0, 1) * RadarOpacity,
                fontSpacing: 8, scale: .45f);
        }
    }

    private static Vector4 ContactColor(in RadarContact contact) => contact.Type switch
    {
        RadarContactType.Enemy => RadarPalette.Enemy,
        RadarContactType.Teammate => RadarPalette.Teammate,
        RadarContactType.PrimeHunter => RadarPalette.PrimeHunter,
        _ => RadarPalette.Objective
    };

    private static float RadarOpacity
        => float.IsFinite(RadarSettings.Opacity)
            ? Math.Clamp(RadarSettings.Opacity, RadarSettings.MinimumOpacity, RadarSettings.MaximumOpacity)
            : 1;

    private static Vector4 RadarColor(Vector4 color)
        => new(color.X, color.Y, color.Z, color.W * RadarOpacity);

    private static ColorRgba ToTextColor(Vector4 color)
        => new((byte)(Math.Clamp(color.X, 0, 1) * 255),
            (byte)(Math.Clamp(color.Y, 0, 1) * 255),
            (byte)(Math.Clamp(color.Z, 0, 1) * 255), 255);

    private static Vector2 Point(RadarLayout layout, float x, float y, float aspectFix)
        => new(layout.CenterX + x * aspectFix, layout.CenterY + y);
    private static Vector2 Scaled(Vector2 value, float aspectFix) => new(value.X * aspectFix, value.Y);

    private void AddDiamond(Vector2 center, float radius, float aspectFix, Vector4 color)
    {
        Vector2 top = center + new Vector2(0, -radius);
        Vector2 right = center + new Vector2(radius * aspectFix, 0);
        Vector2 bottom = center + new Vector2(0, radius);
        Vector2 left = center + new Vector2(-radius * aspectFix, 0);
        AddTriangle(top, left, right, color);
        AddTriangle(bottom, right, left, color);
    }

    private void AddQuad(float left, float top, float right, float bottom, Vector4 color)
        => AddQuadPoints(new Vector2(right, top), new Vector2(left, top),
            new Vector2(right, bottom), new Vector2(left, bottom), color);

    private void AddQuadPoints(Vector2 topRight, Vector2 topLeft, Vector2 bottomRight,
        Vector2 bottomLeft, Vector4 color)
    {
        AddTriangle(topRight, topLeft, bottomRight, color);
        AddTriangle(bottomRight, topLeft, bottomLeft, color);
    }

    private void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color)
    {
        if (_radarGeometry.Count == 0)
        {
            _radarGeometry.Add(new(a, color));
            _radarGeometry.Add(new(b, color));
            _radarGeometry.Add(new(c, color));
            return;
        }
        if (_radarGeometry.Count + 5 > 2048) return;
        _radarGeometry.Add(_radarGeometry[^1]);
        _radarGeometry.Add(new(a, color));
        _radarGeometry.Add(new(a, color));
        _radarGeometry.Add(new(b, color));
        _radarGeometry.Add(new(c, color));
    }
}
