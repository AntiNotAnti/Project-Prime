using System;
namespace MphRead.Mods.Network;

/// <summary>Opt-in population target. Skill selects existing AI behavior, never combat multipliers.</summary>
public sealed record BotFillPolicy(int MinimumParticipants = 0, int Skill = 1)
{
    public void Validate(int capacity)
    {
        if (MinimumParticipants < 0 || MinimumParticipants > capacity || Skill is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(MinimumParticipants));
    }
    public int DesiredBots(int humans, int capacity) => humans == 0 ? 0 : Math.Clamp(MinimumParticipants - humans, 0, capacity - humans);
    public static BotFillPolicy FromEnvironment()
    {
        string? target = Environment.GetEnvironmentVariable("PRIME_BOT_FILL");
        string? skill = Environment.GetEnvironmentVariable("PRIME_BOT_SKILL");
        return new(target == null ? 0 : int.Parse(target), skill == null ? 1 : int.Parse(skill));
    }
}
