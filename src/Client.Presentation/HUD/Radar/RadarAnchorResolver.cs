using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Hud.Radar;

/// <summary>
/// Resolves the world point used as a radar anchor for a player. The radar
/// follows the transformed gameplay collision volume rather than the authored
/// locator/model origin. This keeps biped and alternate forms horizontally
/// stable and avoids inventing a vertical morph cue: while a morph is in
/// progress <see cref="PlayerEntity.Volume"/> remains the active collision
/// volume for that transition step.
/// </summary>
public static class RadarAnchorResolver
{
    public static Vector3 Resolve(PlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return Resolve(player.Position, player.Volume);
    }

    /// <summary>
    /// Resolves a transformed collision volume center, with a finite actor
    /// position as the uninitialized/test fallback.
    /// </summary>
    public static Vector3 Resolve(Vector3 actorPosition, in CollisionVolume volume)
    {
        Vector3 center = volume.Type switch
        {
            VolumeType.Sphere => volume.SpherePosition,
            VolumeType.Cylinder => volume.CylinderPosition
                + volume.CylinderVector * (volume.CylinderDot * .5f),
            VolumeType.Box => volume.BoxPosition
                + volume.BoxVector1 * (volume.BoxDot1 * .5f)
                + volume.BoxVector2 * (volume.BoxDot2 * .5f)
                + volume.BoxVector3 * (volume.BoxDot3 * .5f),
            _ => Vector3.Zero
        };
        if (IsFinite(center) && IsUsable(volume)) return center;
        return IsFinite(actorPosition) ? actorPosition : Vector3.Zero;
    }

    private static bool IsUsable(in CollisionVolume volume)
        => volume.Type switch
        {
            VolumeType.Sphere => float.IsFinite(volume.SphereRadius)
                && volume.SphereRadius > 0,
            VolumeType.Cylinder => float.IsFinite(volume.CylinderRadius)
                && volume.CylinderRadius > 0 && float.IsFinite(volume.CylinderDot)
                && MathF.Abs(volume.CylinderDot) > 0,
            VolumeType.Box => float.IsFinite(volume.BoxDot1)
                && float.IsFinite(volume.BoxDot2) && float.IsFinite(volume.BoxDot3)
                && volume.BoxDot1 > 0 && volume.BoxDot2 > 0 && volume.BoxDot3 > 0,
            _ => false
        };

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
