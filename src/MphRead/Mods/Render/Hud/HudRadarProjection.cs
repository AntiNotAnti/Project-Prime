using System;
using System.Numerics;
namespace MphRead.Mods.Render.Hud;

/// <summary>World +Z is north, -X is screen right in the engine's handedness.</summary>
public readonly record struct RadarBasis(Vector2 Forward, Vector2 Right)
{
    public Vector2 Project(Vector3 delta) => new(delta.X * Right.X + delta.Z * Right.Y,
        delta.X * Forward.X + delta.Z * Forward.Y);
    public float Heading(float heading) => MathF.Atan2(Project(new(MathF.Sin(heading), 0, MathF.Cos(heading))).X,
        Project(new(MathF.Sin(heading), 0, MathF.Cos(heading))).Y);
}
public static class HudRadarProjection
{
    public static RadarBasis BuildBasis(Vector3 facing, HudRadarOrientation orientation)
    {
        Vector2 forward = new(facing.X, facing.Z);
        if (orientation == HudRadarOrientation.NorthUp || forward.LengthSquared() < .00000001f) forward = Vector2.UnitY;
        else forward = Vector2.Normalize(forward);
        return new(forward, new(-forward.Y, forward.X));
    }
    public static RadarElevation ClassifyElevation(float delta, float threshold) => delta > threshold
        ? RadarElevation.Above : delta < -threshold ? RadarElevation.Below : RadarElevation.Level;
    public static bool Project(RadarContact contact, Vector3 origin, RadarBasis basis, HudRadarRuntime style,
        float radius, out RadarProjectedContact result)
    {
        Vector3 delta = contact.Position - origin;
        Vector2 local = basis.Project(delta);
        float distance = style.Style == HudRadarStyle.Square ? MathF.Max(MathF.Abs(local.X), MathF.Abs(local.Y)) : local.Length();
        float fraction = distance / (24f * style.RangeScale);
        bool outside = fraction > 1;
        result = new(local * (radius / (24f * style.RangeScale)) / MathF.Max(1, fraction),
            MathF.Atan2(local.X, local.Y), fraction, ClassifyElevation(delta.Y, style.ElevationThreshold), outside);
        bool meaningful = local.LengthSquared() >= .0004f
            || style.Elevation != HudRadarElevationMode.Off && MathF.Abs(delta.Y) > style.ElevationThreshold
            || contact.Kind >= RadarContactKind.Objective;
        return meaningful && !(outside && style.OutOfRange == HudRadarOutOfRangeMode.Hide);
    }
    public static bool Visible(RadarContactKind kind, HudRadarRuntime style) => kind switch
    {
        RadarContactKind.Hunter => style.Hunters,
        RadarContactKind.Weapon => style.Weapons,
        RadarContactKind.Powerup => style.Powerups,
        _ => style.Objectives
    };
}
