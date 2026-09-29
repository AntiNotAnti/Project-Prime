using System;
using System.Linq;
using MphRead.Mods.Multiplayer;

namespace MphRead.Mods.Network
{
    public enum SessionPhase : byte { Lobby, Starting, InMatch, PostMatch }
    public enum ServerSessionPolicy : byte { Continuous, Lobby }
    public enum MatchFormat : byte { Auto, FreeForAll, OneVsOne, TwoVsTwo, ThreeVsThree, FourVsFour, TwoVsTwoVsTwoVsTwo, Custom }

    /// <summary>
    /// The wire keeps one ushort for a mode's win condition. Point-scored
    /// modes use it directly, Survival stores spare lives, and Defender /
    /// Prime Hunter store the required control time in seconds.
    /// </summary>
    public static class MatchGoalRules
    {
        public static bool UsesLives(GameMode mode) =>
            mode is GameMode.Survival or GameMode.SurvivalTeams;

        public static bool UsesTimeTarget(GameMode mode) =>
            mode is GameMode.Defender or GameMode.DefenderTeams or GameMode.PrimeHunter;

        public static ushort DefaultValue(GameMode mode) => mode switch
        {
            GameMode.Battle or GameMode.BattleTeams => 7,
            GameMode.Survival or GameMode.SurvivalTeams => 2, // two spare lives = three total
            GameMode.Bounty or GameMode.BountyTeams => 3,
            GameMode.Capture => 5,
            GameMode.Defender or GameMode.DefenderTeams => 90,
            GameMode.Nodes or GameMode.NodesTeams => 70,
            GameMode.PrimeHunter => 90,
            _ => 0
        };
    }

    [Flags]
    public enum SessionRules : ushort
    {
        None = 0, FriendlyFire = 1, AffinityWeapons = 2, ShadowFreeze = 4,
        RequireReady = 8, AllowJoinInProgress = 16, LockTeams = 32,
        HideOpponentHealth = 64, DisablePowerups = 128, SpawnProtection = 256,
        VanillaDuelResources = 512, InstaGib = 1024, LowTier = 2048, NoImperialist = 4096, EnhancedHunters = 8192
    }

    public readonly record struct MatchDefinition
    {
        public string RoomKey { get; init; }
        public NetworkMapIdentity MapIdentity { get; init; }
        public GameMode Mode { get; init; }
        public MatchFormat Format { get; init; }
        public TeamLayout CustomTeams { get; init; }
        public ushort TimeLimitSeconds { get; init; }
        public ushort PointGoal { get; init; }
        public bool FriendlyFire { get; init; }
        public bool AffinityWeapons { get; init; }
        public bool EnhancedHunters { get; init; }
        public bool ShadowFreeze { get; init; }
        public bool HideOpponentHealth { get; init; }
        public bool DisablePowerups { get; init; }
        public bool VanillaDuelResources { get; init; }
        public bool SpawnProtection { get; init; }
        public bool InstaGib { get; init; }
        public bool LowTier { get; init; }
        public bool NoImperialist { get; init; }
        public MatchDefinition NormalizeLegacy() => Mode == GameMode.InstaGib
            ? this with { Mode = GameMode.Battle, InstaGib = true } : this;

        public void ApplyModifiers(SceneGameState state)
        {
            state.FriendlyFire = FriendlyFire;
            state.AffinityWeapons = AffinityWeapons;
            state.EnhancedHunters = EnhancedHunters;
            state.InstaGib = InstaGib || Mode == GameMode.InstaGib;
            state.LowTier = LowTier;
            state.NoImperialist = NoImperialist;
            state.ShadowFreeze = ShadowFreeze;
            state.SpawnProtection = SpawnProtection;
        }

        public string ModifierSummary => String.Join(" • ", new[] {
            InstaGib ? "Insta-Gib" : null, LowTier ? "Low Tier" : null,
            NoImperialist ? "No Imp" : null }.Where(value => value != null));

        public SessionRules Rules => (FriendlyFire ? SessionRules.FriendlyFire : 0)
            | (AffinityWeapons ? SessionRules.AffinityWeapons : 0)
            | (EnhancedHunters ? SessionRules.EnhancedHunters : 0)
            | (ShadowFreeze ? SessionRules.ShadowFreeze : 0)
            | (HideOpponentHealth ? SessionRules.HideOpponentHealth : 0)
            | (DisablePowerups ? SessionRules.DisablePowerups : 0)
            | (SpawnProtection ? SessionRules.SpawnProtection : 0)
            | (VanillaDuelResources ? SessionRules.VanillaDuelResources : 0)
            | (InstaGib ? SessionRules.InstaGib : 0)
            | (LowTier ? SessionRules.LowTier : 0)
            | (NoImperialist ? SessionRules.NoImperialist : 0);
    }
}
