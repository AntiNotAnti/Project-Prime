using System;
using System.Globalization;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal static class ImpactVisualOptions
{
    internal static float MaxDistance { get; private set; } = 4;
    internal static float MinDirectionDot { get; private set; } = .5f;
    internal static uint HoldFrames { get; private set; } = 3;
    internal static bool Configure(string? distance, string? angle, string? hold)
    {
        float d = 4, a = 60; uint h = 3;
        if (distance != null && (!float.TryParse(distance, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
            || !float.IsFinite(d) || d is < 0 or > 8)) return false;
        if (angle != null && (!float.TryParse(angle, NumberStyles.Float, CultureInfo.InvariantCulture, out a)
            || !float.IsFinite(a) || a is < 0 or > 60)) return false;
        if (hold != null && (!uint.TryParse(hold, out h) || h > 8)) return false;
        MaxDistance = d; MinDirectionDot = MathF.Cos(MathHelper.DegreesToRadians(a)); HoldFrames = h;
        return true;
    }
}

/// <summary>A one-draw endpoint with the same lifecycle fence as the projectile.
/// It is never installed in Position, PastPositions, velocity or collision state.</summary>
internal readonly record struct ImpactDrawEndpoint(ShotKey Shot, uint Component, ulong Frame, Vector3 Point)
{
    internal bool TryPoint(ShotKey shot, uint component, ulong frame, out Vector3 point)
    {
        point = Point;
        return Component != 0 && Shot == shot && Component == component && Frame == frame;
    }
}
