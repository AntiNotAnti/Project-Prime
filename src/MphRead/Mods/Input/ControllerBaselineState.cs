namespace MphRead.Mods.Input
{
    /// <summary>
    /// Compatibility shim for the former assisted/unassisted controller comparison.
    /// Controller aim assistance is permanently disabled, so every runtime is baseline.
    /// </summary>
    public static class ControllerBaselineState
    {
        public static bool Enabled => true;

        public static void Load()
        {
            Apply();
        }

        public static bool Toggle()
        {
            Apply();
            return true;
        }

        private static void Apply()
        {
            AimAssist.AimAssistDebug.UnassistedArm = true;
        }
    }
}
