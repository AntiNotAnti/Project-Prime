using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Immutable Parkour v3 base tuning inherits v2. Wall-run acceptance is separately versioned by protocol79.
internal static class ParkourMovementProfileV3
{
    internal const int ProfileVersion = 3;
    internal const int SlideTicks = ParkourMovementProfileV2.SlideTicks;
    internal const int SlideEntryTicks = ParkourMovementProfileV2.SlideEntryTicks;
    internal const float SlideFriction = ParkourMovementProfileV2.SlideFriction;
    internal const float GroundSpeed = ParkourMovementProfileV2.GroundSpeed;
    internal const float SlideMinimumSpeed = ParkourMovementProfileV2.SlideMinimumSpeed;
    internal const float CrouchSpeed = ParkourMovementProfileV2.CrouchSpeed;
    internal static MomentumTuning Tuning => ParkourMovementProfileV2.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => ParkourMovementProfileV2.CrouchTuning(sliding);
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
        => ParkourMovementProfileV2.Step(velocity, input, crouched, sliding);
}
