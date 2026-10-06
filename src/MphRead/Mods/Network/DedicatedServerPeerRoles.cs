namespace MphRead.Mods.Network;

public sealed partial class DedicatedServer
{
    private bool TrySetPeerRole(Peer peer, bool spectating)
    {
        bool changed = peer.Spectating != spectating;
        if (spectating)
        {
            peer.TeamIndex = -1;
            peer.LobbyReady = false;
        }
        else if (changed)
        {
            sbyte team = ChooseTeam(CurrentDefinition, peer);
            if (LobbyRules.TeamCount(CurrentDefinition) > 0 && team < 0)
            {
                Log($"slot {peer.SlotIndex} remains a spectator: combat teams are full");
                Tell(peer, "The combat teams are full. You are still spectating.");
                return false;
            }
            peer.TeamIndex = team;
            peer.LobbyReady = false;
        }
        peer.Spectating = spectating;
        if (changed) CareerPeerRoleChanged(peer);
        if (Simulating) NetSession.SetAuthoritySpectating(peer.SlotIndex, spectating);
        return true;
    }
}
