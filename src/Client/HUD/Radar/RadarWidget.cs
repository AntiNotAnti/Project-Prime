using System;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

/// <summary>Pure projection of legal prepared contacts; never queries the scene.</summary>
public static class RadarWidget
{
    public static RadarPoint Project(in RadarContact contact, Vector3 origin, Vector3 facing,
        RadarOrientation orientation, float range = RadarSettings.Range)
    {
        if (!float.IsFinite(range) || range <= 0) throw new ArgumentOutOfRangeException(nameof(range));
        Vector3 delta = contact.Position - origin;
        Vector2 forward = orientation == RadarOrientation.North ? -Vector2.UnitY : new(facing.X, facing.Z);
        if (!float.IsFinite(forward.LengthSquared) || forward.LengthSquared < 0.000001f) forward = -Vector2.UnitY;
        else forward.Normalize();
        Vector2 right = new(-forward.Y, forward.X);
        Vector2 horizontal = new(delta.X, delta.Z);
        Vector2 relative = new(Vector2.Dot(horizontal, right) / range, -Vector2.Dot(horizontal, forward) / range);
        bool clamped = relative.LengthSquared > 1;
        if (clamped) relative.Normalize();
        RadarElevation elevation = delta.Y > RadarSettings.ElevationThreshold ? RadarElevation.Above
            : delta.Y < -RadarSettings.ElevationThreshold ? RadarElevation.Below : RadarElevation.Same;
        return new(contact, relative, elevation, clamped);
    }

    public static string Symbol(in RadarContact contact) => contact.Type switch
    {
        RadarContactType.Enemy => "o",
        RadarContactType.Teammate => "<>",
        RadarContactType.PrimeHunter => "P",
        _ => contact.Objective switch
        {
            RadarObjective.Flag => "F", RadarObjective.Base => "B",
            RadarObjective.Node => "N", RadarObjective.Defender => "D", _ => "+"
        }
    };
}
