using System;

namespace MphRead.Mods.Network;

// Native replay construction has no custom definition/bootstrap contract yet.
// Keep the selected scene marked through readiness loss and disconnect: neither
// can undo custom combat already observed there. The ownerless fallback covers
// packet admission before a foreground scene exists, without retaining a scene.
internal static class CustomReplayBoundary
{
    // Frozen availability first became a live rule in protocol70. A later bump
    // cannot make those unsupported custom archives inspectable as native ones.
    internal const int FirstFrozenAvailabilityProtocol = 70;
    private static bool _ownerlessSelected;
    internal const string Error = "Replay recording and killcams are unavailable in custom weapon scenes. Start a fresh native scene to record.";

    internal static bool IsBlocked(Scene? scene = null)
    {
        // Frozen availability already makes this a custom match, including
        // the interval before its first replica page is received.
        if (NetSession.PublicCustomWeaponAvailability) return true;
        var owner = scene ?? GameState.Current.Owner;
        if (owner == null) return _ownerlessSelected;
        if (_ownerlessSelected)
        {
            owner.CustomReplayUnsupported = true;
            _ownerlessSelected = false;
        }
        return owner.CustomReplayUnsupported || owner.CustomCombat != null || owner.OfflineWeapons != null;
    }

    internal static bool TryActivateClient(out string? error)
    {
        if (ReplayCapture.LiveRecordingActive) { error = Error; return false; }
        if (GameState.Current.Owner is { } owner)
        {
            if (!owner.CustomReplayUnsupported) Mods.KillCam.Reset();
            owner.CustomReplayUnsupported = true;
            _ownerlessSelected = false;
        }
        else { _ownerlessSelected = true; Mods.KillCam.Reset(); }
        error = null;
        return true;
    }

    internal static void RequireActivationAllowed()
    {
        if (ReplayCapture.LiveRecordingActive) throw new InvalidOperationException(Error);
    }

    internal static void ResetMatch() => _ownerlessSelected = false;
}
