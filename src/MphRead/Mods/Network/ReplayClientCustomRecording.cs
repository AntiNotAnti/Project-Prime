using System;

namespace MphRead.Mods.Network;

internal static partial class ReplayCapture
{
    internal static bool CustomClientRecordingScope()
    {
        if (Environment.GetEnvironmentVariable("PROJECT_PRIME_CUSTOM_WEAPON_RECORDING") != "1"
            || !NetSession.Active || !NetSession.IsClient || NetSession.IsAuthority || Headless.Active || DemoPlayback.IsActive
            || !NetSession.RequiredWeaponPackagesReady || !NetSession.CustomWeaponReplicaReady
            || Recorder.CustomWeaponCaptureFailed || Recorder.CustomWeaponContext is not { } context
            || context.Bootstrap.OriginKind != ReplayCustomWeaponOriginKind.Recipient
            || GameState.Current.Owner is not { } owner || owner.Services.IsReplica
            || owner.PrivateCharacterGameplayUnsupported || owner.DeveloperMomentumPracticeTuning != null
            || owner.OfflineWeapons != null || owner.CustomCombat != null)
            return false;
        return owner.HasOnlineWeaponPackage(context.Bootstrap.Start, context.Bootstrap.Packages.Digest);
    }

    internal static bool CanStartClientRecording(out string? error)
    {
        if (CanStartLiveRecording(out error)) return true;
        if (!CustomClientRecordingScope() || LiveRecordingActive
            || !Recorder.TryCaptureCustomWeaponState(out _, out _)) return false;
        error = null; return true;
    }
}
