using System;
using MphRead.Mods.Network;

namespace MphRead.Replay;

internal static class ServerReplayPolicy
{
    internal static bool Validate(MatchRules rules, bool authenticated, bool reported, string? directory)
    {
        bool enabled = !string.IsNullOrWhiteSpace(directory);
        if (rules.RulesetPreset == RulesetPreset.Duel && (authenticated || reported) && !enabled)
            throw new ArgumentException("Authenticated or reported Duel requires PRIME_SERVER_REPLAY_DIRECTORY.");
        return enabled;
    }

    internal static bool MayStart(bool enabled, ServerReplaySession? recording)
    {
        if (recording?.Status is { State: "failed" } failure)
            throw new InvalidOperationException("Authoritative replay failed: " + failure.Error);
        return !enabled || recording?.Ready == true;
    }
}
