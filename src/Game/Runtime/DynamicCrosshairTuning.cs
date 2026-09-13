using System;

namespace MphRead;

/// <summary>
/// Shared bounds and response math for the source-agnostic dynamic crosshair.
/// Client settings use the same clamps as gameplay so malformed preferences
/// cannot introduce non-finite aim or camera state.
/// </summary>
public static class DynamicCrosshairTuning
{
    public const float DefaultTravelDegrees = 0;
    public const float MaximumTravelDegrees = 30;
    public const float FollowRangeDegrees = 15;
    public const float DefaultMovementSensitivity = 1;
    public const float MinimumMovementSensitivity = 0.1f;
    public const float MaximumMovementSensitivity = 10;
    public const float DefaultTurnSpeed = 1;
    public const float MinimumTurnSpeed = 0.1f;
    public const float MaximumTurnSpeed = 10;

    public static float TravelDegrees(float value)
        => float.IsFinite(value)
            ? Math.Clamp(value, DefaultTravelDegrees, MaximumTravelDegrees)
            : DefaultTravelDegrees;

    public static float MovementSensitivity(float value)
        => float.IsFinite(value)
            ? Math.Clamp(value, MinimumMovementSensitivity,
                MaximumMovementSensitivity)
            : DefaultMovementSensitivity;

    public static float TurnSpeed(float value)
        => float.IsFinite(value)
            ? Math.Clamp(value, MinimumTurnSpeed, MaximumTurnSpeed)
            : DefaultTurnSpeed;

    public static bool UsesLegacyCameraResponse(float travelDegrees, float turnSpeed)
        => TravelDegrees(travelDegrees) == DefaultTravelDegrees
            && TurnSpeed(turnSpeed) == DefaultTurnSpeed;

}
