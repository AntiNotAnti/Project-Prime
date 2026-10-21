using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// Values describe the picture only. They never advance a native animation,
// select an anchor, move a body, or participate in weapon/collision queries.
internal readonly record struct ParkourAnimationPose(float MantleProgress, float GrappleAmount,
    Vector3 RopeDirection, double Seconds)
{
    internal bool Active => MantleProgress > 0 || GrappleAmount > 0;
    internal static byte EncodeMantle(float progress)
        => float.IsFinite(progress) && progress >= 0
            ? (byte)(1 + Math.Clamp((int)MathF.Round(Math.Clamp(progress, 0, 1) * 14), 0, 14)) : (byte)0;
    internal static float DecodeMantle(byte phase) => phase is >= 1 and <= 15 ? (phase - 1) / 14f : -1;
    internal float MantleWeight => MantleProgress <= 0 ? 0
        : Smooth(MantleProgress / .12f) * Smooth((1 - MantleProgress) / .16f);
    internal static float Smooth(float value)
    { value = Math.Clamp(value, 0, 1); return value * value * (3 - 2 * value); }

    internal Vector3 GunOffset
    {
        get
        {
            float mantle = MantleWeight, grapple = Math.Clamp(GrappleAmount, 0, 1);
            return new Vector3(.025f * mantle, -.13f * mantle - .035f * grapple,
                -.04f * mantle);
        }
    }
}
