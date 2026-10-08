using System;
using System.Collections.Immutable;
using MphRead.Mods.Network;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.Core;

public enum OfflineChoice
{
    Arena, GameType, Matchup, Bots, BotDifficulty, Hunter, Suit, TrainingHunter,
    Drill, Weapon, Duration, Targets, Movement, TrainingDifficulty, Distance, Scope
}

public enum OfflineRuleToggle
{
    AutoReset, FriendlyFire, AffinityWeapons, EnhancedHunters, ShadowFreeze,
    SpawnProtection, Fiesta, InstaGib, LowTier, NoImperialist, BalancedMode, HunterRadar
}

public enum OfflineTrainingToggle { HeadshotsOnly, InfiniteAmmo, ReloadOnHit, ClickSkipsReload, FixedSeed }

public readonly record struct OfflineActionResult(bool Accepted, string Message)
{
    public static OfflineActionResult Ok => new(true, "");
    public static OfflineActionResult Reject(string message) => new(false, message);
}

public readonly record struct OfflineArenaSnapshot(string Key, string Label, bool Compatible, string Reason);

/// <summary>Explicit edit surface, never an arbitrary MenuSettings property writer.</summary>
public sealed record OfflineRulesDraft
{
    public string PointGoal { get; init; } = "7";
    public string TimeLimit { get; init; } = "7:00";
    public string TimeGoal { get; init; } = "1:30";
    public string DamageLevel { get; init; } = "medium";
    public bool AutoReset { get; init; }
    public bool FriendlyFire { get; init; }
    public bool AffinityWeapons { get; init; }
    public bool EnhancedHunters { get; init; }
    public bool ShadowFreeze { get; init; }
    public bool SpawnProtection { get; init; }
    public bool Fiesta { get; init; }
    public bool InstaGib { get; init; }
    public bool LowTier { get; init; }
    public bool NoImperialist { get; init; }
    public bool BalancedMode { get; init; }
    public bool HunterRadar { get; init; }

    public static OfflineRulesDraft Read(MenuSettings s) => new()
    {
        PointGoal = s.PointGoal, TimeLimit = s.TimeLimit, TimeGoal = s.TimeGoal,
        DamageLevel = s.DamageLevel, AutoReset = s.AutoReset == "on", FriendlyFire = s.FriendlyFire == "on",
        AffinityWeapons = s.AffinityWeapons == "on", EnhancedHunters = s.EnhancedHunters == "on",
        ShadowFreeze = s.ShadowFreeze == "on", SpawnProtection = s.SpawnProtection == "on",
        Fiesta = s.Fiesta == "on", InstaGib = s.InstaGib == "on", LowTier = s.LowTier == "on",
        NoImperialist = s.NoImperialist == "on", BalancedMode = s.BalancedMode == "on", HunterRadar = s.HunterRadar == "on"
    };

    internal void Write(MenuSettings s)
    {
        static string Flag(bool value) => value ? "on" : "off";
        s.PointGoal = PointGoal; s.TimeLimit = TimeLimit; s.TimeGoal = TimeGoal; s.DamageLevel = DamageLevel;
        s.AutoReset = Flag(AutoReset); s.FriendlyFire = Flag(FriendlyFire); s.AffinityWeapons = Flag(AffinityWeapons);
        s.EnhancedHunters = Flag(EnhancedHunters); s.ShadowFreeze = Flag(ShadowFreeze); s.SpawnProtection = Flag(SpawnProtection);
        s.Fiesta = Flag(Fiesta); s.InstaGib = Flag(InstaGib); s.LowTier = Flag(LowTier);
        s.NoImperialist = Flag(NoImperialist); s.BalancedMode = Flag(BalancedMode); s.HunterRadar = Flag(HunterRadar);
    }

    internal MatchDefinition Match(GameMode mode) => new()
    {
        Mode = mode, FriendlyFire = FriendlyFire, AffinityWeapons = AffinityWeapons,
        EnhancedHunters = EnhancedHunters, ShadowFreeze = ShadowFreeze, SpawnProtection = SpawnProtection,
        Fiesta = Fiesta, InstaGib = InstaGib, LowTier = LowTier, NoImperialist = NoImperialist,
        BalancedMode = BalancedMode, OctolithAutoReset = Multiplayer.MatchModifierRules.UsesOctolith(mode) && AutoReset
    };
}

/// <summary>Copied immutable values for one Offline controller lifetime.</summary>
public sealed record OfflineSnapshot
{
    public Guid Lifetime { get; init; }
    public long Revision { get; init; }
    public ImmutableArray<OfflineArenaSnapshot> Arenas { get; init; } = ImmutableArray<OfflineArenaSnapshot>.Empty;
    public string ArenaKey { get; init; } = "";
    public string ArenaLabel { get; init; } = "";
    public int GameType { get; init; }
    public string GameTypeLabel { get; init; } = "";
    public int Matchup { get; init; }
    public string MatchupLabel { get; init; } = "";
    public bool MatchupEditable { get; init; }
    public GameMode Mode { get; init; }
    public Hunter Hunter { get; init; }
    public int Suit { get; init; }
    public int Bots { get; init; }
    public int BotDifficulty { get; init; }
    public string BotDifficultyLabel { get; init; } = "";
    public Hunter TrainingHunter { get; init; }
    public AimTrainerDefinition Training { get; init; }
    public OfflineRulesDraft Rules { get; init; } = new();
    public bool RulesOpen { get; init; }
    public bool TrainingOptionsOpen { get; init; }
    public bool CanLaunchMatch { get; init; }
    public bool CanLaunchTraining { get; init; }
    public string Availability { get; init; } = "";
    public string ArenaError { get; init; } = "";
    public string Error { get; init; } = "";
    public bool PointGoalVisible => !MatchGoalRules.UsesTimeTarget(Mode) && Mode is not (GameMode.GunGame or GameMode.OneInTheChamber);
    public bool TimeLimitVisible => Mode != GameMode.OneInTheChamber;
    public bool TimeGoalVisible => MatchGoalRules.UsesTimeTarget(Mode);
    public bool FriendlyFireVisible => GameState.IsTeamMode(Mode);
    public bool AutoResetVisible => Multiplayer.MatchModifierRules.UsesOctolith(Mode);
}

/// <summary>Authoritative engine boundary; no renderer or GUI controller is constructed here.</summary>
public interface IOfflineBackend
{
    bool CanLaunch(out string reason);
    bool ValidateArena(string room, GameMode mode, int participants, out string reason);
    bool ValidateRules(MatchDefinition match, out string reason);
    void CommitSettings(MenuSettings settings);
    void ApplySettings(MenuSettings settings);
    LaunchPlan CreateMatch(MenuSettings settings, string room, GameMode mode, Hunter hunter, int suit, int bots, int level);
    LaunchPlan CreateTraining(AimTrainerDefinition training, Hunter hunter, int suit);
    uint NewTrainingSeed();
}

public sealed class OfflineEngineBackend : IOfflineBackend
{
    public bool CanLaunch(out string reason)
    {
        reason = DemoPlayback.IsActive ? "Return to Theatre and close the current replay before starting another session."
            : Network.NetSession.Active && Network.NetSession.PersistentLobby ? "Leave your multiplayer lobby before starting local gameplay."
            : !GameFiles.Ready ? "Set up game files before starting local gameplay." : "";
        return reason.Length == 0;
    }
    public bool ValidateArena(string room, GameMode mode, int participants, out string reason) =>
        Multiplayer.MapModeCapabilities.Supports(room, mode, Multiplayer.MatchWorldProfile.Resolve(participants), out reason, participants);
    public bool ValidateRules(MatchDefinition match, out string reason) => Multiplayer.MatchModifierRules.Validate(match, out reason);
    public void CommitSettings(MenuSettings settings) => GameState.CommitSettings(settings);
    public void ApplySettings(MenuSettings settings) => GameSettings.Apply(settings);
    public LaunchPlan CreateMatch(MenuSettings settings, string room, GameMode mode, Hunter hunter, int suit, int bots, int level) =>
        OfflineLaunch.Create(settings, room, mode, hunter, suit, bots, level);
    public LaunchPlan CreateTraining(AimTrainerDefinition training, Hunter hunter, int suit) => AimTrainerLaunch.Create(training, hunter, suit);
    public uint NewTrainingSeed() => (uint)Random.Shared.Next(1, int.MaxValue);
}
