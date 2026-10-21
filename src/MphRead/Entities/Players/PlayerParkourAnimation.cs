using System;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private byte _modMantleVisualPhase;
    private ulong _modMantleVisualReceived;
    private uint _modMantleVisualSource;
    private bool _modMantleVisualHasSource;

    private bool ModParkourAnimationBodyAllowed => _scene.GameState.MovementModel == MovementModel.Parkour
        // Rendering must not invoke the retiring gameplay getter for a private rig.
        && _modCharacterGameplay.Variant == CharacterGameplayVariant.Native
        && ModMomentumBodyEligible && !ModCrouched && !IsAltForm && !IsMorphing && !IsUnmorphing
        && LoadFlags.TestFlag(LoadFlags.Spawned) && Hunter is >= Hunter.Samus and <= Hunter.Weavel;

    internal byte ModLocalMantleVisualPhase
    {
        get
        {
            if (!_modMantling || !ModParkourAnimationBodyAllowed || !_scene.MomentumOwnsPlayer(this)) return 0;
            float progress;
            if (_modMantleStage == 0)
            {
                float rise = _modMantleRaised.Y - _modMantleStart.Y;
                progress = .04f + .36f * Math.Clamp((Position.Y - _modMantleStart.Y) / Math.Max(.001f, rise), 0, 1);
            }
            else if (_modMantleStage == 1)
            {
                float travel = (_modMantleTarget - _modMantleStart).Xz.Length;
                progress = .4f + .44f * Math.Clamp((Position - _modMantleStart).Xz.Length / Math.Max(.001f, travel), 0, 1);
            }
            else progress = .92f;
            return ParkourAnimationPose.EncodeMantle(progress);
        }
    }
    internal byte ModPublishedMantleVisualPhase => _scene.MomentumOwnsPlayer(this)
        ? ModLocalMantleVisualPhase
        : ModParkourAnimationBodyAllowed && (_scene.Services.IsReplica
            || _scene.FrameCount >= _modMantleVisualReceived && _scene.FrameCount - _modMantleVisualReceived <= 30)
            ? _modMantleVisualPhase : (byte)0;

    private void ModResetParkourAnimation()
    { _modMantleVisualPhase = 0; _modMantleVisualReceived = 0; _modMantleVisualHasSource = false; }

    // This visual hint is consumed only after the same accepted pose/control
    // seam as shot proof. It never enables the mantle controller or moves a body.
    internal void ModObserveMantleVisual(in IntentPacket intent)
    {
        if (!NetSession.Active || !NetSession.IsAuthority || _scene.Services.IsReplica
            || !NetSession.MatchesStream(intent.MatchId, intent.AuthorityEpoch)
            || !NetPlayerLifecycle.Matches(SlotIndex, intent.SlotGeneration, intent.LifeId)
            || _modMantleVisualHasSource && !NetLifecycleTracker.Newer(intent.Frame, _modMantleVisualSource)) return;
        _modMantleVisualHasSource = true; _modMantleVisualSource = intent.Frame;
        _modMantleVisualReceived = _scene.FrameCount;
        _modMantleVisualPhase = ModParkourAnimationBodyAllowed && !ModGrappleState.Active
            && intent.MantleVisualPhase <= 15 ? intent.MantleVisualPhase : (byte)0;
    }
    internal void ModApplyReplicatedMantleVisual(byte phase, uint sourceFrame, bool local = false)
    {
        if (local && !_scene.Services.IsReplica) return;
        if (_modMantleVisualHasSource && !NetLifecycleTracker.Newer(sourceFrame, _modMantleVisualSource)) return;
        _modMantleVisualHasSource = true; _modMantleVisualSource = sourceFrame;
        _modMantleVisualPhase = phase <= 15 ? phase : (byte)0;
        _modMantleVisualReceived = _scene.FrameCount;
    }
    internal void ModRestoreMantleVisual(byte phase)
    { ModResetParkourAnimation(); ModApplyReplicatedMantleVisual(phase, 0); }

    internal ParkourAnimationPose ModGetParkourAnimationPose()
    {
        if (ParkourPoseOverlay.SuppressForCheck || !ModParkourAnimationBodyAllowed) return default;
        float mantle = ParkourAnimationPose.DecodeMantle(ModPublishedMantleVisualPhase);
        bool grapple = _modGrapple.Active && _modGrapple.IsValid
            && (_scene.Services.IsReplica || !_scene.MomentumOwnsPlayer(this) || ModGrappleOwnsMovement);
        Vector3 rope = grapple ? _modGrapple.Anchor - Position.AddY(ModBipedEyeOffset) : Vector3.Zero;
        double alpha = FrameTiming.Active && FrameTiming.HighRefreshPresentation ? FrameTiming.PresentationAlpha : 0;
        return new(Math.Max(0, mantle), grapple ? 1 : 0, rope, (_scene.FrameCount + alpha) / 60d);
    }
}
