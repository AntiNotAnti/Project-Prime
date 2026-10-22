// Engine boundary stubs only. The actual local presenter and portable draft
// controller are source-linked; these checks do not claim engine integration.
namespace MphRead
{
    public enum GameMode : byte { SinglePlayer }
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
    // Compile-only engine boundary for the source-linked local appearance
    // policy. These checks exercise the native Hunter/Suit presenter, not
    // imported-body admission, gameplay, replay or character assets.
    public enum LaunchKind { Offline }
    public enum OfflineCharacterMode { Native, TrooperScoutDronePractice = 2 }
    public sealed record LocalAppearanceMatchRules(bool RiftlineM7Available = false);
    public sealed record LaunchPlan(LocalBodyAppearance LocalBody, LaunchKind Kind,
        Hunter Hunter, GameMode Mode, OfflineCharacterMode OfflineCharacter,
        LocalAppearanceMatchRules MatchRules, LocalDroneAppearance LocalDrone = LocalDroneAppearance.Native);
    internal static class OfflineCharacterGameplay
    {
        // Imported gameplay is outside this source-only presenter fixture. Keep
        // the real drone policy linked while refusing this engine boundary.
        internal static bool TryAdmit(LaunchPlan plan, out string reason)
        { reason = "Imported character gameplay is unavailable in this source-only check."; return false; }
    }
    public static class LauncherPrefs
    {
        public static Hunter LastHunter { get; set; } = Hunter.Samus;
        public static int LastColor { get; set; }
        internal static bool SaveSucceeds = true;
        internal static int SaveAttempts;
        public static bool TrySave() { SaveAttempts++; return SaveSucceeds; }
    }
}
namespace MphRead.Mods.Network
{
    public static class NetSession { public static bool Active => false; }
    public static class DemoPlayback { public static bool IsActive => false; }
    public static class ReplayCapture { public static bool LiveRecordingActive => false; }
}
namespace MphRead.Mods.Render.Characters
{
    public static class CharacterSourceRootWitness
    {
        public static bool QualifiedForCurrentProcess => false;
        public const string UnsupportedPlatformMessage = "Imported character assets are unavailable in this source-only check.";
    }
}
