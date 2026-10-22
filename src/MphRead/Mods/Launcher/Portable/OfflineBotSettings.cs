using System;
using System.Collections.Immutable;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher;

/// <summary>Local setup choices; Random is resolved separately for each bot at launch.</summary>
public readonly record struct OfflineBotSettings(Hunter Hunter, int Suit, int Level,
    int Team = -1, byte Handicap = 0)
{
    public bool Valid(bool lowTier) => (Hunter == Hunter.Random || HunterRules.Allowed(Hunter, lowTier))
        && Suit is >= 0 and <= 3 && Level is >= 0 and <= 3
        && Team is >= -1 and <= 1 && PlayerHandicap.IsValid(Handicap);

    public static bool Validate(ImmutableArray<OfflineBotSettings> roster, int bots, bool lowTier)
    {
        if (roster.IsDefaultOrEmpty) return true; // Legacy callers use the shared difficulty.
        if (bots is < 0 or > 7 || roster.Length < bots || roster.Length > 7) return false;
        for (int i = 0; i < bots; i++)
            if (!roster[i].Valid(lowTier)) return false;
        return true;
    }

    public static string Difficulty(int level) => level switch
    {
        0 => "EASY", 1 => "NORMAL", 2 => "HARD", 3 => "INSANE", _ => "UNKNOWN"
    };
}
