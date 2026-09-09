using System.Collections.Generic;
using System.Linq;
using FruityPrime.Server.Shared;
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

        if (snapshot.Mode.IsTeamMode() && RepresentedTeams(snapshot, players) < 2)
            return new(false, "Team mode needs players on both teams.");

        return new(true, "");
    }

    internal static int RepresentedTeams(LobbySnapshot snapshot, IReadOnlyList<LobbyMember>? players = null)
    {
        LobbyMember[] humanPlayers = players?.ToArray()
            ?? snapshot.Members.Where(member => !member.Observer).ToArray();
        HashSet<byte> teams = humanPlayers.Select(member => member.Team).ToHashSet();
        for (int bot = 0; bot < snapshot.BotCount; bot++)
            teams.Add((byte)((humanPlayers.Length + bot) % 2));
        return teams.Count;
    }
}
