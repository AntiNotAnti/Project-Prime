using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

// One admission policy for native/legacy/direct offline launch owners.
internal static class OfflineMatchPreflight
{
    internal static bool TryAdmit(LaunchPlan plan, out string reason, bool prepareRoom = false)
    {
        reason = "";
        if (plan.Kind != LaunchKind.Offline) return true;
        if (NetSession.Active) { reason = "Disconnect before starting offline play."; return false; }
        int total = 1 + Math.Clamp(plan.Bots, 0, PlayerEntity.SlotCapacity - 1);
        MapDefinition? project = CustomRooms.Definitions.FirstOrDefault(d =>
            string.Equals(d.Name, plan.RoomKey, StringComparison.OrdinalIgnoreCase));
        if (project != null && !MapModeCapabilities.Supports(project, plan.Mode, total, out reason)) return false;
        if (prepareRoom) CustomRooms.GenerateMissing(plan.RoomKey);
        if (CustomRooms.WhyUnplayable(plan.RoomKey) is { } unplayable)
        { reason = unplayable; return false; }
        return MapModeCapabilities.Supports(plan.RoomKey, plan.Mode, MatchWorldProfile.Resolve(total),
            out reason, configuredPlayers: total, allowLocalSource: true);
    }
}
