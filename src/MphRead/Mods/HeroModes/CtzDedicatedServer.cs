using System;
using System.Linq;
using MphRead.Mods.HeroModes;
using ProjectPrime.CaptureZone;

namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private bool CtzSelectAllowed(Peer peer, byte hunter, out string reason)
    {
        reason = "";
        if (!HeroRules.Active(CurrentDefinition.Mode)) return true;
        if (peer.Spectating) { reason = "Spectators do not reserve hunters."; return false; }
        bool inLobby = _phase == SessionPhase.Lobby;
        var player = _sim?.ActiveScene?.Players.Items[peer.SlotIndex];
        bool respawn = !inLobby && _phase == SessionPhase.InMatch && player != null && player.Health == 0 && player.RespawnTimer > 0;
        if (!inLobby && !respawn) { reason = "Change hunters only in the lobby or during your respawn countdown."; return false; }
        try
        {
            var core = HeroRosterBridge.Create(BuildRoster(), LobbyRules.TeamCapacity(CurrentDefinition, peer.TeamIndex),
                inLobby ? ZoneLobbyPhase.Lobby : ZoneLobbyPhase.Combat);
            if (respawn) core.SetRespawnSelection(HeroRosterBridge.SlotId(peer.SlotIndex), true);
            var result = core.Select(HeroRosterBridge.SlotId(peer.SlotIndex), HeroRules.ToZone((Hunter)hunter));
            reason = result.Message; return result.Accepted;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { reason = ex.Message; return false; }
    }
    private bool CtzTeamAllowed(Peer peer, sbyte team, out string reason)
    {
        reason = "";
        if (!HeroRules.Active(CurrentDefinition.Mode)) return true;
        try
        {
            var core = HeroRosterBridge.Create(BuildRoster(), LobbyRules.TeamCapacity(CurrentDefinition, team));
            var result = core.ChangeTeam(HeroRosterBridge.SlotId(peer.SlotIndex), team);
            reason = result.Message; return result.Accepted;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { reason = ex.Message; return false; }
    }
    private byte CtzFreeHunter(int team, Peer? except = null)
    {
        for (byte h = 0; h < 7; h++)
            if (!_peers.Any(p => p != except && !p.Spectating && p.TeamIndex == team && p.Hunter == h)
                && !_bots.Any(b => b.TeamIndex == team && b.Hunter == h)) return h;
        throw new InvalidOperationException("No free hunter in a four-player CTZ team.");
    }
    private void NormalizeCtzRoster()
    {
        if (!HeroRules.Active(CurrentDefinition.Mode)) return;
        var used = new bool[2, 7];
        foreach (var peer in _peers.OrderBy(p => p.SlotIndex))
        {
            peer.DamageReduction = 0;
            if (peer.Spectating || peer.TeamIndex is < 0 or > 1) continue;
            if (peer.Hunter >= 7 || used[peer.TeamIndex, peer.Hunter])
            {
                for (byte h = 0; h < 7; h++) if (!used[peer.TeamIndex, h])
                { peer.Hunter = h; Tell(peer, $"CTZ reserves one hunter per team. Your initial selection is {(Hunter)h}; choose another available hunter in the lobby."); break; }
            }
            used[peer.TeamIndex, peer.Hunter] = true;
        }
    }
    private bool CtzPeersCompatible() => _peers.All(p => p.CtzCapability == CtzRules.Capability);
}
