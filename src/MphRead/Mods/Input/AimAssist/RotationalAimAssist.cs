using System;
using System.Numerics;
using MphRead.Formats;

namespace MphRead.Mods.Input.AimAssist
{
    /// <summary>
    /// Production controller aim assist. The player supplies position; assistance only
    /// slows the player's own turn inside a visible target bubble and contributes a
    /// bounded fraction of visible target angular velocity. There is deliberately no
    /// positional magnetism, flick snap, projectile lead or automatic head refinement.
    /// </summary>
    public static class RotationalAimAssist
    {
        public const float AcquireRadius = 1.35f;
        public const float SlowdownRadius = 1.10f;
        public const float RotationRadius = .90f;
        public const float ReleaseRadius = 1.50f;
        public const float ChallengerRatio = 1.30f;
        public const float ChallengerMargin = .10f;
        public const float StrafeRotationScale = .25f;
        public const float NeutralRetentionSeconds = .35f;
        public const float MaxRotationalSpeed = 55f;

        public static AimAssistResult Apply(AimAssistState state,
            ReadOnlySpan<AimAssistTarget> targets, Vector2 raw, Vector2 physicalStick,
            float moveIntent, float dt, bool eligible, AimAssistWeaponProfile profile,
            bool firing = false, AimAssistShotPhase shotPhase = AimAssistShotPhase.None)
        {
            if (!AimAssistMath.Finite(raw))
            {
                raw = Vector2.Zero;
            }
            if (!AimAssistMath.Finite(physicalStick)
                || !float.IsFinite(moveIntent) || !float.IsFinite(dt)
                || dt <= 0 || dt > .1f || !eligible)
            {
                state.Reset();
                return new(raw.X, raw.Y, StickIntent: physicalStick,
                    Firing: firing, ScopeBlend: profile.ScopeBlend, ShotPhase: shotPhase,
                    PlayerContribution: raw.Length());
            }

            float stickMagnitude = Math.Clamp(physicalStick.Length(), 0, 1);
            float rightIntent = AimAssistMath.Smooth(AimAssistTuning.IntentStart,
                AimAssistTuning.IntentFull, stickMagnitude);
            state.SecondsSinceLookIntent = rightIntent > 0
                ? 0 : state.SecondsSinceLookIntent + dt;
            bool strafeRetention = state.TargetSlot >= 0 && moveIntent >= .15f
                && state.SecondsSinceLookIntent <= NeutralRetentionSeconds;

            // A neutral right stick never acquires a target. Left-stick movement can
            // retain one briefly at reduced rotational strength, matching the modern
            // CoD-style distinction between acquisition and rotational retention.
            if (rightIntent <= 0 && !strafeRetention)
            {
                state.Reset();
                return new(raw.X, raw.Y, StickIntent: physicalStick,
                    Firing: firing, ScopeBlend: profile.ScopeBlend, ShotPhase: shotPhase,
                    PlayerContribution: raw.Length());
            }

            int bestIndex = -1;
            int retainedIndex = -1;
            int occludedRetainedIndex = -1;
            float bestScore = float.NegativeInfinity;
            float retainedScore = float.NegativeInfinity;
            float bestAlignment = 0;
            float bestNormalized = float.PositiveInfinity;
            Vector2 bestError = default;
            AimAssistRegion? bestRegion = null;
            AimAssistPointType bestPoint = AimAssistPointType.UpperChest;
            float bestCoverage = 0;

            for (int i = 0; i < targets.Length; i++)
            {
                ref readonly AimAssistTarget target = ref targets[i];
                bool retained = target.Slot == state.TargetSlot
                    && target.Life == state.TargetLife;
                if (!target.Eligible || !float.IsFinite(target.Distance)
                    || target.Distance < .2f || target.Distance > 60)
                {
                    continue;
                }

                if (!TryVisibleGeometry(target, profile, out Vector2 error,
                        out AimAssistRegion? region, out AimAssistPointType point,
                        out float coverage))
                {
                    if (retained)
                    {
                        occludedRetainedIndex = i;
                    }
                    continue;
                }

                if (!AimAssistMath.Finite(error))
                {
                    continue;
                }
                if (!retained && rightIntent <= 0)
                {
                    continue;
                }

                float normalized = NormalizedError(error, region);
                float normalizedLimit = retained ? ReleaseRadius : AcquireRadius;
                float cone = retained ? profile.ReleaseCone : profile.Cone;
                if (normalized > normalizedLimit || error.Length() > cone)
                {
                    continue;
                }

                float alignment = AimAssistMath.Alignment(physicalStick, error);
                float closeness = 1 - Math.Clamp(normalized / normalizedLimit, 0, 1);
                float distancePreference = 1 - Math.Clamp(target.Distance / 60f, 0, 1);
                float score = .62f * closeness
                    + .12f * distancePreference
                    + .14f * alignment
                    + .12f * Math.Clamp(coverage, 0, 1)
                    + (retained ? .18f : 0);

                if (retained)
                {
                    retainedIndex = i;
                    retainedScore = score;
                }
                if (score > bestScore)
                {
                    bestIndex = i;
                    bestScore = score;
                    bestAlignment = alignment;
                    bestNormalized = normalized;
                    bestError = error;
                    bestRegion = region;
                    bestPoint = point;
                    bestCoverage = coverage;
                }
            }

            if (retainedIndex >= 0 && bestIndex != retainedIndex)
            {
                bool decisive = bestScore >= retainedScore * ChallengerRatio
                    && bestScore - retainedScore >= ChallengerMargin;
                if (!decisive)
                {
                    bestIndex = retainedIndex;
                    ref readonly AimAssistTarget retainedTarget = ref targets[retainedIndex];
                    TryVisibleGeometry(retainedTarget, profile, out bestError,
                        out bestRegion, out bestPoint, out bestCoverage);
                    bestNormalized = NormalizedError(bestError, bestRegion);
                    bestAlignment = AimAssistMath.Alignment(physicalStick, bestError);
                    bestScore = retainedScore;
                }
            }

            if (bestIndex < 0)
            {
                if (occludedRetainedIndex >= 0
                    && state.OccludedSeconds + dt <= AimAssistTuning.OcclusionGrace)
                {
                    ref readonly AimAssistTarget hidden = ref targets[occludedRetainedIndex];
                    state.OccludedSeconds += dt;
                    state.TrackingState = AimAssistTrackingState.OccludedRetention;
                    float decay = MathF.Exp(-AimAssistTuning.OccludedMotionDecayRate * dt);
                    state.AngularVelocity *= decay;
                    state.BodyTrackingConfidence *= decay;
                    ClearLegacyAssistState(state);
                    StoreFrame(state, raw, physicalStick, raw, dt,
                        state.PreviousError, visible: false, headGeometry: false);
                    return new(raw.X, raw.Y, hidden.Slot, Friction: 1,
                        RotationStrength: 0, PointType: hidden.BodyPointType,
                        Occluded: true, TargetAge: state.RetainedSeconds,
                        TrackingState: AimAssistTrackingState.OccludedRetention,
                        StickIntent: physicalStick, Firing: firing,
                        BodyTrackingConfidence: state.BodyTrackingConfidence,
                        VisibilityCoverage: 0, PlayerContribution: raw.Length(),
                        ScopeBlend: profile.ScopeBlend, ShotPhase: shotPhase);
                }

                state.Reset();
                return new(raw.X, raw.Y, StickIntent: physicalStick,
                    Firing: firing, ScopeBlend: profile.ScopeBlend, ShotPhase: shotPhase,
                    PlayerContribution: raw.Length());
            }

            ref readonly AimAssistTarget selected = ref targets[bestIndex];
            bool sameTarget = selected.Slot == state.TargetSlot
                && selected.Life == state.TargetLife;
            bool headGeometry = bestPoint == AimAssistPointType.Head;
            bool sameGeometry = headGeometry
                ? state.PreviousHeadVisible
                : state.PreviousBodyVisible && !state.PreviousHeadVisible;
            bool haveMotionHistory = sameTarget && sameGeometry
                && state.PreviousDeltaTime > 0 && state.OccludedSeconds == 0;

            if (!sameTarget)
            {
                ResetTargetHistory(state);
                state.TargetSlot = selected.Slot;
                state.TargetLife = selected.Life;
            }
            state.OccludedSeconds = 0;
            state.RetainedSeconds = sameTarget ? state.RetainedSeconds + dt : 0;

            float coverageRate = bestCoverage < state.SmoothedBodyVisibility
                ? AimAssistTuning.VisibilityDecayRate : AimAssistTuning.VisibilityRiseRate;
            state.SmoothedBodyVisibility += (bestCoverage - state.SmoothedBodyVisibility)
                * (1 - MathF.Exp(-coverageRate * dt));
            float visibility = Math.Clamp(state.SmoothedBodyVisibility, 0, 1);

            Vector2 targetVelocity = Vector2.Zero;
            bool motionTransition = false;
            if (haveMotionHistory)
            {
                Vector2 motion = AimAssistMath.AngularDelta(bestError, state.PreviousError)
                    + state.PreviousOutput;
                if (AimAssistMath.Finite(motion)
                    && motion.Length() <= AimAssistTuning.MotionDiscontinuity)
                {
                    Vector2 measured = AimAssistMath.ClampLength(motion / state.PreviousDeltaTime,
                        AimAssistTuning.MaxTrackedSpeed);
                    motionTransition = state.AngularVelocity.LengthSquared() > 4
                        && measured.LengthSquared() > 4
                        && Vector2.Dot(state.AngularVelocity, measured) < 0;
                    float rate = motionTransition
                        ? AimAssistTuning.VelocityFilterRate * 2.5f
                        : AimAssistTuning.VelocityFilterRate;
                    targetVelocity = state.AngularVelocity
                        + (measured - state.AngularVelocity)
                        * (1 - MathF.Exp(-rate * dt));
                    targetVelocity = AimAssistMath.ClampLength(targetVelocity,
                        AimAssistTuning.MaxTrackedSpeed);
                }
            }
            state.AngularVelocity = targetVelocity;
            state.MotionTransition = motionTransition;

            float slowdownWeight = 1 - AimAssistMath.Smooth(0, SlowdownRadius, bestNormalized);
            float deliberateExit = AimAssistMath.Smooth(.72f, .98f, stickMagnitude);
            slowdownWeight *= visibility * (1 - .55f * deliberateExit);
            if (selected.HeadVisible && AimAssistMath.InsideHead(selected))
            {
                // Precision reward only after the player actually reaches the real head
                // band. It never creates vertical or horizontal camera correction.
                slowdownWeight = Math.Min(1, slowdownWeight * 1.08f);
            }
            float frictionFloor = FrictionFloor(profile);
            float friction = 1 - slowdownWeight * (1 - frictionFloor);
            friction = Math.Clamp(friction, frictionFloor, 1);
            Vector2 slowed = raw * friction;

            float rotationWeight = 1 - AimAssistMath.Smooth(0, RotationRadius, bestNormalized);
            float velocitySpeed = targetVelocity.Length();
            float trackingDot = 0;
            if (stickMagnitude > .0001f && velocitySpeed > .0001f)
            {
                trackingDot = Math.Clamp(Vector2.Dot(physicalStick, targetVelocity)
                    / (stickMagnitude * velocitySpeed), -1, 1);
            }

            bool opposingBreak = stickMagnitude >= .15f && trackingDot < -.15f;
            float inputQualification;
            if (rightIntent > 0)
            {
                float alignmentQualification = AimAssistMath.Smooth(-.05f, .70f, trackingDot);
                float magnitudeQualification = AimAssistMath.Smooth(.04f, .35f, stickMagnitude);
                inputQualification = (.35f + .65f * alignmentQualification)
                    * (.60f + .40f * magnitudeQualification);
                if (trackingDot < 0)
                {
                    inputQualification *= 1 - AimAssistMath.Smooth(.05f, .70f, -trackingDot);
                }
            }
            else
            {
                inputQualification = strafeRetention ? StrafeRotationScale : 0;
            }

            float distanceScale = DistanceRotationScale(selected.Distance);
            float rotationStrength = profile.Rotation * rotationWeight * visibility
                * inputQualification * distanceScale;
            if (opposingBreak && trackingDot <= -.70f)
            {
                rotationStrength = 0;
            }
            rotationStrength = Math.Clamp(rotationStrength, 0, 1);

            Vector2 cameraVelocity = slowed / dt;
            Vector2 residualVelocity = AimAssistMath.RelativeTrackingVelocity(
                targetVelocity, cameraVelocity);
            float speedCap = Math.Min(MaxRotationalSpeed,
                Math.Max(10, profile.MaxTrackingSpeed));
            Vector2 rotationalVelocity = AimAssistMath.ClampLength(
                residualVelocity * rotationStrength, speedCap);
            Vector2 rotationalDelta = rotationalVelocity * dt;
            if (!AimAssistMath.Finite(rotationalDelta))
            {
                rotationalDelta = Vector2.Zero;
            }

            Vector2 output = slowed + rotationalDelta;
            float confidenceGoal = Math.Clamp(rotationWeight * visibility
                * (rightIntent > 0 ? .55f + .45f * inputQualification : StrafeRotationScale), 0, 1);
            float confidenceRate = confidenceGoal >= state.BodyTrackingConfidence
                ? AimAssistTuning.TrackingConfidenceRiseRate
                : AimAssistTuning.TrackingConfidenceDecayRate;
            state.BodyTrackingConfidence += (confidenceGoal - state.BodyTrackingConfidence)
                * (1 - MathF.Exp(-confidenceRate * dt));
            state.HeadTrackingConfidence = 0;
            state.HeadBlend = 0;
            state.TrackingState = headGeometry
                ? AimAssistTrackingState.TrackingHead
                : AimAssistTrackingState.TrackingBody;
            state.PreviousInsideBody = AimAssistMath.InsideBody(selected);
            state.PreviousInsideHead = selected.HeadVisible && AimAssistMath.InsideHead(selected);
            ClearLegacyAssistState(state);

            float precisionWeight = Math.Clamp(slowdownWeight * (1 - friction + .15f), 0, 1);
            StoreFrame(state, raw, physicalStick, output, dt, bestError,
                visible: true, headGeometry: headGeometry);

            return new(output.X, output.Y, selected.Slot, friction, rotationStrength,
                bestPoint, HeadBlend: 0, Score: bestScore, InputAlignment: bestAlignment,
                HeadPrediction: 0, Occluded: false, Saturated: rotationalVelocity.Length()
                    >= speedCap - .001f, TargetAge: state.RetainedSeconds,
                TrackingState: state.TrackingState, PositionCorrection: Vector2.Zero,
                TrackingCorrection: rotationalDelta, StickIntent: physicalStick,
                FlickActive: false, StrafeTracking: rightIntent <= 0 && strafeRetention,
                OpposingBreak: opposingBreak, Firing: firing,
                MotionPhase: AimAssistMotionPhase.Matched,
                BodyTrackingConfidence: state.BodyTrackingConfidence,
                HeadTrackingConfidence: 0, ShotCommitted: false,
                VisibilityCoverage: visibility, FilterRelease: precisionWeight * .65f,
                TurnAccelerationBrake: precisionWeight,
                NormalizedError: bestNormalized,
                PlayerContribution: slowed.Length(),
                AssistContribution: rotationalDelta.Length(),
                ScopeBlend: profile.ScopeBlend, ShotPhase: shotPhase,
                MotionTransition: motionTransition);
        }

        private static bool TryVisibleGeometry(in AimAssistTarget target,
            AimAssistWeaponProfile profile, out Vector2 error, out AimAssistRegion? region,
            out AimAssistPointType point, out float coverage)
        {
            if (target.BodyVisible)
            {
                error = AimAssistMath.BodyError(target);
                region = target.BodyRegion;
                point = target.BodyPointType;
                coverage = Math.Clamp(target.BodyVisibility, 0, 1);
                return AimAssistMath.Finite(error);
            }
            if (profile.Head && target.HeadVisible && AimAssistMath.VisibleHead(target, profile))
            {
                error = AimAssistMath.HeadError(target);
                region = target.HeadRegion;
                point = AimAssistPointType.Head;
                coverage = Math.Clamp(target.HeadVisibility, 0, 1);
                return AimAssistMath.Finite(error);
            }
            error = default;
            region = null;
            point = target.BodyPointType;
            coverage = 0;
            return false;
        }

        private static float NormalizedError(Vector2 error, AimAssistRegion? region)
            => region is { } r
                ? AimAssistMath.NormalizeToRegion(error, r).Length()
                : error.Length();

        private static float FrictionFloor(AimAssistWeaponProfile profile)
        {
            if (profile.Weapon == BeamType.Imperialist)
            {
                return profile.ScopeBlend >= .5f ? .58f : .68f;
            }
            return profile.Weapon switch
            {
                BeamType.PowerBeam => .55f,
                BeamType.VoltDriver => .58f,
                BeamType.ShockCoil => .58f,
                BeamType.Judicator => .60f,
                BeamType.Missile => .62f,
                BeamType.Magmaul => .62f,
                BeamType.Battlehammer => .65f,
                BeamType.OmegaCannon => .68f,
                _ => .60f
            };
        }

        private static float DistanceRotationScale(float distance)
        {
            float close = .60f + .40f * AimAssistMath.Smooth(1.5f, 6f, distance);
            float far = 1 - .40f * AimAssistMath.Smooth(28f, 60f, distance);
            return Math.Min(close, far);
        }

        private static void ResetTargetHistory(AimAssistState state)
        {
            state.RetainedSeconds = 0;
            state.OccludedSeconds = 0;
            state.BodyTrackingConfidence = 0;
            state.HeadTrackingConfidence = 0;
            state.SmoothedBodyVisibility = 0;
            state.SmoothedHeadVisibility = 0;
            state.AngularVelocity = Vector2.Zero;
            state.HeadAngularVelocity = Vector2.Zero;
            state.AngularAcceleration = Vector2.Zero;
            state.HeadAngularAcceleration = Vector2.Zero;
            state.PreviousDeltaTime = 0;
            state.PreviousBodyVisible = false;
            state.PreviousHeadVisible = false;
        }

        private static void ClearLegacyAssistState(AimAssistState state)
        {
            state.FlickActive = false;
            state.FlickConsumed = false;
            state.FlickBraking = false;
            state.FlickTarget = -1;
            state.FlickAge = 0;
            state.FlickSpeed = 0;
            state.FlickPeak = 0;
            state.FlickDirection = Vector2.Zero;
            state.HeadCandidateSeconds = 0;
            state.HeadBlend = 0;
            state.HeadTrackingConfidence = 0;
            state.ShotCommitSeconds = 0;
            state.ServoVelocity = Vector2.Zero;
            state.CorrectionBudgetUsed = 0;
        }

        private static void StoreFrame(AimAssistState state, Vector2 raw,
            Vector2 physicalStick, Vector2 output, float dt, Vector2 error,
            bool visible, bool headGeometry)
        {
            state.PreviousStick = physicalStick;
            state.PushStick(physicalStick);
            state.PreviousRaw = raw;
            state.PreviousOutput = output;
            state.PreviousCameraVelocity = raw / dt;
            state.PushCameraVelocity(state.PreviousCameraVelocity);
            state.PreviousError = error;
            state.PreviousDeltaTime = dt;
            state.PreviousBodyVisible = visible && !headGeometry;
            state.PreviousHeadVisible = visible && headGeometry;
        }
    }
}
