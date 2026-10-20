using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Walkable slope travel and earned slide gravity change in v5. The v4 flat
// glide, entry window and all historical profile classes remain unchanged.
internal static class TraversalMovementProfileV5
{
    internal const int ProfileVersion = 5;
    internal const int SlideTicks = TraversalMovementProfileV4.SlideTicks;
    internal const int SlideEntryTicks = TraversalMovementProfileV4.SlideEntryTicks;
    internal const float SlideFriction = TraversalMovementProfileV4.SlideFriction;
    internal const float GroundSpeed = TraversalMovementProfileV4.GroundSpeed;
    internal const float SlideMinimumSpeed = TraversalMovementProfileV4.SlideMinimumSpeed;
    internal const float CrouchSpeed = TraversalMovementProfileV4.CrouchSpeed;
    internal static MomentumTuning Tuning => TraversalMovementProfileV4.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => TraversalMovementProfileV4.CrouchTuning(sliding);
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
        => TraversalMovementProfileV4.Step(velocity, input, crouched, sliding);
}
