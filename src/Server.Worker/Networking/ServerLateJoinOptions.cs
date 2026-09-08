using System;

namespace MphRead.Mods.Network;

public static class ServerLateJoinOptions
{
    public static LateJoinPolicy? Parse(string? value, bool present = false) => value?.ToLowerInvariant() switch
    {
        null when !present => null,
        "immediate" => LateJoinPolicy.JoinImmediately,
        "next" => LateJoinPolicy.SpectateUntilNextMatch,
        "disabled" => LateJoinPolicy.Disabled,
        _ => throw new ArgumentException("-latejoin requires immediate, next or disabled.")
    };
}
