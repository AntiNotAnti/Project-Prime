using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    public enum MorphBoostBranch { Shoulder, TouchBoost, SkipShoulder }

    public static class MorphBallTouchRules
    {
        public const float TouchRollPerDsPixel = 5f / 4096f;
        public const int TouchBoostThresholdSquared = 8100;

        public static MorphBoostBranch Arbitrate(bool boosting, bool canTouchBoost,
            in MorphTouchReport touch)
        {
            if (boosting || !canTouchBoost || !touch.Continued)
                return MorphBoostBranch.Shoulder;
            int dx = touch.Delta4X, dy = touch.Delta4Y;
            return dx * dx + dy * dy > TouchBoostThresholdSquared
                ? MorphBoostBranch.TouchBoost : MorphBoostBranch.SkipShoulder;
        }

        public static Vector2 TouchRoll(short dx, short dy, float scale,
            float fbX, float fbZ, float lrX, float lrZ, float share)
            => new(
                (-dy * scale * fbX - dx * scale * lrX) * share,
                (-dy * scale * fbZ - dx * scale * lrZ) * share);

        public static Vector2 TouchBoostImpulse(short dx, short dy, float speedMax,
            float forwardX, float forwardZ, float sideX, float sideZ)
        {
            float fx = dx, fy = dy;
            float mag = MathF.Sqrt(fx * fx + fy * fy);
            if (!(mag > 0)) return Vector2.Zero;
            float side = -fx / mag * speedMax;
            float forward = -fy / mag * speedMax;
            return new Vector2(forwardX * forward + sideX * side,
                forwardZ * forward + sideZ * side);
        }
    }

    public static class MorphBallBoostStateMachine
    {
        public readonly record struct Result(MorphBoostBranch Branch, bool TouchFired, ushort ShoulderCharge);

        public sealed class SampleLatch
        {
            internal uint Identity;
            internal bool Valid;
            internal MorphBoostBranch Branch;
            public void Reset() { Identity = 0; Valid = false; Branch = MorphBoostBranch.Shoulder; }
        }

        public static Result Advance(SampleLatch latch, uint identity,
            bool boosting, ref bool canTouchBoost, in MorphTouchReport touch,
            bool shoulderHeld, ref ushort charge, ushort min, ushort max)
        {
            if (!touch.Down) canTouchBoost = true;
            bool fresh = !latch.Valid || latch.Identity != identity;
            if (fresh)
            {
                latch.Valid = true;
                latch.Identity = identity;
                latch.Branch = MorphBallTouchRules.Arbitrate(boosting, canTouchBoost, touch);
            }

            if (latch.Branch == MorphBoostBranch.TouchBoost)
            {
                if (fresh)
                {
                    canTouchBoost = false;
                    return new(latch.Branch, true, 0);
                }
                return new(latch.Branch, false, 0);
            }
            if (latch.Branch == MorphBoostBranch.SkipShoulder)
                return new(latch.Branch, false, 0);

            if (shoulderHeld)
            {
                if (charge < max) charge++;
                return new(latch.Branch, false, 0);
            }

            ushort spent = charge > min ? charge : (ushort)0;
            charge = 0;
            return new(latch.Branch, false, spent);
        }
    }
}
