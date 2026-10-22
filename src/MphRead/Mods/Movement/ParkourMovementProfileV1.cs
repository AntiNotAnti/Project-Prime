using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Parkour owns its version independently; its base movement remains frozen to Traversal v5.
internal static class ParkourMovementProfileV1
{
    internal const int ProfileVersion = 1;
    internal const int SlideTicks = TraversalMovementProfileV5.SlideTicks;
    internal const int SlideEntryTicks = TraversalMovementProfileV5.SlideEntryTicks;
    internal const float SlideFriction = TraversalMovementProfileV5.SlideFriction;
    internal const float GroundSpeed = TraversalMovementProfileV5.GroundSpeed;
    internal const float SlideMinimumSpeed = TraversalMovementProfileV5.SlideMinimumSpeed;
    internal const float CrouchSpeed = TraversalMovementProfileV5.CrouchSpeed;
    internal static MomentumTuning Tuning => TraversalMovementProfileV5.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => TraversalMovementProfileV5.CrouchTuning(sliding);
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
        => TraversalMovementProfileV5.Step(velocity, input, crouched, sliding);
}
