using System;

namespace MphRead.Mods.Network
{
    /// <summary>Strict parsing for the two opt-in spawn settings exposed by the dedicated server.</summary>
    public static class ServerSpawnOptions
    {
        public static SpawnPolicy ParsePolicy(string? value, bool present = false) => value?.ToLowerInvariant() switch
        {
            null when !present => SpawnPolicy.Classic,
            "classic" => SpawnPolicy.Classic,
            "enhanced" => SpawnPolicy.Enhanced,
            "duel" => SpawnPolicy.Duel,
            _ => throw new ArgumentException("-spawnpolicy requires classic, enhanced, or duel.")
        };

        public static bool ParseCancellation(string? value, bool present)
        {
            if (!present) { return false; }
            if (value != null && Boolean.TryParse(value, out bool enabled)) { return enabled; }
            throw new ArgumentException("-cancelspawnprotection requires true or false.");
        }
    }
}
