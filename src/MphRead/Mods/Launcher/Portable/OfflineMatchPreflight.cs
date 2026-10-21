using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Launcher;

// One admission policy for native/legacy/direct offline launch owners.
internal static class OfflineMatchPreflight
{
    internal static bool TryAdmit(LaunchPlan plan, out string reason, bool prepareRoom = false)
    {
        reason = "";
        if (!LocalBodyAppearanceRules.TryAdmit(plan, out reason)) return false;
        if (!LocalDroneAppearanceRules.TryAdmit(plan, out reason)) return false;
        if (!LocalArsenalBodyRules.TryAdmit(plan, out reason)) return false;
        if (!LocalArsenalDroneRules.TryAdmit(plan, out reason)) return false;
        if (!OfflineCharacterGameplay.TryAdmit(plan, out reason)) return false;
        if (plan.MatchRules.RiftlineM7Available
            && plan.Kind is not (LaunchKind.Offline or LaunchKind.Online or LaunchKind.Host))
        { reason = "Riftline M7 pickups require offline Battle; Adventure, demos and training are unsupported."; return false; }
        if (plan.Kind != LaunchKind.Offline) return true;
        if (NetSession.Active) { reason = "Disconnect before starting offline play."; return false; }
        if (plan.MatchRules.RiftlineM7Available)
        {
            if (plan.MatchRules.Mode != plan.Mode)
            { reason = "Riftline M7 launch mode must match its frozen match rules."; return false; }
            if (!MatchModifierRules.Validate(plan.MatchRules, out reason)
                || !CustomWeaponMatchPolicy.TryAdmit(plan.Mode, plan.MatchRules.Rules,
                    online: false, plan.Bots, out reason)) return false;
            if (ReplayCapture.LiveRecordingActive)
            { reason = CustomReplayBoundary.Error; return false; }
            if (!StockRiftlinePickups.Supports(plan.RoomKey))
            { reason = "Riftline M7 pickup placements currently support the 27 stock multiplayer maps."; return false; }
        }
        int total = 1 + Math.Clamp(plan.Bots, 0, PlayerEntity.SlotCapacity - 1);
        MapDefinition? project = CustomRooms.Definitions.FirstOrDefault(d =>
            string.Equals(d.Name, plan.RoomKey, StringComparison.OrdinalIgnoreCase));
        if (project != null && plan.MatchRules.RiftlineM7Available)
        { reason = "Riftline M7 custom-map pickup layouts are not available yet."; return false; }
        if (project != null && !MapModeCapabilities.Supports(project, plan.Mode, total, out reason)) return false;
        if (prepareRoom) CustomRooms.GenerateMissing(plan.RoomKey);
        if (CustomRooms.WhyUnplayable(plan.RoomKey) is { } unplayable)
        { reason = unplayable; return false; }
        return MapModeCapabilities.Supports(plan.RoomKey, plan.Mode, MatchWorldProfile.Resolve(total),
            out reason, configuredPlayers: total, allowLocalSource: true);
    }
}
