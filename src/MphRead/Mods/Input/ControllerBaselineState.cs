namespace MphRead.Mods.Input
{
    /// <summary>
    /// Diagnostic A/B switch for the unassisted controller baseline.
    /// Production starts assisted; the baseline can still be forced by the
    /// existing debug/command-line path without exposing a gameplay setting.
    /// </summary>
    public static class ControllerBaselineState
    {
        public static bool Enabled => AimAssist.AimAssistDebug.UnassistedArm;

        public static void Load()
        {
            AimAssist.AimAssistDebug.UnassistedArm = false;
        }

        public static bool Toggle()
        {
            AimAssist.AimAssistDebug.UnassistedArm = !AimAssist.AimAssistDebug.UnassistedArm;
            return AimAssist.AimAssistDebug.UnassistedArm;
        }
    }
}
