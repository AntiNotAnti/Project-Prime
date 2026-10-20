using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// A longer earned glide, with no added impulse or steering acceleration.
// Earlier profile classes remain immutable for recorded rule attribution.
internal static class TraversalMovementProfileV4
{
    internal const int ProfileVersion = 4;
    internal const int SlideTicks = 72;
    internal const int SlideEntryTicks = TraversalMovementProfileV3.SlideEntryTicks;
    internal const float SlideFriction = .45f;
    internal const float GroundSpeed = TraversalMovementProfileV3.GroundSpeed;
    internal const float SlideMinimumSpeed = TraversalMovementProfileV3.SlideMinimumSpeed;
    internal const float CrouchSpeed = TraversalMovementProfileV3.CrouchSpeed;
    internal static MomentumTuning Tuning => TraversalMovementProfileV3.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => sliding
        ? new(0, SlideFriction, 0, 2, GroundSpeed) : Tuning;

    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
    {
        if (!crouched || !input.Grounded) return TraversalMovementProfileV3.Step(velocity, input);
        return new(MomentumMath.Step(velocity, input with { DesiredSpeed = CrouchSpeed },
            CrouchTuning(sliding)), false, false);
    }
}
