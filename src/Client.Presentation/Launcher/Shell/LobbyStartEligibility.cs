using System.Collections.Generic;
using System.Linq;
using ProjectPrime.Server.Shared;
using MphRead;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>Client-side explanation of the server's explicit lobby start gates.</summary>
internal readonly record struct LobbyStartEligibility(bool CanStart, string Message)
{
    public static LobbyStartEligibility Evaluate(LobbySnapshot snapshot)
    {
        if (snapshot.Phase != LobbyPhase.Open)
            return new(false, "This lobby is no longer open.");

        if (string.IsNullOrWhiteSpace(snapshot.MapKey))
            return new(false, "Choose a map first; changing settings resets readiness, then all players must be Ready.");

        LobbyMember[] players = snapshot.Members.Where(member => !member.Observer).ToArray();
        if (players.Length == 0)
            return new(false, "Add a player first; then all players must be Ready.");

        if (players.Any(member => !member.Ready))
            return new(false, "All players must be Ready. Changing settings resets readiness.");

        int teamCount = snapshot.Mode.IsTeamMode()
            ? snapshot.Rules?.TeamCount ?? 2 : 1;
        if (snapshot.Mode.IsTeamMode()
            && RepresentedTeams(snapshot, players) < teamCount)
            return new(false, "Team mode needs players on every configured team.");

        return new(true, "");
    }

    internal static int RepresentedTeams(LobbySnapshot snapshot, IReadOnlyList<LobbyMember>? players = null)
    {
        LobbyMember[] humanPlayers = players?.ToArray()
            ?? snapshot.Members.Where(member => !member.Observer).ToArray();
        HashSet<byte> teams = humanPlayers.Select(member => member.Team).ToHashSet();
        int teamCount = snapshot.Mode.IsTeamMode()
            ? snapshot.Rules?.TeamCount ?? 2 : 1;
        for (int bot = 0; bot < snapshot.BotCount; bot++)
            teams.Add((byte)((humanPlayers.Length + bot) % teamCount));
        return teams.Count;
    }
}
