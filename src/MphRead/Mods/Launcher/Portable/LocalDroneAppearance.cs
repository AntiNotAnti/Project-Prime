using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

// Presentation only; importing or selecting this never grants drone mechanics.
public enum LocalDroneAppearance { Native = 0, Alpha = 1, Gamma = 2 }

public static class LocalDroneAppearanceRules
{
    public static bool Available => LocalBodyAppearanceRules.Available;
    public static LocalDroneAppearance Canonical(LocalDroneAppearance value)
        => value is >= LocalDroneAppearance.Native and <= LocalDroneAppearance.Gamma ? value : LocalDroneAppearance.Native;
    public static bool MatchesHunter(LocalDroneAppearance value, Hunter hunter)
        => value == LocalDroneAppearance.Native || value is LocalDroneAppearance.Alpha or LocalDroneAppearance.Gamma && hunter == Hunter.Weavel;
    public static LocalDroneAppearance CanonicalForHunter(LocalDroneAppearance value, Hunter hunter)
        => MatchesHunter(value, hunter) ? value : LocalDroneAppearance.Native;
    public static string? BundleId(LocalDroneAppearance value) => value switch
    { LocalDroneAppearance.Alpha => "trooper-drone-alpha", LocalDroneAppearance.Gamma => "trooper-drone-gamma", _ => null };
    public static bool TryAdmit(LaunchPlan plan, out string reason)
    {
        reason = "";
        if (plan.LocalDrone == LocalDroneAppearance.Native) return true;
        if (Canonical(plan.LocalDrone) != plan.LocalDrone)
            reason = "Unknown local drone appearance.";
        else if (plan.Kind != LaunchKind.Offline || plan.OfflineCharacter != OfflineCharacterMode.TrooperScoutDronePractice
            || plan.LocalBody != LocalBodyAppearance.Native || !MatchesHunter(plan.LocalDrone, plan.Hunter)
            || NetSession.Active || DemoPlayback.IsActive || ReplayCapture.LiveRecordingActive)
            reason = "Imported drones require explicit solo Scout Drone practice with native body presentation.";
        else if (!OfflineCharacterGameplay.TryAdmit(plan, out reason)) return false;
        else if (!Available) reason = "Imported drone rendering is unavailable on this platform.";
        return reason.Length == 0;
    }
}
