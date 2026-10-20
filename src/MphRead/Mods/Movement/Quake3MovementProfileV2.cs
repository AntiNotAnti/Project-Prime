using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Protocol 62 adds contact-only held hops and additive self-missile jumps.
// The horizontal coefficients remain identical to the immutable first profile.
internal static class Quake3MovementProfileV2
{
    internal const int ProfileVersion = 2;
    internal const float GroundSpeed = Quake3MovementProfileV1.GroundSpeed;
    internal const float RocketImpulsePerDamage = .025f;
    internal const float MaximumRocketImpulse = 1.2f; // below the existing 1.5 wire bound
    internal const float RocketBodyAimHeight = .5f;
    internal static MomentumTuning Tuning => Quake3MovementProfileV1.Tuning;
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input)
        => Quake3MovementProfileV1.Step(velocity, input);

    // Native velocity units. Health damage remains the existing weapon outcome;
    // distance attenuates the physical push independently of its damage curve.
    // Both local contacts and authority-validated self-splash use this function.
    internal static Vector3 RocketImpulse(uint damage, Vector3 origin, Vector3 body, float radius)
    {
        if (!Finite(origin) || !Finite(body) || !float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "A finite explosion and positive radius are required.");
        double x = (double)body.X - origin.X, y = (double)body.Y - origin.Y, z = (double)body.Z - origin.Z;
        double distance = Math.Sqrt(x * x + y * y + z * z);
        double strength = Math.Min(MaximumRocketImpulse,
            damage * (double)RocketImpulsePerDamage * Math.Max(0, 1 - distance / radius));
        y += RocketBodyAimHeight;
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length == 0) return new Vector3(0, (float)strength, 0);
        return new Vector3((float)(x / length * strength), (float)(y / length * strength),
            (float)(z / length * strength));
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X)
        && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
