using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Native Hunters share these fixed body/eye dimensions. Parkour is limited to
// native variants; this shared offset also freezes certificate/replay geometry.
internal static class ParkourStance
{
    internal const float TuckRise = (3686f / 4096 + .5f) * (1 - TraversalStance.HeightRatio);
    internal static bool CanTuck(bool parkour, bool eligible, bool airborne, bool requested)
        => parkour && eligible && airborne && requested;
    internal static float Bottom(float bottom, bool crouched, bool tucked)
        => bottom + (crouched && tucked ? TuckRise : 0);
    internal static float Top(float bottom, float top, float radius, bool crouched, bool tucked)
        => TraversalStance.Top(bottom, top, radius, crouched) + (crouched && tucked ? TuckRise : 0);

    // Four-bit cumulative stance-pivot count survives lost/reordered packets.
    // More than one landing in eight source ticks cannot describe normal play;
    // the caller zeroes discontinuous report velocity rather than fabricating it.
    internal static Vector3 PhysicalTravel(Vector3 delta, byte before, byte after, out bool valid)
    {
        int count = (after - before) & 15;
        valid = count <= 1;
        return delta - Vector3.UnitY * (count * TuckRise);
    }
}
