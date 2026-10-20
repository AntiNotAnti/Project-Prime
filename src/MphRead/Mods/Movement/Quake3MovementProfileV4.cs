using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Ground slope travel changes in v4; horizontal coefficients stay immutable.
internal static class Quake3MovementProfileV4
{
    internal const int ProfileVersion = 4;
    internal const float GroundSpeed = Quake3MovementProfileV3.GroundSpeed;
    internal static MomentumTuning Tuning => Quake3MovementProfileV3.Tuning;
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input)
        => Quake3MovementProfileV3.Step(velocity, input);
}
