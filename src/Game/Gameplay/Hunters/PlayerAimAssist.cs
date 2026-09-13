using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>
    /// The only aim-assist tuning object. Keeping the values together makes
    /// the geometry, hysteresis and escape response use one profile in every
    /// weapon and form.
    /// </summary>
    internal readonly record struct AimAssistProfile(
        float AcquireConeDegrees,
        float RetainConeDegrees,
        float FrictionOuterConeDegrees,
        float FrictionStrongConeDegrees,
        float MaxSlowdown,
        float RotationConeDegrees,
        float MaxYawRate,
        float MaxPitchRate,
        float MinimumStickIntent,
        float MaximumDistance,
        float DistanceScoreWeight,
        float SwitchingMarginDegrees,
        float EscapeNeutralDot)
    {
        internal static AimAssistProfile Default => new(
            // Double the rotational correction while widening acquisition
            // and friction more conservatively. This makes controller assist
            // materially stronger without doubling slowdown and making the
            // stick feel trapped on a target.
            AcquireConeDegrees: 8f,
            RetainConeDegrees: 11f,
            FrictionOuterConeDegrees: 8f,
            FrictionStrongConeDegrees: 2.5f,
            MaxSlowdown: 0.40f,
            // Keep the wider profile from overpowering a full directed stick
            // input at the edge of the rotational assist cone.
            RotationConeDegrees: 6f,
            MaxYawRate: 60f,
            MaxPitchRate: 44f,
            MinimumStickIntent: 0.08f,
            MaximumDistance: 80f,
            DistanceScoreWeight: 0.0025f,
            SwitchingMarginDegrees: 0.50f,
            EscapeNeutralDot: 0.10f);
    }

    /// <summary>
    /// Firing-time target measurements. False target flags are intentional:
    /// no target is represented explicitly instead of being inferred from a
    /// numeric sentinel.
    /// </summary>
    public readonly record struct AimAssistTargetObservation(
        bool HasNearestEnemy,
        float NearestEnemyErrorDegrees,
        bool HasRetainedAssistTarget,
        float RetainedAssistTargetErrorDegrees)
    {
        public static AimAssistTargetObservation NoTargets => new(
            false, float.NaN, false, float.NaN);
    }

    /// <summary>
    /// Conservative local controller assistance. It changes only the local
    /// look delta before the existing aim update; projectile, collision and
    /// network formats remain untouched.
    /// </summary>
    internal sealed class PlayerAimAssist
    {
        private static readonly AimAssistProfile Profile = AimAssistProfile.Default;

        // Preserve the focused-test/source compatibility surface while making
        // Profile the sole source of tuning values.
        internal static AimAssistProfile DefaultProfile => Profile;
        internal static float AcquireConeDegrees => Profile.AcquireConeDegrees;
        internal static float RetainConeDegrees => Profile.RetainConeDegrees;
        internal static float FrictionOuterConeDegrees => Profile.FrictionOuterConeDegrees;
        internal static float FrictionStrongConeDegrees => Profile.FrictionStrongConeDegrees;
        internal static float MaxSlowdown => Profile.MaxSlowdown;
        internal static float RotationConeDegrees => Profile.RotationConeDegrees;
        internal static float MaxYawRate => Profile.MaxYawRate;
        internal static float MaxPitchRate => Profile.MaxPitchRate;
        internal static float MinimumStickIntent => Profile.MinimumStickIntent;

        private PlayerEntity? _retained;
        private float _acquisitionSeconds;

        internal PlayerEntity? RetainedTarget => _retained;

        public void Reset()
        {
            _retained = null;
            _acquisitionSeconds = 0;
        }

        public Vector2 Process(PlayerEntity owner, in LocalLookFrame frame,
            float deltaSeconds)
            => ProcessDetailed(owner, frame, deltaSeconds).AppliedDelta;

        internal AimAssistResult ProcessDetailed(PlayerEntity owner,
            in LocalLookFrame frame, float deltaSeconds)
        {
            Vector2 input = frame.DeltaDegrees;
            if (!Eligible(owner, frame, deltaSeconds))
            {
                Reset();
                ObserveFrame(owner, frame, input, input, float.NaN, float.NaN);
                return new AimAssistResult(input, input, null, float.NaN,
                    float.NaN, 0, 1, 0);
            }

            PlayerEntity? target = SelectTarget(owner, retentionScale: 1,
                out Vector2 error, out float angularDistance);
            float escapeIntent = target == null ? 0
                : ComputeEscapeIntent(input, error, owner.Controls.InvertAimX,
                    owner.Controls.InvertAimY);

            // A retained target gets a wider cone only while the player's
            // pre-assist stick intent is not leaving it. Re-evaluating the
            // retained candidate with this scale makes retention fade
            // continuously instead of waiting for a hard cone edge.
            if (target == _retained && escapeIntent > 0)
            {
                float retentionScale = 1 - escapeIntent;
                target = SelectTarget(owner, retentionScale, out error,
                    out angularDistance);
                if (target == null)
                {
                    _retained = null;
                    _acquisitionSeconds = Math.Min(
                        _acquisitionSeconds + deltaSeconds, 60);
                    ObserveFrame(owner, frame, input, input,
                        float.NaN, float.NaN);
                    return new AimAssistResult(input, input, null, float.NaN,
                        float.NaN, 0, 1, escapeIntent);
                }
                escapeIntent = ComputeEscapeIntent(input, error,
                    owner.Controls.InvertAimX, owner.Controls.InvertAimY);
            }

            if (target == null)
            {
                _retained = null;
                _acquisitionSeconds = Math.Min(
                    _acquisitionSeconds + deltaSeconds, 60);
                ObserveFrame(owner, frame, input, input, float.NaN, float.NaN);
                return new AimAssistResult(input, input, null, float.NaN,
                    float.NaN, 0, 1, escapeIntent);
            }

            bool acquired = target != _retained;
            float acquisitionMilliseconds = acquired
                ? _acquisitionSeconds * 1000 : float.NaN;
            _retained = target;
            if (acquired) _acquisitionSeconds = 0;

            // UpdateAimX/Y applies these inversion signs after this method.
            // Convert the desired geometric correction to the value that must
            // be supplied before that operation. Escape classification above
            // compares the transformed stick with the correction's applied
            // camera-turn space.
            Vector2 appliedError = ApplyAimInversion(error,
                owner.Controls.InvertAimX, owner.Controls.InvertAimY);
            Vector2 result = ApplyAssistance(input, appliedError,
                angularDistance, frame.Magnitude, frame.AimAssistStrength,
                deltaSeconds, escapeIntent);
            float friction = FrictionMultiplier(angularDistance,
                frame.AimAssistStrength, escapeIntent);
            float rotation = (result - input * friction).Length;
            Vector2 appliedInput = ApplyAimInversion(input,
                owner.Controls.InvertAimX, owner.Controls.InvertAimY);
            Vector2 appliedResult = ApplyAimInversion(result,
                owner.Controls.InvertAimX, owner.Controls.InvertAimY);
            float preAssistError = (error - appliedInput).Length;
            float postAssistError = (error - appliedResult).Length;
            owner.ModScene.Services.ObserveAimAssist(owner, acquired,
                acquisitionMilliseconds, angularDistance, rotation, friction);
            ObserveFrame(owner, frame, input, result,
                preAssistError, postAssistError);
            return new AimAssistResult(input, result, target, angularDistance,
                acquisitionMilliseconds, rotation, friction, escapeIntent);
        }

        /// <summary>
        /// Apply assist in the value space consumed by UpdateAimX/Y. The
        /// optional escape value is continuous: away intent removes both
        /// rotational pull and friction, while tangent/toward input retains
        /// the normal profile.
        /// </summary>
        internal static Vector2 ApplyAssistance(Vector2 input, Vector2 error,
            float angularDistance, float stickMagnitude, float strength,
            float deltaSeconds)
            => ApplyAssistance(input, error, angularDistance, stickMagnitude,
                strength, deltaSeconds, ComputeEscapeIntent(input, error));

        internal static Vector2 ApplyAssistance(Vector2 input, Vector2 error,
            float angularDistance, float stickMagnitude, float strength,
            float deltaSeconds, float escapeIntent)
        {
            strength = SanitizeStrength(strength);
            escapeIntent = SanitizeEscape(escapeIntent);
            float friction = FrictionMultiplier(angularDistance, strength,
                escapeIntent);
            input *= friction;

            if (stickMagnitude >= Profile.MinimumStickIntent
                && angularDistance <= Profile.RotationConeDegrees)
            {
                float coneScale = 1f - angularDistance / Profile.RotationConeDegrees;
                float intentScale = Math.Clamp((stickMagnitude
                    - Profile.MinimumStickIntent) / (1f - Profile.MinimumStickIntent), 0, 1);
                float scale = strength * SmoothStep(coneScale)
                    * (0.35f + 0.65f * intentScale)
                    * (1f - escapeIntent);
                input.X += ClampToward(error.X,
                    Profile.MaxYawRate * deltaSeconds * scale);
                input.Y += ClampToward(error.Y,
                    Profile.MaxPitchRate * deltaSeconds * scale);
            }
            return input;
        }

        internal static float FrictionMultiplier(float angularDistance,
            float strength, float escapeIntent = 0)
        {
            strength = SanitizeStrength(strength);
            escapeIntent = SanitizeEscape(escapeIntent);
            float proximity = 1f - Math.Clamp(
                (angularDistance - Profile.FrictionStrongConeDegrees)
                    / (Profile.FrictionOuterConeDegrees
                        - Profile.FrictionStrongConeDegrees), 0, 1);
            float baseMultiplier = 1f - Profile.MaxSlowdown * strength
                * SmoothStep(proximity);
            // Leaving a target should feel free immediately, but the blend is
            // smooth so a near-tangent stick path does not pop in sensitivity.
            return baseMultiplier + (1f - baseMultiplier) * escapeIntent;
        }

        /// <summary>
        /// Return a 0..1 measure of the player's intent to leave the target.
        /// The stick value is transformed through the same inversion signs
        /// that UpdateAimX/Y applies. ModAimDeltaTowards already returns a
        /// geometric correction in that applied (camera-turn) coordinate, so
        /// it is compared without a second inversion. This avoids inverted
        /// axes being mistaken for escape intent.
        /// </summary>
        internal static float ComputeEscapeIntent(Vector2 preAssistAngularLook,
            Vector2 targetError, bool invertX = false, bool invertY = false)
        {
            Vector2 appliedLook = ApplyAimInversion(preAssistAngularLook,
                invertX, invertY);
            Vector2 appliedError = targetError;
            if (!IsUsable(appliedLook) || !IsUsable(appliedError)
                || appliedLook.LengthSquared < 0.000001f
                || appliedError.LengthSquared < 0.000001f)
            {
                return 0;
            }
            float dot = Vector2.Dot(appliedLook.Normalized(),
                appliedError.Normalized());
            if (!float.IsFinite(dot)) return 0;
            float away = Math.Clamp(-dot, 0, 1);
            float thresholded = Math.Clamp((away - Profile.EscapeNeutralDot)
                / (1f - Profile.EscapeNeutralDot), 0, 1);
            return SmoothStep(thresholded);
        }

        /// <summary>
        /// Measure the view-ray against the nearest visible enemy and the
        /// currently retained assist target. This is called only from the
        /// opt-in firing telemetry path, never from the assist hot loop.
        /// </summary>
        internal static AimAssistTargetObservation MeasureFiringTargets(
            PlayerEntity owner, Vector3 viewRay)
        {
            if (!IsUsable(viewRay) || viewRay.LengthSquared < 0.000001f)
            {
                return AimAssistTargetObservation.NoTargets;
            }
            viewRay = viewRay.Normalized();
            Vector3 origin = owner.CameraInfo.Position;
            PlayerEntity? nearest = null;
            float nearestDistanceSquared = float.MaxValue;
            float nearestError = float.NaN;
            float maxDistanceSquared = Profile.MaximumDistance
                * Profile.MaximumDistance;
            for (int i = 0; i < owner.ModScene.Players.Count; i++)
            {
                PlayerEntity candidate = owner.ModScene.Players[i];
                if (!IsValidCandidate(owner, candidate, maxDistanceSquared,
                    out float distanceSquared))
                {
                    continue;
                }
                if (distanceSquared >= nearestDistanceSquared
                    || !HasLineOfSight(owner, candidate))
                {
                    continue;
                }
                float error = AngularError(origin, viewRay,
                    candidate.ModAssistAimTarget);
                if (!float.IsFinite(error)) continue;
                nearest = candidate;
                nearestDistanceSquared = distanceSquared;
                nearestError = error;
            }

            PlayerEntity? retained = owner.ModAimAssistRetainedTarget;
            float retainedError = float.NaN;
            bool hasRetained = retained != null
                && IsValidCandidate(owner, retained, maxDistanceSquared,
                    out _)
                && HasLineOfSight(owner, retained);
            if (hasRetained)
            {
                retainedError = AngularError(origin, viewRay,
                    retained!.ModAssistAimTarget);
                hasRetained = float.IsFinite(retainedError);
            }
            return new AimAssistTargetObservation(nearest != null,
                nearestError, hasRetained, retainedError);
        }

        private static bool Eligible(PlayerEntity owner, in LocalLookFrame frame,
            float deltaSeconds)
        {
            return frame.AimAssistEnabled && frame.AimAssistStrength > 0
                && frame.Device == LookDeviceKind.GamepadStick
                && !frame.HasPrecisionContributor
                && (frame.Contributors & LookDeviceKind.GamepadGyro) == 0
                && frame.Magnitude >= Profile.MinimumStickIntent
                && float.IsFinite(deltaSeconds) && deltaSeconds > 0
                && owner.ModInPlay && !owner.IsBot
                && owner.SlotIndex == owner.ModScene.LocalPlayerSlot
                && !owner.ModScene.Services.DesiredSpectating
                && !owner.Flags1.TestFlag(PlayerFlags1.NoAimInput)
                && !owner.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)
                && owner.ModScene.CameraSequences.Current == null
                && !owner.ModScene.FrameAdvance && !owner.ModScene.FrameAdvanceLastFrame;
        }

        private PlayerEntity? SelectTarget(PlayerEntity owner,
            float retentionScale, out Vector2 error, out float angularDistance)
        {
            error = Vector2.Zero;
            angularDistance = float.MaxValue;
            retentionScale = SanitizeEscape(retentionScale);
            Candidate best0 = default;
            Candidate best1 = default;
            Candidate best2 = default;
            Candidate retained = default;
            int bestCount = 0;
            bool hasRetained = false;
            float maxDistanceSquared = Profile.MaximumDistance
                * Profile.MaximumDistance;
            for (int i = 0; i < owner.ModScene.Players.Count; i++)
            {
                PlayerEntity candidate = owner.ModScene.Players[i];
                if (!IsValidCandidate(owner, candidate, maxDistanceSquared,
                    out float distanceSquared))
                {
                    continue;
                }
                (float X, float Y) aim = owner.ModAimDeltaTowards(
                    candidate.ModAssistAimTarget);
                float angle = MathF.Sqrt(aim.X * aim.X + aim.Y * aim.Y);
                float cone = candidate == _retained
                    ? Profile.AcquireConeDegrees
                        + (Profile.RetainConeDegrees - Profile.AcquireConeDegrees)
                            * retentionScale
                    : Profile.AcquireConeDegrees;
                if (!float.IsFinite(angle) || angle > cone) continue;
                Candidate value = new(candidate,
                    new Vector2(aim.X, aim.Y), angle,
                    angle + MathF.Sqrt(distanceSquared) * Profile.DistanceScoreWeight);
                if (candidate == _retained)
                {
                    retained = value;
                    hasRetained = true;
                }
                Insert(ref best0, ref best1, ref best2, ref bestCount, value);
            }

            bool hasBestVisible = false;
            Candidate bestVisible = default;
            bool hasRetainedVisible = false;
            Candidate retainedVisible = default;
            for (int i = 0; i < bestCount; i++)
            {
                Candidate candidate = i == 0 ? best0 : i == 1 ? best1 : best2;
                if (!HasLineOfSight(owner, candidate.Player!)) continue;
                if (!hasBestVisible || candidate.Score < bestVisible.Score)
                {
                    bestVisible = candidate;
                    hasBestVisible = true;
                }
                if (candidate.Player == _retained)
                {
                    retainedVisible = candidate;
                    hasRetainedVisible = true;
                }
            }
            // A retained candidate is always checked, even if three new
            // candidates outranked it. Retention must not disappear merely
            // because the cheap shortlist was full.
            if (hasRetained && !Contains(best0, best1, best2, bestCount, retained)
                && HasLineOfSight(owner, retained.Player!))
            {
                retainedVisible = retained;
                hasRetainedVisible = true;
            }
            if (!hasBestVisible && !hasRetainedVisible) return null;

            Candidate selected;
            if (hasRetainedVisible && hasBestVisible
                && bestVisible.Player != _retained
                && retainedVisible.Score <= bestVisible.Score
                    + Profile.SwitchingMarginDegrees * retentionScale)
            {
                // The switching margin is the sole retained preference. It
                // is not combined with a second score bonus.
                selected = retainedVisible;
            }
            else if (hasRetainedVisible && !hasBestVisible)
            {
                selected = retainedVisible;
            }
            else
            {
                selected = bestVisible;
            }
            error = selected.Error;
            angularDistance = selected.Angle;
            return selected.Player;
        }

        private static bool IsValidCandidate(PlayerEntity owner,
            PlayerEntity candidate, float maxDistanceSquared,
            out float distanceSquared)
        {
            distanceSquared = (candidate.ModAssistAimTarget
                - owner.CameraInfo.Position).LengthSquared;
            return candidate != owner && candidate.ModInPlay
                && candidate.LoadFlags.TestFlag(LoadFlags.Spawned)
                && IsOpponent(owner.ModScene.Match.Rules.Teams,
                    owner.TeamIndex, candidate.TeamIndex)
                && float.IsFinite(distanceSquared) && distanceSquared > 0
                && distanceSquared <= maxDistanceSquared;
        }

        private static bool HasLineOfSight(PlayerEntity owner,
            PlayerEntity candidate)
        {
            CollisionResult hit = default;
            return !CollisionDetection.CheckBetweenPoints(
                owner.CameraInfo.Position, candidate.ModAssistAimTarget,
                TestFlags.None, owner.ModScene, ref hit);
        }

        private static float AngularError(Vector3 origin, Vector3 viewRay,
            Vector3 target)
        {
            Vector3 delta = target - origin;
            if (!IsUsable(delta) || delta.LengthSquared < 0.000001f) return float.NaN;
            float dot = Math.Clamp(Vector3.Dot(viewRay, delta.Normalized()), -1, 1);
            return MathHelper.RadiansToDegrees(MathF.Acos(dot));
        }

        private static void Insert(ref Candidate first, ref Candidate second,
            ref Candidate third, ref int count, in Candidate value)
        {
            if (count == 0 || value.Score < first.Score)
            {
                third = second;
                second = first;
                first = value;
            }
            else if (count == 1 || value.Score < second.Score)
            {
                third = second;
                second = value;
            }
            else if (count == 2 || value.Score < third.Score)
            {
                third = value;
            }
            if (count < 3) count++;
        }

        private static bool Contains(in Candidate first, in Candidate second,
            in Candidate third, int count, in Candidate value)
            => count > 0 && first.Player == value.Player
                || count > 1 && second.Player == value.Player
                || count > 2 && third.Player == value.Player;

        private static void ObserveFrame(PlayerEntity owner,
            in LocalLookFrame frame, Vector2 preAssist, Vector2 postAssist,
            float preAssistAngularError, float postAssistAngularError)
            => owner.ModScene.Services.ObserveAimAssistFrame(owner, frame.Device,
                preAssist, postAssist, preAssistAngularError,
                postAssistAngularError);

        private static Vector2 ApplyAimInversion(Vector2 value,
            bool invertX, bool invertY)
        {
            if (invertX) value.X *= -1;
            if (invertY) value.Y *= -1;
            return value;
        }

        private static float SanitizeStrength(float value)
            => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

        private static float SanitizeEscape(float value)
            => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

        private static bool IsUsable(Vector2 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y);

        private static bool IsUsable(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);

        private static float ClampToward(float error, float maximum)
            => Math.Clamp(error, -maximum, maximum);

        internal static bool IsOpponent(bool teams, int ownerTeam, int candidateTeam)
            => !teams || candidateTeam != ownerTeam;

        private static float SmoothStep(float value)
        {
            value = Math.Clamp(value, 0, 1);
            return value * value * (3f - 2f * value);
        }

        internal readonly record struct AimAssistResult(
            Vector2 PreAssistDelta,
            Vector2 AppliedDelta,
            PlayerEntity? Target,
            float AngularErrorDegrees,
            float AcquisitionMilliseconds,
            float RotationalDegrees,
            float FrictionMultiplier,
            float EscapeIntent);

        private readonly record struct Candidate(PlayerEntity? Player,
            Vector2 Error, float Angle, float Score);
    }

    public partial class PlayerEntity
    {
        private readonly PlayerAimAssist _aimAssist = new();
        internal Scene ModScene => _scene;
    }
}
