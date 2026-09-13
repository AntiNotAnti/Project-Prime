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
    private readonly RadarZoomController _radarZoom = new();

    private RadarProfile CurrentRadarProfile
        => RadarSettings.ForContext(_player._scene.Match.Rules.Mode.ToString(),
#if ANDROID
            RadarDeviceClass.Handheld
#else
            RadarDeviceClass.Desktop
#endif
        );

    private void DrawEnhancedRadar()
    {
        RadarProfile profile = CurrentRadarProfile;
        float range = _radarZoom.Update(profile, _radarFrame.Origin, _radarFrame.Contacts,
            Presentation.CapturedPresentationTime);
        float aspectFix = HudAspectFix;
        RadarLayout layout = RadarLayoutCalculator.Calculate(profile.Anchor, profile.Scale,
            profile.OffsetX, profile.OffsetY, aspectFix);
        if (profile.BackgroundBlur > 0)
            Presentation.DrawHudFlatBox(layout.Left - aspectFix, layout.Top - 1,
                layout.Left + layout.Width + aspectFix, layout.Top + layout.Height + 1,
                RadarColor(profile.Colors.Background.Vector, profile, profile.BackgroundBlur * .3f));
        Presentation.DrawHudFlatBox(layout.Left, layout.Top,
            layout.Left + layout.Width, layout.Top + layout.Height,
            RadarColor(profile.Colors.Background.Vector, profile, profile.BackgroundDim));

        RadarMapPresentation map = Presentation.RadarMapPresentation;
        if (map.Geometry is RadarMapGeometry geometry)
            DrawRadarMap(layout, geometry, map.FillTextures, map.OutlineTextures, profile, range);

        _radarGeometry.Clear();
        DrawRadarFrame(layout, aspectFix, profile);
        DrawRadarPlayer(layout, aspectFix, profile);
        DrawRadarContacts(layout, aspectFix, profile, range);
        if (_radarGeometry.Count >= 3) Presentation.DrawHudGeometry(_radarGeometry);
        DrawRadarLabels(layout, aspectFix, profile, range);
        DrawRadarCompass(layout, aspectFix, profile);
    }

    private void DrawRadarMap(RadarLayout layout, RadarMapGeometry geometry,
        IReadOnlyList<TextureIdentity> fills, IReadOnlyList<TextureIdentity> outlines,
        RadarProfile profile, float range)
    {
        if (fills.Count == 0) return;
        Vector2 center = geometry.WorldToMap(_radarFrame.Origin);
        Vector2 worldSize = geometry.WorldSize;
        float du = worldSize.X > .0001f ? range / worldSize.X : .5f;
        float dv = worldSize.Y > .0001f ? range / worldSize.Y : .5f;
        Vector4 uv = new(center.X - du, center.Y - dv, center.X + du, center.Y + dv);
        float rotation = profile.Orientation == RadarOrientation.Heading
            ? RadarWidget.HeadingAngle(_radarFrame.Facing) : 0;
        int current = geometry.FindCurrentFloor(_radarFrame.Origin.Y);
        foreach (int floor in RadarPresentationPolicy.VisibleFloors(profile.FloorMode, current, fills.Count))
        {
            float alpha = floor == current ? profile.FloorBrightness : profile.AdjacentFloorOpacity;
            if (profile.MapFill)
                Presentation.DrawHudTexture(fills[floor], layout.Left, layout.Top,
                    layout.Left + layout.Width, layout.Top + layout.Height,
                    Brightened(profile.Colors.Floor.Vector, profile.FloorBrightness), uv, rotation,
                    (floor == current ? 1 : profile.AdjacentFloorOpacity) * profile.Opacity);
            if (profile.MapOutlines && (uint)floor < (uint)outlines.Count)
                Presentation.DrawHudTexture(outlines[floor], layout.Left, layout.Top,
                    layout.Left + layout.Width, layout.Top + layout.Height,
                    profile.Colors.MapOutline.Vector, uv, rotation,
                    MathF.Min(1, (floor == current ? 1 : alpha + .2f)) * profile.Opacity);
        }
    }

    private void DrawRadarFrame(RadarLayout layout, float aspectFix, RadarProfile profile)
    {
        float thicknessX = aspectFix, thicknessY = 1;
        Vector4 border = RadarColor(profile.Colors.Border.Vector, profile);
        Vector4 ring = RadarColor(profile.Colors.Ring.Vector, profile);
        AddQuad(layout.Left, layout.Top, layout.Left + layout.Width, layout.Top + thicknessY, ring);
        AddQuad(layout.Left, layout.Top + layout.Height - thicknessY,
            layout.Left + layout.Width, layout.Top + layout.Height, ring);
        AddQuad(layout.Left, layout.Top, layout.Left + thicknessX, layout.Top + layout.Height, ring);
        AddQuad(layout.Left + layout.Width - thicknessX, layout.Top,
            layout.Left + layout.Width, layout.Top + layout.Height, ring);
        DrawRadarFrameCorners(layout, aspectFix, border);
        if (profile.Grid)
        {
            AddQuad(layout.CenterX - thicknessX / 2, layout.Top, layout.CenterX + thicknessX / 2,
                layout.Top + layout.Height, ring);
            AddQuad(layout.Left, layout.CenterY - .5f, layout.Left + layout.Width,
                layout.CenterY + .5f, ring);
        }
        if (!profile.DistanceRings) goto compass;
        const int segments = 28;
        for (int ringIndex = 1; ringIndex <= 2; ringIndex++)
        {
            float radius = layout.Radius * ringIndex / 2;
            float inner = radius - .45f, outer = radius + .15f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = MathHelper.TwoPi * i / segments;
                float a1 = MathHelper.TwoPi * (i + 1) / segments;
                AddQuadPoints(Point(layout, MathF.Cos(a0) * outer, MathF.Sin(a0) * outer, aspectFix),
                    Point(layout, MathF.Cos(a0) * inner, MathF.Sin(a0) * inner, aspectFix),
                    Point(layout, MathF.Cos(a1) * outer, MathF.Sin(a1) * outer, aspectFix),
                    Point(layout, MathF.Cos(a1) * inner, MathF.Sin(a1) * inner, aspectFix), ring);
            }
        }
compass:
        if (profile.Compass && profile.Orientation == RadarOrientation.North)
            AddTriangle(Point(layout, 0, -layout.Radius + 1, aspectFix),
                Point(layout, -2, -layout.Radius + 5, aspectFix),
                Point(layout, 2, -layout.Radius + 5, aspectFix), border);
        else if (profile.Orientation == RadarOrientation.Heading)
        {
            Vector4 forward = RadarColor(profile.Colors.Objective.Vector, profile, .9f);
            AddTriangle(Point(layout, 0, -layout.Radius + 1, aspectFix),
                Point(layout, -1.8f, -layout.Radius + 4, aspectFix),
                Point(layout, 1.8f, -layout.Radius + 4, aspectFix), forward);
        }
    }

    private void DrawRadarFrameCorners(RadarLayout layout, float aspectFix, Vector4 color)
    {
        float x = 7 * aspectFix;
        const float y = 7;
        const float thicknessY = 1.35f;
        float thicknessX = 1.35f * aspectFix;
        AddQuad(layout.Left, layout.Top, layout.Left + x, layout.Top + thicknessY, color);
        AddQuad(layout.Left, layout.Top, layout.Left + thicknessX, layout.Top + y, color);
        AddQuad(layout.Left + layout.Width - x, layout.Top,
            layout.Left + layout.Width, layout.Top + thicknessY, color);
        AddQuad(layout.Left + layout.Width - thicknessX, layout.Top,
            layout.Left + layout.Width, layout.Top + y, color);
        AddQuad(layout.Left, layout.Top + layout.Height - thicknessY,
            layout.Left + x, layout.Top + layout.Height, color);
        AddQuad(layout.Left, layout.Top + layout.Height - y,
            layout.Left + thicknessX, layout.Top + layout.Height, color);
        AddQuad(layout.Left + layout.Width - x, layout.Top + layout.Height - thicknessY,
            layout.Left + layout.Width, layout.Top + layout.Height, color);
        AddQuad(layout.Left + layout.Width - thicknessX, layout.Top + layout.Height - y,
            layout.Left + layout.Width, layout.Top + layout.Height, color);
    }

    private void DrawRadarPlayer(RadarLayout layout, float aspectFix, RadarProfile profile)
    {
        float angle = profile.Orientation == RadarOrientation.Heading
            ? 0 : RadarWidget.HeadingAngle(_radarFrame.Facing);
        Vector2 Rotate(float x, float y)
        {
            float cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
            return Point(layout, x * cosine - y * sine, x * sine + y * cosine, aspectFix);
        }
        AddTriangle(Rotate(0, -4), Rotate(-2.8f, 3), Rotate(2.8f, 3),
            RadarColor(profile.Colors.Player.Vector, profile));
    }

    private void DrawRadarContacts(RadarLayout layout, float aspectFix, RadarProfile profile, float range)
    {
        foreach (RadarContact contact in _radarFrame.Contacts)
        {
            float alpha = RadarPresentationPolicy.Alpha(profile, contact, _radarFrame.Tick);
            if (alpha <= 0) continue;
            RadarPoint point = RadarWidget.Project(contact, _radarFrame.Origin,
                _radarFrame.Facing, profile.Orientation, range, profile.ElevationThreshold);
            if (point.Clamped && !profile.EdgeArrows) continue;
            Vector4 baseColor = RadarPresentationPolicy.Color(profile, contact, point.Elevation);
            Vector4 color = new(baseColor.X, baseColor.Y, baseColor.Z,
                baseColor.W * alpha * profile.Opacity);
            float markerScale = profile.MarkerScale * (contact.Type == RadarContactType.Objective
                && profile.ObjectiveEmphasis ? 1.3f : 1);
            float radius = point.Clamped
                ? RadarPresentationPolicy.EdgeMarkerRadius(layout.Radius,
                    markerScale, profile.ClampedEdgeScale)
                : layout.Radius;
            Vector2 center = Point(layout, point.RelativePosition.X * radius,
                point.RelativePosition.Y * radius, aspectFix);
            if (profile.MarkerOutline)
                DrawRadarMarker(center, point, contact, markerScale * 1.35f, aspectFix,
                    new Vector4(0, 0, 0, color.W * .85f), profile);
            DrawRadarMarker(center, point, contact, markerScale, aspectFix, color, profile);
            if (profile.ElevationIndicators && point.Elevation != RadarElevation.Same)
                DrawElevationCue(center, point.Elevation, markerScale, aspectFix,
                    RadarPresentationPolicy.ElevationColor(profile, point.Elevation),
                    alpha * profile.Opacity);
        }
    }

    private void DrawRadarMarker(Vector2 center, RadarPoint point, RadarContact contact,
        float markerScale, float aspectFix, Vector4 color, RadarProfile profile)
    {
        if (point.Clamped)
        {
            Vector2 direction = point.RelativePosition.LengthSquared > .0001f
                ? point.RelativePosition.Normalized() : -Vector2.UnitY;
            Vector2 tangent = new(-direction.Y, direction.X);
            float size = markerScale * profile.ClampedEdgeScale;
            Vector2 tip = center + Scaled(direction * 2 * size, aspectFix);
            Vector2 back = center - Scaled(direction * 3 * size, aspectFix);
            AddTriangle(tip, back + Scaled(tangent * 2.2f * size, aspectFix),
                back - Scaled(tangent * 2.2f * size, aspectFix), color);
        }
        else
        {
            switch (RadarPresentationPolicy.MarkerShape(contact))
            {
                case RadarMarkerShape.Square:
                    AddSquare(center, 2.25f * markerScale, aspectFix, color);
                    break;
                case RadarMarkerShape.Triangle:
                    AddTriangle(Point(center, 0, -2.8f * markerScale, aspectFix),
                        Point(center, -2.7f * markerScale, 2.1f * markerScale, aspectFix),
                        Point(center, 2.7f * markerScale, 2.1f * markerScale, aspectFix), color);
                    break;
                case RadarMarkerShape.DoubleDiamond:
                {
                    AddDiamond(center, 3.25f * markerScale, aspectFix, color);
                    Vector4 background = profile.Colors.Background.Vector;
                    AddDiamond(center, 1.25f * markerScale, aspectFix,
                        new Vector4(background.X, background.Y, background.Z, color.W));
                    break;
                }
                default:
                    AddDiamond(center, 1.8f * markerScale, aspectFix, color);
                    break;
            }
        }
    }

    private void DrawElevationCue(Vector2 center, RadarElevation elevation,
        float markerScale, float aspectFix, Vector4 baseColor, float alpha)
    {
        Vector4 color = new(baseColor.X, baseColor.Y, baseColor.Z,
            baseColor.W * alpha);
        float x = 4.5f * markerScale;
        float y = elevation == RadarElevation.Above
            ? -3.2f * markerScale : 3.2f * markerScale;
        float direction = elevation == RadarElevation.Above ? -1 : 1;
        AddTriangle(Point(center, x, y + direction * 1.8f * markerScale, aspectFix),
            Point(center, x - 1.5f * markerScale, y - direction * .8f * markerScale,
                aspectFix),
            Point(center, x + 1.5f * markerScale, y - direction * .8f * markerScale,
                aspectFix), color);
    }

    private void DrawRadarLabels(RadarLayout layout, float aspectFix, RadarProfile profile, float range)
    {
        if (!profile.Labels) return;
        foreach (RadarContact contact in _radarFrame.Contacts)
        {
            float alpha = RadarPresentationPolicy.Alpha(profile, contact, _radarFrame.Tick);
            if (alpha <= 0) continue;
            RadarPoint point = RadarWidget.Project(contact, _radarFrame.Origin,
                _radarFrame.Facing, profile.Orientation, range, profile.ElevationThreshold);
            if (point.Clamped) continue;
            Vector2 center = Point(layout, point.RelativePosition.X * layout.Radius,
                point.RelativePosition.Y * layout.Radius, aspectFix);
            Vector4 color = RadarPresentationPolicy.Color(profile, contact, point.Elevation);
            DrawText2D(center.X, center.Y - 2.2f, Align.Center, 0,
                RadarWidget.Symbol(contact), ToTextColor(color),
                alpha: alpha * profile.Opacity,
                fontSpacing: 8, scale: .45f);
        }
    }

    private void DrawRadarCompass(RadarLayout layout, float aspectFix, RadarProfile profile)
    {
        if (!profile.Compass) return;
        float rotation = profile.Orientation == RadarOrientation.Heading
            ? RadarWidget.HeadingAngle(_radarFrame.Facing) : 0;
        string[] labels = { "N", "E", "S", "W" };
        for (int i = 0; i < labels.Length; i++)
        {
            float angle = -MathHelper.PiOver2 + i * MathHelper.PiOver2 + rotation;
            Vector2 position = Point(layout, MathF.Cos(angle) * (layout.Radius - 5),
                MathF.Sin(angle) * (layout.Radius - 5), aspectFix);
            DrawText2D(position.X, position.Y - 1.5f, Align.Center, 0, labels[i],
                ToTextColor(profile.Colors.Border.Vector), alpha: profile.Opacity,
                fontSpacing: 7, scale: .34f);
        }
    }

    private static Vector4 RadarColor(Vector4 color, RadarProfile profile, float alpha = 1)
        => new(color.X, color.Y, color.Z, color.W * profile.Opacity * alpha);

    private static Vector4 Brightened(Vector4 color, float brightness)
        => new(color.X * brightness, color.Y * brightness, color.Z * brightness, color.W);

    private static ColorRgba ToTextColor(Vector4 color)
        => new((byte)(Math.Clamp(color.X, 0, 1) * 255),
            (byte)(Math.Clamp(color.Y, 0, 1) * 255),
            (byte)(Math.Clamp(color.Z, 0, 1) * 255), 255);

    private static Vector2 Point(RadarLayout layout, float x, float y, float aspectFix)
        => new(layout.CenterX + x * aspectFix, layout.CenterY + y);
    private static Vector2 Point(Vector2 center, float x, float y, float aspectFix)
        => new(center.X + x * aspectFix, center.Y + y);
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

    private void AddSquare(Vector2 center, float radius, float aspectFix, Vector4 color)
        => AddQuad(center.X - radius * aspectFix, center.Y - radius,
            center.X + radius * aspectFix, center.Y + radius, color);

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
