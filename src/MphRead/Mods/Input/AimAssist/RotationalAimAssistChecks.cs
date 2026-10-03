using System.Numerics;

namespace MphRead.Mods.Input.AimAssist
{
    internal static class RotationalAimAssistChecks
    {
        public static void Run()
        {
            void Check(bool ok, string name)
                => GamepadChecks.Check(ok, "rotational aim assist: " + name);

            const float dt = 1f / 60;
            AimAssistWeaponProfile power = AimAssistWeaponProfile.For(BeamType.PowerBeam);

            AimAssistTarget Inside(float distance = 12)
                => new(1, 1, Vector2.Zero, new(0, -.2f), distance, true, true,
                    BodyRegion: new(-.5f, .5f, -.5f, .5f),
                    HeadRegion: new(-.22f, .22f, -.35f, -.05f),
                    BodySurface: new(Vector2.Zero, true),
                    HeadSurface: new(new(0, -.05f), false),
                    BodyVisibility: 1, HeadVisibility: 1);

            var state = new AimAssistState();
            var target = Inside();
            var slowed = RotationalAimAssist.Apply(state, new[] { target },
                new Vector2(.5f, 0), new Vector2(.5f, 0), 0, dt, true, power);
            Check(slowed.TargetSlot == 1 && slowed.Friction < 1
                && slowed.X > 0 && slowed.X < .5f,
                "visible target bubble slows player turn without reversing it");
            Check(slowed.PositionCorrection == Vector2.Zero && slowed.HeadBlend == 0
                && !slowed.FlickActive,
                "production path has no positional magnetism, head refinement or flick snap");

            state.Reset();
            var staticNoPull = RotationalAimAssist.Apply(state, new[] { target },
                Vector2.Zero, new Vector2(.5f, 0), 0, dt, true, power);
            Check(staticNoPull.X == 0 && staticNoPull.Y == 0,
                "static target never moves a stationary camera");

            state.Reset();
            var leftOnlyAcquire = RotationalAimAssist.Apply(state, new[] { target },
                Vector2.Zero, Vector2.Zero, .8f, dt, true, power);
            Check(leftOnlyAcquire.TargetSlot == -1 && leftOnlyAcquire.RotationStrength == 0,
                "left stick alone cannot acquire a target");

            AimAssistState SeedMotion() => new()
            {
                TargetSlot = 1,
                TargetLife = 1,
                PreviousError = Vector2.Zero,
                PreviousDeltaTime = dt,
                PreviousOutput = Vector2.Zero,
                PreviousBodyVisible = true,
                SmoothedBodyVisibility = 1,
                SecondsSinceLookIntent = 0,
                RetainedSeconds = .15f
            };
            var moving = target with
            {
                BodyError = new(.10f, 0),
                BodyRegion = new(.10f, .60f, -.4f, .4f),
                BodySurface = new(new(.10f, 0), false)
            };

            var alignedState = SeedMotion();
            var aligned = RotationalAimAssist.Apply(alignedState, new[] { moving },
                Vector2.Zero, new Vector2(.5f, 0), 0, dt, true, power);
            Check(aligned.TrackingCorrection.X > 0 && aligned.PositionCorrection == Vector2.Zero,
                "aligned right-stick tracking receives target angular velocity only");

            var strafeState = SeedMotion();
            var strafe = RotationalAimAssist.Apply(strafeState, new[] { moving },
                Vector2.Zero, Vector2.Zero, .8f, dt, true, power);
            Check(strafe.StrafeTracking && strafe.TrackingCorrection.X > 0
                && strafe.RotationStrength < aligned.RotationStrength,
                "left-stick retention keeps reduced rotational assistance");

            var opposedState = SeedMotion();
            var opposed = RotationalAimAssist.Apply(opposedState, new[] { moving },
                Vector2.Zero, new Vector2(-.8f, 0), 0, dt, true, power);
            Check(opposed.OpposingBreak && opposed.RotationStrength == 0
                && opposed.TrackingCorrection == Vector2.Zero,
                "strong opposing right-stick input immediately breaks rotation");

            var headState = new AimAssistState();
            var headOffset = target with { HeadError = new(0, -.35f) };
            var headResult = RotationalAimAssist.Apply(headState, new[] { headOffset },
                new Vector2(.25f, 0), new Vector2(.5f, 0), 0, dt, true, power);
            Check(headResult.Y == 0 && headResult.HeadBlend == 0,
                "visible head never creates automatic vertical correction");

            var hiddenState = SeedMotion();
            var hidden = target with
            {
                BodyVisible = false,
                HeadVisible = false,
                BodyVisibility = 0,
                HeadVisibility = 0
            };
            var covered = RotationalAimAssist.Apply(hiddenState, new[] { hidden },
                new Vector2(.2f, .1f), new Vector2(.4f, 0), 0, dt, true, power);
            Check(covered.Occluded && covered.X == .2f && covered.Y == .1f
                && covered.RotationStrength == 0 && covered.Friction == 1,
                "occlusion grace retains identity but never assists through cover");

            var first = target with
            {
                BodyError = new(.10f, 0),
                BodyRegion = new(.10f, .60f, -.4f, .4f),
                BodySurface = new(new(.10f, 0), false)
            };
            var challenger = target with
            {
                Slot = 2,
                BodyError = new(.08f, 0),
                BodyRegion = new(.08f, .58f, -.4f, .4f),
                BodySurface = new(new(.08f, 0), false)
            };
            var hysteresisState = SeedMotion();
            var hysteresis = RotationalAimAssist.Apply(hysteresisState,
                new[] { first, challenger }, Vector2.Zero, new Vector2(.5f, 0),
                0, dt, true, power);
            Check(hysteresis.TargetSlot == 1,
                "small challenger improvement cannot steal a retained target");

            var imperialist = AimAssistWeaponProfile.For(BeamType.Imperialist, 1);
            var powerState = SeedMotion();
            var imperialistState = SeedMotion();
            var powerTrack = RotationalAimAssist.Apply(powerState, new[] { moving },
                Vector2.Zero, new Vector2(.5f, 0), 0, dt, true, power);
            var imperialistTrack = RotationalAimAssist.Apply(imperialistState, new[] { moving },
                Vector2.Zero, new Vector2(.5f, 0), 0, dt, true, imperialist);
            Check(imperialistTrack.RotationStrength < powerTrack.RotationStrength,
                "scoped Imperialist favors precision slowdown over rotational tracking");

            var closeState = SeedMotion();
            var midState = SeedMotion();
            var closeTrack = RotationalAimAssist.Apply(closeState,
                new[] { moving with { Distance = 2 } }, Vector2.Zero,
                new Vector2(.5f, 0), 0, dt, true, power);
            var midTrack = RotationalAimAssist.Apply(midState,
                new[] { moving with { Distance = 12 } }, Vector2.Zero,
                new Vector2(.5f, 0), 0, dt, true, power);
            Check(closeTrack.RotationStrength < midTrack.RotationStrength,
                "point-blank rotation is reduced to avoid camera yanks on fast crossovers");
        }
    }
}
