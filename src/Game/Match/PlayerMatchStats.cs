using System;

namespace MphRead
{
    /// <summary>One slot's statistics. The temporary array adapter permits incremental
    /// migration of legacy callers without introducing a second source of truth.</summary>
    public sealed class PlayerMatchStats
    {
        private readonly MatchRuntime _match;
        public int Slot { get; }

        internal PlayerMatchStats(MatchRuntime match, int slot)
        {
            _match = match;
            Slot = slot;
        }

        public int Stars { get => _match.Stars[Slot]; set => _match.Stars[Slot] = value; }
        public int Standings { get => _match.Standings[Slot]; set => _match.Standings[Slot] = value; }
        public int Points { get => _match.Points[Slot]; set => _match.Points[Slot] = value; }
        public int Kills { get => _match.Kills[Slot]; set => _match.Kills[Slot] = value; }
        public int DamageDealt { get => _match.DamageDealt[Slot]; set => _match.DamageDealt[Slot] = value; }
        public int BipedKills { get => _match.BipedKills[Slot]; set => _match.BipedKills[Slot] = value; }
        public int AltFormKills { get => _match.AltFormKills[Slot]; set => _match.AltFormKills[Slot] = value; }
        public int LongestKillStreak { get => _match.LongestKillStreak[Slot]; set => _match.LongestKillStreak[Slot] = value; }
        public int Assists { get => _match.Assists[Slot]; set => _match.Assists[Slot] = value; }
        public int Deaths { get => _match.Deaths[Slot]; set => _match.Deaths[Slot] = value; }
        public int BeamDamageMax { get => _match.BeamDamageMax[Slot]; set => _match.BeamDamageMax[Slot] = value; }
        public int BeamDamageDealt { get => _match.BeamDamageDealt[Slot]; set => _match.BeamDamageDealt[Slot] = value; }
        public int DamageCount { get => _match.DamageCount[Slot]; set => _match.DamageCount[Slot] = value; }
        public int AltDamageCount { get => _match.AltDamageCount[Slot]; set => _match.AltDamageCount[Slot] = value; }
        public int KillStreak { get => _match.KillStreak[Slot]; set => _match.KillStreak[Slot] = value; }
        public int Suicides { get => _match.Suicides[Slot]; set => _match.Suicides[Slot] = value; }
        public int FriendlyKills { get => _match.FriendlyKills[Slot]; set => _match.FriendlyKills[Slot] = value; }
        public int HeadshotKills { get => _match.HeadshotKills[Slot]; set => _match.HeadshotKills[Slot] = value; }
        public int OctolithScores { get => _match.OctolithScores[Slot]; set => _match.OctolithScores[Slot] = value; }
        public int OctolithDrops { get => _match.OctolithDrops[Slot]; set => _match.OctolithDrops[Slot] = value; }
        public int OctolithStops { get => _match.OctolithStops[Slot]; set => _match.OctolithStops[Slot] = value; }
        public int NodesCaptured { get => _match.NodesCaptured[Slot]; set => _match.NodesCaptured[Slot] = value; }
        public int NodesLost { get => _match.NodesLost[Slot]; set => _match.NodesLost[Slot] = value; }
        public int KillsAsPrime { get => _match.KillsAsPrime[Slot]; set => _match.KillsAsPrime[Slot] = value; }
        public int PrimesKilled { get => _match.PrimesKilled[Slot]; set => _match.PrimesKilled[Slot] = value; }
        // Legacy seconds, including Survival's -1 (MAX) result sentinel.
        public float Time { get => _match.Time[Slot]; set => _match.Time[Slot] = value; }

        public int GetBeamKills(int weapon)
        {
            if ((uint)weapon >= 9) { throw new ArgumentOutOfRangeException(nameof(weapon)); }
            return _match.BeamKills[Slot, weapon];
        }

        public void SetBeamKills(int weapon, int kills)
        {
            if ((uint)weapon >= 9) { throw new ArgumentOutOfRangeException(nameof(weapon)); }
            _match.BeamKills[Slot, weapon] = kills;
        }
    }
}
