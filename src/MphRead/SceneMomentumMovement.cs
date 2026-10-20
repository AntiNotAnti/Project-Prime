using System;
using MphRead.Entities;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead;

// Bounded developer evidence; never serialized or consulted by gameplay.
internal readonly record struct MomentumStepSample(ulong SimulationFrame, int Slot,
    Vector3 NativeVelocityBefore, Vector3 NativeVelocityAfter,
    MomentumInput Input, MomentumTuning Tuning, MomentumContactPhase ContactPhase,
    bool SpeedLimited, bool IncomingOverCap);

public partial class Scene
{
    private bool _developerMomentumRequested, _developerMomentumRetired;
    private bool _developerMomentumDiagnostics, _developerMomentumInputBlocked = true;
    private MomentumPracticeTuning? _developerMomentumPracticeTuning;
    private PlayerEntity? _developerMomentumPracticeOwner;
    private int _developerMomentumPracticeSlot = -1;
    // Retain the selection after retirement: this scene cannot produce a v1-labelled recording.
    internal MomentumPracticeTuning? DeveloperMomentumPracticeTuning => _developerMomentumPracticeTuning;
    internal long DeveloperMomentumSteps { get; private set; }
    internal MomentumStepSample? LastMomentumStep { get; private set; }

    /// <summary>
    /// Private offline experiment, selected once after room setup and before the
    /// first simulation tick. No preference, lobby, server or replay binds it.
    /// </summary>
    internal bool TryEnableDeveloperMomentum(bool captureDiagnostics = false, MomentumPracticeTuning? practiceTuning = null)
    {
        if (_developerMomentumRequested || FrameCount != 0 || !_roomLoaded
            || !DeveloperMomentumSceneEligible()
            || Players.Main == null
            || Players.Main.ModCharacterGameplay.Variant != CharacterGameplayVariant.Native)
            return false;
        if (practiceTuning != null && (ReplayCapture.LiveRecordingActive
            || Players.Main.IsBot || !MomentumOwnsPlayer(Players.Main))) return false;
        practiceTuning?.Validate();
        _developerMomentumPracticeTuning = practiceTuning;
        if (practiceTuning != null)
        {
            _developerMomentumPracticeOwner = Players.Main;
            _developerMomentumPracticeSlot = Players.Main.SlotIndex;
        }
        _developerMomentumRequested = true;
        _developerMomentumDiagnostics = captureDiagnostics;
        return true;
    }

    internal bool DeveloperMomentumActive
    {
        get
        {
            if (!_developerMomentumRequested || _developerMomentumRetired) return false;
            if (DeveloperMomentumSceneEligible()) return true;
            // A scene that lost its offline owner cannot silently re-enable the
            // experiment later after a disconnect or foreground switch.
            _developerMomentumRetired = true;
            return false;
        }
    }

    // The scene owns the selected rule before entities spawn. Online preference
    // files never select this path: the admitted server definition must agree.
    internal bool MomentumMovementActive => DeveloperMomentumActive
        || MovementModelRules.UsesMomentumSolver(GameState.MovementModel)
        && MomentumSceneEligible()
        && (!NetSession.Active ? !Services.PlayerReplication.Active
            : NetRoomChange.GameplayReady && (NetSession.ActiveMatchDefinition?.MovementModel
                ?? MovementModelRules.FromRules((MatchModifierFlags)(NetSession.ServerMatch?.RuleBits ?? 0)))
                == GameState.MovementModel);

    private bool MomentumSceneEligible() =>
        ForegroundLease.ThreadId == Environment.CurrentManagedThreadId
        && ReferenceEquals(global::MphRead.GameState.Current, GameState)
        && ReferenceEquals(PlayerEntity.LegacyRegistry, Players)
        && !Exiting && !_cleanupComplete && !_visualReleaseComplete
        && !SideScene && !Services.IsReplica && !IsReplayLab && AimTrainer == null
        && GameState.Multiplayer && !GameState.BalancedMode && !GameState.EnhancedHunters
        && !DemoPlayback.IsActive;

    internal bool MomentumOwnsPlayer(PlayerEntity player) => !NetHooks.IsPuppet(player)
        && (player.IsBot
            ? MovementModelRules.UsesMomentumSolver(GameState.MovementModel) && (!NetSession.Active || NetSession.IsAuthority)
            : ReferenceEquals(Players.Main, player) && player.SlotIndex == NetHooks.LocalSlot);

    private bool DeveloperMomentumSceneEligible() =>
        ForegroundLease.ThreadId == Environment.CurrentManagedThreadId
        && ReferenceEquals(global::MphRead.GameState.Current, GameState)
        && ReferenceEquals(PlayerEntity.LegacyRegistry, Players)
        && !Exiting && !_cleanupComplete && !_visualReleaseComplete
        && !SideScene && !Services.IsReplica && !IsReplayLab && AimTrainer == null
        && GameState.Multiplayer && !GameState.BalancedMode && !GameState.EnhancedHunters
        && !NetSession.Active && !Services.PlayerReplication.Active && !DemoPlayback.IsActive
        && (_developerMomentumPracticeTuning == null || !ReplayCapture.LiveRecordingActive
            && ReferenceEquals(Players.Main, _developerMomentumPracticeOwner)
            && _developerMomentumPracticeOwner != null
            && _developerMomentumPracticeOwner.SlotIndex == _developerMomentumPracticeSlot
            && _developerMomentumPracticeSlot == NetHooks.LocalSlot
            && !_developerMomentumPracticeOwner.IsBot
            && !NetHooks.IsPuppet(_developerMomentumPracticeOwner)
            && _developerMomentumPracticeOwner.ModCharacterGameplay.Variant == CharacterGameplayVariant.Native);

    internal bool DeveloperMomentumInputAllowed => !_developerMomentumInputBlocked;

    // Reuse the existing input suppression decision rather than polling devices
    // or maintaining a second set of UI/focus conditions in the movement code.
    private void ModNoteMomentumInputAvailability(bool blocked)
    {
        if (!_developerMomentumRequested && !MovementModelRules.UsesMomentumSolver(GameState.MovementModel)) return;
        _developerMomentumInputBlocked = blocked;
        _ = MomentumMovementActive;
    }

    internal void NoteDeveloperMomentumStep(in MomentumStepSample sample)
    {
        if (!_developerMomentumDiagnostics) return;
        if (DeveloperMomentumSteps < long.MaxValue) DeveloperMomentumSteps++;
        LastMomentumStep = sample;
    }
}
