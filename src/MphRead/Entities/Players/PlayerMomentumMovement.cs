using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Render;
using MphRead.Formats.Collision;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private Vector3 _modRampSupport;
    private ulong _modRampSupportFrame = ulong.MaxValue;

    internal bool ModTraversalActive => _scene.GameState.MovementModel == MovementModel.Traversal
        && ModUsesQuake3Techniques;

    internal bool ModMovementBlastWeaponEligible(BeamType beam) => ModQuake3RocketImpulseEligible
        && (beam == BeamType.Missile || beam == BeamType.Magmaul && ModTraversalActive);

    private float ModRampJumpLift => ModTraversalActive && ModMomentumEligible
        && Flags1.TestFlag(PlayerFlags1.Standing) && _scene.FrameCount > 0
        && _modRampSupportFrame == _scene.FrameCount - 1
        ? TraversalMovementProfileV1.RampLift(Speed, _modRampSupport) : 0;

    private bool ModSurfContact(in CollisionResult result) => ModTraversalActive && ModMomentumBodyEligible
        && _scene.TraversalTrainingSurfaces && result.EntityCollision == null && result.Field0 == 0
        && result.Flags.TestFlag(CollisionFlags.TraversalSurfSurface)
        && result.Plane.Y > .05f && result.Plane.Y < .8f;

    // Existing integration advances Position by Speed / 2 at 60 Hz. Convert
    // horizontal native velocity to units/second only at the pure solver seam.
    internal const float NativeVelocityUnitsPerSecond = FrameTiming.SimulationHz / 2f;

    internal bool ModMomentumEligible => _scene.MomentumMovementActive
        && (IsBot || _scene.DeveloperMomentumInputAllowed) && _scene.MomentumOwnsPlayer(this)
        && ModMomentumBodyEligible;

    private bool ModMomentumBodyEligible => LoadFlags.TestFlag(LoadFlags.Active)
        && _health > 0 && _frozenTimer == 0 && !_field6D0
        && !IsAltForm && !IsMorphing && !IsUnmorphing && !IsPrimeHunter
        && !Flags1.TestAny(PlayerFlags1.UsedJumpPad | PlayerFlags1.Boosting | PlayerFlags1.OnLava | PlayerFlags1.OnAcid)
        && !Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck | PlayerFlags2.SpireClimbing
            | PlayerFlags2.Spectating | PlayerFlags2.AltAttack | PlayerFlags2.GravityOverride)
        && _jumpPadControlLock == 0 && _jumpPadControlLockMin == 0
        && _q3LaunchGravity == 0 && _q3LaunchFrames == 0 && _accelerationTimer == 0
        && _slipperiness == 0 && _standingEntCol == null && _spireLedgeCrestTimer == 0
        && AttachedEnemy == null && (!IsMainPlayer || _scene.CameraSequences.Current == null) && !Controls.Morph.IsPressed
        && ModCharacterGameplay.Variant == CharacterGameplayVariant.Native;

    // A new physical contact is required: Grounded includes a native grace
    // period. The contact edge also prevents re-jumping while an upward body
    // still touches the floor. Held input never spends airborne SpaceJump.
    private bool ModUsesQuake3Techniques => _scene.Quake3TechniquesActive
        && (IsBot || !_scene.DeveloperMomentumActive);

    private bool ModQuake3HeldJumpRequested => ModUsesQuake3Techniques
        && ModMomentumEligible && !ModSliding && Controls.Jump.IsDown && Flags1.TestFlag(PlayerFlags1.Standing)
        && !Flags1.TestFlag(PlayerFlags1.StandingPrevious);

    // Combat runs on authoritative puppet bodies too; do not require local
    // input ownership when deriving the authority's self-splash payment.
    internal bool ModQuake3RocketImpulseEligible => ModUsesQuake3Techniques && ModMomentumBodyEligible;

    private bool ModTryApplyMomentum(bool nativeJumpAccepted)
    {
        if (!ModMomentumEligible) return false;

        float x = Controls.MoveRight.IsDown ? Controls.AnalogScaleX(1)
            : Controls.MoveLeft.IsDown ? -Controls.AnalogScaleX(-1) : 0;
        float y = Controls.MoveUp.IsDown ? Controls.AnalogScaleY(1)
            : Controls.MoveDown.IsDown ? -Controls.AnalogScaleY(-1) : 0;
        if (Hunter is < Hunter.Samus or > Hunter.Weavel) return false;

        float dt = (float)FrameTiming.StepSeconds;
        // The selected draft survives retirement for recording refusal only.
        // An admitted rule must never consume a retired draft. Bots retain
        // their match profile while the local human experiments privately.
        bool developerOwns = !IsBot && _scene.DeveloperMomentumActive;
        MomentumPracticeTuning? practice = developerOwns ? _scene.DeveloperMomentumPracticeTuning : null;
        bool quake3 = !developerOwns && _scene.GameState.MovementModel == MovementModel.Quake3;
        bool traversal = !developerOwns && _scene.GameState.MovementModel == MovementModel.Traversal;
        MomentumTuning tuning = practice?.Tuning ?? (traversal ? ModCrouched
            ? TraversalMovementProfileV3.CrouchTuning(ModSliding) : TraversalMovementProfileV3.Tuning
            : quake3 ? Quake3MovementProfileV3.Tuning : MomentumProfileV1.Tuning);
        MomentumContactPhase contact = MomentumProfileV1.Contact(
            Flags1.TestFlag(PlayerFlags1.Standing), Flags1.TestFlag(PlayerFlags1.StandingPrevious),
            Speed.Y, nativeJumpAccepted);
        var input = new MomentumInput(new Vector2(x, y),
            new Vector3(_field70, 0, _field74), new Vector3(-_field78, 0, -_field7C),
            practice?.GroundSpeed ?? (traversal ? TraversalMovementProfileV1.GroundSpeed : quake3 ? Quake3MovementProfileV3.GroundSpeed : MomentumProfileV1.GroundSpeed),
            MomentumProfileV1.UsesGroundFriction(contact), dt);
        Vector3 before = Speed;
        Vector3 horizontal = new(before.X * NativeVelocityUnitsPerSecond, 0,
            before.Z * NativeVelocityUnitsPerSecond);
        MomentumProfileResult result = practice != null
            ? new MomentumProfileResult(practice.Step(horizontal, input), false, false)
            : traversal ? TraversalMovementProfileV3.Step(horizontal, input, ModCrouched, ModSliding)
            : quake3 ? Quake3MovementProfileV3.Step(horizontal, input) : MomentumProfileV1.Step(horizontal, input);
        // Y is never multiplied/divided: preserve native jump/gravity bits.
        Speed = new Vector3(result.Velocity.X / NativeVelocityUnitsPerSecond, before.Y,
            result.Velocity.Z / NativeVelocityUnitsPerSecond);
        _scene.NoteDeveloperMomentumStep(new MomentumStepSample(_scene.FrameCount, SlotIndex, before, Speed,
            input, tuning, contact, result.SpeedLimited, result.IncomingOverCap, ModCrouched, ModSliding));
        return true;
    }
}
