using System;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private readonly MorphBallBoostStateMachine.SampleLatch _morphTouchBoostSample = new();

        private void ApplyNativeMorphTouchRoll(ref Vector3 speedDelta)
        {
            if (Hunter != Hunter.Samus || !IsAltForm || IsMorphing || IsUnmorphing
                || Flags1.TestFlag(PlayerFlags1.NoAimInput))
            {
                return;
            }

            MorphTouchReport touch = Input.MorphTouch.Report();
            float share = Input.MorphTouch.TakeRollShare();
            if (!touch.Down || share <= 0) return;

            float scale = MorphBallTouchRules.TouchRollPerDsPixel;
            if (_jumpPadControlLockMin > 0)
                scale *= Fixed.ToFloat(Values.JumpPadSlideFactor);

            Vector2 roll = MorphBallTouchRules.TouchRoll(
                touch.Delta4X, touch.Delta4Y, scale,
                _altRollFbX, _altRollFbZ, _altRollLrX, _altRollLrZ, share);
            speedDelta.X += roll.X;
            speedDelta.Z += roll.Y;
            if (touch.Delta4X != 0 || touch.Delta4Y != 0)
                Input.HasInput = true;
        }

        private void ProcessNativeMorphBallBoost(ref Vector3 speedDelta)
        {
            if (Hunter != Hunter.Samus) return;

            MorphTouchReport touch = Input.MorphTouch.Report();
            bool canTouchBoost = Flags1.TestFlag(PlayerFlags1.CanTouchBoost);
            ushort min = (ushort)(Values.BoostChargeMin * 2);
            ushort max = (ushort)(Values.BoostChargeMax * 2);
            bool shoulderHeld = Controls.Boost.IsDown || Controls.Zoom.IsDown;
            var result = MorphBallBoostStateMachine.Advance(
                _morphTouchBoostSample, Input.MorphTouch.Identity,
                Flags1.TestFlag(PlayerFlags1.Boosting), ref canTouchBoost,
                touch, shoulderHeld, ref _boostCharge, min, max);

            if (canTouchBoost) Flags1 |= PlayerFlags1.CanTouchBoost;
            else Flags1 &= ~PlayerFlags1.CanTouchBoost;

            if (result.TouchFired)
            {
                Vector2 impulse = MorphBallTouchRules.TouchBoostImpulse(
                    touch.Delta4X, touch.Delta4Y,
                    Fixed.ToFloat(Values.BoostSpeedMax),
                    CameraInfo.Field48, CameraInfo.Field4C,
                    CameraInfo.Field50, CameraInfo.Field54);
                float cap = Fixed.ToFloat(Values.BoostSpeedCap);
                if (_hSpeedCap < cap) _hSpeedCap = cap;
                speedDelta.X += impulse.X;
                speedDelta.Z += impulse.Y;
                _boostDamage = Values.AltAttackDamage;
                BeginMorphBoostPresentation();
                return;
            }

            ushort spent = result.ShoulderCharge;
            if (spent == 0) return;
            if (Features.FullBoostCharge) spent = max;

            float boostCap = Fixed.ToFloat(Values.BoostSpeedCap) * spent / Math.Max(1f, max);
            if (_hSpeedCap < boostCap) _hSpeedCap = boostCap;
            float factor = Fixed.ToFloat(Values.BoostSpeedMin)
                + spent * (Fixed.ToFloat(Values.BoostSpeedMax)
                    - Fixed.ToFloat(Values.BoostSpeedMin)) / Math.Max(1f, max);
            speedDelta.X += _field70 * factor;
            speedDelta.Z += _field74 * factor;
            _boostDamage = (ushort)(Values.AltAttackDamage * spent / Math.Max(1, max));
            BeginMorphBoostPresentation();
        }

        private void BeginMorphBoostPresentation()
        {
            int sfx = Metadata.HunterSfx[(int)Hunter, (int)HunterSfx.Boost];
            _soundSource.PlaySfx(sfx);
            _altAttackCooldown = (ushort)(Values.AltAttackCooldown * 2);
            Flags1 |= PlayerFlags1.Boosting;
            ModControllerFeedback(Mods.Input.GamepadFeedback.Boost);
            if (IsMainPlayer)
                _boostInst.SetAnimation(start: 0, target: 10, frames: 11, afterAnim: 0);
            if (_boostEffect != null)
            {
                _scene.UnlinkEffectEntry(_boostEffect);
                _boostEffect = null;
            }
            _boostEffect = _scene.SpawnEffectGetEntry(
                136, _gunVec2, _facingVector, Position);
            _boostEffect?.SetElementExtension(true);
        }

        internal MorphTouchReport ModMorphTouchReport()
            => Input.MorphTouch.Report();

        internal void ModApplyReportedMorphTouch(MorphTouchReport report, uint intentFrame)
            => Input.MorphTouch.ApplyReported(report, intentFrame);

        private void ResetNativeMorphTouch()
        {
            Input.MorphTouch.Suspend();
            _morphTouchBoostSample.Reset();
        }
    }
}
