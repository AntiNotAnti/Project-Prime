using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// Values describe the picture only. They never advance a native animation,
// select an anchor, move a body, or participate in weapon/collision queries.
internal readonly record struct ParkourAnimationPose(float MantleProgress, float GrappleAmount,
    Vector3 RopeDirection, double Seconds, byte WallCue = 0, float WallAmount = 0,
    Vector3 WallNormal = default, bool WallTucked = false)
{
    internal bool Active => MantleProgress > 0 || GrappleAmount > 0 || WallAmount > 0;
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
            int kind = ParkourWallVisualCodec.Kind(WallCue);
            float wall = Math.Clamp(WallAmount, 0, 1);
            return new Vector3(.025f * mantle, -.13f * mantle - .035f * grapple
                - wall * (kind == 3 ? .06f : kind == 2 ? .035f : .012f),
                -.04f * mantle - (kind == 3 ? .02f * wall : 0));
        }
    }
}
