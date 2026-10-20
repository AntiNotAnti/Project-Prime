using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Existing integration advances Position by Speed / 2 at 60 Hz. Convert
    // horizontal native velocity to units/second only at the pure solver seam.
    internal const float NativeVelocityUnitsPerSecond = FrameTiming.SimulationHz / 2f;

    internal bool ModMomentumEligible => _scene.MomentumMovementActive
        && (IsBot || _scene.DeveloperMomentumInputAllowed) && _scene.MomentumOwnsPlayer(this)
        && LoadFlags.TestFlag(LoadFlags.Active) && _health > 0 && _frozenTimer == 0 && !_field6D0
        && !IsAltForm && !IsMorphing && !IsUnmorphing && !IsPrimeHunter
        && !Flags1.TestAny(PlayerFlags1.UsedJumpPad | PlayerFlags1.Boosting | PlayerFlags1.OnLava | PlayerFlags1.OnAcid)
        && !Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck | PlayerFlags2.SpireClimbing
            | PlayerFlags2.Spectating | PlayerFlags2.AltAttack | PlayerFlags2.GravityOverride)
        && _jumpPadControlLock == 0 && _jumpPadControlLockMin == 0
        && _q3LaunchGravity == 0 && _q3LaunchFrames == 0 && _accelerationTimer == 0
        && _slipperiness == 0 && _standingEntCol == null && _spireLedgeCrestTimer == 0
        && AttachedEnemy == null && (!IsMainPlayer || _scene.CameraSequences.Current == null) && !Controls.Morph.IsPressed
        && ModCharacterGameplay.Variant == CharacterGameplayVariant.Native;

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
        MomentumTuning tuning = practice?.Tuning ?? (quake3 ? Quake3MovementProfileV1.Tuning : MomentumProfileV1.Tuning);
        MomentumContactPhase contact = MomentumProfileV1.Contact(
            Flags1.TestFlag(PlayerFlags1.Standing), Flags1.TestFlag(PlayerFlags1.StandingPrevious),
            Speed.Y, nativeJumpAccepted);
        var input = new MomentumInput(new Vector2(x, y),
            new Vector3(_field70, 0, _field74), new Vector3(-_field78, 0, -_field7C),
            practice?.GroundSpeed ?? (quake3 ? Quake3MovementProfileV1.GroundSpeed : MomentumProfileV1.GroundSpeed),
            MomentumProfileV1.UsesGroundFriction(contact), dt);
        Vector3 before = Speed;
        Vector3 horizontal = new(before.X * NativeVelocityUnitsPerSecond, 0,
            before.Z * NativeVelocityUnitsPerSecond);
        MomentumProfileResult result = practice != null
            ? new MomentumProfileResult(practice.Step(horizontal, input), false, false)
            : quake3 ? Quake3MovementProfileV1.Step(horizontal, input) : MomentumProfileV1.Step(horizontal, input);
        // Y is never multiplied/divided: preserve native jump/gravity bits.
        Speed = new Vector3(result.Velocity.X / NativeVelocityUnitsPerSecond, before.Y,
            result.Velocity.Z / NativeVelocityUnitsPerSecond);
        _scene.NoteDeveloperMomentumStep(new MomentumStepSample(_scene.FrameCount, SlotIndex, before, Speed,
            input, tuning, contact, result.SpeedLimited, result.IncomingOverCap));
        return true;
    }
}
