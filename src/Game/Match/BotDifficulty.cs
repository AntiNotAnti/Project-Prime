using System;

namespace MphRead;

/// <summary>
/// Host-selected bot challenge. Values are stable control-plane data; gameplay
/// maps them onto the retail AI's three behavior bands plus deterministic
/// human-like timing and accuracy profiles.
/// </summary>
public enum BotDifficulty : byte
{
    Beginner = 0,
    Easy = 1,
    Normal = 2,
    Hard = 3,
    Expert = 4
}

public static class BotDifficultyRules
{
    public static int LegacyLevel(this BotDifficulty difficulty)
        => difficulty switch
        {
            BotDifficulty.Beginner or BotDifficulty.Easy => 0,
            BotDifficulty.Normal => 1,
            BotDifficulty.Hard or BotDifficulty.Expert => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
        };
}
