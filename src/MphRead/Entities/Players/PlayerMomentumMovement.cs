using System;
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

    internal bool ModMomentumEligible => _scene.DeveloperMomentumActive
        && _scene.DeveloperMomentumInputAllowed && ReferenceEquals(_scene.Players.Main, this)
        && SlotIndex == Mods.Network.NetHooks.LocalSlot && !IsBot
        && LoadFlags.TestFlag(LoadFlags.Active) && _health > 0 && _frozenTimer == 0 && !_field6D0
        && !IsAltForm && !IsMorphing && !IsUnmorphing && !IsPrimeHunter
        && !Flags1.TestAny(PlayerFlags1.UsedJumpPad | PlayerFlags1.Boosting | PlayerFlags1.OnLava | PlayerFlags1.OnAcid)
        && !Flags2.TestAny(PlayerFlags2.BipedLock | PlayerFlags2.BipedStuck | PlayerFlags2.SpireClimbing
            | PlayerFlags2.Spectating | PlayerFlags2.AltAttack | PlayerFlags2.GravityOverride)
        && _jumpPadControlLock == 0 && _jumpPadControlLockMin == 0
        && _q3LaunchGravity == 0 && _q3LaunchFrames == 0 && _accelerationTimer == 0
        && _slipperiness == 0 && _standingEntCol == null && _spireLedgeCrestTimer == 0
        && AttachedEnemy == null && _scene.CameraSequences.Current == null && !Controls.Morph.IsPressed
        && ModCharacterGameplay.Variant == CharacterGameplayVariant.Native;

    private bool ModTryApplyMomentum()
    {
        if (!ModMomentumEligible) return false;

        float x = Controls.MoveRight.IsDown ? Controls.AnalogScaleX(1)
            : Controls.MoveLeft.IsDown ? -Controls.AnalogScaleX(-1) : 0;
        float y = Controls.MoveUp.IsDown ? Controls.AnalogScaleY(1)
            : Controls.MoveDown.IsDown ? -Controls.AnalogScaleY(-1) : 0;
        bool strafing = Flags1.TestFlag(PlayerFlags1.Strafing);
        float nativeCap = Fixed.ToFloat(strafing ? Values.StrafeSpeedCap : Values.WalkSpeedCap);
        float traction = Fixed.ToFloat(strafing ? Values.StrafeBipedTraction : Values.WalkBipedTraction);
        if (nativeCap <= 0) return false;

        float dt = (float)FrameTiming.StepSeconds;
        float desiredSpeed = nativeCap * NativeVelocityUnitsPerSecond;
        // Developer profile v0 derives its scale from the native Hunter tables.
        // PR-03 calibrates/finalizes the competitive profile; no Quake world-unit
        // tuning set is imported here. Stop-speed floor is deliberately zero.
        float acceleration = traction * FrameTiming.SimulationHz / nativeCap;
        var tuning = new MomentumTuning(acceleration,
            (1 - Math.Clamp(Fixed.ToFloat(Values.StandSpeedFactor), 0, 1)) * NativeVelocityUnitsPerSecond,
            0, acceleration,
            desiredSpeed * (1 - Math.Clamp(Fixed.ToFloat(Values.AirSpeedFactor), 0, 1)));
        var input = new MomentumInput(new Vector2(x, y),
            new Vector3(_field70, 0, _field74), new Vector3(-_field78, 0, -_field7C),
            desiredSpeed, Flags1.TestFlag(PlayerFlags1.Standing) && Speed.Y <= 0, dt);
        Vector3 before = Speed;
        Vector3 result = MomentumMath.Step(new Vector3(before.X * NativeVelocityUnitsPerSecond, 0,
            before.Z * NativeVelocityUnitsPerSecond), input, tuning);
        // Y is never multiplied/divided: preserve native jump/gravity bits.
        Speed = new Vector3(result.X / NativeVelocityUnitsPerSecond, before.Y,
            result.Z / NativeVelocityUnitsPerSecond);
        _scene.NoteDeveloperMomentumStep(new MomentumStepSample(_scene.FrameCount, SlotIndex, before, Speed, input, tuning));
        return true;
    }
}
