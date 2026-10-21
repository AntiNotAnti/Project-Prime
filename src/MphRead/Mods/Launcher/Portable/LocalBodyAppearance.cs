using System;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Characters;

namespace MphRead.Mods.Launcher;

// Local presentation identity. Never a Hunter, gameplay capability or wire ID.
public enum LocalBodyAppearance { Native, DarkSamusEchoes, DarkSamusOvercharged }

public static class LocalBodyAppearanceRules
{
    public static bool Available => CharacterSourceRootWitness.QualifiedForCurrentProcess;
    public static LocalBodyAppearance Canonical(LocalBodyAppearance value)
        => value is >= LocalBodyAppearance.Native and <= LocalBodyAppearance.DarkSamusOvercharged
            ? value : LocalBodyAppearance.Native;
    public static string? BundleId(LocalBodyAppearance value) => value switch
    {
        LocalBodyAppearance.DarkSamusEchoes => "darksamus-echoes",
        LocalBodyAppearance.DarkSamusOvercharged => "darksamus-overcharged-echoes",
        _ => null
    };
    public static bool TryAdmit(LaunchPlan plan, out string reason)
    {
        reason = "";
        if (plan.LocalBody == LocalBodyAppearance.Native) return true;
        if (Canonical(plan.LocalBody) != plan.LocalBody)
            reason = "Unknown local body appearance.";
        else if (plan.Kind != LaunchKind.Offline || plan.Hunter != Hunter.Samus
            || plan.Mode == GameMode.SinglePlayer || plan.OfflineCharacter != OfflineCharacterMode.Native
            || plan.MatchRules.RiftlineM7Available || NetSession.Active || DemoPlayback.IsActive
            || ReplayCapture.LiveRecordingActive)
            reason = "Imported bodies require a local offline Samus match without custom weapons or replay recording.";
        else if (!Available)
            reason = CharacterSourceRootWitness.UnsupportedPlatformMessage;
        return reason.Length == 0;
    }
}
