using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Owns local setup drafts and validates launch plans on the engine thread.</summary>
public sealed class OfflineController : IDisposable
{
    private static readonly int[] Durations = { 30, 60, 120, 300 };
    private static readonly string[] Skills = { "Easy", "Normal", "Hard", "Insane" };
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly MenuSettings _settings;
    private readonly IOfflineBackend _backend;
    private readonly ImmutableArray<string> _rooms;
    private readonly Guid _lifetime = Guid.NewGuid();
    private long _revision;
    private int _room, _gameType, _matchup, _bots, _skill, _suit;
    private Hunter _hunter, _trainingHunter;
    private AimTrainerDefinition _training;
    private OfflineRulesDraft? _rules;
    private OfflineSnapshot? _lastSnapshot;
    private ImmutableArray<OfflineArenaSnapshot> _arenaSnapshots;
    private GameMode _arenaMode;
    private int _arenaBots = -1;
    private bool _trainingOptions, _disposed;
    private bool _launchIssued;
    private string _error = "";
    private LaunchPlan? _pending;

    public OfflineController(MenuSettings settings, IReadOnlyList<string> rooms, IOfflineBackend? backend = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentNullException.ThrowIfNull(rooms);
        _backend = backend ?? new OfflineEngineBackend();
        _rooms = rooms.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToImmutableArray();
        _room = Math.Max(0, _rooms.IndexOf(settings.RoomKey));
        _matchup = settings.TeamPlay == "on" ? 1 : 0;
        _bots = Math.Clamp(LauncherPrefs.Bots, 0, PlayerEntity.SlotCapacity - 1);
        _skill = Math.Clamp(LauncherPrefs.BotLevel, 0, Skills.Length - 1);
        _suit = Math.Clamp(LauncherPrefs.LastColor, 0, 3);
        _hunter = ValidHunter(LauncherPrefs.LastHunter, settings.LowTier == "on");
        _trainingHunter = ValidHunter(LauncherPrefs.LastHunter, false);
        _training = LauncherPrefs.Training.Sanitize();
        if (!Durations.Contains(_training.DurationSeconds)) _training = _training with { DurationSeconds = 60 };
        NormalizeMode();
    }

    public OfflineSnapshot Snapshot()
    {
        Verify();
        bool ready = _backend.CanLaunch(out string unavailable) && !_launchIssued;
        string arena = SelectedRoom;
        bool compatible = arena.Length > 0 && _backend.ValidateArena(arena, SelectedMode, _bots + 1, out _);
        string arenaError = "";
        if (arena.Length == 0) arenaError = "No arenas are available. Set up game files first.";
        else _backend.ValidateArena(arena, SelectedMode, _bots + 1, out arenaError);
        if (_arenaSnapshots.IsDefault || _arenaMode != SelectedMode || _arenaBots != _bots)
        {
            _arenaMode = SelectedMode; _arenaBots = _bots;
            _arenaSnapshots = _rooms.Select(key =>
            {
                bool supported = _backend.ValidateArena(key, SelectedMode, _bots + 1, out string reason);
                return new OfflineArenaSnapshot(key, RoomName(key), supported, reason);
            }).ToImmutableArray();
        }
        var snapshot = new OfflineSnapshot
        {
            Lifetime = _lifetime, Arenas = _arenaSnapshots, ArenaKey = arena,
            ArenaLabel = arena.Length == 0 ? "No arenas available" : RoomName(arena),
            GameType = _gameType, GameTypeLabel = MatchTypeCatalog.GameTypes[_gameType].Label,
            Matchup = _matchup, MatchupLabel = MatchTypeCatalog.BasicMatchups[_matchup].Label,
            MatchupEditable = !MatchTypeCatalog.GameTypes[_gameType].TeamOnly && !MatchTypeCatalog.GameTypes[_gameType].FfaOnly,
            Mode = SelectedMode, Hunter = _hunter, TrainingHunter = _trainingHunter, Suit = _suit,
            Bots = _bots, BotDifficulty = _skill, BotDifficultyLabel = Skills[_skill], Training = _training,
            Rules = _rules ?? OfflineRulesDraft.Read(_settings), RulesOpen = _rules != null,
            TrainingOptionsOpen = _trainingOptions, CanLaunchMatch = ready && compatible,
            CanLaunchTraining = ready, Availability = unavailable, ArenaError = arenaError, Error = _error
        };
        if (snapshot != _lastSnapshot) { _lastSnapshot = snapshot; _revision++; }
        return snapshot with { Revision = _revision };
    }

    public OfflineActionResult Cycle(OfflineChoice choice)
    {
        Verify();
        if (!Enum.IsDefined(choice)) return Reject("Unknown offline choice.");
        if (_launchIssued) return Reject("A local launch is already pending.");
        // Match setup cannot change underneath the rule sheet being validated.
        if (_rules != null) return Reject("Apply or cancel the match rules before changing setup.");
        switch (choice)
        {
            case OfflineChoice.Arena: if (_rooms.Length > 0) _room = (_room + 1) % _rooms.Length; break;
            case OfflineChoice.GameType: _gameType = (_gameType + 1) % MatchTypeCatalog.GameTypes.Length; NormalizeMode(); break;
            case OfflineChoice.Matchup: _matchup = (_matchup + 1) % MatchTypeCatalog.BasicMatchups.Length; NormalizeMode(); break;
            case OfflineChoice.Bots: _bots = (_bots + 1) % PlayerEntity.SlotCapacity; break;
            case OfflineChoice.BotDifficulty: _skill = (_skill + 1) % Skills.Length; break;
            case OfflineChoice.Hunter: _hunter = NextHunter(_hunter, _settings.LowTier == "on"); break;
            case OfflineChoice.Suit: _suit = (_suit + 1) % 4; break;
            case OfflineChoice.TrainingHunter: _trainingHunter = NextHunter(_trainingHunter, false); break;
            case OfflineChoice.Drill:
                _training = _training with { Drill = Next(_training.Drill) };
                ConfigureDrill(); break;
            case OfflineChoice.Weapon:
                if (_training.Drill != AimTrainerDrill.ImperialistPrecision)
                    _training = _training with { Weapon = (BeamType)(((int)_training.Weapon + 1) % 8) }; break;
            case OfflineChoice.Duration:
                _training = _training with { DurationSeconds = Durations[(Array.IndexOf(Durations, _training.DurationSeconds) + 1) % Durations.Length] }; break;
            case OfflineChoice.Targets:
                _training = _training with { TargetCount = _training.Drill == AimTrainerDrill.MultiTargetFlick
                    ? _training.TargetCount == 4 ? 5 : 4 : _training.TargetCount % (PlayerEntity.SlotCapacity - 1) + 1 }; break;
            case OfflineChoice.Movement: _training = _training with { Movement = Next(_training.Movement) }; break;
            case OfflineChoice.TrainingDifficulty: _training = _training with { Difficulty = Next(_training.Difficulty) }; break;
            case OfflineChoice.Distance: _training = _training with { Distance = Next(_training.Distance) }; break;
            case OfflineChoice.Scope: _training = _training with { Scope = Next(_training.Scope) }; break;
        }
        _training = _training.Sanitize();
        _error = "";
        return OfflineActionResult.Ok;
    }

    public OfflineActionResult SelectArena(string key)
    {
        Verify();
        if (_rules != null || _launchIssued) return Reject("Close match rules before selecting an arena.");
        int index = _rooms.IndexOf(key);
        if (index < 0) return Reject("The selected arena is no longer available.");
        if (!_backend.ValidateArena(key, SelectedMode, _bots + 1, out string reason)) return Reject(reason);
        _room = index; _error = ""; return OfflineActionResult.Ok;
    }

    public void OpenRules() { Verify(); if (!_launchIssued) { _rules = OfflineRulesDraft.Read(_settings); _error = ""; } }
    public void CancelRules() { Verify(); _rules = null; _error = ""; }
    public void SetRuleFields(string pointGoal, string timeLimit, string timeGoal)
    {
        Verify();
        if (_rules != null) _rules = _rules with { PointGoal = pointGoal, TimeLimit = timeLimit, TimeGoal = timeGoal };
    }

    public OfflineActionResult ToggleRule(OfflineRuleToggle flag)
    {
        Verify();
        if (_rules == null || !Enum.IsDefined(flag)) return Reject("Open match rules before editing them.");
        var r = _rules;
        _rules = flag switch
        {
            OfflineRuleToggle.AutoReset => r with { AutoReset = !r.AutoReset },
            OfflineRuleToggle.FriendlyFire => r with { FriendlyFire = !r.FriendlyFire },
            OfflineRuleToggle.AffinityWeapons => r with { AffinityWeapons = !r.AffinityWeapons },
            OfflineRuleToggle.EnhancedHunters => r with { EnhancedHunters = !r.EnhancedHunters },
            OfflineRuleToggle.ShadowFreeze => r with { ShadowFreeze = !r.ShadowFreeze },
            OfflineRuleToggle.SpawnProtection => r with { SpawnProtection = !r.SpawnProtection },
            OfflineRuleToggle.Fiesta => r with { Fiesta = !r.Fiesta },
            OfflineRuleToggle.InstaGib => r with { InstaGib = !r.InstaGib, NoImperialist = r.InstaGib && r.NoImperialist },
            OfflineRuleToggle.LowTier => r with { LowTier = !r.LowTier },
            OfflineRuleToggle.NoImperialist => r with { NoImperialist = !r.NoImperialist, InstaGib = r.NoImperialist && r.InstaGib },
            OfflineRuleToggle.BalancedMode => r with { BalancedMode = !r.BalancedMode },
            OfflineRuleToggle.HunterRadar => r with { HunterRadar = !r.HunterRadar },
            _ => r
        };
        _error = ""; return OfflineActionResult.Ok;
    }

    public void CycleDamage()
    {
        Verify(); if (_rules == null) return;
        _rules = _rules with { DamageLevel = _rules.DamageLevel == "low" ? "medium" : _rules.DamageLevel == "medium" ? "high" : "low" };
    }

    public OfflineActionResult ApplyRules()
    {
        Verify();
        if (_rules == null) return Reject("No match rule draft is open.");
        var draft = _rules;
        if (!int.TryParse(draft.PointGoal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int points)
            || points < 0 || points > 999 || !Duration(draft.TimeLimit) || !Duration(draft.TimeGoal))
            return Reject("Use a point goal from 0–999 and durations as m:ss.");
        if (!_backend.ValidateRules(draft.Match(SelectedMode), out string reason)) return Reject(reason);
        string[] old = CaptureSettings();
        try
        {
            draft.Write(_settings);
            _settings.OneInTheChamber = "off";
            _backend.CommitSettings(_settings);
        }
        catch (Exception ex)
        {
            RestoreSettings(old);
            return Reject("Could not save rules: " + ex.Message);
        }
        _backend.ApplySettings(_settings);
        _hunter = ValidHunter(_hunter, draft.LowTier);
        _rules = null; _error = ""; return OfflineActionResult.Ok;
    }

    public void OpenTrainingOptions() { Verify(); if (!_launchIssued) _trainingOptions = true; }
    public void CloseTrainingOptions() { Verify(); _trainingOptions = false; }
    public OfflineActionResult ToggleTraining(OfflineTrainingToggle flag)
    {
        Verify();
        if (!Enum.IsDefined(flag) || _launchIssued) return Reject("Training options cannot be edited now.");
        _training = flag switch
        {
            OfflineTrainingToggle.HeadshotsOnly => _training with { HeadshotsOnly = !_training.HeadshotsOnly },
            OfflineTrainingToggle.InfiniteAmmo => _training with { InfiniteAmmo = !_training.InfiniteAmmo },
            OfflineTrainingToggle.ReloadOnHit => _training with { ReloadOnHit = !_training.ReloadOnHit },
            OfflineTrainingToggle.ClickSkipsReload => _training with { NormalImperialistReload = !_training.NormalImperialistReload },
            OfflineTrainingToggle.FixedSeed => _training with { FixedSeed = !_training.FixedSeed },
            _ => _training
        };
        _training = _training.Sanitize(); _error = ""; return OfflineActionResult.Ok;
    }

    public OfflineActionResult RequestMatch()
    {
        Verify();
        if (!CheckLaunch(out string reason)) return Reject(reason);
        if (SelectedRoom.Length == 0) return Reject("Select an arena before starting the match.");
        if (!_backend.ValidateArena(SelectedRoom, SelectedMode, _bots + 1, out reason)) return Reject(reason);
        var rules = OfflineRulesDraft.Read(_settings).Match(SelectedMode);
        if (!_backend.ValidateRules(rules, out reason)) return Reject(reason);
        try
        {
            // OfflineLaunch remains the preference/plan authority. Carry every exposed
            // modifier, including Enhanced Hunters, into the final explicit match rules.
            _pending = _backend.CreateMatch(_settings, SelectedRoom, SelectedMode, _hunter, _suit, _bots, _skill)
                with { MatchRules = rules };
            _launchIssued = true;
            _error = ""; return OfflineActionResult.Ok;
        }
        catch (Exception ex) { return Reject("Could not start local match: " + ex.Message); }
    }

    public OfflineActionResult RequestTraining()
    {
        Verify();
        if (!CheckLaunch(out string reason)) return Reject(reason);
        try
        {
            var training = (_training with { Seed = _training.FixedSeed ? _training.Seed : _backend.NewTrainingSeed() }).Sanitize();
            _pending = _backend.CreateTraining(training, _trainingHunter, _suit);
            _launchIssued = true;
            _error = ""; return OfflineActionResult.Ok;
        }
        catch (Exception ex) { return Reject("Could not start training: " + ex.Message); }
    }

    public bool TryTakeLaunch(out LaunchPlan plan)
    {
        Verify();
        if (_pending is not LaunchPlan ready) { plan = default; return false; }
        _pending = null; plan = ready; return true;
    }
    public void ReportLaunchFailure(string reason) { Verify(); _pending = null; _launchIssued = false; _error = reason ?? "Local gameplay could not be loaded."; }
    public void CancelPendingLaunch() { Verify(); _pending = null; _launchIssued = false; }
    public void Refresh() { Verify(); _hunter = ValidHunter(_hunter, _settings.LowTier == "on"); }
    public void Dispose() { VerifyOwner(); _disposed = true; _pending = null; _launchIssued = false; _rules = null; }

    private bool CheckLaunch(out string reason)
    {
        reason = _launchIssued ? "A local launch is already pending."
            : _rules != null || _trainingOptions ? "Close the open options before starting gameplay." : "";
        return reason.Length == 0 && _backend.CanLaunch(out reason);
    }
    private OfflineActionResult Reject(string reason) { _error = reason; return OfflineActionResult.Reject(reason); }
    private string SelectedRoom => _rooms.IsEmpty ? "" : _rooms[_room];
    private GameMode SelectedMode => MatchTypeCatalog.GameTypes[_gameType].Resolve(MatchTypeCatalog.BasicMatchups[_matchup].Format);
    private void NormalizeMode() => _matchup = MatchTypeCatalog.BasicMatchupIndex(MatchTypeCatalog.NormalizeFormat(
        MatchTypeCatalog.GameTypes[_gameType], MatchTypeCatalog.BasicMatchups[_matchup].Format));
    private void ConfigureDrill()
    {
        _training = _training.Drill switch
        {
            AimTrainerDrill.StrafeTracking => _training with { Movement = AimTrainerMovement.HorizontalStrafe },
            AimTrainerDrill.JumpTracking => _training with { Movement = AimTrainerMovement.JumpStrafe },
            AimTrainerDrill.TimedFlick => _training with { Movement = AimTrainerMovement.Static, TargetCount = 1 },
            AimTrainerDrill.MultiTargetFlick => _training with { Movement = AimTrainerMovement.Static, TargetCount = 5 },
            AimTrainerDrill.ImperialistPrecision => _training with { Weapon = BeamType.Imperialist },
            _ => _training
        };
    }
    private static T Next<T>(T value) where T : struct, Enum
    {
        var options = Enum.GetValues<T>(); return options[(Array.IndexOf(options, value) + 1) % options.Length];
    }
    private static Hunter ValidHunter(Hunter value, bool low) => value == Hunter.Random ? value
        : Multiplayer.HunterRules.Allowed(value, low) ? value : Multiplayer.HunterRules.Pool(low)[0];
    private static Hunter NextHunter(Hunter value, bool low)
    {
        var values = Multiplayer.HunterRules.Pool(low).Append(Hunter.Random).ToArray();
        return values[(Array.IndexOf(values, value) + 1) % values.Length];
    }
    private static bool Duration(string value) => TimeSpan.TryParseExact(value, @"m\:ss", CultureInfo.InvariantCulture, out _)
        || TimeSpan.TryParseExact(value, @"mm\:ss", CultureInfo.InvariantCulture, out _);
    private static string RoomName(string key) => Metadata.RoomMetadata.TryGetValue(key, out var room) ? room.InGameName ?? key : key;
    private string[] CaptureSettings() => new[] { _settings.PointGoal, _settings.TimeLimit, _settings.TimeGoal, _settings.DamageLevel,
        _settings.AutoReset, _settings.FriendlyFire, _settings.AffinityWeapons, _settings.EnhancedHunters, _settings.ShadowFreeze,
        _settings.SpawnProtection, _settings.Fiesta, _settings.InstaGib, _settings.LowTier, _settings.NoImperialist,
        _settings.BalancedMode, _settings.HunterRadar, _settings.OneInTheChamber };
    private void RestoreSettings(string[] values)
    {
        _settings.PointGoal = values[0]; _settings.TimeLimit = values[1]; _settings.TimeGoal = values[2]; _settings.DamageLevel = values[3];
        _settings.AutoReset = values[4]; _settings.FriendlyFire = values[5]; _settings.AffinityWeapons = values[6];
        _settings.EnhancedHunters = values[7]; _settings.ShadowFreeze = values[8]; _settings.SpawnProtection = values[9];
        _settings.Fiesta = values[10]; _settings.InstaGib = values[11]; _settings.LowTier = values[12]; _settings.NoImperialist = values[13];
        _settings.BalancedMode = values[14]; _settings.HunterRadar = values[15]; _settings.OneInTheChamber = values[16];
    }
    private void Verify() { VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private void VerifyOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Offline setup belongs to the engine thread.");
    }
}
