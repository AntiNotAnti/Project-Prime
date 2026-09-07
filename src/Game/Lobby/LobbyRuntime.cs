using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MphRead;

/// <summary>
/// Server-owned lobby truth. This type contains no gameplay entities, score, health,
/// damage, hit detection, or simulation clock.
/// </summary>
public sealed class LobbyRuntime
{
    public const byte MaximumPlayers = 8;
    public const byte MaximumObservers = 16;
    public const int MaximumDisplayNameLength = 16;

    private readonly List<LobbyPlayer> _players = new(MaximumPlayers);
    private readonly List<LobbyPlayer> _observers = new(MaximumObservers);
    private readonly ReadOnlyCollection<LobbyPlayer> _playerView;
    private readonly ReadOnlyCollection<LobbyPlayer> _observerView;

    public uint SessionId { get; }
    public uint Revision { get; private set; }
    public LobbyPhase Phase { get; private set; } = LobbyPhase.Open;
    public LobbyPolicy Policy { get; }
    public LobbyRulesDraft Draft { get; }
    public IReadOnlyList<LobbyPlayer> Players => _playerView;
    public IReadOnlyList<LobbyPlayer> Observers => _observerView;
    public ulong HostConnectionId { get; private set; }
    public uint LastCompletedMatchId { get; private set; }
    public uint ActiveVoteRevision { get; private set; }

    public LobbyRuntime(uint sessionId, LobbyPolicy policy, MatchRules initialRules,
        uint initialRevision = 1)
    {
        if (sessionId == 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
        if (initialRevision == 0) throw new ArgumentOutOfRangeException(nameof(initialRevision));
        SessionId = sessionId;
        Revision = initialRevision;
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Draft = new LobbyRulesDraft(initialRules);
        _playerView = _players.AsReadOnly();
        _observerView = _observers.AsReadOnly();
    }

    public LobbyPermissions PermissionsFor(ulong connectionId)
    {
        if (connectionId == 0 || !TryFind(connectionId, out LobbyPlayer player))
            return LobbyPermissions.None;
        if (player.DisconnectedGrace) return LobbyPermissions.None;
        LobbyPermissions permissions = player.Observer ? LobbyPermissions.None : LobbyPermissions.Player;
        if (player.Host) permissions |= LobbyPermissions.Host;
        if (player.Admin) permissions |= LobbyPermissions.Admin;
        if (Phase != LobbyPhase.Open)
            permissions &= LobbyPermissions.ReturnToLobby | LobbyPermissions.Rematch | LobbyPermissions.Moderate;
        return permissions;
    }

    public bool TryAdd(LobbyPlayer player)
    {
        if (Phase != LobbyPhase.Open || !Valid(player) || TryFind(player.ConnectionId, out _)) return false;
        List<LobbyPlayer> target = player.Observer ? _observers : _players;
        int capacity = player.Observer ? MaximumObservers : MaximumPlayers;
        if (target.Count >= capacity || player.Host && HostConnectionId != 0) return false;
        target.Add(Normalize(player));
        if (player.Host) HostConnectionId = player.ConnectionId;
        Changed();
        return true;
    }

    public bool TryRemove(ulong connectionId)
    {
        if (!TryLocate(connectionId, out List<LobbyPlayer> members, out int index)) return false;
        bool wasHost = members[index].Host;
        members.RemoveAt(index);
        if (wasHost) MigrateHost();
        Changed();
        return true;
    }

    public bool TryUpdate(LobbyPlayer player)
    {
        if (Phase != LobbyPhase.Open || !Valid(player)
            || !TryLocate(player.ConnectionId, out List<LobbyPlayer> members, out int index)
            || members[index].Observer != player.Observer) return false;
        LobbyPlayer current = members[index];
        player = Normalize(player);
        if (current == player) return false;
        if (player.Host && !current.Host && HostConnectionId != 0) return false;
        members[index] = player;
        if (current.Host && !player.Host) MigrateHost(player.ConnectionId);
        else if (player.Host) HostConnectionId = player.ConnectionId;
        Changed();
        return true;
    }

    /// <summary>
    /// Updates connection lifecycle state in any lobby phase without reopening
    /// the player-edit surface. Entering grace revokes ready and host authority.
    /// </summary>
    public bool TrySetConnectionStatus(ulong connectionId, bool loading, bool disconnectedGrace)
    {
        if (!TryLocate(connectionId, out List<LobbyPlayer> members, out int index)) return false;
        LobbyPlayer current = members[index];
        if (current.Bot && (loading || disconnectedGrace)
            || current.Observer && disconnectedGrace
            || loading && disconnectedGrace) return false;
        LobbyPlayer updated = current with
        {
            Loading = loading,
            DisconnectedGrace = disconnectedGrace,
            Ready = disconnectedGrace ? false : current.Ready,
            Host = disconnectedGrace ? false : current.Host
        };
        if (!Valid(updated) || updated == current) return false;
        members[index] = updated;
        if (current.Host && !updated.Host) MigrateHost(connectionId);
        Changed();
        return true;
    }

    public bool TrySetRules(MatchRules rules)
    {
        if (Phase != LobbyPhase.Open || !Draft.Replace(rules)) return false;
        Changed();
        return true;
    }

    public bool TrySetSelection(LobbySelection selection)
    {
        if (Phase != LobbyPhase.Open || !Draft.Select(selection)) return false;
        Changed();
        return true;
    }

    /// <summary>Advances the shared revision for server-owned lobby policy state.</summary>
    public bool TryMarkPolicyChanged()
    {
        if (Phase != LobbyPhase.Open) return false;
        Changed();
        return true;
    }

    public bool CanStart(ulong requesterConnectionId, bool force = false)
    {
        if (Phase != LobbyPhase.Open || ConnectedPlayerCount < Policy.MinimumPlayers) return false;
        LobbyPermissions permissions = PermissionsFor(requesterConnectionId);
        if ((permissions & LobbyPermissions.StartMatch) == 0) return false;
        if (!Policy.ReadyRequired || AllPlayersReady) return true;
        return force && Policy.HostMayForceStart;
    }

    public bool AllPlayersReady
    {
        get
        {
            if (ConnectedPlayerCount < Policy.MinimumPlayers) return false;
            foreach (LobbyPlayer player in _players)
                if (!player.DisconnectedGrace && !player.Bot && !player.Ready) return false;
            return true;
        }
    }

    public int ConnectedPlayerCount
    {
        get
        {
            int count = 0;
            foreach (LobbyPlayer player in _players)
                if (!player.DisconnectedGrace) count++;
            return count;
        }
    }

    public bool TryBeginStarting(ulong requesterConnectionId, bool force = false)
    {
        if (!CanStart(requesterConnectionId, force)) return false;
        Phase = LobbyPhase.Starting;
        Changed();
        return true;
    }

    /// <summary>Trusted server-owner transition after its own ready check.</summary>
    public bool TryBeginStartingByServer()
    {
        if (Phase != LobbyPhase.Open) return false;
        Phase = LobbyPhase.Starting;
        Changed();
        return true;
    }

    public bool TryLock()
    {
        if (Phase != LobbyPhase.Starting) return false;
        _ = Draft.Freeze();
        Phase = LobbyPhase.Locked;
        Changed();
        return true;
    }

    public bool TryReopen()
    {
        if (Phase == LobbyPhase.Open) return false;
        Phase = LobbyPhase.Open;
        Changed();
        return true;
    }

    public bool TrySetCompletedMatch(uint matchId)
    {
        if (matchId == 0 || matchId == LastCompletedMatchId) return false;
        LastCompletedMatchId = matchId;
        Changed();
        return true;
    }

    public bool TrySetActiveVote(uint voteRevision)
    {
        if (ActiveVoteRevision == voteRevision) return false;
        ActiveVoteRevision = voteRevision;
        Changed();
        return true;
    }

    public bool TryFind(ulong connectionId, out LobbyPlayer player)
    {
        if (TryLocate(connectionId, out List<LobbyPlayer> members, out int index))
        {
            player = members[index];
            return true;
        }
        player = default;
        return false;
    }

    private bool TryLocate(ulong connectionId, out List<LobbyPlayer> members, out int index)
    {
        members = _players;
        index = -1;
        if (connectionId == 0) return false;
        index = _players.FindIndex(value => value.ConnectionId == connectionId);
        if (index >= 0) { members = _players; return true; }
        index = _observers.FindIndex(value => value.ConnectionId == connectionId);
        if (index >= 0) { members = _observers; return true; }
        return false;
    }

    private static LobbyPlayer Normalize(LobbyPlayer player)
        => player.Bot && !player.Observer && !player.Ready ? player with { Ready = true } : player;

    private void MigrateHost(ulong excludedConnectionId = 0)
    {
        HostConnectionId = 0;
        // Preserve join order within each identity class, while preferring an
        // authenticated human over a guest. Bots and observers never own a lobby.
        if (TryPromoteHost(authenticated: true, excludedConnectionId)) return;
        _ = TryPromoteHost(authenticated: false, excludedConnectionId);
    }

    private bool TryPromoteHost(bool authenticated, ulong excludedConnectionId)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i].Bot || _players[i].DisconnectedGrace
                || _players[i].ConnectionId == excludedConnectionId
                || _players[i].Authenticated != authenticated) continue;
            _players[i] = _players[i] with { Host = true };
            HostConnectionId = _players[i].ConnectionId;
            return true;
        }
        return false;
    }

    private bool Valid(LobbyPlayer player)
    {
        if (player.ConnectionId == 0 || String.IsNullOrWhiteSpace(player.DisplayName)
            || player.DisplayName.Length > MaximumDisplayNameLength
            || player.Hunter > Hunter.Guardian || player.Bot && player.Observer
            || player.PlayerId is { IsEmpty: true } || player.Bot && player.PlayerId.HasValue
            || player.Bot && (player.Host || player.Admin || player.Loading || player.DisconnectedGrace)
            || player.Observer && (player.Ready || player.Host || player.Team != byte.MaxValue)
            || player.Observer && player.DisconnectedGrace
            || player.DisconnectedGrace && (player.Ready || player.Loading || player.Host)
            || !player.Observer && player.Team >= MaximumPlayers) return false;
        foreach (char character in player.DisplayName)
            if (character is < ' ' or > '~') return false;
        return true;
    }

    private void Changed()
    {
        uint revision = unchecked(Revision + 1);
        Revision = revision == 0 ? 1 : revision;
    }
}
