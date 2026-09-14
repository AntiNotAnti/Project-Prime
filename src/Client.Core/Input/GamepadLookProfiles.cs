namespace MphRead.Mods.Input
{
    public enum GamepadResponseCurvePreset
    {
        Linear,
        Balanced,
        Precision,
        Custom
    }

    public enum GamepadTurnAccelerationPreset
    {
        Off,
        Standard,
        Fast
    }

    public enum GamepadGyroMode
    {
        Off,
        Always,
        ZoomOnly,
        HoldButton
    }

    public enum GamepadGyroActivation
    {
        LeftTrigger,
        LeftBumper,
        RightThumb
    }

    public enum GamepadStickAimMode
    {
        Traditional,
        FlickStick
    }

    /// <summary>
    /// Allocation-free mappings from user-facing preset identities to the
    /// primitive values consumed by <see cref="GamepadLookProcessor"/> and
    /// persisted by the shared input settings model.
    /// </summary>
    internal static class GamepadLookProfiles
    {
        internal const float LinearExponent = 1;
        internal const float BalancedExponent = GamepadLookProcessor.DefaultExponent;
        internal const float PrecisionExponent = 2;

        // Initial tuning values. Hardware validation is still required before
        // treating Fast as competitive-performance evidence.
        internal const float FastBoostDelaySeconds = 0.09f;
        internal const float FastBoostRampSeconds = 0.06f;

        internal static float ResponseExponent(GamepadResponseCurvePreset preset,
            float customExponent)
            => preset switch
            {
                GamepadResponseCurvePreset.Linear => LinearExponent,
                GamepadResponseCurvePreset.Balanced => BalancedExponent,
                GamepadResponseCurvePreset.Precision => PrecisionExponent,
                GamepadResponseCurvePreset.Custom => customExponent,
                _ => BalancedExponent
            };

        internal static GamepadTurnAccelerationProfile TurnAcceleration(
            GamepadTurnAccelerationPreset preset)
            => preset switch
            {
                GamepadTurnAccelerationPreset.Off => new(
                    Enabled: false,
                    GamepadLookProcessor.DefaultOuterBoostStart,
                    GamepadLookProcessor.DefaultOuterYawBoost,
                    GamepadLookProcessor.DefaultOuterPitchBoost,
                    GamepadLookProcessor.DefaultBoostDelaySeconds,
                    GamepadLookProcessor.DefaultBoostRampSeconds),
                GamepadTurnAccelerationPreset.Fast => new(
                    Enabled: true,
                    GamepadLookProcessor.DefaultOuterBoostStart,
                    GamepadLookProcessor.DefaultOuterYawBoost,
                    GamepadLookProcessor.DefaultOuterPitchBoost,
                    FastBoostDelaySeconds,
                    FastBoostRampSeconds),
                _ => new(
                    Enabled: true,
                    GamepadLookProcessor.DefaultOuterBoostStart,
                    GamepadLookProcessor.DefaultOuterYawBoost,
                    GamepadLookProcessor.DefaultOuterPitchBoost,
                    GamepadLookProcessor.DefaultBoostDelaySeconds,
                    GamepadLookProcessor.DefaultBoostRampSeconds)
            };
    }

    internal readonly record struct GamepadTurnAccelerationProfile(
        bool Enabled,
        float OuterBoostStart,
        float OuterYawBoost,
        float OuterPitchBoost,
        float BoostDelaySeconds,
        float BoostRampSeconds);
}
