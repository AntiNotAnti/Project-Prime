using System;

namespace MphRead.Mods.Network;

/// <summary>Count balancing with stable slot ordering. No rating or party inference.</summary>
public static class TeamAllocator
{
    public const byte Unassigned = byte.MaxValue;

    public static byte Select(ReadOnlySpan<byte> teams, byte tieBreak = 0,
        bool favorTrailingScore = false, int orangeScore = 0, int greenScore = 0)
    {
        Count(teams, out int orange, out int green);
        if (orange != green) return orange < green ? (byte)0 : (byte)1;
        if (favorTrailingScore && orangeScore != greenScore)
            return orangeScore < greenScore ? (byte)0 : (byte)1;
        if (tieBreak > 1) throw new ArgumentOutOfRangeException(nameof(tieBreak));
        return tieBreak;
    }

    /// <summary>Moves the minimum number of players, highest slot first. Call only before play.</summary>
    public static int Rebalance(Span<byte> teams)
    {
        Count(teams, out int orange, out int green);
        int changes = 0;
        for (int slot = teams.Length - 1; slot >= 0 && Math.Abs(orange - green) > 1; slot--)
        {
            byte larger = orange > green ? (byte)0 : (byte)1;
            if (teams[slot] != larger) continue;
            teams[slot] = (byte)(1 - larger);
            orange += larger == 0 ? -1 : 1;
            green += larger == 1 ? -1 : 1;
            changes++;
        }
        return changes;
    }

    private static void Count(ReadOnlySpan<byte> teams, out int orange, out int green)
    {
        if (teams.Length > 8) throw new ArgumentOutOfRangeException(nameof(teams));
        orange = green = 0;
        foreach (byte team in teams)
        {
            if (team == 0) orange++;
            else if (team == 1) green++;
            else if (team != Unassigned) throw new ArgumentOutOfRangeException(nameof(teams));
        }
    }
}
