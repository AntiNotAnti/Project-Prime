using System;

namespace MphRead.Mods.Network;

/// <summary>Count balancing with stable slot ordering. No rating or party inference.</summary>
public static class TeamAllocator
{
    public const byte Unassigned = byte.MaxValue;

    public static byte Select(ReadOnlySpan<byte> teams, byte tieBreak = 0,
        bool favorTrailingScore = false, int orangeScore = 0, int greenScore = 0,
        int teamCount = 2)
    {
        Span<int> counts = stackalloc int[MatchRules.MaximumTeamCount];
        Count(teams, teamCount, counts);
        int minimum = counts[0];
        for (int team = 1; team < teamCount; team++) minimum = Math.Min(minimum, counts[team]);
        if (teamCount == 2 && counts[0] == counts[1]
            && favorTrailingScore && orangeScore != greenScore)
            return orangeScore < greenScore ? (byte)0 : (byte)1;
        if (tieBreak >= teamCount) throw new ArgumentOutOfRangeException(nameof(tieBreak));
        if (counts[tieBreak] == minimum) return tieBreak;
        for (byte team = 0; team < teamCount; team++)
            if (counts[team] == minimum) return team;
        return 0;
    }

    /// <summary>Moves the minimum number of players, highest slot first. Call only before play.</summary>
    public static int Rebalance(Span<byte> teams, int teamCount = 2)
    {
        Span<int> counts = stackalloc int[MatchRules.MaximumTeamCount];
        Count(teams, teamCount, counts);
        int changes = 0;
        for (int slot = teams.Length - 1; slot >= 0; slot--)
        {
            int largest = 0;
            int smallest = 0;
            for (int team = 1; team < teamCount; team++)
            {
                if (counts[team] > counts[largest]) largest = team;
                if (counts[team] < counts[smallest]) smallest = team;
            }
            if (counts[largest] - counts[smallest] <= 1) break;
            if (teams[slot] != largest) continue;
            teams[slot] = (byte)smallest;
            counts[largest]--;
            counts[smallest]++;
            changes++;
        }
        return changes;
    }

    private static void Count(ReadOnlySpan<byte> teams, int teamCount,
        Span<int> counts)
    {
        if (teams.Length > 8) throw new ArgumentOutOfRangeException(nameof(teams));
        if (teamCount is < 2 or > MatchRules.MaximumTeamCount)
            throw new ArgumentOutOfRangeException(nameof(teamCount));
        counts.Clear();
        foreach (byte team in teams)
        {
            if (team < teamCount) counts[team]++;
            else if (team != Unassigned) throw new ArgumentOutOfRangeException(nameof(teams));
        }
    }
}
