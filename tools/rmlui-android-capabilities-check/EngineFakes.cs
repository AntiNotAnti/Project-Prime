// Engine boundary stubs only. The actual local presenter and portable draft
// controller are source-linked; these checks do not claim engine integration.
namespace MphRead
{
    public enum Hunter : byte
    {
        Samus = 0, Kanden = 1, Trace = 2, Sylux = 3, Noxus = 4,
        Spire = 5, Weavel = 6, Guardian = 7, Random = 8
    }
}
namespace MphRead.Mods.Multiplayer
{
    public static class HunterRules
    {
        public static bool Allowed(Hunter hunter, bool lowTier)
            => lowTier ? hunter is Hunter.Kanden or Hunter.Spire or Hunter.Noxus or Hunter.Weavel
                : hunter is >= Hunter.Samus and <= Hunter.Weavel;
    }
}
namespace MphRead.Mods.Launcher
{
    public static class LauncherPrefs
    {
        public static Hunter LastHunter { get; set; } = Hunter.Samus;
        public static int LastColor { get; set; }
        internal static bool SaveSucceeds = true;
        internal static int SaveAttempts;
        public static bool TrySave() { SaveAttempts++; return SaveSucceeds; }
    }
}
