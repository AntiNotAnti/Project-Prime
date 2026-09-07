using System;
using System.Collections.Generic;
using MphRead.Entities;

namespace MphRead.Mods.Network;

public sealed partial class ServerNetwork
{
    public bool AdminRosterLocked { get; set; }
    public bool AdminTeamsAssigned { get; private set; }
    public void AssignAdminTeam(ServerPeer peer, byte team)
    {
        if (!Rules.Teams || Phase != MatchPhase.WaitingForPlayers || peer.IsObserver || team > 1)
            throw new InvalidOperationException("Teams can be assigned only before countdown in a team mode.");
        peer.TeamIndex = team; PlayerEntity.Players[peer.Slot].TeamIndex = team;
        AdminTeamsAssigned = true; _rosterDirty = true;
    }
    public void ClearAdminTeamAssignments() => AdminTeamsAssigned = false;
    private readonly HashSet<ulong> _adminMuted = new();
    internal bool IsAdminMuted(ulong connectionId) => _adminMuted.Contains(connectionId);
    public void SetAdminMuted(ulong connectionId, bool muted)
    {
        if (muted) _adminMuted.Add(connectionId); else _adminMuted.Remove(connectionId);
    }
}
