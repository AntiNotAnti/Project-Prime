using System;
using MphRead.Entities;

namespace MphRead
{
    /// <summary>Validated immutable multiplayer configuration. Runtime counters and
    /// the effective Survival radar state belong to MatchRuntime.</summary>
    public sealed record MatchRules
    {
        public MatchMode Mode { get; }
        public string RoomKey { get; }
        public int MaxPlayers { get; }
        public TimeSpan? TimeLimit { get; }
        public int ScoreGoal { get; }
        public TimeSpan? ObjectiveTimeGoal { get; }
        /// <summary>Extra lives after the initial spawn, matching legacy PointGoal in Survival.</summary>
        public int StartingLives { get; }
        public bool FriendlyFire { get; }
        public bool AffinityWeapons { get; }
        public bool PlayerRadar { get; }
        public bool OctolithReset { get; }
        public int DamageLevel { get; }
        public SpawnPolicy SpawnPolicy { get; }
        public bool CancelSpawnProtectionOnOffensiveAction { get; }
        public bool Teams => Mode.IsTeamMode();
        public bool IsOctolithMode => Mode is MatchMode.Capture or MatchMode.Bounty or MatchMode.TeamBounty;
        public bool IsSurvival => Mode is MatchMode.Survival or MatchMode.TeamSurvival;
        public bool IsObjectiveTimeMode => Mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter;
        public int LegacyPointGoal => IsSurvival ? StartingLives : ScoreGoal;
        public float LegacyTimeGoal => ObjectiveTimeGoal.HasValue ? (float)ObjectiveTimeGoal.Value.TotalSeconds : 0;

        public MatchRules(MatchMode mode, string roomKey, int maxPlayers = PlayerEntity.SlotCapacity,
            TimeSpan? timeLimit = null, int scoreGoal = 0, TimeSpan? objectiveTimeGoal = null,
            int startingLives = 0, bool friendlyFire = false, bool affinityWeapons = false,
            bool playerRadar = false, bool octolithReset = false, int damageLevel = 1,
            SpawnPolicy spawnPolicy = SpawnPolicy.Classic, bool cancelSpawnProtectionOnOffensiveAction = false)
        {
            _ = mode.ToLegacyMode();
            if (String.IsNullOrWhiteSpace(roomKey)) { throw new ArgumentException("A room key is required.", nameof(roomKey)); }
            if (maxPlayers < 1 || maxPlayers > PlayerEntity.SlotCapacity) { throw new ArgumentOutOfRangeException(nameof(maxPlayers)); }
            // Null means unlimited. Zero is retained: legacy setup uses zero for an
            // already expired match, whereas rotation parsing translates zero to null.
            if (timeLimit < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(timeLimit)); }
            if (objectiveTimeGoal < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(objectiveTimeGoal)); }
            if (scoreGoal < 0) { throw new ArgumentOutOfRangeException(nameof(scoreGoal)); }
            if (startingLives < 0) { throw new ArgumentOutOfRangeException(nameof(startingLives)); }
            if ((uint)damageLevel > 2) { throw new ArgumentOutOfRangeException(nameof(damageLevel)); }
            if (!Enum.IsDefined(spawnPolicy)) { throw new ArgumentOutOfRangeException(nameof(spawnPolicy)); }
            SpawnPolicy = spawnPolicy;
            CancelSpawnProtectionOnOffensiveAction = cancelSpawnProtectionOnOffensiveAction;
            Mode = mode;
            RoomKey = roomKey;
            MaxPlayers = maxPlayers;
            TimeLimit = timeLimit;
            ScoreGoal = scoreGoal;
            ObjectiveTimeGoal = objectiveTimeGoal;
            StartingLives = startingLives;
            FriendlyFire = friendlyFire;
            AffinityWeapons = affinityWeapons;
            PlayerRadar = playerRadar;
            OctolithReset = octolithReset;
            DamageLevel = damageLevel;
        }

        /// <summary>Converts setup values without treating the current remaining
        /// clock as a new rule. Supply the originally configured time limit.</summary>
        public static MatchRules FromLegacy(MatchMode mode, string roomKey, float timeLimit,
            int pointGoal, float timeGoal, int maxPlayers = PlayerEntity.SlotCapacity,
            bool friendlyFire = false, bool affinityWeapons = false, bool playerRadar = false,
            bool octolithReset = false, int damageLevel = 1)
        {
            if (!Single.IsFinite(timeLimit) || (timeLimit < 0 && timeLimit != -1)) { throw new ArgumentOutOfRangeException(nameof(timeLimit)); }
            if (!Single.IsFinite(timeGoal) || timeGoal < 0) { throw new ArgumentOutOfRangeException(nameof(timeGoal)); }
            bool survival = mode is MatchMode.Survival or MatchMode.TeamSurvival;
            return new MatchRules(mode, roomKey, maxPlayers,
                timeLimit < 0 ? null : TimeSpan.FromSeconds(timeLimit),
                scoreGoal: survival ? 0 : pointGoal,
                objectiveTimeGoal: TimeSpan.FromSeconds(timeGoal),
                startingLives: survival ? pointGoal : 0,
                friendlyFire, affinityWeapons, playerRadar, octolithReset, damageLevel);
        }

        public MatchRules With(MatchMode? mode = null, string? roomKey = null, int? maxPlayers = null,
            TimeSpan? timeLimit = null, bool clearTimeLimit = false, int? scoreGoal = null,
            TimeSpan? objectiveTimeGoal = null, int? startingLives = null,
            bool? friendlyFire = null, bool? affinityWeapons = null, bool? playerRadar = null,
            bool? octolithReset = null, int? damageLevel = null,
            SpawnPolicy? spawnPolicy = null, bool? cancelSpawnProtectionOnOffensiveAction = null)
        {
            return new MatchRules(mode ?? Mode, roomKey ?? RoomKey, maxPlayers ?? MaxPlayers,
                clearTimeLimit ? null : timeLimit ?? TimeLimit, scoreGoal ?? ScoreGoal,
                objectiveTimeGoal ?? ObjectiveTimeGoal, startingLives ?? StartingLives,
                friendlyFire ?? FriendlyFire, affinityWeapons ?? AffinityWeapons,
                playerRadar ?? PlayerRadar, octolithReset ?? OctolithReset, damageLevel ?? DamageLevel,
                spawnPolicy ?? SpawnPolicy, cancelSpawnProtectionOnOffensiveAction ?? CancelSpawnProtectionOnOffensiveAction);
        }

        public static MatchRules CreateDefault(MatchMode mode, string roomKey, int maxPlayers = PlayerEntity.SlotCapacity)
        {
            _ = mode.ToLegacyMode();
            bool battle = mode is MatchMode.Battle or MatchMode.TeamBattle;
            bool survival = mode is MatchMode.Survival or MatchMode.TeamSurvival;
            int points = mode switch
            {
                MatchMode.Battle or MatchMode.TeamBattle => 7,
                MatchMode.Bounty or MatchMode.TeamBounty => 3,
                MatchMode.Capture => 5,
                MatchMode.Nodes or MatchMode.TeamNodes => 70,
                _ => 0
            };
            bool timedObjective = mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter;
            return new MatchRules(mode, roomKey, maxPlayers, TimeSpan.FromMinutes(battle ? 7 : 15),
                points, timedObjective ? TimeSpan.FromSeconds(90) : null, survival ? 2 : 0);
        }
    }
}
