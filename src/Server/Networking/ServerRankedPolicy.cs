using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Ranked remains fail-closed on the public UDP transport until reconnect and
/// gameplay packets have proof of possession beyond a replayable bearer ticket.
/// </summary>
public static class ServerRankedPolicy
{
    public const string PublicDisabledReason =
        "Public Ranked is disabled until the transport proves ticket possession.";

    public static bool HasLockedConstraints(MatchRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.RankingEligibility != RankingEligibility.VerifiedServerOnly
            || rules.LateJoinPolicy == LateJoinPolicy.Disabled
                && rules.TeamBalancePolicy == TeamBalancePolicy.Locked;
    }

    public static bool PublicStartAllowed(MatchRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.RankingEligibility != RankingEligibility.VerifiedServerOnly;
    }

    public static void ValidatePublicStart(MatchRules rules)
    {
        if (!HasLockedConstraints(rules))
            throw new ArgumentException("Ranked requires disabled late join and locked server team assignment.", nameof(rules));
        if (!PublicStartAllowed(rules)) throw new InvalidOperationException(PublicDisabledReason);
    }
}
