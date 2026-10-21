using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

// The existing solo request remains solo-only. This capability belongs solely
// to an explicitly selected Arsenal practice, independent of its Body choice.
public static class LocalArsenalDroneRules
{
    public static bool Available => LocalDroneAppearanceRules.Available;
    public static bool TryAdmit(LaunchPlan plan, out string reason)
    {
        reason = "";
        if (plan.LocalArsenalDrone == LocalDroneAppearance.Native) return true;
        if (plan.LocalArsenalDrone is not (LocalDroneAppearance.Alpha or LocalDroneAppearance.Gamma))
            reason = "Unknown Arsenal drone appearance.";
        else if (plan.Kind != LaunchKind.Offline || plan.Hunter != Hunter.Weavel
            || plan.OfflineCharacter != OfflineCharacterMode.TrooperArsenalPractice
            || plan.LocalBody != LocalBodyAppearance.Native || plan.LocalDrone != LocalDroneAppearance.Native
            || NetSession.Active || DemoPlayback.IsActive || ReplayCapture.LiveRecordingActive)
            reason = "Imported Arsenal drones require explicit offline Trooper Arsenal practice.";
        else if (!OfflineCharacterGameplay.TryAdmit(plan, out reason)
            || !LocalArsenalBodyRules.TryAdmit(plan, out reason)) return false;
        else if (!Available) reason = "Imported Arsenal drone rendering is unavailable on this platform.";
        return reason.Length == 0;
    }
}
