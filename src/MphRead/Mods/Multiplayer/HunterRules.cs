using System;
using System.Collections.Generic;
using MphRead.Mods.Network;

namespace MphRead.Mods.Multiplayer
{
    public static class HunterRules
    {
        private static readonly Hunter[] Low = { Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Weavel };
        private static readonly Hunter[] All = { Hunter.Samus, Hunter.Kanden, Hunter.Trace, Hunter.Sylux, Hunter.Noxus, Hunter.Spire, Hunter.Weavel };
        public static IReadOnlyList<Hunter> Pool(bool lowTier) => Array.AsReadOnly(lowTier ? Low : All);
        public static bool Allowed(Hunter hunter, MatchDefinition match) => Allowed(hunter, match.LowTier);
        public static bool Allowed(Hunter hunter, bool lowTier) => lowTier
            ? hunter is Hunter.Kanden or Hunter.Spire or Hunter.Noxus or Hunter.Weavel
            : hunter >= Hunter.Samus && hunter <= Hunter.Weavel;
        public static Hunter Sanitize(Hunter hunter, bool lowTier) => Allowed(hunter, lowTier) ? hunter : Hunter.Kanden;
        public static Hunter RandomAllowed(uint random, bool lowTier)
        {
            var pool = lowTier ? Low : All;
            return pool[random % (uint)pool.Length];
        }
        public static Hunter Resolve(Hunter hunter, bool lowTier) => hunter == Hunter.Random
            ? RandomAllowed((uint)Random.Shared.Next(), lowTier) : Sanitize(hunter, lowTier);
    }
}
