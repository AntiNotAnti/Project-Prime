using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Protocol 69 adds an accepted crouched stance and a finite power-slide window.
// Traversal v1 remains immutable for protocol-68 recording attribution.
internal static class TraversalMovementProfileV2
{
    internal const int ProfileVersion = 2;
    internal const int SlideTicks = 48;
    internal const float GroundSpeed = TraversalMovementProfileV1.GroundSpeed;
    internal const float SlideMinimumSpeed = GroundSpeed * 1.1f;
    internal const float CrouchSpeed = GroundSpeed * .5f;
    internal static MomentumTuning Tuning => TraversalMovementProfileV1.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => sliding
        ? new(0, .8f, 0, 2, GroundSpeed) : Tuning;

    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
    {
        if (!crouched || !input.Grounded) return TraversalMovementProfileV1.Step(velocity, input);
        return new(MomentumMath.Step(velocity, input with { DesiredSpeed = CrouchSpeed },
            CrouchTuning(sliding)), false, false);
    }
}
