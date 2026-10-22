using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Protocol 71 retains established strafe-jump coefficients. Self-blasts measure
// the real biped capsule rather than treating its lower sphere centre as a point.
internal static class Quake3MovementProfileV3
{
    internal const int ProfileVersion = 3;
    internal const float GroundSpeed = Quake3MovementProfileV2.GroundSpeed;
    internal static MomentumTuning Tuning => Quake3MovementProfileV2.Tuning;
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input)
        => Quake3MovementProfileV2.Step(velocity, input);

    internal static float BlastDistance(Vector3 origin, Vector3 body,
        float bottom = -.5f, float top = 1.1f, float bodyRadius = .5f)
    {
        if (!Finite(origin) || !Finite(body) || !float.IsFinite(bottom) || !float.IsFinite(top)
            || !float.IsFinite(bodyRadius) || bodyRadius <= 0 || top - bottom < 2 * bodyRadius)
            throw new ArgumentOutOfRangeException(nameof(bodyRadius), "A finite biped capsule is required.");
        double x = (double)origin.X - body.X, z = (double)origin.Z - body.Z;
        double low = (double)body.Y + bottom + bodyRadius, high = (double)body.Y + top - bodyRadius;
        double y = origin.Y - Math.Clamp((double)origin.Y, low, high);
        return (float)Math.Max(0, Math.Sqrt(x * x + y * y + z * z) - bodyRadius);
    }

    // Base weapon splash determines physical strength; health handicaps and
    // later health routing cannot weaken or amplify the same physical blast.
    // Native damage curves/LOS/immunity still gate the admitted self hit.
    internal static Vector3 RocketImpulse(uint splashDamage, Vector3 origin, Vector3 body, float radius,
        float bottom = -.5f, float top = 1.1f, float bodyRadius = .5f)
    {
        if (!float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "A finite positive blast radius is required.");
        float distance = BlastDistance(origin, body, bottom, top, bodyRadius);
        double strength = Math.Min(Quake3MovementProfileV2.MaximumRocketImpulse,
            splashDamage * (double)Quake3MovementProfileV2.RocketImpulsePerDamage * Math.Max(0, 1 - distance / radius));
        double x = (double)body.X - origin.X;
        double y = (double)body.Y - origin.Y + Quake3MovementProfileV2.RocketBodyAimHeight;
        double z = (double)body.Z - origin.Z;
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length == 0) return new(0, (float)strength, 0);
        return new((float)(x / length * strength), (float)(y / length * strength), (float)(z / length * strength));
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
