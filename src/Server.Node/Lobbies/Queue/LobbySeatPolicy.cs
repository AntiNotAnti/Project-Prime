using ProjectPrime.Server.Shared;
using MphRead;

namespace ProjectPrime.Server.Node.Lobbies.Queue;

/// <summary>Centralized queue policy resolution. In-match ImmediateSeat is
/// deliberately downgraded to a next-boundary policy because MatchSpec is
/// immutable and cannot accept a late roster mutation.</summary>
internal static class LobbySeatPolicyResolver
{
    public static LobbySeatPolicy Effective(LobbyPhase phase, MatchMode mode, LobbySeatPolicy configured)
    {
        if (phase == LobbyPhase.Open) return configured;
        if (phase == LobbyPhase.PostMatch)
            return configured == LobbySeatPolicy.ObserverUntilNextMatch
                ? LobbySeatPolicy.ObserverUntilNextMatch : LobbySeatPolicy.NextMatchSeat;
        if (mode is MatchMode.Survival or MatchMode.TeamSurvival)
            return LobbySeatPolicy.NextMatchSeat;
        return configured == LobbySeatPolicy.ImmediateSeat
            ? LobbySeatPolicy.NextMatchSeat : configured;
    }

    public static bool CanOffer(LobbyPhase phase, LobbySeatPolicy policy)
        => phase == LobbyPhase.Open || phase == LobbyPhase.PostMatch && policy != LobbySeatPolicy.ObserverUntilNextMatch;
}
