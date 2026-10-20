using System;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

internal static class OfflineCharacterGameplay
{
    internal static bool TryAdmit(LaunchPlan plan, out string reason)
    {
        reason = "";
        if (plan.OfflineCharacter == OfflineCharacterMode.Native) return true;
        if (plan.OfflineCharacter is not (OfflineCharacterMode.TrooperWallClingPractice or OfflineCharacterMode.TrooperScoutDronePractice))
        { reason = "Unknown offline character mechanics."; return false; }
        if (plan.Kind != LaunchKind.Offline || NetSession.Active || DemoPlayback.IsActive)
        { reason = "Trooper mechanics practice requires a fresh offline Battle."; return false; }
        if (!plan.IsPlaytest || plan.Hunter != Hunter.Weavel || plan.Mode != GameMode.Battle || plan.Bots != 0)
        { reason = "Trooper practice requires Weavel, Battle and zero bots."; return false; }
        if (plan.MatchRules.Mode != plan.Mode || plan.MatchRules.BalancedMode || plan.MatchRules.EnhancedHunters || plan.MatchRules.LowTier
            || plan.MatchRules.RiftlineM7Available || plan.MatchRules.MovementModel != MovementModel.Native)
        { reason = "Trooper practice requires native movement without Balanced, Enhanced, Low Tier or M7 rules."; return false; }
        if (ReplayCapture.LiveRecordingActive)
        { reason = "Stop recording before starting Trooper practice."; return false; }
        return true;
    }

    internal static bool TryBind(Scene scene, LaunchPlan plan, out string reason)
    {
        if (!TryAdmit(plan, out reason)) return false;
        if (plan.OfflineCharacter == OfflineCharacterMode.Native) return true;
        // Only the exact already-created local slot may receive the rule,
        // before room/player initialization. A failed bind aborts the launch.
        var players = scene.Players.Items;
        if (players.Count == 0 || players[0].IsBot || scene.GameState.BalancedMode || scene.GameState.EnhancedHunters
            || !players[0].ModTryBindCharacterGameplay(plan.OfflineCharacter == OfflineCharacterMode.TrooperScoutDronePractice
                ? CharacterGameplayRules.TrooperDroneV1 : CharacterGameplayRules.TrooperV1))
        { reason = "The local Trooper player owner could not be bound. Reopen offline setup."; return false; }
        return true;
    }
}
