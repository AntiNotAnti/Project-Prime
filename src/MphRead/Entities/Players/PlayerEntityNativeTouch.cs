using System;
using MphRead.Formats;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private readonly NativeTouchSample _nativeTouch = new();
        private readonly MorphBallBoostStateMachine _nativeBoost = new();
        private bool _nativeTouchInAlt;
        private bool _nativeRemoteClassic;
        private ushort _nativeRemoteGeneration, _nativeRemoteLife;

        private bool ModIsLocalTouchOwner => IsMainPlayer
            && (!_scene.Services.PlayerReplication.Active
                || _scene.Services.PlayerReplication.LocalSlot == SlotIndex);

        internal bool ModUsesClassicTouch => Hunter == Hunter.Samus && !IsBot
            && (ModIsLocalTouchOwner ? Mods.InputSettings.ClassicMorphBallTouch : _nativeRemoteClassic);

        internal NativeTouchReport ModNativeTouchReport()
            => ModUsesClassicTouch && ModIsLocalTouchOwner ? _nativeTouch.Current : default;

        internal void ModReceiveNativeTouch(in NativeTouchReport report, ushort generation, ushort life)
        {
            if (IsMainPlayer && (!_scene.Services.PlayerReplication.Active
                || _scene.Services.PlayerReplication.LocalSlot == SlotIndex)) return;
            if (_nativeRemoteGeneration != generation || _nativeRemoteLife != life)
            {
                _nativeBoost.Reset();
                _nativeTouch.Suspend();
                _nativeRemoteGeneration = generation;
                _nativeRemoteLife = life;
            }
            _nativeRemoteClassic = Hunter == Hunter.Samus && report.Active;
            _nativeTouch.ApplyRemote(_nativeRemoteClassic ? report : default, generation, life);
            _nativeTouchInAlt = _nativeRemoteClassic;
        }

        private void ModStopNativeTouch()
        {
            if (!_nativeTouchInAlt) return;
            _nativeTouch.Suspend();
            _nativeBoost.Reset();
            _nativeTouchInAlt = false;
            _nativeRemoteClassic = false;
        }

        private void ModStepNativeTouch()
        {
            if (!ModUsesClassicTouch || !IsAltForm || IsMorphing || IsUnmorphing
                || _health <= 0 || _frozenTimer != 0
                || Flags1.TestFlag(PlayerFlags1.NoAimInput)
                || Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen))
            {
                if (ModIsLocalTouchOwner) ModStopNativeTouch();
                else _nativeTouch.TakeRollShare();
                return;
            }

            _nativeTouchInAlt = true;
            if (!ModIsLocalTouchOwner) return; // Remote sample was admitted by PlayerReplicationBridge.
            if (OperatingSystem.IsAndroid())
            {
                _nativeTouch.StepAbsolute(NativeTouchHost.Down, NativeTouchHost.X, NativeTouchHost.Y);
            }
            else if (PointerDevice.Active)
            {
                PointerSample pointer = PointerDevice.Current;
                bool down = pointer.InContact
                    && (!StylusZone.Enabled || StylusZone.Aiming)
                    && !StylusZone.Placing;
                float x = pointer.X / Math.Max(1, PointerDevice.SurfaceWidth);
                float y = pointer.Y / Math.Max(1, PointerDevice.SurfaceHeight);
                float regionX = StylusZone.Enabled ? StylusZone.Left : 0f;
                float regionY = StylusZone.Enabled ? StylusZone.Top : 0f;
                float regionW = StylusZone.Enabled ? StylusZone.Width : 1f;
                float regionH = StylusZone.Enabled ? StylusZone.Height : 1f;
                int dsX = (int)MathF.Round(Math.Clamp((x - regionX) / Math.Max(regionW, .001f), 0, 1) * 255);
                int dsY = (int)MathF.Round(Math.Clamp((y - regionY) / Math.Max(regionH, .001f), 0, 1) * 191);
                _nativeTouch.StepAbsolute(down, dsX, dsY);
            }
            else
            {
                _nativeTouch.StepRelative(Controls.MouseAim, Input.MouseDeltaX, Input.MouseDeltaY,
                    Mods.InputSettings.AltSwipeSensitivity);
            }
            // A native drag immediately frees a stale camera direction lock.
            if (_nativeTouch.Current.Delta4X != 0 || _nativeTouch.Current.Delta4Y != 0)
                Flags1 &= ~PlayerFlags1.AltDirOverride;
            _boostAimLock = 0; // ModernFlick's steering lock is not native touch behavior.
        }

        private void ModApplyNativeTouchRoll(ref Vector3 speedDelta)
        {
            float share = _nativeTouch.TakeRollShare();
            NativeTouchReport touch = _nativeTouch.Current;
            if (!ModUsesClassicTouch || !touch.Down || share <= 0) return;
            float factor = _jumpPadControlLockMin > 0
                ? Fixed.ToFloat(Values.JumpPadSlideFactor) : 1f;
            (float x, float z) = MorphBallTouchRules.Roll(touch.Delta4X, touch.Delta4Y,
                share, factor, _altRollFbX, _altRollFbZ, _altRollLrX, _altRollLrZ);
            speedDelta.X += x;
            speedDelta.Z += z;
        }

        private void ModProcessNativeTouchBoost(ref Vector3 speedDelta, bool shoulderHeld)
        {
            NativeTouchReport touch = _nativeTouch.Current;
            var state = new MorphBallBoostStateMachine.State
            {
                Boosting = Flags1.TestFlag(PlayerFlags1.Boosting),
                CanTouchBoost = Flags1.TestFlag(PlayerFlags1.CanTouchBoost),
                Charge = _boostCharge
            };
            ushort maximum = (ushort)(Values.BoostChargeMax * 2);
            var result = _nativeBoost.Advance(ref state, touch, shoulderHeld,
                (ushort)(Values.BoostChargeMin * 2), maximum, Features.FullBoostCharge);
            _boostCharge = state.Charge;
            if (state.Boosting) Flags1 |= PlayerFlags1.Boosting;
            else Flags1 &= ~PlayerFlags1.Boosting;
            if (state.CanTouchBoost) Flags1 |= PlayerFlags1.CanTouchBoost;
            else Flags1 &= ~PlayerFlags1.CanTouchBoost;
            if (result.Kind == MorphBallBoostStateMachine.Fired.None) return;

            bool touchBoost = result.Kind == MorphBallBoostStateMachine.Fired.Touch;
            float speed = touchBoost ? Fixed.ToFloat(Values.BoostSpeedMax)
                : Fixed.ToFloat(Values.BoostSpeedMin)
                    + result.Spent * (Fixed.ToFloat(Values.BoostSpeedMax) - Fixed.ToFloat(Values.BoostSpeedMin))
                    / Math.Max(1, (int)maximum);
            float cap = Fixed.ToFloat(Values.BoostSpeedCap)
                * (touchBoost ? 1f : result.Spent / (float)Math.Max(1, (int)maximum));
            _hSpeedCap = MathF.Max(_hSpeedCap, cap);
            float dirX = _field70, dirZ = _field74;
            if (touchBoost)
            {
                (dirX, dirZ) = MorphBallTouchRules.Boost(touch.Delta4X, touch.Delta4Y,
                    1f, _altRollFbX, _altRollFbZ, _altRollLrX, _altRollLrZ);
            }
            speedDelta.X += dirX * speed;
            speedDelta.Z += dirZ * speed;
            _altAttackCooldown = (ushort)(Values.AltAttackCooldown * 2);
            _boostDamage = touchBoost ? (ushort)Values.AltAttackDamage
                : (ushort)(Values.AltAttackDamage * result.Spent / Math.Max(1, (int)maximum));
            _soundSource.PlaySfx(Metadata.HunterSfx[(int)Hunter, (int)HunterSfx.Boost]);
            ModControllerFeedback(Mods.Input.GamepadFeedback.Boost);
            if (IsMainPlayer)
                _boostInst.SetAnimation(start: 0, target: 10, frames: 11, afterAnim: 0);
            if (_boostEffect != null)
            {
                _scene.UnlinkEffectEntry(_boostEffect);
                _boostEffect = null;
            }
            Vector3 side = touchBoost ? new Vector3(dirZ, 0, -dirX) : _gunVec2;
            Vector3 forward = touchBoost ? new Vector3(dirX, 0, dirZ) : _facingVector;
            _boostEffect = _scene.SpawnEffectGetEntry(136, side, forward, Position);
            if (_boostEffect != null) _boostEffect.SetElementExtension(true);
        }
    }
}
