namespace MphRead.Mods.Input.AimAssist
{
    // Deliberately not player preferences. Changes require regression and balance validation.
    public static class AimAssistTuning
    {
        public const float HeadFlickCaptureSeconds = .090f, HeadFlickSnapGain = 75f;
        // Flick capture already passed trajectory/alignment/visibility gates, so
        // it may settle a little faster than ordinary positional magnetism.
        // Keep the envelope unchanged; this only strengthens the final landing.
        public const float FlickSnapSpeedScale = 1.20f;
        // Ordinary aim correction is deliberately slow, but a flick that has
        // already passed all trajectory/LOS/target gates needs enough velocity
        // to be perceptible before the 90 ms capture window ends.
        public const float FlickSnapMaxSpeed = 24f, FlickSnapScopedMaxSpeed = 18f;
        public const float FlickDirectionalSpeed = 14f, FlickDirectionalMinMagnitude = .45f;
        // A fast same-direction turn may not change stick magnitude/direction
        // enough to trip the ordinary flick detectors, so arm on camera-speed
        // rise too. This is only an arm signal; capture still requires a real
        // predicted head crossing.
        public const float FlickCameraSpeed = 42f, FlickCameraRiseRatio = .70f;
        public const float FlickPassThroughStickSpeed = 24f;
        public const float FlickTargetAlignment = .55f;
        public const float FlickCaptureAlignment = .60f, FlickPredictedCaptureAlignment = .48f;
        public const float FlickLandingMinSeconds = .045f, FlickLandingMaxSeconds = .080f;
        public const float FlickRadiusMinScale = .90f, FlickRadiusMaxScale = 1.45f;
        public const float FlickBrakeRatio = .72f;
        public const float HeadSafeInset = .20f, HeadSafePositionScale = .40f;
        public const float HeadSafeMotionBias = .14f, HeadSafeMotionSpeed = 45f;
        public const float HeadHorizontalPositionGain = 1.08f, HeadVerticalPositionGain = 1.95f;
        public const float HeadHorizontalTrackingGain = 1.08f, HeadVerticalTrackingGain = 1.38f;
        public const float AcquireCone = 8.25f, ReleaseCone = 10.5f, InnerCone = 2.6f;
        public const float MinimumFriction = .60f;
        public const float RotationAssistMultiplier = 4.25f;
        public const float ChallengerRatio = 1.28f, InputAlignmentWeight = .16f;
        public const float HeadDelay = .080f, IntentionalHeadDelay = .025f;
        public const float MaxHeadBlend = .90f, IntentionalMaxHeadBlend = 1f;
        public const float HeadAcquireCone = 1.90f, HeadReleaseCone = 2.90f;
        public const float HeadIntentStick = .12f, HeadIntentAlignment = .30f;
        public const float HeadCandidateBodyRatio = 1.15f;
        public const float HeadSelectionBias = .18f;
        public const float HeadPredictionSeconds = 0f, MaxHeadPrediction = .60f;
        public const float OcclusionGrace = .060f;
        public const float TrackingConfidenceMin = .26f, TrackingConfidenceRiseRate = 10f;
        public const float TrackingConfidenceDecayRate = 1.8f, TrackingConfidenceStrafeDecayRate = .50f;
        public const float HeadConfidenceRiseRate = 14f, HeadConfidenceDecayRate = 3f;
        public const float StrafeTrackingMinimum = .24f, StrafeTrackingMaximum = .46f;
        public const float OccludedMotionDecayRate = 7f;
        public const float ShotCommitSeconds = .060f, ShotCommitFrictionScale = 1.24f;
        public const float TrajectoryHorizon = .085f, TrajectoryScoreWeight = .22f;
        public const float TrajectoryAcquireThreshold = .92f;
        public const float FreshTargetVisibility = .12f;
        public const float RetainedServoDelay = .050f;
        public const float ServoScaleMax = 1.35f;
        public const float PrecisionFilterReleaseRate = 2.5f;
        public const float VelocityFilterRate = 12f, HeadVelocityFilterRate = 16f;
        public const float MotionAccelerationRate = 18f, MaxTrackedAcceleration = 900f;
        public const float MotionServoLookahead = .025f, MotionDirectionRate = 18f;
        public const float MotionPhaseSpeed = 5f, MotionMatchedSpeed = 2f;
        public const float MotionTransitionVelocityDelta = 22f;
        public const float MotionTransitionSeconds = .080f;
        public const float VisibilityRiseRate = 12f, VisibilityDecayRate = 20f;
        public const float HeadBlendRate = 14f, HeadFallbackRate = 14f;
        public const float IntentStart = .04f, IntentFull = .20f;
        public const float MaxTrackedSpeed = 120f, MotionDiscontinuity = 12f;
        public const float ScopeTransitionEpsilon = .015f;
        public const float TurnAccelerationBrakeResponse = 18f;
    }

    // Kept for source compatibility with older tests/tools. Production selection
    // now resolves directly from BeamType so every weapon can have its own feel.
    public enum AimAssistWeaponClass { Standard, Tracking, Precision, Projectile, Splash }

    public readonly record struct AimAssistWeaponProfile(float Cone, float ReleaseCone, float Inner,
        float Rotation, float MaxSpeed, bool Head)
    {
        public float FrictionStrength { get; init; } = .38f;
        public float PositionGain { get; init; } = 1f;
        public float TrackingGain { get; init; } = 1.05f;
        public float MaxPositionSpeed { get; init; } = 12;
        public float MaxTrackingSpeed { get; init; } = 30;
        public float NormalizedAcquire { get; init; } = 2.5f;
        public float NormalizedRelease { get; init; } = 3.25f;
        public float NormalizedInner { get; init; } = .55f;
        public float ServoFrequency { get; init; } = 10f;
        public float CorrectionBudgetDegrees { get; init; } = 1f;
        public float CorrectionBudgetRecovery { get; init; } = 5f;
        public float BodyAimHeight { get; init; } = .65f;
        public float HeadRangeValue { get; init; } = 15f;
        public bool Precision { get; init; }
        public bool Scoped { get; init; }
        public BeamType Weapon { get; init; } = BeamType.PowerBeam;
        public float ScopeBlend { get; init; }
        public float HeadRange => HeadRangeValue;

        public static AimAssistWeaponProfile For(BeamType weapon, float scopeBlend = 0)
        {
            scopeBlend = System.Math.Clamp(scopeBlend, 0, 1);
            AimAssistWeaponProfile hip = Hip(weapon);
            AimAssistWeaponProfile scoped = ScopedProfile(weapon, hip);
            AimAssistWeaponProfile profile = Blend(hip, scoped, scopeBlend);
            return Strengthen(profile) with
            {
                Weapon = weapon,
                ScopeBlend = scopeBlend,
                Scoped = scopeBlend >= .5f
            };
        }

        public static AimAssistWeaponProfile For(AimAssistWeaponClass weapon, bool scoped)
            => For(weapon switch
            {
                AimAssistWeaponClass.Tracking => BeamType.ShockCoil,
                AimAssistWeaponClass.Precision => BeamType.Imperialist,
                AimAssistWeaponClass.Projectile => BeamType.Judicator,
                AimAssistWeaponClass.Splash => BeamType.Missile,
                _ => BeamType.PowerBeam
            }, scoped ? 1 : 0);

        private static AimAssistWeaponProfile Hip(BeamType weapon) => weapon switch
        {
            BeamType.PowerBeam => new(7, 9, 2.4f, .80f, 20, true)
            {
                Weapon = weapon, FrictionStrength = .38f, PositionGain = .95f, TrackingGain = 1.05f,
                MaxPositionSpeed = 12, MaxTrackingSpeed = 28, NormalizedAcquire = 2.6f,
                NormalizedRelease = 3.3f, NormalizedInner = .55f, ServoFrequency = 10f,
                CorrectionBudgetDegrees = 1.1f, CorrectionBudgetRecovery = 5f,
                BodyAimHeight = .65f, HeadRangeValue = 15
            },
            BeamType.Missile => new(6.5f, 8, 2.2f, .40f, 12, false)
            {
                Weapon = weapon, FrictionStrength = .30f, PositionGain = .65f, TrackingGain = .85f,
                MaxPositionSpeed = 8, MaxTrackingSpeed = 20, NormalizedAcquire = 2.35f,
                NormalizedRelease = 3f, NormalizedInner = .65f, ServoFrequency = 8f,
                CorrectionBudgetDegrees = .70f, CorrectionBudgetRecovery = 4.5f, BodyAimHeight = .48f
            },
            BeamType.VoltDriver => new(7, 9, 2.2f, .90f, 22, true)
            {
                Weapon = weapon, FrictionStrength = .42f, PositionGain = 1f, TrackingGain = 1.15f,
                MaxPositionSpeed = 12, MaxTrackingSpeed = 32, NormalizedAcquire = 2.55f,
                NormalizedRelease = 3.35f, NormalizedInner = .50f, ServoFrequency = 11.5f,
                CorrectionBudgetDegrees = 1.15f, CorrectionBudgetRecovery = 5.5f,
                BodyAimHeight = .64f, HeadRangeValue = 15
            },
            BeamType.Battlehammer => new(6, 8, 2.2f, .42f, 12, false)
            {
                Weapon = weapon, FrictionStrength = .30f, PositionGain = .62f, TrackingGain = .82f,
                MaxPositionSpeed = 8, MaxTrackingSpeed = 18, NormalizedAcquire = 2.25f,
                NormalizedRelease = 2.9f, NormalizedInner = .65f, ServoFrequency = 8f,
                CorrectionBudgetDegrees = .65f, CorrectionBudgetRecovery = 4.25f, BodyAimHeight = .50f
            },
            BeamType.Imperialist => new(5, 7, 1.8f, .60f, 12, true)
            {
                Weapon = weapon, Precision = true, FrictionStrength = .32f, PositionGain = .75f,
                TrackingGain = 1.15f, MaxPositionSpeed = 8, MaxTrackingSpeed = 30,
                NormalizedAcquire = 2f, NormalizedRelease = 2.75f, NormalizedInner = .40f,
                ServoFrequency = 14f, CorrectionBudgetDegrees = .68f, CorrectionBudgetRecovery = 4f,
                BodyAimHeight = .72f, HeadRangeValue = 60
            },
            BeamType.Judicator => new(6.25f, 8, 2.2f, .50f, 14, false)
            {
                Weapon = weapon, FrictionStrength = .32f, PositionGain = .68f, TrackingGain = .88f,
                MaxPositionSpeed = 9, MaxTrackingSpeed = 20, NormalizedAcquire = 2.3f,
                NormalizedRelease = 3f, NormalizedInner = .62f, ServoFrequency = 8.5f,
                CorrectionBudgetDegrees = .72f, CorrectionBudgetRecovery = 4.5f, BodyAimHeight = .52f
            },
            BeamType.Magmaul => new(6.5f, 8.25f, 2.3f, .42f, 12, false)
            {
                Weapon = weapon, FrictionStrength = .31f, PositionGain = .62f, TrackingGain = .82f,
                MaxPositionSpeed = 8, MaxTrackingSpeed = 18, NormalizedAcquire = 2.4f,
                NormalizedRelease = 3.1f, NormalizedInner = .68f, ServoFrequency = 8f,
                CorrectionBudgetDegrees = .70f, CorrectionBudgetRecovery = 4.25f, BodyAimHeight = .46f
            },
            BeamType.ShockCoil => new(7, 9, 2.4f, .90f, 24, false)
            {
                Weapon = weapon, FrictionStrength = .44f, PositionGain = .30f, TrackingGain = 1.28f,
                MaxPositionSpeed = 5, MaxTrackingSpeed = 36, NormalizedAcquire = 2.7f,
                NormalizedRelease = 3.5f, NormalizedInner = .72f, ServoFrequency = 10f,
                CorrectionBudgetDegrees = 1.2f, CorrectionBudgetRecovery = 6f, BodyAimHeight = .56f
            },
            BeamType.OmegaCannon => new(5.5f, 7.25f, 2.2f, .28f, 10, false)
            {
                Weapon = weapon, FrictionStrength = .24f, PositionGain = .45f, TrackingGain = .70f,
                MaxPositionSpeed = 6, MaxTrackingSpeed = 15, NormalizedAcquire = 2.1f,
                NormalizedRelease = 2.75f, NormalizedInner = .70f, ServoFrequency = 7f,
                CorrectionBudgetDegrees = .55f, CorrectionBudgetRecovery = 3.75f, BodyAimHeight = .50f
            },
            _ => new(AimAssistTuning.AcquireCone, AimAssistTuning.ReleaseCone,
                AimAssistTuning.InnerCone, .75f, 18, false) { Weapon = weapon }
        };

        private static AimAssistWeaponProfile ScopedProfile(BeamType weapon, AimAssistWeaponProfile hip)
        {
            if (weapon == BeamType.Imperialist)
            {
                return hip with
                {
                    Cone = 3.2f, ReleaseCone = 4.4f, Inner = 1.1f, Rotation = .42f, MaxSpeed = 8,
                    FrictionStrength = .28f, PositionGain = .55f, TrackingGain = 1.25f,
                    MaxPositionSpeed = 4, MaxTrackingSpeed = 20, NormalizedAcquire = 1.55f,
                    NormalizedRelease = 2.15f, NormalizedInner = .30f, ServoFrequency = 16f,
                    CorrectionBudgetDegrees = .50f, CorrectionBudgetRecovery = 3.5f,
                    BodyAimHeight = .78f, Scoped = true
                };
            }
            return hip with
            {
                Cone = hip.Cone * .78f,
                ReleaseCone = hip.ReleaseCone * .78f,
                Inner = hip.Inner * .72f,
                Rotation = hip.Rotation * .82f,
                PositionGain = hip.PositionGain * .72f,
                MaxPositionSpeed = hip.MaxPositionSpeed * .70f,
                NormalizedAcquire = hip.NormalizedAcquire * .82f,
                NormalizedRelease = hip.NormalizedRelease * .82f,
                NormalizedInner = hip.NormalizedInner * .80f,
                CorrectionBudgetDegrees = hip.CorrectionBudgetDegrees * .78f,
                Scoped = true
            };
        }

        private static AimAssistWeaponProfile Strengthen(AimAssistWeaponProfile p)
            => p with
            {
                Cone = p.Cone * 1.08f,
                ReleaseCone = p.ReleaseCone * 1.10f,
                FrictionStrength = System.Math.Min(.52f, p.FrictionStrength * 1.08f),
                PositionGain = p.PositionGain * 1.08f,
                TrackingGain = p.TrackingGain * 1.18f,
                MaxPositionSpeed = p.MaxPositionSpeed * 1.10f,
                MaxTrackingSpeed = p.MaxTrackingSpeed * 1.18f,
                NormalizedAcquire = p.NormalizedAcquire * 1.16f,
                NormalizedRelease = p.NormalizedRelease * 1.14f,
                NormalizedInner = p.NormalizedInner * 1.06f,
                ServoFrequency = p.ServoFrequency * 1.14f,
                CorrectionBudgetDegrees = p.CorrectionBudgetDegrees * 1.18f,
                CorrectionBudgetRecovery = p.CorrectionBudgetRecovery * 1.08f
            };

        private static AimAssistWeaponProfile Blend(AimAssistWeaponProfile a,
            AimAssistWeaponProfile b, float t)
        {
            float L(float x, float y) => x + (y - x) * t;
            return a with
            {
                Cone = L(a.Cone, b.Cone), ReleaseCone = L(a.ReleaseCone, b.ReleaseCone),
                Inner = L(a.Inner, b.Inner), Rotation = L(a.Rotation, b.Rotation),
                MaxSpeed = L(a.MaxSpeed, b.MaxSpeed), FrictionStrength = L(a.FrictionStrength, b.FrictionStrength),
                PositionGain = L(a.PositionGain, b.PositionGain), TrackingGain = L(a.TrackingGain, b.TrackingGain),
                MaxPositionSpeed = L(a.MaxPositionSpeed, b.MaxPositionSpeed),
                MaxTrackingSpeed = L(a.MaxTrackingSpeed, b.MaxTrackingSpeed),
                NormalizedAcquire = L(a.NormalizedAcquire, b.NormalizedAcquire),
                NormalizedRelease = L(a.NormalizedRelease, b.NormalizedRelease),
                NormalizedInner = L(a.NormalizedInner, b.NormalizedInner),
                ServoFrequency = L(a.ServoFrequency, b.ServoFrequency),
                CorrectionBudgetDegrees = L(a.CorrectionBudgetDegrees, b.CorrectionBudgetDegrees),
                CorrectionBudgetRecovery = L(a.CorrectionBudgetRecovery, b.CorrectionBudgetRecovery),
                BodyAimHeight = L(a.BodyAimHeight, b.BodyAimHeight),
                HeadRangeValue = L(a.HeadRangeValue, b.HeadRangeValue),
                Scoped = t >= .5f
            };
        }
    }
}
