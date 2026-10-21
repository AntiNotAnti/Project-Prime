using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Parkour v5 retains immutable v4 base tuning; protocol82 versions grapple admission separately.
internal static class ParkourMovementProfileV5
{
    internal const int ProfileVersion = 5;
    internal const int SlideTicks = ParkourMovementProfileV4.SlideTicks;
    internal const int SlideEntryTicks = ParkourMovementProfileV4.SlideEntryTicks;
    internal const float SlideFriction = ParkourMovementProfileV4.SlideFriction;
    internal const float GroundSpeed = ParkourMovementProfileV4.GroundSpeed;
    internal const float SlideMinimumSpeed = ParkourMovementProfileV4.SlideMinimumSpeed;
    internal const float CrouchSpeed = ParkourMovementProfileV4.CrouchSpeed;
    internal static MomentumTuning Tuning => ParkourMovementProfileV4.Tuning;
    internal static MomentumTuning CrouchTuning(bool sliding) => ParkourMovementProfileV4.CrouchTuning(sliding);
    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input,
        bool crouched = false, bool sliding = false)
        => ParkourMovementProfileV4.Step(velocity, input, crouched, sliding);
}
