using System;
using MphRead.Entities;
using MphRead.Mods.Training;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>
/// One owner-thread presentation draft for the existing AimTrainerLaunch.
/// No global launcher prefs, scene, target spawns or input state are owned here.
/// </summary>
public sealed class RmlUiTrainingDraft
{
    private static readonly int[] Durations = { 30, 60, 120, 300 };
    private AimTrainerDefinition _draft;
    private Hunter _hunter;

    public RmlUiTrainingDraft(AimTrainerDefinition saved, Hunter hunter)
    {
        _draft = saved.Sanitize();
        _hunter = (uint)hunter < 7 ? hunter : Hunter.Samus;
    }

    public AimTrainerDefinition Current => _draft;
    public Hunter Hunter => _hunter;

    private static T Cycle<T>(T current, int count) where T : Enum =>
        (T)Enum.ToObject(typeof(T), ((Convert.ToInt32(current) + 1) % count));

    public bool Choice(int index)
    {
        switch (index)
        {
            case 7:
                _hunter = (Hunter)(((int)_hunter + 1) % 7);
                return true;
            case 8:
                _draft = _draft with { Drill = Cycle(_draft.Drill, Enum.GetValues<AimTrainerDrill>().Length) };
                MatchDrill(resetMovement: true);
                return true;
            case 9:
                if (_draft.Drill == AimTrainerDrill.ImperialistPrecision) return false;
                _draft = _draft with { Weapon = (BeamType)(((int)_draft.Weapon + 1) % 8) };
                return true;
            case 10:
                int current = Array.IndexOf(Durations, _draft.DurationSeconds);
                _draft = _draft with { DurationSeconds = Durations[(current + 1) % Durations.Length] };
                return true;
            case 11:
                if (_draft.Drill == AimTrainerDrill.TimedFlick) return false;
                _draft = _draft with
                {
                    TargetCount = _draft.Drill == AimTrainerDrill.MultiTargetFlick
                        ? _draft.TargetCount == 4 ? 5 : 4
                        : _draft.TargetCount % 7 + 1
                };
                return true;
            case 12:
                _draft = _draft with { Movement = Cycle(_draft.Movement, Enum.GetValues<AimTrainerMovement>().Length) };
                return true;
            case 13:
                _draft = _draft with { Difficulty = Cycle(_draft.Difficulty, Enum.GetValues<TrainingDifficulty>().Length) };
                return true;
            case 14:
                _draft = _draft with { Distance = Cycle(_draft.Distance, Enum.GetValues<TrainingDistance>().Length) };
                return true;
            case 15:
                _draft = _draft with { Scope = Cycle(_draft.Scope, Enum.GetValues<TrainingScope>().Length) };
                return true;
            default: return false;
        }
    }

    private void MatchDrill(bool resetMovement)
    {
        var drill = _draft.Drill;
        if (resetMovement)
        {
            if (drill == AimTrainerDrill.StrafeTracking)
                _draft = _draft with { Movement = AimTrainerMovement.HorizontalStrafe };
            else if (drill == AimTrainerDrill.JumpTracking)
                _draft = _draft with { Movement = AimTrainerMovement.JumpStrafe };
            else if (drill is AimTrainerDrill.TimedFlick or AimTrainerDrill.MultiTargetFlick)
                _draft = _draft with { Movement = AimTrainerMovement.Static };
        }
        if (drill == AimTrainerDrill.TimedFlick)
            _draft = _draft with { TargetCount = 1 };
        if (drill == AimTrainerDrill.MultiTargetFlick)
            _draft = _draft with { TargetCount = Math.Clamp(_draft.TargetCount, 4, 5) };
        if (drill == AimTrainerDrill.ImperialistPrecision)
            _draft = _draft with { Weapon = BeamType.Imperialist };
        if (drill == AimTrainerDrill.HeadshotPrecision)
            _draft = _draft with { HeadshotsOnly = true };
        _draft = _draft.Sanitize();
    }

    public bool Toggle(int index)
    {
        switch (index)
        {
            case 0 when _draft.Drill != AimTrainerDrill.HeadshotPrecision:
                _draft = _draft with { HeadshotsOnly = !_draft.HeadshotsOnly };
                return true;
            case 1:
                _draft = _draft with { InfiniteAmmo = !_draft.InfiniteAmmo };
                return true;
            case 2:
                _draft = _draft with { ReloadOnHit = !_draft.ReloadOnHit };
                return true;
            case 3:
                _draft = _draft with { NormalImperialistReload = !_draft.NormalImperialistReload };
                return true;
            case 4:
                _draft = _draft with { FixedSeed = !_draft.FixedSeed };
                return true;
            default: return false;
        }
    }

    public AimTrainerDefinition BuildForLaunch()
    {
        // Retry() creates a new seed only for non-fixed drills, exactly like
        // the existing legacy training workspace.
        return _draft.Sanitize().Retry();
    }

    public string HunterLabel => _hunter.ToString().ToUpperInvariant();
    public string DrillLabel => _draft.Drill.ToString();
    public string WeaponLabel => _draft.Weapon.ToString();
    public string DurationLabel => _draft.DurationSeconds + " SECONDS";
    public string TargetLabel => _draft.TargetCount.ToString();
    public string MovementLabel => _draft.Movement.ToString();
    public string DifficultyLabel => _draft.Difficulty.ToString();
    public string DistanceLabel => _draft.Distance.ToString();
    public string ScopeLabel => _draft.Scope.ToString();
    public bool CanChangeWeapon => _draft.Drill != AimTrainerDrill.ImperialistPrecision;
    public bool CanChangeTargets => _draft.Drill != AimTrainerDrill.TimedFlick;
    public bool CanChangeHeadshots => _draft.Drill != AimTrainerDrill.HeadshotPrecision;
    public bool ToggleOn(int i) => i switch
    {
        0 => _draft.HeadshotsOnly, 1 => _draft.InfiniteAmmo,
        2 => _draft.ReloadOnHit, 3 => !_draft.NormalImperialistReload,
        4 => _draft.FixedSeed, _ => false
    };
    public string ToggleLabel(int i) => i switch
    {
        0 => "HEADSHOTS ONLY",
        1 => "INFINITE AMMO",
        2 => "REFILL AMMO ON HIT",
        3 => "IMPERIALIST: CLICK SKIPS RELOAD",
        4 => "FIXED SEED",
        _ => ""
    } + " // " + (ToggleOn(i) ? "ON" : "OFF");
}
