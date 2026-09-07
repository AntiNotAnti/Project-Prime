using System;

namespace MphRead.Mods.Network;
public enum VotePolicy { PublicRotation, PrivateRematch }
public sealed record ServerVoteOptions(VotePolicy Policy)
{
    public static ServerVoteOptions Parse(string? value, bool present, RulesetPreset preset)
    {
        value ??= Environment.GetEnvironmentVariable("PRIME_VOTE_POLICY");
        if (value == null && !present)
            return new(preset == RulesetPreset.Duel || Environment.GetEnvironmentVariable("PRIME_PRACTICE") == "1"
                ? VotePolicy.PrivateRematch : VotePolicy.PublicRotation);
        return value?.ToLowerInvariant() switch
        {
            "public" or "publicrotation" => new(VotePolicy.PublicRotation),
            "private" or "privaterematch" => new(VotePolicy.PrivateRematch),
            _ => throw new ArgumentException("-votepolicy / PRIME_VOTE_POLICY requires public or private.")
        };
    }
}
