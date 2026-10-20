using System;

namespace MphRead.Mods.Movement;

// Feet stay at the native bottom. Never make the capsule shorter than its
// diameter, and return the original values exactly for every standing body.
internal static class TraversalStance
{
    internal const float HeightRatio = .65f;
    internal static float Top(float bottom, float top, float radius, bool crouched)
        => crouched ? bottom + Math.Max(2 * radius, (top - bottom) * HeightRatio) : top;

    internal static float EyeOffset(float bottom, float top, float radius, float eye, bool crouched)
        => crouched ? bottom + (eye - bottom) * ((Top(bottom, top, radius, true) - bottom) / (top - bottom)) : eye;

    // The custom biped muzzle has an existing shared .4 offset (trace starts
    // at .2), independent of weapon or Hunter. Retain that standing contract;
    // the accepted crouch scales it about the shared native -.5 foot anchor.
    internal static float CustomMuzzleOffset(bool crouched) => crouched ? -.5f + .9f * HeightRatio : .4f;
    internal static float CustomTraceOffset(bool crouched) => crouched ? -.5f + .7f * HeightRatio : .2f;
}
