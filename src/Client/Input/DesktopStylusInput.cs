namespace MphRead.Mods.Input
{
    /// <summary>SDL-owned stylus source consumed by the neutral input pass.</summary>
    internal static class DesktopStylusInput
    {
        public static StylusInput Input { get; } = new();

        public static StylusState ConsumeState() => Input.ConsumeState();

        public static void Cancel() => Input.Cancel();
    }
}
