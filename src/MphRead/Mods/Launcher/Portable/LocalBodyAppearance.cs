using System;
using MphRead.Mods.Network;
using MphRead.Mods.Render.Characters;

namespace MphRead.Mods.Launcher;

// Local presentation identity. Never a Hunter, gameplay capability or wire ID.
public enum LocalBodyAppearance { Native = 0, DarkSamusEchoes = 1, DarkSamusOvercharged = 2,
    TrooperPower = 3, TrooperIce = 4, TrooperWave = 5, TrooperPlasma = 6 }

public static class LocalBodyAppearanceRules
{
    public static bool Available =>
#if MPHREAD_SERVER || ANDROID
        false;
#else
        CharacterSourceRootWitness.QualifiedForCurrentProcess;
#endif
    public static LocalBodyAppearance Canonical(LocalBodyAppearance value)
        => value is >= LocalBodyAppearance.Native and <= LocalBodyAppearance.TrooperPlasma
            ? value : LocalBodyAppearance.Native;
    public static bool MatchesHunter(LocalBodyAppearance value, Hunter hunter) => value switch
    {
        LocalBodyAppearance.Native => true,
        LocalBodyAppearance.DarkSamusEchoes or LocalBodyAppearance.DarkSamusOvercharged => hunter == Hunter.Samus,
        >= LocalBodyAppearance.TrooperPower and <= LocalBodyAppearance.TrooperPlasma => hunter == Hunter.Weavel,
        _ => false
    };
    public static LocalBodyAppearance CanonicalForHunter(LocalBodyAppearance value, Hunter hunter)
        => MatchesHunter(value, hunter) ? value : LocalBodyAppearance.Native;
    public static string? BundleId(LocalBodyAppearance value) => value switch
    {
        LocalBodyAppearance.DarkSamusEchoes => "darksamus-echoes",
        LocalBodyAppearance.DarkSamusOvercharged => "darksamus-overcharged-echoes",
        LocalBodyAppearance.TrooperPower => "trooper-power",
        LocalBodyAppearance.TrooperIce => "trooper-ice",
        LocalBodyAppearance.TrooperWave => "trooper-wave",
        LocalBodyAppearance.TrooperPlasma => "trooper-plasma",
        _ => null
    };
    public static bool TryAdmit(LaunchPlan plan, out string reason)
    {
        reason = "";
        if (plan.LocalBody == LocalBodyAppearance.Native) return true;
        if (Canonical(plan.LocalBody) != plan.LocalBody)
            reason = "Unknown local body appearance.";
        else if (plan.Kind != LaunchKind.Offline || !MatchesHunter(plan.LocalBody, plan.Hunter)
            || plan.Mode == GameMode.SinglePlayer || plan.OfflineCharacter != OfflineCharacterMode.Native
            || plan.MatchRules.RiftlineM7Available || NetSession.Active || DemoPlayback.IsActive
            || ReplayCapture.LiveRecordingActive)
            reason = "Imported bodies require a matching local offline Hunter without custom weapons or replay recording.";
        else if (!Available)
            reason = CharacterSourceRootWitness.UnsupportedPlatformMessage;
        return reason.Length == 0;
    }
}
