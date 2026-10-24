#if MPHREAD_AVALONIA
using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using ProjectPrime.CaptureZone.Ui;

namespace MphRead.Mods.HeroModes;
internal static class HeroSelectionFactory
{
    public static CaptureZoneLobbyView Create(bool respawn = false) => new(
        () => HeroRosterBridge.Snapshot(NetSession.LobbyRoster(),
            NetSession.ServerSession is { } session ? LobbyRules.TeamCapacity(session.Match, 0) : 4),
        HeroRosterBridge.SlotId(NetSession.LocalSlot),
        hunter =>
        {
            if (!NetSession.Active) return "Connect to a CTZ lobby first.";
            if (respawn && (NetSession.LocalSlot < 0 || NetSession.LocalSlot >= PlayerEntity.Players.Count
                || PlayerEntity.Players[NetSession.LocalSlot].Health > 0
                || PlayerEntity.Players[NetSession.LocalSlot].RespawnTimer == 0))
                return "Hunter changes are only allowed during your respawn countdown.";
            if (!respawn && !NetSession.IsInLobby) return "Wait for the lobby or use respawn selection.";
            var roster = NetSession.LobbyRoster();
            int me = Array.IndexOf(roster.Slots, (byte)NetSession.LocalSlot, 0, roster.Count);
            if (me < 0 || (roster.Flags[me] & HeroRosterBridge.SpectatorFlag) != 0) return "Spectators do not reserve hunters.";
            Hunter requested = HeroRules.ToGame(hunter);
            for (int i = 0; i < roster.Count; i++)
                if (i != me && roster.Teams[i] == roster.Teams[me] && roster.Hunters[i] == (byte)requested
                    && (roster.Flags[i] & HeroRosterBridge.SpectatorFlag) == 0)
                    return $"Already selected by your teammate: {roster.Names[i]}.";
            NetSession.LocalHunter = requested;
            NetSession.SendIdentify();
            return $"Requested {hunter}; waiting for the authoritative roster.";
        }, "HERO SHOOTER", h => HeroRules.WeaponDetails(HeroRules.ToGame(h)), () => HeroRosterBridge.SlotId(NetSession.LocalSlot));
}
#endif
