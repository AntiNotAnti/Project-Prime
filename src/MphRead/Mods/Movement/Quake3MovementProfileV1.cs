using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Immutable protocol-60 Quake3 profile. Private JSON tuning never alters this contract.
// Native jumps, gravity, collision, alternate forms and impulses remain outside it.
internal static class Quake3MovementProfileV1
{
    internal const int ProfileVersion = 1;
    internal const float GroundSpeed = MomentumProfileV1.GroundSpeed;
    internal static MomentumTuning Tuning => new(10, 6, GroundSpeed * .3125f, 1, GroundSpeed);
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input)
        => new(MomentumMath.Step(velocity, input with { DesiredSpeed = GroundSpeed }, Tuning), false, false);
}
