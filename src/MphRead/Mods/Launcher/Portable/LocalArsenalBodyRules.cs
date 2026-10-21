using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

// A distinct local request, never a gameplay rule or a native-body capability.
public static class LocalArsenalBodyRules
{
    public static bool Available => LocalBodyAppearanceRules.Available;

    public static bool TryAdmit(LaunchPlan plan, out string reason)
    {
        reason = "";
        if (plan.LocalArsenalBody == LocalBodyAppearance.Native) return true;
        if (plan.LocalArsenalBody is < LocalBodyAppearance.TrooperPower or > LocalBodyAppearance.TrooperPlasma)
            reason = "Arsenal appearance requires a selected Trooper body.";
        else if (plan.OfflineCharacter != OfflineCharacterMode.TrooperArsenalPractice
            || plan.Kind != LaunchKind.Offline || plan.Hunter != Hunter.Weavel
            || plan.LocalBody != LocalBodyAppearance.Native || plan.LocalDrone != LocalDroneAppearance.Native
            || NetSession.Active || DemoPlayback.IsActive || ReplayCapture.LiveRecordingActive)
            reason = "Imported Arsenal bodies require explicit offline Trooper Arsenal practice.";
        else if (!OfflineCharacterGameplay.TryAdmit(plan, out reason)) return false;
        else if (!Available)
            reason = Render.Characters.CharacterSourceRootWitness.UnsupportedPlatformMessage;
        return reason.Length == 0;
    }
}
