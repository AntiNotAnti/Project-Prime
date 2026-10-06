using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Validate a complete owner intent before ordering, retention, relay or
/// recording. Owner movement remains authoritative for position within the existing
/// finite world bounds; this is not movement reconciliation.</summary>
internal static class NetIntentPolicy
{
    internal static bool Sane(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z)
        && MathF.Abs(value.X) < 100000 && MathF.Abs(value.Y) < 100000
        && MathF.Abs(value.Z) < 100000;
    internal static bool Validate(in IntentPacket intent)
    {
        if (!Sane(intent.Position) || !Sane(intent.Aim)
            || intent.WeaponSelect != 255 && intent.WeaponSelect > (byte)BeamType.OmegaCannon
            || !NetFireEvents.Validate(intent)) return false;
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            FireEvent fire = intent.FireEvents[i];
            if (fire.HasPose && (!Sane(fire.Origin) || !Sane(fire.Direction)
                || !Sane(fire.Aim) || !Sane(fire.View))) return false;
        }
        return true;
    }
}
