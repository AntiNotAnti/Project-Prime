using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Immutable Parkour v2 base tuning inherits v1. Wall-strafe acceptance is separately versioned by protocol78.
internal static class ParkourMovementProfileV2
{
    internal const int ProfileVersion = 2;
    internal const int SlideTicks = ParkourMovementProfileV1.SlideTicks;
    internal const int SlideEntryTicks = ParkourMovementProfileV1.SlideEntryTicks;
    internal const float SlideFriction = ParkourMovementProfileV1.SlideFriction;
    internal const float GroundSpeed = ParkourMovementProfileV1.GroundSpeed;
    internal const float SlideMinimumSpeed = ParkourMovementProfileV1.SlideMinimumSpeed;
    internal const float CrouchSpeed = ParkourMovementProfileV1.CrouchSpeed;
    internal static MomentumTuning Tuning => ParkourMovementProfileV1.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => ParkourMovementProfileV1.CrouchTuning(sliding);
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
        => ParkourMovementProfileV1.Step(velocity, input, crouched, sliding);
}
