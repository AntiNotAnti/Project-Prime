using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

public enum LobbyStartBlock
{
    None,
    LobbyDisabled,
    WrongPhase,
    NotHost,
    TooFewPlayers,
    NotReady,
    InvalidTeams,
    TournamentLocked,
    ExternalGate
}

public readonly record struct LobbyStartDecision(LobbyStartBlock Block, string Message)
{
    public bool Allowed => Block == LobbyStartBlock.None;
    public static LobbyStartDecision Allow => new(LobbyStartBlock.None, "");
}

public static class LobbyStartPolicy
{
    public static bool UsesLobbyBeforeFirstMatch(LobbyPolicyKind policy)
        => policy == LobbyPolicyKind.PersistentLobby;

    public static bool UsesLobbyAfterMatch(LobbyPolicyKind policy)
        => policy is LobbyPolicyKind.IntermissionLobby or LobbyPolicyKind.PersistentLobby;

    public static LobbyPolicy ForPrivate() => LobbyPolicy.PrivateHosted;

    public static LobbyPolicy ForPractice(bool autoStart = false)
        => autoStart
            ? new LobbyPolicy(LobbyPolicyKind.NoLobby, readyRequired: false, minimumPlayers: 1,
                hostMayForceStart: true)
            : LobbyPolicy.PrivateHosted;

    public static LobbyStartDecision Evaluate(LobbyRuntime runtime, ulong requesterConnectionId,
        bool force, bool tournamentStartAllowed, Func<MatchRules, bool>? externalGate = null)
    {
        if (runtime.Policy.Kind == LobbyPolicyKind.NoLobby)
            return new(LobbyStartBlock.LobbyDisabled, "This server starts matches automatically.");
        if (runtime.Phase != LobbyPhase.Open)
            return new(LobbyStartBlock.WrongPhase, "The lobby is already starting or locked.");
        if ((runtime.PermissionsFor(requesterConnectionId) & LobbyPermissions.StartMatch) == 0)
            return new(LobbyStartBlock.NotHost, "Only the lobby host or an administrator can start.");
        if (runtime.ConnectedPlayerCount < runtime.Policy.MinimumPlayers)
            return new(LobbyStartBlock.TooFewPlayers, "The lobby does not have enough players.");
        if (runtime.Policy.ReadyRequired && !runtime.AllPlayersReady
            && !(force && runtime.Policy.HostMayForceStart))
            return new(LobbyStartBlock.NotReady, "Every human player must be ready.");
        if (!TeamsAreValid(runtime.Draft.Current, runtime.Players))
            return new(LobbyStartBlock.InvalidTeams, "Both teams need a player before the match can start.");
        if (!tournamentStartAllowed)
            return new(LobbyStartBlock.TournamentLocked, "Tournament control has not released this round.");
        if (externalGate != null && !externalGate(runtime.Draft.Current))
            return new(LobbyStartBlock.ExternalGate, "A required server service is not ready.");
        return LobbyStartDecision.Allow;
    }

    /// <summary>
    /// Trusted tournament control has its own authenticated ready check, so it
    /// bypasses player-host permission and lobby ready flags while retaining the
    /// authoritative capacity, team, and service gates.
    /// </summary>
    public static LobbyStartDecision EvaluateServer(LobbyRuntime runtime,
        Func<MatchRules, bool>? externalGate = null)
    {
        if (runtime.Policy.Kind == LobbyPolicyKind.NoLobby)
            return new(LobbyStartBlock.LobbyDisabled, "This server starts matches automatically.");
        if (runtime.Phase != LobbyPhase.Open)
            return new(LobbyStartBlock.WrongPhase, "The lobby is already starting or locked.");
        if (runtime.ConnectedPlayerCount < runtime.Policy.MinimumPlayers)
            return new(LobbyStartBlock.TooFewPlayers, "The lobby does not have enough players.");
        if (!TeamsAreValid(runtime.Draft.Current, runtime.Players))
            return new(LobbyStartBlock.InvalidTeams, "Both teams need a player before the match can start.");
        if (externalGate != null && !externalGate(runtime.Draft.Current))
            return new(LobbyStartBlock.ExternalGate, "A required server service is not ready.");
        return LobbyStartDecision.Allow;
    }

    private static bool TeamsAreValid(MatchRules rules, IReadOnlyList<LobbyPlayer> players)
    {
        if (!rules.Teams) return true;
        bool orange = false, green = false;
        foreach (LobbyPlayer player in players)
        {
            if (player.DisconnectedGrace) continue;
            if (player.Team == 0) orange = true;
            else if (player.Team == 1) green = true;
        }
        return orange && green;
    }
}
