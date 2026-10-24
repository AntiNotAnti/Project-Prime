using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.HeroModes;

/// <summary>Native menu adapter for the existing authoritative roster reservations.</summary>
internal static class HeroSelectionPolicy
{
    internal static Hunter[] Available(RosterPacket roster, int slot)
    {
        int me = Array.IndexOf(roster.Slots, (byte)slot, 0, roster.Count);
        if (me < 0 || (roster.Flags[me] & HeroRosterBridge.SpectatorFlag) != 0) return [];
        return Enumerable.Range(0, 7).Select(i => (Hunter)i).Where(hunter =>
            !Enumerable.Range(0, roster.Count).Any(i => i != me && roster.Teams[i] == roster.Teams[me]
                && roster.Hunters[i] == (byte)hunter && (roster.Flags[i] & HeroRosterBridge.SpectatorFlag) == 0)).ToArray();
    }
    internal static bool CanChangeDuringRespawn => NetSession.Active && !DemoPlayback.IsActive
        && NetSession.ActiveMatchDefinition is { } match && (HeroRules.Active(match.Mode) || CtfRules.Active(match.Mode))
        && PlayerEntity.Main is { Health: 0, RespawnTimer: > 0 };
    internal static string RequestNext()
    {
        if (!CanChangeDuringRespawn) return "Change hunters during your respawn countdown.";
        Hunter[] available = Available(NetSession.LobbyRoster(), NetSession.LocalSlot);
        if (available.Length == 0) return "No hunter reservation is available for this team.";
        int current = Array.IndexOf(available, NetSession.LocalHunter);
        NetSession.LocalHunter = available[(current + 1) % available.Length];
        NetSession.SendIdentify();
        return "";
    }
}
