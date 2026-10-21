using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Immutable Parkour v4 base tuning inherits v3. Mantle acceptance is separately versioned by protocol80.
internal static class ParkourMovementProfileV4
{
    internal const int ProfileVersion = 4;
    internal const int SlideTicks = ParkourMovementProfileV3.SlideTicks;
    internal const int SlideEntryTicks = ParkourMovementProfileV3.SlideEntryTicks;
    internal const float SlideFriction = ParkourMovementProfileV3.SlideFriction;
    internal const float GroundSpeed = ParkourMovementProfileV3.GroundSpeed;
    internal const float SlideMinimumSpeed = ParkourMovementProfileV3.SlideMinimumSpeed;
    internal const float CrouchSpeed = ParkourMovementProfileV3.CrouchSpeed;
    internal static MomentumTuning Tuning => ParkourMovementProfileV3.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => ParkourMovementProfileV3.CrouchTuning(sliding);
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
        => ParkourMovementProfileV3.Step(velocity, input, crouched, sliding);
}
