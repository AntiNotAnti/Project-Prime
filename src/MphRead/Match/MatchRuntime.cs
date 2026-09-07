using System;
using MphRead.Entities;

namespace MphRead
{
    /// <summary>Mutable state owned by one scene. Per-slot views and internal
    /// aggregation storage share the same counters.</summary>
    public sealed class MatchRuntime
    {
        // Assigned from the existing server/protocol identity at the session boundary.
        public uint MatchId { get; set; }
        public MatchRules Rules { get; private set; }
        public MatchPhase Phase { get; set; } = MatchPhase.Playing;
        public uint PhaseStartTick { get; set; }
        public uint PhaseEndTick { get; set; }
        public bool HasPhaseDeadline { get; set; }
        public uint PhaseRevision { get; set; } = 1;
        internal bool UsesServerLifecycle { get; set; }
        public float MatchTime { get; set; }
        public int ActivePlayers { get; set; }
        public int PrimeHunter { get; set; } = -1;
        public bool ForceEndGame { get; set; }
        // Survival dynamically reveals the remaining players; this is effective state,
        // while Rules.PlayerRadar retains the configured setting.
        public bool RadarPlayers { get; set; }
        public System.Collections.Generic.IReadOnlyList<PlayerMatchStats> Players { get; }
        internal int[] Stars { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] Standings { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] TeamStandings { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] ResultSlots { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] Points { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] TeamPoints { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] Kills { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] TeamKills { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] Deaths { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] TeamDeaths { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] BeamDamageMax { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] BeamDamageDealt { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] DamageCount { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] AltDamageCount { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] KillStreak { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] Suicides { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] FriendlyKills { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] HeadshotKills { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] OctolithScores { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] OctolithDrops { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] OctolithStops { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] NodesCaptured { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] NodesLost { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] KillsAsPrime { get; } = new int[PlayerEntity.SlotCapacity];
        internal int[] PrimesKilled { get; } = new int[PlayerEntity.SlotCapacity];
        internal float[] Time { get; } = new float[PlayerEntity.SlotCapacity];
        internal float[] TeamTime { get; } = new float[PlayerEntity.SlotCapacity];
        internal int[,] BeamKills { get; } = new int[PlayerEntity.SlotCapacity, 9];

        private readonly Scene? _scene;
        private readonly MatchLogic? _logic;
        private readonly MatchFlow? _flow;
        public MatchLogic Logic => _logic ?? throw new InvalidOperationException("Match logic requires an owning scene.");
        public MatchFlow Flow => _flow ?? throw new InvalidOperationException("Match flow requires an owning scene.");
        public MatchResult? Result { get; private set; }
        internal MatchEndReason? PendingEndReason { get; set; }

        // Format-facing state adapter; connection loss belongs to the session.
        public MatchState LegacyState
        {
            get => Phase switch
            {
                MatchPhase.WaitingForPlayers or MatchPhase.Countdown or MatchPhase.Playing => MatchState.InProgress,
                MatchPhase.Ending => MatchState.GameOver,
                MatchPhase.Intermission => MatchState.Ending,
                _ => throw new InvalidOperationException("Unknown match phase.")
            };
            set => Phase = value switch
            {
                MatchState.InProgress => MatchPhase.Playing,
                MatchState.GameOver => MatchPhase.Ending,
                MatchState.Ending => MatchPhase.Intermission,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Connection state is not match state.")
            };
        }

        public MatchRuntime(MatchRules rules, Scene? scene = null)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _scene = scene;
            if (scene != null)
            {
                _logic = new MatchLogic(scene, this);
                _flow = new MatchFlow(scene, this);
            }
            MatchTime = rules.TimeLimit.HasValue ? (float)rules.TimeLimit.Value.TotalSeconds : -1;
            RadarPlayers = rules.PlayerRadar;
            var players = new PlayerMatchStats[PlayerEntity.SlotCapacity];
            for (int slot = 0; slot < players.Length; slot++)
            {
                players[slot] = new PlayerMatchStats(this, slot);
            }
            Players = Array.AsReadOnly(players);
        }

        internal void CaptureResult(float completedAtSimulationTime)
        {
            if (_scene?.IsHeadless != true)
            {
                throw new InvalidOperationException("Only an authoritative scene may capture a match result.");
            }
            Result ??= new MatchResult(this, completedAtSimulationTime,
                ForceEndGame ? MatchEndReason.Forced : PendingEndReason ?? MatchEndReason.TimeLimit);
        }

        internal void ResetCompetitiveState()
        {
            Array.Clear(Stars);
            Array.Clear(Standings);
            Array.Clear(TeamStandings);
            Array.Clear(ResultSlots);
            Array.Clear(Points);
            Array.Clear(TeamPoints);
            Array.Clear(Kills);
            Array.Clear(TeamKills);
            Array.Clear(Deaths);
            Array.Clear(TeamDeaths);
            Array.Clear(Time);
            Array.Clear(TeamTime);
            Array.Clear(BeamDamageMax);
            Array.Clear(BeamDamageDealt);
            Array.Clear(DamageCount);
            Array.Clear(AltDamageCount);
            Array.Clear(KillStreak);
            Array.Clear(Suicides);
            Array.Clear(FriendlyKills);
            Array.Clear(HeadshotKills);
            Array.Clear(BeamKills);
            Array.Clear(OctolithScores);
            Array.Clear(OctolithDrops);
            Array.Clear(OctolithStops);
            Array.Clear(NodesCaptured);
            Array.Clear(NodesLost);
            Array.Clear(KillsAsPrime);
            Array.Clear(PrimesKilled);
            ActivePlayers = 0;
            PrimeHunter = -1;
            ForceEndGame = false;
            RadarPlayers = Rules.PlayerRadar;
            ResetResult();
        }

        internal void ResetResult()
        {
            Result = null;
            PendingEndReason = null;
        }

        /// <summary>Replace a validated configuration at a setup or replication boundary.
        /// Does not reset timers or accumulated scores.</summary>
        public void ApplyRules(MatchRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }
    }
}
