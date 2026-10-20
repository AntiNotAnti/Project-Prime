using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Protocol 71 makes a running slide reachable and admits one short landing buffer.
// Existing movement coefficients and immutable v1/v2 attribution are retained.
internal static class TraversalMovementProfileV3
{
    internal const int ProfileVersion = 3;
    internal const int SlideTicks = 48;
    internal const int SlideEntryTicks = 6;
    internal const float GroundSpeed = TraversalMovementProfileV1.GroundSpeed;
    internal const float SlideMinimumSpeed = GroundSpeed * .9f;
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
