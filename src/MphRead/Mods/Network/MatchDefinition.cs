using System;
using System.Linq;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Movement;

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
            mode is GameMode.Survival or GameMode.SurvivalTeams or GameMode.OneInTheChamber;

        public static bool UsesTimeTarget(GameMode mode) =>
            mode is GameMode.Defender or GameMode.DefenderTeams or GameMode.PrimeHunter or GameMode.Relic or GameMode.Hardpoint or GameMode.HardpointTeams;

        public static ushort DefaultValue(GameMode mode) => mode switch
        {
            GameMode.Battle or GameMode.BattleTeams or GameMode.GunGame => 7,
            GameMode.Survival or GameMode.SurvivalTeams or GameMode.OneInTheChamber => 2, // two spare lives = three total
            GameMode.Bounty or GameMode.BountyTeams => 3,
            GameMode.Capture => 5,
            GameMode.KillConfirmed or GameMode.KillConfirmedTeams or GameMode.Headhunter => 25,
            GameMode.Defender or GameMode.DefenderTeams => 90,
            GameMode.Nodes or GameMode.NodesTeams => 70,
            GameMode.PrimeHunter or GameMode.Relic => 90,
            GameMode.Hardpoint or GameMode.HardpointTeams => 150,
            _ => 0
        };
    }

    [Flags]
    public enum LobbyRuleFlags : ushort
    {
        None = 0, RequireReady = 1, AllowJoinInProgress = 2, LockTeams = 4, HideOpponentHealth = 8
    }

    [Flags]
    public enum MatchModifierFlags : uint
    {
        None = 0, FriendlyFire = 1, AffinityWeapons = 2, ShadowFreeze = 4,
        DisablePowerups = 8, SpawnProtection = 16, VanillaDuelResources = 32,
        InstaGib = 64, LowTier = 128, NoImperialist = 256, OctolithAutoReset = 512,
        EnhancedHunters = 1024, Fiesta = 2048, OneInTheChamber = 4096, BalancedMode = 8192, ThirdPersonCamera = 16384,
        MomentumMovement = 32768, Quake3Movement = 65536, TraversalMovement = 131072
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
        public bool Fiesta { get; init; }
        public bool OneInTheChamber { get; init; }
        public bool InstaGib { get; init; }
        public bool LowTier { get; init; }
        public bool NoImperialist { get; init; }
        public bool BalancedMode { get; init; }
        // Rendering-only choice, owned by the match host and shared with every client.
        public bool ThirdPersonCamera { get; init; }
        public MovementModel MovementModel { get; init; }
        public bool OctolithAutoReset { get; init; }
        public MatchDefinition NormalizeLegacy() => Mode == GameMode.InstaGib
            ? this with { Mode = GameMode.Battle, InstaGib = true }
            : Mode == GameMode.OneInTheChamber ? this with { PointGoal = 2, TimeLimitSeconds = 0 } : this;

        /// <summary>Load-boundary rules, before player resources or world entities are created.</summary>
        public void ApplyInitialConfiguration(SceneGameState state)
        {
            _ = MovementModelRules.Modifier(MovementModel);
            MatchDefinition match = NormalizeLegacy();
            state.ConfigureMatchMode(match.Mode, applyDefaults: false);
            match.ApplyModifiers(state);
            state.PointGoal = MatchGoalRules.UsesTimeTarget(match.Mode) ? 0 : match.PointGoal;
            state.TimeGoal = MatchGoalRules.UsesTimeTarget(match.Mode) ? match.PointGoal : 0;
            state.MatchTime = match.TimeLimitSeconds == 0 ? -1 : match.TimeLimitSeconds;
        }

        public void ApplyModifiers(SceneGameState state)
        {
            _ = MovementModelRules.Modifier(MovementModel);
            state.MovementModel = MovementModel;
            state.FriendlyFire = FriendlyFire;
            state.AffinityWeapons = AffinityWeapons;
            state.Fiesta = Fiesta; state.OneInTheChamber = OneInTheChamber || Mode == GameMode.OneInTheChamber;
            if (Mode == GameMode.OneInTheChamber) state.PointGoal = 2;
            state.EnhancedHunters = EnhancedHunters;
            state.InstaGib = InstaGib || Mode == GameMode.InstaGib;
            state.LowTier = LowTier;
            state.NoImperialist = NoImperialist;
            state.BalancedMode = BalancedMode;
            state.ThirdPersonCamera = ThirdPersonCamera;
            state.OctolithReset = OctolithAutoReset;
            state.ShadowFreeze = ShadowFreeze;
            state.SpawnProtection = SpawnProtection;
        }

        public string ModifierSummary => String.Join(" • ", new[] {
            Fiesta ? "Fiesta" : null, OneInTheChamber && Mode != GameMode.OneInTheChamber ? "One in the Chamber" : null, InstaGib ? "Insta-Gib" : null, LowTier ? "Low Tier" : null,
            NoImperialist ? "No Imp" : null,
            BalancedMode ? "Balanced" : null, ThirdPersonCamera ? "Third person" : null,
            MovementModel == Movement.MovementModel.Momentum ? "Momentum v1"
                : MovementModel == Movement.MovementModel.Quake3 ? "Quake III"
                : MovementModel == Movement.MovementModel.Traversal ? "Traversal" : null }.Where(value => value != null));

        public MatchModifierFlags Rules => (FriendlyFire ? MatchModifierFlags.FriendlyFire : 0)
            | (AffinityWeapons ? MatchModifierFlags.AffinityWeapons : 0)
            | (EnhancedHunters ? MatchModifierFlags.EnhancedHunters : 0)
            | (ShadowFreeze ? MatchModifierFlags.ShadowFreeze : 0)
            | (DisablePowerups ? MatchModifierFlags.DisablePowerups : 0)
            | (SpawnProtection ? MatchModifierFlags.SpawnProtection : 0)
            | (VanillaDuelResources ? MatchModifierFlags.VanillaDuelResources : 0)
            | (Fiesta ? MatchModifierFlags.Fiesta : 0)
            | (OneInTheChamber ? MatchModifierFlags.OneInTheChamber : 0)
            | (InstaGib ? MatchModifierFlags.InstaGib : 0)
            | (LowTier ? MatchModifierFlags.LowTier : 0)
            | (NoImperialist ? MatchModifierFlags.NoImperialist : 0)
            | (BalancedMode ? MatchModifierFlags.BalancedMode : 0)
            | (ThirdPersonCamera ? MatchModifierFlags.ThirdPersonCamera : 0)
            | (OctolithAutoReset ? MatchModifierFlags.OctolithAutoReset : 0)
            | MovementModelRules.Modifier(MovementModel);
    }
}
