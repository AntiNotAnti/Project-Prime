using System;
using MphRead.Entities;

namespace MphRead
{
    /// <summary>Mutable state owned by one scene. Compatibility arrays are also the backing
    /// storage for Players; no statistics are copied between these interfaces.</summary>
    public sealed class MatchRuntime
    {
        // Assigned from the existing server/protocol identity at the session boundary.
        public uint MatchId { get; set; }
        public MatchRules Rules { get; private set; }
        public MatchPhase Phase { get; set; } = MatchPhase.Playing;
        public float MatchTime { get; set; }
        public int ActivePlayers { get; set; }
        public int PrimeHunter { get; set; } = -1;
        public bool ForceEndGame { get; set; }
        // Survival dynamically reveals the remaining players; this is effective state,
        // while Rules.PlayerRadar retains the configured setting.
        public bool RadarPlayers { get; set; }
        public bool TempoChanged { get; set; }
        public bool StateChanged { get; set; }
        public float MatchEndTime { get; set; }
        public float LastAlarmTime { get; set; }
        public int NextAlarmIndex { get; set; }
        public System.Collections.Generic.IReadOnlyList<PlayerMatchStats> Players { get; }
        public int[] Stars { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] Standings { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] TeamStandings { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] ResultSlots { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] Points { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] TeamPoints { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] Kills { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] TeamKills { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] Deaths { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] TeamDeaths { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] BeamDamageMax { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] BeamDamageDealt { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] DamageCount { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] AltDamageCount { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] KillStreak { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] Suicides { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] FriendlyKills { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] HeadshotKills { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] OctolithScores { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] OctolithDrops { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] OctolithStops { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] NodesCaptured { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] NodesLost { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] KillsAsPrime { get; } = new int[PlayerEntity.SlotCapacity];
        public int[] PrimesKilled { get; } = new int[PlayerEntity.SlotCapacity];
        public float[] Time { get; } = new float[PlayerEntity.SlotCapacity];
        public float[] TeamTime { get; } = new float[PlayerEntity.SlotCapacity];
        public int[,] BeamKills { get; } = new int[PlayerEntity.SlotCapacity, 9];

        public MatchRuntime(MatchRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            MatchTime = rules.TimeLimit.HasValue ? (float)rules.TimeLimit.Value.TotalSeconds : -1;
            RadarPlayers = rules.PlayerRadar;
            var players = new PlayerMatchStats[PlayerEntity.SlotCapacity];
            for (int slot = 0; slot < players.Length; slot++)
            {
                players[slot] = new PlayerMatchStats(this, slot);
            }
            Players = Array.AsReadOnly(players);
        }

        /// <summary>Replace a validated configuration at a setup or replication boundary.
        /// Does not reset timers or accumulated scores.</summary>
        public void ApplyRules(MatchRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }
    }
}
