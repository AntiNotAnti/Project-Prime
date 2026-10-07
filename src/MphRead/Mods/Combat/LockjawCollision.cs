using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Combat
{
    /// <summary>Pure Lockjaw wire/snare geometry.</summary>
    internal static class LockjawCollision
    {
        private const float ZeroThreshold = 0.000001f;
        private const float SnareHalfThickness = 0.75f;

        internal static bool WireOverlapsVolume(CollisionVolume volume,
            Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            if (segment.LengthSquared < ZeroThreshold)
                return volume.TestPoint(from);

            if (volume.Type != VolumeType.Box)
            {
                CollisionResult result = default;
                return CollisionDetection.CheckCylinderOverlapVolume(
                    volume, from, to, 0, ref result);
            }

            float enter = 0;
            float exit = 1;
            Vector3[] axes = { volume.BoxVector1, volume.BoxVector2, volume.BoxVector3 };
            float[] lengths = { volume.BoxDot1, volume.BoxDot2, volume.BoxDot3 };
            for (int i = 0; i < 3; i++)
            {
                float start = Vector3.Dot(from - volume.BoxPosition, axes[i]);
                float delta = Vector3.Dot(segment, axes[i]);
                if (MathF.Abs(delta) < ZeroThreshold)
                {
                    if (start < 0 || start > lengths[i]) return false;
                    continue;
                }
                float a = -start / delta;
                float b = (lengths[i] - start) / delta;
                enter = MathF.Max(enter, MathF.Min(a, b));
                exit = MathF.Min(exit, MathF.Max(a, b));
                if (enter > exit) return false;
            }
            return true;
        }

        internal static bool SnareContainsPoint(
            Vector3 zero, Vector3 one, Vector3 two, Vector3 position)
        {
            Vector3 zeroToOne = one - zero;
            Vector3 oneToTwo = two - one;
            Vector3 normalRaw = Vector3.Cross(oneToTwo, zeroToOne);
            if (normalRaw.LengthSquared < ZeroThreshold) return false;
            Vector3 normal = normalRaw.Normalized();
            Vector3 zeroToPosition = position - zero;
            float dot = Vector3.Dot(normal, zeroToPosition);
            if (!(dot > -SnareHalfThickness && dot < SnareHalfThickness))
                return false;

            return Vector3.Dot(Vector3.Cross(zeroToPosition, zeroToOne), normal) > 0
                && Vector3.Dot(Vector3.Cross(position - one, oneToTwo), normal) > 0
                && Vector3.Dot(Vector3.Cross(position - two, zero - two), normal) > 0;
        }

        internal static bool SnareOverlapsVolume(
            Vector3 zero, Vector3 one, Vector3 two, CollisionVolume volume)
        {
            Vector3 normal = Vector3.Cross(one - zero, two - zero);
            float lengthSquared = normal.LengthSquared;
            if (lengthSquared < ZeroThreshold) return false;
            Vector3 center = volume.GetCenter();
            Vector3 projected = center - normal
                * (Vector3.Dot(center - zero, normal) / lengthSquared);
            CollisionResult result = default;
            return SnareContainsPoint(zero, one, two, projected)
                && CollisionDetection.CheckSphereOverlapVolume(
                    volume, projected, SnareHalfThickness, ref result);
        }
    }
}
