using System;
using MphRead.Mods.Input;
using MphRead.Mods.Input.AimAssist;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>
    /// Controller-only assistance for the Noxus affinity-Judicator shadow-freeze technique.
    ///
    /// This never aims at an enemy. It only helps the player reach and hold the steep
    /// downward pitch that exploits the cartridge ice-wave geometry. Target positions are
    /// consulted only after aiming, for the optional visible-target haptic cue.
    /// </summary>
    public partial class PlayerEntity
    {
        private bool _shadowFreezeAssistActive;
        private bool _shadowFreezeAngleLatched;
        private bool _shadowFreezeTargetLatched;
        private int _shadowFreezeReleaseFrames;
        private float _shadowFreezePreviousCameraY;
        private float _shadowFreezeAdaptiveStrength = 1f;
        private float _shadowFreezeReleaseErrorEma;
        private int _shadowFreezeRetainedTarget = -1;
        private int _shadowFreezeCueTarget = -1;
        private long _shadowFreezeRetainedUntil;

        internal bool ModShadowFreezeAngleReady { get; private set; }
        internal bool ModShadowFreezeTargetReady { get; private set; }

        private bool ModShadowFreezeWeapon(out WeaponInfo weapon, out float charge)
        {
            weapon = EquipInfo.Weapon;
            charge = 0;
            if (CurrentWeapon != BeamType.Judicator
                || weapon.Beam != BeamType.Judicator
                || !weapon.Flags.TestFlag(WeaponFlags.AoeCharged)
                || !_scene.GameState.ShadowFreeze)
            {
                return false;
            }

            int fullCharge = Math.Max(1, weapon.FullCharge * 2);
            charge = Math.Clamp(EquipInfo.ChargeLevel / (float)fullCharge, 0f, 1f);
            return true;
        }

        /// <summary>
        /// Shape the camera input for an intentional shadow-freeze attempt.
        /// Returns true only while the dedicated technique assist owns the pitch.
        /// Yaw is never steered toward a target and deliberate horizontal input is preserved.
        /// </summary>
        private bool ModApplyShadowFreezeControllerAssist(float x, float y,
            out float assistedX, out float assistedY)
        {
            assistedX = x;
            assistedY = y;

            bool controllerOwned = GamepadInput.FrameSnapshot.State.Connected
                && GamepadContexts.Focused && !GamepadContexts.MenuVisible
                && GamepadContexts.Current == GamepadContext.Gameplay
                && !GamepadInput.WheelHeld
                && InputSourceTracker.Current == InputSource.Gamepad;
            if (!controllerOwned || IsAltForm || AimAssistDebug.UnassistedArm
                || !ModShadowFreezeWeapon(out _, out float charge))
            {
                ModResetShadowFreezeControllerAssist();
                _shadowFreezePreviousCameraY = y;
                return false;
            }

            bool released = Controls.Shoot.IsReleased;
            bool holding = Controls.Shoot.IsDown;
            bool fullCharge = ModChargeReady;
            bool postRelease = _shadowFreezeReleaseFrames > 0;
            bool shotState = holding || released || postRelease;
            bool downwardIntent = y < -0.08f
                || ShadowFreezeAssistMath.InCaptureRange(_aimY);

            bool wasActive = _shadowFreezeAssistActive;
            if (!_shadowFreezeAssistActive && shotState && charge >= .40f && downwardIntent
                && (_aimY <= ShadowFreezeAssistMath.ExitPitch || y <= -.60f))
            {
                _shadowFreezeAssistActive = true;
                AimAssistTelemetry.ShadowFreezeAttempt();
            }
            else if (_shadowFreezeAssistActive && !postRelease
                && (!shotState || charge < .20f
                    || (_aimY > ShadowFreezeAssistMath.ExitPitch && y > .30f)))
            {
                _shadowFreezeAssistActive = false;
            }

            if (!_shadowFreezeAssistActive && !postRelease)
            {
                _shadowFreezePreviousCameraY = y;
                return false;
            }

            float chargeStrength = ShadowFreezeAssistMath.Smooth(.40f, 1f, charge);
            if (postRelease)
            {
                chargeStrength = Math.Max(chargeStrength,
                    _shadowFreezeReleaseFrames / (float)ShadowFreezeAssistMath.ReleaseBlendFrames);
            }

            // Hard downward pushes on an analogue stick often leak a little X. Remove only
            // the tiny accidental component; deliberate yaw remains 1:1. This is not a
            // steering mode and never remaps the axes.
            var physical = GamepadInput.AimStick;
            if (MathF.Abs(physical.Y) >= .65f && MathF.Abs(physical.X) < .16f)
            {
                float leakage = Math.Clamp(MathF.Abs(physical.X) / .16f, 0f, 1f);
                assistedX *= .18f + .82f * leakage;
            }

            float currentMultiplier = ShadowFreezeAssistMath.RangeMultiplier(_aimY);
            float precision = ShadowFreezeAssistMath.Smooth(
                ShadowFreezeAssistMath.CaptureRangeMultiplier,
                ShadowFreezeAssistMath.ReadyRangeMultiplier,
                currentMultiplier);

            // Preserve fast travel into the technique, then progressively slow the final
            // downward degrees so the outer-stick acceleration cannot throw the player
            // straight through the useful window.
            if (assistedY < 0)
            {
                float slow = .50f * precision * chargeStrength;
                assistedY *= 1f - slow;
            }

            bool downwardFlick = y <= -1.25f && _shadowFreezePreviousCameraY > -.35f;
            float projectedPitch = Math.Clamp(_aimY + assistedY, -85f, 85f);
            float error = ShadowFreezeAssistMath.TargetPitch - projectedPitch;
            float gain = fullCharge ? .34f : .14f;
            float correctionCap = downwardFlick ? 2.60f : fullCharge ? .95f : .45f;
            correctionCap *= Math.Max(.35f, chargeStrength) * _shadowFreezeAdaptiveStrength;
            float correction = Math.Clamp(error * gain, -correctionCap, correctionCap);
            assistedY += correction;

            bool readyNow = fullCharge && ShadowFreezeAssistMath.AngleReady(_aimY);
            if (readyNow)
            {
                // Hysteresis/retention: tiny stick noise should not knock the pitch out of
                // the window, but a deliberate upward move exits immediately.
                bool deliberateExit = y > 1.05f;
                if (!deliberateExit && assistedY > 0)
                {
                    assistedY *= .20f;
                }

                // Brake before crossing the chosen sweet spot. The engine still owns the
                // absolute -85 degree pitch clamp.
                float toTarget = ShadowFreezeAssistMath.TargetPitch - _aimY;
                if (assistedY < toTarget)
                {
                    assistedY = toTarget;
                }
            }

            if (released && fullCharge && (wasActive || _shadowFreezeAssistActive))
            {
                // Rescue a release that is already very close, then preserve that pose
                // briefly so the trigger edge cannot be accompanied by a one-frame stick
                // wobble. This is bounded and cannot pull a distant aim into the technique.
                float releaseProjected = Math.Clamp(_aimY + assistedY, -85f, 85f);
                float releaseError = ShadowFreezeAssistMath.TargetPitch - releaseProjected;
                if (MathF.Abs(releaseError) <= 6f)
                {
                    assistedY += Math.Clamp(releaseError, -1.50f, 1.50f);
                }

                releaseProjected = Math.Clamp(_aimY + assistedY, -85f, 85f);
                float underReach = Math.Max(0f, releaseProjected - ShadowFreezeAssistMath.TargetPitch);
                _shadowFreezeReleaseErrorEma = _shadowFreezeReleaseErrorEma == 0
                    ? underReach
                    : _shadowFreezeReleaseErrorEma * .82f + underReach * .18f;
                // Session-local automatic tuning only changes assist strength, never the
                // target angle. It is intentionally small and bounded.
                _shadowFreezeAdaptiveStrength = Math.Clamp(
                    .95f + _shadowFreezeReleaseErrorEma * .035f, .95f, 1.20f);
                AimAssistTelemetry.ShadowFreezeRelease(
                    MathF.Abs(ShadowFreezeAssistMath.TargetPitch - releaseProjected),
                    _shadowFreezeAdaptiveStrength);
                _shadowFreezeReleaseFrames = ShadowFreezeAssistMath.ReleaseBlendFrames;
            }

            if (_shadowFreezeReleaseFrames > 0)
            {
                float blend = _shadowFreezeReleaseFrames
                    / (float)ShadowFreezeAssistMath.ReleaseBlendFrames;
                float projected = Math.Clamp(_aimY + assistedY, -85f, 85f);
                float releaseError = ShadowFreezeAssistMath.TargetPitch - projected;
                float releaseCorrection = Math.Clamp(releaseError * .22f * blend,
                    -.80f * blend, .80f * blend);
                assistedY += releaseCorrection;
                _shadowFreezeReleaseFrames--;
                if (_shadowFreezeReleaseFrames == 0 && !Controls.Shoot.IsDown)
                {
                    _shadowFreezeAssistActive = false;
                }
            }

            float brake = Math.Max(.35f * chargeStrength, precision * (fullCharge ? 1f : .55f));
            if (downwardFlick) brake = Math.Max(brake, .82f);
            if (_shadowFreezeReleaseFrames > 0) brake = Math.Max(brake, .90f);
            GamepadInput.SetAimPrecisionContext(
                Math.Clamp(.35f + precision * .55f, 0f, 1f),
                Math.Clamp(brake, 0f, 1f));

            _shadowFreezePreviousCameraY = y;
            return true;
        }

        /// <summary>
        /// Update player-facing readiness feedback after the camera has consumed this
        /// frame's controller turn. The collision query is the real Judicator ice-wave
        /// geometry. Visibility gates acquisition so vibration cannot reveal a hidden player.
        /// </summary>
        private void ModUpdateShadowFreezeFeedback()
        {
            if (!ModShadowFreezeWeapon(out WeaponInfo weapon, out _)
                || !ModChargeReady || !_shadowFreezeAssistActive)
            {
                ModClearShadowFreezeFeedback();
                return;
            }

            bool angleReady = ShadowFreezeAssistMath.AngleReady(_aimY);
            ModShadowFreezeAngleReady = angleReady;
            if (angleReady && !_shadowFreezeAngleLatched)
            {
                GamepadHaptics.Play(GamepadFeedback.ShadowFreezeReady);
                AimAssistTelemetry.ShadowFreezeAngleReady();
            }
            _shadowFreezeAngleLatched = angleReady;

            if (!ShadowFreezeAssistMath.InCaptureRange(_aimY))
            {
                ModShadowFreezeTargetReady = false;
                _shadowFreezeTargetLatched = false;
                _shadowFreezeCueTarget = -1;
                return;
            }

            Vector3 direction = _aimPosition - _muzzlePos;
            if (!Single.IsFinite(direction.X) || !Single.IsFinite(direction.Y)
                || !Single.IsFinite(direction.Z) || direction.LengthSquared < .000001f)
            {
                ModShadowFreezeTargetReady = false;
                _shadowFreezeTargetLatched = false;
                _shadowFreezeCueTarget = -1;
                return;
            }
            direction = direction.Normalized();
            float maxDistance = weapon.ChargedDistance / 4096f;
            long now = Environment.TickCount64;
            bool visibleHit = false;
            bool retainedHit = false;
            int visibleSlot = -1;

            foreach (PlayerEntity target in _scene.GetPlayerEntities())
            {
                if (target == this || target.Health == 0
                    || !target.LoadFlags.TestFlag(LoadFlags.Active)
                    || !target.LoadFlags.TestFlag(LoadFlags.Spawned)
                    || target.CurAlpha < .95f
                    || (_scene.GameState.Teams && TeamIndex == target.TeamIndex))
                {
                    continue;
                }

                if (!BeamProjectileEntity.ModShadowFreezeWouldHit(
                    _muzzlePos, direction, maxDistance, target.Position))
                {
                    continue;
                }

                var volume = PlayerVolumes[(int)target.Hunter, target.IsAltForm ? 2 : 0];
                Vector3 visiblePoint = target.Position + volume.SpherePosition;
                bool visible = AssistVisible(visiblePoint);
                if (visible)
                {
                    visibleHit = true;
                    visibleSlot = target.SlotIndex;
                    _shadowFreezeRetainedTarget = visibleSlot;
                    _shadowFreezeRetainedUntil = now + ShadowFreezeAssistMath.OcclusionRetentionMs;
                    break;
                }

                // Retain only a target that was already visibly acquired. A newly hidden
                // player can never create the cue.
                if (target.SlotIndex == _shadowFreezeRetainedTarget
                    && now <= _shadowFreezeRetainedUntil)
                {
                    retainedHit = true;
                }
            }

            bool wouldHit = visibleHit || retainedHit;
            ModShadowFreezeTargetReady = wouldHit;
            if (visibleHit && (!_shadowFreezeTargetLatched || visibleSlot != _shadowFreezeCueTarget))
            {
                GamepadHaptics.Play(GamepadFeedback.ShadowFreezeTarget);
                AimAssistTelemetry.ShadowFreezeTargetCue();
            }

            _shadowFreezeTargetLatched = wouldHit;
            if (visibleHit)
            {
                _shadowFreezeCueTarget = visibleSlot;
            }
            else if (!wouldHit)
            {
                _shadowFreezeCueTarget = -1;
                _shadowFreezeRetainedTarget = -1;
                _shadowFreezeRetainedUntil = 0;
            }
        }

        private void ModClearShadowFreezeFeedback()
        {
            ModShadowFreezeAngleReady = false;
            ModShadowFreezeTargetReady = false;
            _shadowFreezeAngleLatched = false;
            _shadowFreezeTargetLatched = false;
            _shadowFreezeCueTarget = -1;
            _shadowFreezeRetainedTarget = -1;
            _shadowFreezeRetainedUntil = 0;
        }

        private void ModResetShadowFreezeControllerAssist()
        {
            _shadowFreezeAssistActive = false;
            _shadowFreezeReleaseFrames = 0;
            _shadowFreezePreviousCameraY = 0;
            ModClearShadowFreezeFeedback();
        }
    }

    internal static class ShadowFreezeAssistMath
    {
        internal const float CaptureRangeMultiplier = 2.5f;
        internal const float ReadyRangeMultiplier = 8f;
        // The biped camera clamps at -85 degrees, whose cartridge projection is
        // about 11.47x. Stay a hair inside the clamp so the assist can settle
        // instead of grinding against it every frame.
        internal const float TargetRangeMultiplier = 11.25f;
        internal const int ReleaseBlendFrames = 6;
        internal const int OcclusionRetentionMs = 180;

        internal static readonly float CapturePitch = PitchForRangeMultiplier(CaptureRangeMultiplier);
        internal static readonly float ReadyPitch = PitchForRangeMultiplier(ReadyRangeMultiplier);
        internal static readonly float TargetPitch = PitchForRangeMultiplier(TargetRangeMultiplier);
        internal static readonly float ExitPitch = PitchForRangeMultiplier(1.8f);

        internal static float RangeMultiplier(float pitchDegrees)
        {
            if (!float.IsFinite(pitchDegrees) || pitchDegrees >= 0)
            {
                return 1f;
            }
            float radians = MathF.Abs(pitchDegrees) * MathF.PI / 180f;
            float cosine = MathF.Abs(MathF.Cos(radians));
            return cosine <= .001f ? 1000f : 1f / cosine;
        }

        internal static float PitchForRangeMultiplier(float multiplier)
        {
            multiplier = Math.Max(1f, multiplier);
            return -MathF.Acos(1f / multiplier) * 180f / MathF.PI;
        }

        internal static bool InCaptureRange(float pitchDegrees)
            => pitchDegrees < 0 && RangeMultiplier(pitchDegrees) >= CaptureRangeMultiplier;

        internal static bool AngleReady(float pitchDegrees)
            => pitchDegrees < 0 && RangeMultiplier(pitchDegrees) >= ReadyRangeMultiplier;

        internal static float Smooth(float start, float end, float value)
        {
            if (!float.IsFinite(value) || end <= start) return 0;
            float t = Math.Clamp((value - start) / (end - start), 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
