using System;
namespace MphRead.Mods.Network;

/// <summary>Opt-in population target and human-like AI profile. Difficulty
/// never changes combat statistics or grants hidden information.</summary>
public sealed record BotFillPolicy(int MinimumParticipants = 0,
    BotDifficulty Difficulty = BotDifficulty.Normal)
{
    [Obsolete("Use Difficulty. This projection exposes only the retail 0/1/2 band.")]
    public int Skill => Difficulty.LegacyLevel();

    // Preserve the former 0/1/2 API without silently reinterpreting 1 as Easy
    // or 2 as Normal now that the product-facing enum has five values.
    public BotFillPolicy(int minimumParticipants, int legacySkill)
        : this(minimumParticipants, legacySkill switch
        {
            0 => BotDifficulty.Beginner,
            1 => BotDifficulty.Normal,
            2 => BotDifficulty.Hard,
            _ => (BotDifficulty)byte.MaxValue
        }) { }

    public void Validate(int capacity)
    {
        if (MinimumParticipants < 0 || MinimumParticipants > capacity)
            throw new ArgumentOutOfRangeException(nameof(MinimumParticipants));
        if (!Enum.IsDefined(Difficulty))
            throw new ArgumentOutOfRangeException(nameof(Difficulty));
    }
    public int DesiredBots(int humans, int capacity) => humans == 0 ? 0 : Math.Clamp(MinimumParticipants - humans, 0, capacity - humans);

    [Obsolete("Use the lobby-owned BotDifficulty setting.")]
    public static BotFillPolicy FromEnvironment()
    {
        string? target = Environment.GetEnvironmentVariable("PRIME_BOT_FILL");
        string? skill = Environment.GetEnvironmentVariable("PRIME_BOT_SKILL");
        return new(target == null ? 0 : int.Parse(target),
            skill == null ? 1 : int.Parse(skill));
    }
}
