using System;
using MphRead.Entities;

namespace MphRead
{
    /// <summary>Authoritative resource-contact admission for the tactical radar.</summary>
    public enum ResourceRadarPolicy : byte
    {
        Disabled = 0,
        SpawnLocations = 1,
        AvailableResources = 2,
        AvailableWithRespawn = 3
    }

    /// <summary>Validated immutable multiplayer configuration. Runtime counters and
    /// the effective Survival radar state belong to MatchRuntime.</summary>
    public sealed record MatchRules
    {
        public const int MaximumTeamCount = 4;
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
        public bool PickupRespawnAnnouncements { get; }
        public int AssistMinimumDamage { get; }
        public int AssistWindowTicks { get; }
        public OvertimePolicy OvertimePolicy { get; }
        public LateJoinPolicy LateJoinPolicy { get; }
        public RulesetPreset RulesetPreset { get; }
        public RankingEligibility RankingEligibility { get; }
        public RadarPolicy RadarPolicy { get; }
        public TeamBalancePolicy TeamBalancePolicy { get; }
        public KillcamPolicy KillcamPolicy { get; }
        /// <summary>Whether Double Damage, Cloak, Deathalt, and Omega Cannon exist in the match.</summary>
        public bool PowerupsEnabled { get; }
        public bool EnhancedHunters { get; }
        /// <summary>Enables the authoritative Project Prime Balanced V1 ruleset.</summary>
        public bool BalancedMode { get; }
        public ResourceRadarPolicy ResourceRadarPolicy { get; }
        public bool Teams => Mode.IsTeamMode();
        public int TeamCount { get; }
        /// <summary>
        /// Retail battle entity data has authored two-, three-, and four-player
        /// layers. Eight-player matches intentionally use the richest authored
        /// layer; connected-player count is never used, so every participant
        /// loads the same pickups and spawn set.
        /// </summary>
        public int EntityLayerPlayerCount => Math.Clamp(MaxPlayers, 2, 4);
        public bool IsOctolithMode => Mode is MatchMode.Capture or MatchMode.Bounty or MatchMode.TeamBounty;
        public bool IsSurvival => Mode is MatchMode.Survival or MatchMode.TeamSurvival;
        public bool IsObjectiveTimeMode => Mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter;
        public int LegacyPointGoal => IsSurvival ? StartingLives : ScoreGoal;
        public float LegacyTimeGoal => ObjectiveTimeGoal.HasValue ? (float)ObjectiveTimeGoal.Value.TotalSeconds : 0;

        public MatchRules(MatchMode mode, string roomKey, int maxPlayers = PlayerEntity.SlotCapacity,
            TimeSpan? timeLimit = null, int scoreGoal = 0, TimeSpan? objectiveTimeGoal = null,
            int startingLives = 0, bool friendlyFire = false, bool affinityWeapons = false,
            bool playerRadar = false, bool octolithReset = false, int damageLevel = 1,
            SpawnPolicy spawnPolicy = SpawnPolicy.Classic, bool cancelSpawnProtectionOnOffensiveAction = false,
            int assistMinimumDamage = 20, int assistWindowTicks = 300,
            OvertimePolicy overtimePolicy = OvertimePolicy.Disabled, LateJoinPolicy lateJoinPolicy = LateJoinPolicy.JoinImmediately, bool pickupRespawnAnnouncements = false,
            RulesetPreset rulesetPreset = RulesetPreset.Classic, RankingEligibility rankingEligibility = RankingEligibility.Unranked,
            RadarPolicy radarPolicy = RadarPolicy.Classic, TeamBalancePolicy teamBalancePolicy = TeamBalancePolicy.BeforeStart,
            KillcamPolicy killcamPolicy = KillcamPolicy.Immediate,
            int teamCount = 2, bool powerupsEnabled = true, bool enhancedHunters = false,
            bool balancedMode = false, ResourceRadarPolicy resourceRadarPolicy = ResourceRadarPolicy.Disabled)
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
            if (assistMinimumDamage is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(assistMinimumDamage));
            if (assistWindowTicks is < 0 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(assistWindowTicks));
            if (!Enum.IsDefined(overtimePolicy)) { throw new ArgumentOutOfRangeException(nameof(overtimePolicy)); }
            if (!Enum.IsDefined(lateJoinPolicy)) { throw new ArgumentOutOfRangeException(nameof(lateJoinPolicy)); }
            if (!Enum.IsDefined(rulesetPreset)) throw new ArgumentOutOfRangeException(nameof(rulesetPreset));
            if (!Enum.IsDefined(rankingEligibility)) throw new ArgumentOutOfRangeException(nameof(rankingEligibility));
            if (!Enum.IsDefined(radarPolicy)) throw new ArgumentOutOfRangeException(nameof(radarPolicy));
            if (!Enum.IsDefined(teamBalancePolicy)) throw new ArgumentOutOfRangeException(nameof(teamBalancePolicy));
            if (!Enum.IsDefined(killcamPolicy)) throw new ArgumentOutOfRangeException(nameof(killcamPolicy));
            if (!Enum.IsDefined(resourceRadarPolicy)) throw new ArgumentOutOfRangeException(nameof(resourceRadarPolicy));
            if ((mode.IsTeamMode() && teamCount is < 2 or > MaximumTeamCount)
                || (!mode.IsTeamMode() && teamCount is not (1 or 2)))
                throw new ArgumentOutOfRangeException(nameof(teamCount));
            if (mode.IsTeamMode() && teamCount > 2
                && mode is not (MatchMode.TeamBattle or MatchMode.TeamSurvival))
                throw new ArgumentException("Three- and four-team play is supported only in Team Battle and Team Survival.");
            if (rulesetPreset == RulesetPreset.Duel && (mode != MatchMode.Battle || maxPlayers != 2))
                throw new ArgumentException("Duel requires Battle mode and two active players.");
            RulesetPreset = rulesetPreset; RankingEligibility = rankingEligibility;
            RadarPolicy = radarPolicy; TeamBalancePolicy = teamBalancePolicy;
            KillcamPolicy = killcamPolicy;
            PowerupsEnabled = powerupsEnabled;
            EnhancedHunters = enhancedHunters;
            BalancedMode = balancedMode;
            ResourceRadarPolicy = resourceRadarPolicy;
            TeamCount = mode.IsTeamMode() ? teamCount : 1;
            OvertimePolicy = overtimePolicy;
            LateJoinPolicy = lateJoinPolicy;
            PickupRespawnAnnouncements = pickupRespawnAnnouncements;
            AssistMinimumDamage = assistMinimumDamage;
            AssistWindowTicks = assistWindowTicks;
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
            PlayerRadar = radarPolicy == RadarPolicy.Enabled || radarPolicy == RadarPolicy.Classic && playerRadar;
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
            SpawnPolicy? spawnPolicy = null, bool? cancelSpawnProtectionOnOffensiveAction = null,
            int? assistMinimumDamage = null, int? assistWindowTicks = null,
            OvertimePolicy? overtimePolicy = null, LateJoinPolicy? lateJoinPolicy = null, bool? pickupRespawnAnnouncements = null,
            RulesetPreset? rulesetPreset = null, RankingEligibility? rankingEligibility = null,
            RadarPolicy? radarPolicy = null, TeamBalancePolicy? teamBalancePolicy = null,
            KillcamPolicy? killcamPolicy = null, int? teamCount = null,
            bool? powerupsEnabled = null, bool? enhancedHunters = null,
            bool? balancedMode = null, ResourceRadarPolicy? resourceRadarPolicy = null)
        {
            return new MatchRules(mode ?? Mode, roomKey ?? RoomKey, maxPlayers ?? MaxPlayers,
                clearTimeLimit ? null : timeLimit ?? TimeLimit, scoreGoal ?? ScoreGoal,
                objectiveTimeGoal ?? ObjectiveTimeGoal, startingLives ?? StartingLives,
                friendlyFire ?? FriendlyFire, affinityWeapons ?? AffinityWeapons,
                playerRadar ?? PlayerRadar, octolithReset ?? OctolithReset, damageLevel ?? DamageLevel,
                spawnPolicy ?? SpawnPolicy, cancelSpawnProtectionOnOffensiveAction ?? CancelSpawnProtectionOnOffensiveAction,
                assistMinimumDamage ?? AssistMinimumDamage, assistWindowTicks ?? AssistWindowTicks,
                overtimePolicy ?? OvertimePolicy, lateJoinPolicy ?? LateJoinPolicy, pickupRespawnAnnouncements ?? PickupRespawnAnnouncements,
                rulesetPreset ?? RulesetPreset, rankingEligibility ?? RankingEligibility,
                radarPolicy ?? RadarPolicy, teamBalancePolicy ?? TeamBalancePolicy,
                killcamPolicy ?? KillcamPolicy, teamCount ?? (Teams ? TeamCount : 2),
                powerupsEnabled ?? PowerupsEnabled, enhancedHunters ?? EnhancedHunters,
                balancedMode ?? BalancedMode, resourceRadarPolicy ?? ResourceRadarPolicy);
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
                points, timedObjective ? TimeSpan.FromSeconds(90) : null, survival ? 2 : 0,
                playerRadar: true,
                lateJoinPolicy: survival ? LateJoinPolicy.SpectateUntilNextMatch : LateJoinPolicy.JoinImmediately);
        }
    }
}
