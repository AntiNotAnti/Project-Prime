using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

internal static partial class MomentumBalanceSceneTests
{
    private const int AdaptiveTicks = 480;
    private sealed record RawCommand(int Tick, bool Forward, bool Left, bool Right, bool Jump,
        float MouseX, float MouseY, float DeltaX);
    private sealed record AdaptiveObservation(int Tick, float PredictedFacingYaw, float ActualFacingYaw,
        float PredictionErrorDegrees, float? TargetFacingYaw, bool CommandClamped, float? WishVelocityProjection,
        bool RegularAirCapWitness, NativeImpulseObservation BeforeNative, NativeImpulseObservation AfterNative, bool MomentumBypass, EnvelopeSample Actual);
    private sealed record NativeImpulseObservation(bool UsedJumpPad, bool Boosting, bool LavaOrAcid,
        int AccelerationTimer, int JumpPadLock, int JumpPadLockMin, float Q3LaunchGravity, int Q3LaunchFrames, bool MomentumEligible)
    {
        public bool ObservedExternalPath => UsedJumpPad || Boosting || LavaOrAcid || AccelerationTimer != 0
            || JumpPadLock != 0 || JumpPadLockMin != 0 || Q3LaunchGravity != 0 || Q3LaunchFrames != 0;
    }
    private sealed record AdaptiveArm(Capture? Body, RawCommand[] Commands, AdaptiveObservation[] Observations,
        string? Failure, int CollisionOverflows, int RecoveryFallbacks);

    internal static int RunAdaptiveEnvelope(string outputPath)
    {
        string path = Path.GetFullPath(outputPath);
        if (File.Exists(path)) { Console.Error.WriteLine("Choose a fresh adaptive envelope JSON; prior evidence is retained."); return 2; }
        _checks = 0;
        var calibrations = new List<object>(); var cases = new List<object>();
        int validPairs = 0, cappedPairs = 0, collisionReviewPairs = 0, externalPathPairs = 0; string? fatal = null;
        try
        {
            Require(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay process");
            Require(ServerSim.Available(out string reason), reason);
            Require(!MphRead.Mods.Input.PointerInput.StylusMode && !MphRead.Mods.Input.PointerDevice.Active
                && InputSettings.MouseSensitivity == 1 && !InputSettings.InvertMouseX && !Features.FixedAimCamera,
                "ordinary default dynamic camera, sensitivity1, noninverted nonstylus mouse");
            Headless.Enter();
            foreach (string room in Rooms)
            foreach (Hunter hunter in Hunters)
            {
                int period = 0; string? calibrationFailure = null;
                try
                {
                    Capture n = RunTrial(room, hunter, false, "jump-only", 180, 60);
                    Capture m = RunTrial(room, hunter, true, "jump-only", 180, 60);
                    Require(n.FirstLandingTick > 2 && n.InferredUpwardJumpPressTicks.SequenceEqual(new[] { 1 })
                        && m.MomentumLegalJumpTicks.SequenceEqual(new[] { 1 }), "stationary native jump calibration");
                    for (int i = 0; i < n.Samples.Length; i++)
                        Require(BitConverter.SingleToInt32Bits(n.Samples[i].Y) == BitConverter.SingleToInt32Bits(m.Samples[i].Y)
                            && BitConverter.SingleToInt32Bits(n.Samples[i].VerticalSpeed) == BitConverter.SingleToInt32Bits(m.Samples[i].VerticalSpeed)
                            && n.Samples[i].Grounded == m.Samples[i].Grounded && n.Samples[i].UsedJump == m.Samples[i].UsedJump,
                            "calibration preserves native Y bits and ground/jump flags");
                    period = n.FirstLandingTick;
                    calibrations.Add(Summary(room, hunter, "jump-only", n, m, period));
                }
                catch (Exception error) { calibrationFailure = error.ToString(); calibrations.Add(new { room, hunter = hunter.ToString(), failure = calibrationFailure }); }
                foreach (bool left in new[] { false, true })
                {
                    string scenario = left ? "adaptive-air-strafe-left" : "adaptive-air-strafe-right";
                    if (calibrationFailure != null)
                    {
                        cases.Add(new { room, hunter = hunter.ToString(), scenario, status = "NOT_RUN_CALIBRATION_FAILED", failure = calibrationFailure });
                        continue;
                    }
                    // Record the actual Momentum controller output first, then replay these exact raw inputs in Native.
                    AdaptiveArm momentum = RunAdaptiveArm(room, hunter, true, left, period, null);
                    AdaptiveArm native = RunAdaptiveArm(room, hunter, false, left, period, momentum.Commands);
                    string? pairFailure = momentum.Failure ?? native.Failure;
                    object? comparison = null;
                    try
                    {
                        Require(momentum.Body != null && native.Body != null, "both arms have actual body samples");
                        comparison = Summary(room, hunter, scenario, native.Body!, momentum.Body!, period);
                        Require(native.Commands.SequenceEqual(momentum.Commands), "Native replay received the exact Momentum-generated raw trace");
                    }
                    catch (Exception error) { pairFailure ??= error.ToString(); }
                    bool capped = momentum.Observations.Any(s => s.RegularAirCapWitness);
                    if (pairFailure == null) validPairs++;
                    bool externalPath = native.Observations.Concat(momentum.Observations).Any(s => s.BeforeNative.ObservedExternalPath || s.AfterNative.ObservedExternalPath);
                    if (pairFailure == null && capped && !externalPath) cappedPairs++;
                    if (externalPath) externalPathPairs++;
                    if (native.CollisionOverflows > 0 || native.RecoveryFallbacks > 0 || momentum.CollisionOverflows > 0 || momentum.RecoveryFallbacks > 0) collisionReviewPairs++;
                    cases.Add(new { room, hunter = hunter.ToString(), scenario, failure = pairFailure, comparison,
                        native = AdaptiveDetails(native), momentum = AdaptiveDetails(momentum),
                        capCoverage = !capped ? "not reached in bounded observation" : externalPath ? "regular incoming air-cap witness; observed external path requires qualification" : "regular incoming air-cap witness; no external path observed in pre/post state" });
                    Console.WriteLine($"MOMENTUM ADAPTIVE ENVELOPE {room}/{hunter}/{scenario}: valid={pairFailure == null}, cap={capped}");
                }
            }
        }
        catch (Exception error) { fatal = error.ToString(); }
        string status = fatal != null || validPairs != 28 ? "FAIL_CAPTURE" : collisionReviewPairs > 0 ? "REVIEW_COLLISION_COUNTERS" : externalPathPairs > 0 ? "REVIEW_EXTERNAL_PATH" : cappedPairs == 28 ? "PASS" : "INCOMPLETE_CAP_COVERAGE";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new {
            status, checks = _checks, fatal, validPairs, cappedPairs, collisionReviewPairs, externalPathPairs, expectedPairs = 28,
            scope = "Bounded synthetic ordinary-input cap observation, actual authored offline Native/Momentum bodies; not competitive balance, physical devices, general collision safety, maximum achievable speed or performance acceptance",
            platform = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), runtime = RuntimeInformation.FrameworkDescription,
            profileVersion = MomentumProfileV1.MomentumProfileVersion, retentionSpeedCap = MomentumProfileV1.RetentionSpeedCap,
            conditions = new {
                simulationHz = 60, rendering = "headless", network = "offline", players = 1, samplesPerArm = AdaptiveTicks,
                seed = "existing unchanged Rng1 8128 / Rng2 0x8128a5a5 native spawn; no reseed/retry search",
                schedule = "W1-48; then mirrored D/A and Space49 plus fixed stationary Native landing period; no contact-dependent jump input",
                controller = "Each 60Hz Momentum input observes actual velocity/body/gun yaw; wish targets perpendicular to actual incoming velocity. Pure default dynamic-camera yaw predictor chooses raw mouse via20 bounded inverse iterations, +/-256px, quantized1/256px. No engine lookahead or state writes.",
                baseline = "Fresh Native arm replays the exact recorded Momentum raw key/mouse trace, not an independently adaptive policy. Controller-generated traces differ by Hunter/map/mirror; every prescribed case and failure is retained.",
                predictor = "PlayerInput.UpdateAimY(0), UpdateAimX, UpdateAimFacing default zero-pitch/nozoom path only; actual next facing checked within0.02degree, otherwise capture fails closed. No private aim method invocation or production API.",
                capWitness = "Numerical regular-cap witness: actual eligible air/legal-jump step is SpeedLimited with no incoming overspeed, uncapped outside cap and actual seam inside. Pre/post pad/boost/hazard/acceleration/locks/Q3 and eligibility observations qualify provenance; any observed external path prevents unqualified arm acceptance. Incoming speed alone does not establish impulse history; incoming overspeed exemptions retained.",
                collision = "Actual native counter deltas and body trajectories only. Zero overflow/recovery does not by itself prove no penetration; nonzero counters require review. Fixed geometry can prevent cap coverage without implying a solver failure.",
                retention = "All480 commands/body/solver observations retained when completed; failing arms retain generated command and completed sample prefixes; all later prescribed cases still attempted; missing calibration explicitly not run. No favorable rerolls or profile changes."
            }, calibrations, cases
        }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        Console.WriteLine($"MOMENTUM ADAPTIVE ENVELOPE {status}: {validPairs}/28 valid, {cappedPairs}/28 cap witnesses");
        return status == "PASS" ? 0 : 1;
    }

    private static object AdaptiveDetails(AdaptiveArm arm) => new {
        arm.Failure, arm.CollisionOverflows, arm.RecoveryFallbacks,
        collisionReviewRequired = arm.CollisionOverflows > 0 || arm.RecoveryFallbacks > 0,
        generatedCommandCount = arm.Commands.Length, completedBodySampleCount = arm.Body?.Samples.Length ?? 0,
        rawCommandTraceSha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(arm.Commands))),
        maximumYawPredictionErrorDegrees = arm.Observations.Length > 0 ? (float?)arm.Observations.Max(s => s.PredictionErrorDegrees) : null,
        clampedCommandSteps = arm.Observations.Count(s => s.CommandClamped),
        regularIncomingAirCapWitnesses = arm.Observations.Count(s => s.RegularAirCapWitness),
        observedExternalPathTicks = arm.Observations.Count(s => s.BeforeNative.ObservedExternalPath || s.AfterNative.ObservedExternalPath),
        momentumBypassTicks = arm.Observations.Count(s => s.MomentumBypass),
        incomingOverspeedSteps = arm.Observations.Count(s => s.Actual.Solver?.IncomingOverCap == true),
        commands = arm.Commands, observations = arm.Observations
    };

    private static AdaptiveArm RunAdaptiveArm(string room, Hunter hunter, bool momentum, bool left, int period, RawCommand[]? replay)
    {
        Scene? scene = null; string? failure = null;
        var bodies = new List<Sample>(AdaptiveTicks); var commands = new List<RawCommand>(AdaptiveTicks);
        var observations = new List<AdaptiveObservation>(AdaptiveTicks); var inferred = new List<int>(); var accepted = new List<int>();
        Vector3 origin = default, facing = default; int firstLanding = -1, seams = 0, limited = 0, overflows = 0, recoveries = 0; long solverSteps = 0;
        try
        {
            Require(momentum == (replay == null), "Momentum records feedback input; Native only replays its trace");
            scene = MomentumSceneTests.Load(hunter, momentum, out KeyboardState keyboard, room, matchRule: true);
            PlayerEntity player = scene.Players.Items[0];
            player.Controls.MouseAim = true; player.Controls.KeyboardAim = false;
            Require(!player.Controls.InvertAimX, "ordinary noninverted native mouse binding");
            var mouse = (MouseState)typeof(Scene).GetField("_mouseState", Hidden)!.GetValue(scene)!;
            var input = (PlayerEntity.PlayerInput)typeof(PlayerEntity).GetProperty("Input", Hidden)!.GetValue(player)!;
            var setPosition = typeof(MouseState).GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!.CreateDelegate<Action<MouseState, Vector2>>();
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            string[] impulseFields = { "_accelerationTimer", "_jumpPadControlLock", "_jumpPadControlLockMin", "_q3LaunchGravity", "_q3LaunchFrames" };
            var fields = impulseFields.Select(name => typeof(PlayerEntity).GetField(name, Hidden)).ToArray();
            Require(fields.All(field => field != null), "native impulse telemetry fields exist; read-only observers");
            NativeImpulseObservation ObserveNative() => new(player.Flags1.TestFlag(PlayerFlags1.UsedJumpPad),
                player.Flags1.TestFlag(PlayerFlags1.Boosting), player.Flags1.TestAny(PlayerFlags1.OnLava | PlayerFlags1.OnAcid),
                Convert.ToInt32(fields[0]!.GetValue(player)), Convert.ToInt32(fields[1]!.GetValue(player)),
                Convert.ToInt32(fields[2]!.GetValue(player)), Convert.ToSingle(fields[3]!.GetValue(player)),
                Convert.ToInt32(fields[4]!.GetValue(player)), player.ModMomentumEligible);
            Vector2 pointer = mouse.Position; origin = player.Position; facing = player.FacingVector; Vector3 previous = origin;
            float previousY = player.Speed.Y, distance = 0; bool airborne = false;
            int count = replay?.Length ?? AdaptiveTicks;
            for (int index = 0; index < count; index++)
            {
                Require(!Features.FixedAimCamera && InputSettings.MouseSensitivity == 1 && !InputSettings.InvertMouseX
                    && !MphRead.Mods.Input.PointerInput.StylusMode && !MphRead.Mods.Input.PointerDevice.Active
                    && player.CameraType == CameraType.First && !player.EquipInfo.Zoomed && !player.Flags1.TestFlag(PlayerFlags1.NoAimInput) && MathF.Abs(player.ModGunVector.Y) < .00001f
                    && MathF.Abs(player.FacingVector.Y) < .00001f, "default dynamic nozoom zero-pitch predictor preconditions");
                bool turn = index >= 48; float? targetYaw = null; bool clamped = false;
                RawCommand command;
                if (replay != null) command = replay[index];
                else
                {
                    float delta = 0;
                    if (turn)
                    {
                        Vector2 velocity = new(player.Speed.X, player.Speed.Z);
                        targetYaw = velocity.LengthSquared > .0000001f ? AdaptiveYaw(velocity.X, velocity.Y) : AdaptiveYaw(player.FacingVector.X, player.FacingVector.Z);
                        (delta, clamped) = AdaptiveMouse(player.FacingVector, player.ModGunVector, targetYaw.Value);
                    }
                    float previousX = pointer.X; pointer.X += delta;
                    command = new(index + 1, !turn, turn && left, turn && !left, turn && (index - 48) % period == 0,
                        pointer.X, pointer.Y, pointer.X - previousX);
                }
                Require(command.Tick == index + 1 && command.MouseY == pointer.Y, "fixed trace identity and zero vertical pointer motion");
                float actualRequestedDelta = command.MouseX - mouse.X;
                Require(actualRequestedDelta == command.DeltaX, "raw trace delta agrees with cumulative mouse producer");
                float predicted = AdaptivePredict(player.FacingVector, player.ModGunVector, actualRequestedDelta);
                setKey(keyboard, Keys.W, command.Forward); setKey(keyboard, Keys.A, command.Left);
                setKey(keyboard, Keys.D, command.Right); setKey(keyboard, Keys.Space, command.Jump);
                setPosition(mouse, new Vector2(command.MouseX, command.MouseY)); commands.Add(command);
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                int overflowBefore = player.ModCollisionContactOverflows, recoveryBefore = player.ModCollisionRecoveryFallbacks;
                bool used = player.Flags1.TestFlag(PlayerFlags1.UsedJump);
                NativeImpulseObservation beforeNative = ObserveNative();
                scene.OnSimulationFrame();
                NativeImpulseObservation afterNative = ObserveNative();
                Vector3 p = player.Position, v = player.Speed;
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                bool upward = command.Jump && !used && player.Flags1.TestFlag(PlayerFlags1.UsedJump) && v.Y > .01f && v.Y > previousY + .01f;
                previousY = v.Y; if (upward) inferred.Add(index + 1);
                if (airborne && grounded && firstLanding < 0) firstLanding = index + 1; airborne |= !grounded;
                distance += new Vector2(p.X - previous.X, p.Z - previous.Z).Length; previous = p;
                bool legal = false, capWitness = false; float? projection = null; EnvelopeSolver? solver = null;
                long steps = scene.DeveloperMomentumSteps - before; solverSteps += steps;
                Require(momentum ? steps is >= 0 and <= 1 : steps == 0, "one eligible Momentum seam at most; no Native solver");
                if (steps == 1)
                {
                    var s = scene.LastMomentumStep!.Value;
                    Require(s.SimulationFrame == frame && s.Slot == player.SlotIndex, "actual same-tick body solver witness");
                    Require(BitConverter.SingleToInt32Bits(s.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(s.NativeVelocityAfter.Y), "native Y bits unchanged at horizontal seam"); seams++;
                    legal = s.ContactPhase == MomentumContactPhase.LegalJump;
                    if (legal)
                    {
                        Require(command.Jump && BitConverter.SingleToInt32Bits(s.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(Fixed.ToFloat(player.Values.JumpSpeed)), "actual native-accepted Hunter jump impulse");
                        accepted.Add(index + 1);
                    }
                    var incoming = new Vector3(s.NativeVelocityBefore.X * 30, 0, s.NativeVelocityBefore.Z * 30);
                    Vector3 uncapped = MomentumMath.Step(incoming, s.Input, s.Tuning);
                    var expected = MomentumProfileV1.Step(incoming, s.Input);
                    float outgoing = new Vector2(s.NativeVelocityAfter.X, s.NativeVelocityAfter.Z).Length * 30;
                    Require(MathF.Abs(s.NativeVelocityAfter.X - expected.Velocity.X / 30) <= .00001f && MathF.Abs(s.NativeVelocityAfter.Z - expected.Velocity.Z / 30) <= .00001f
                        && s.SpeedLimited == expected.SpeedLimited && s.IncomingOverCap == expected.IncomingOverCap, "actual unchanged profile result and flags");
                    Require(outgoing <= MathF.Max(incoming.Length, MomentumProfileV1.RetentionSpeedCap) + .0001f, "retention policy preserves incoming overspeed exemptions without steering growth");
                    if (s.SpeedLimited) limited++;
                    Vector3 wish = s.Input.FlatRight * s.Input.MoveAxes.X + s.Input.FlatForward * s.Input.MoveAxes.Y;
                    if (wish.LengthSquared > 0) projection = Vector3.Dot(incoming, wish.Normalized());
                    capWitness = !s.Input.Grounded && s.SpeedLimited && !s.IncomingOverCap;
                    if (capWitness) Require(uncapped.Length > MomentumProfileV1.RetentionSpeedCap && outgoing <= MomentumProfileV1.RetentionSpeedCap + .0001f, "uncapped air result exceeds regular cap and actual seam remains within it");
                    solver = new(s.ContactPhase.ToString(), s.Input.Grounded, s.Input.MoveAxes.X, s.Input.MoveAxes.Y,
                        s.Input.DesiredSpeed * MathF.Min(1, s.Input.MoveAxes.Length), incoming.Length, uncapped.Length, outgoing,
                        s.Input.Grounded ? null : uncapped.X - incoming.X, s.Input.Grounded ? null : uncapped.Z - incoming.Z, s.SpeedLimited, s.IncomingOverCap);
                }
                int overflow = player.ModCollisionContactOverflows - overflowBefore, recovery = player.ModCollisionRecoveryFallbacks - recoveryBefore;
                overflows += overflow; recoveries += recovery;
                float actualYaw = AdaptiveYaw(player.FacingVector.X, player.FacingVector.Z);
                float error = MathF.Abs(AdaptiveWrap(actualYaw - predicted));
                bodies.Add(new(index + 1, p.X, p.Y, p.Z, v.Y, new Vector2(v.X, v.Z).Length * 30, grounded,
                    player.Flags1.TestFlag(PlayerFlags1.UsedJump), command.Jump, upward, legal,
                    new Vector2(p.X - origin.X, p.Z - origin.Z).Length, distance));
                observations.Add(new(index + 1, predicted, actualYaw, error, targetYaw, clamped, projection, capWitness, beforeNative, afterNative, momentum && steps == 0,
                    new(index + 1, player.FacingVector.X, player.FacingVector.Z, command.DeltaX, input.MouseDeltaX, overflow, recovery, solver)));
                Require(player.ModIsInPlay && player.Health > 0 && !player.IsBot && !player.IsAltForm && !NetSession.Active, "living genuine offline native biped");
                Require(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), "finite native body");
                Require(player.Controls.Jump.IsPressed == command.Jump && player.Controls.MoveUp.IsDown == command.Forward
                    && player.Controls.MoveLeft.IsDown == command.Left && player.Controls.MoveRight.IsDown == command.Right, "actual gathered keyboard trace");
                Require(input.MouseDeltaX == command.DeltaX && input.MouseDeltaY == 0, "actual gathered raw mouse trace");
                Require(error <= .02f, "actual body yaw agrees with declared dynamic predictor within0.02degree");
                Require(overflow >= 0 && recovery >= 0, "native collision counters do not reset");
            }
            Require(bodies.Count == AdaptiveTicks, "all480 bounded body samples completed");
            Require(bodies[47].PlanarSpeed > 1 && bodies.Max(s => s.PlanarDisplacement) > 1, "actual run-up and nontrivial body traversal");
            Require(!momentum || accepted.Count >= 2, "repeated actual Momentum native-accepted jumps");
            Require(observations.Skip(48).Any(s => s.Actual.GatheredMouseDeltaX != 0)
                && observations.Skip(48).Any(s => MathF.Abs(AdaptiveWrap(s.ActualFacingYaw - AdaptiveYaw(facing.X, facing.Z))) > .01f), "actual gathered turn input and body turning");
        }
        catch (Exception error) { failure = error.ToString(); }
        finally
        {
            if (scene != null) try { MomentumSceneTests.Release(scene); } catch (Exception error) { failure ??= error.ToString(); }
        }
        Capture? body = bodies.Count == 0 ? null : new(origin, facing, bodies.ToArray(), inferred.ToArray(), accepted.ToArray(), firstLanding, solverSteps, seams, limited, 0);
        return new(body, commands.ToArray(), observations.ToArray(), failure, overflows, recoveries);
    }

    private static float AdaptiveYaw(float x, float z) => MathHelper.RadiansToDegrees(MathF.Atan2(x, z));
    private static float AdaptiveWrap(float angle) => (angle + 540) % 360 - 180;

    // Read-only yaw model of PlayerInput.UpdateAimFacing for default dynamic, zero-pitch/nozoom input.
    // UpdateAimY(0) eases once before mouse UpdateAimX rotates the gun and eases again.
    private static float AdaptivePredict(Vector3 facing, Vector3 gun, float rawDelta)
    {
        static double Ease(double body, double aim)
        {
            double difference = (body - aim + 3 * Math.PI) % (2 * Math.PI) - Math.PI;
            if (Math.Cos(difference) < 3956d / 4096)
                difference = Math.CopySign(Math.Atan2(1060, 3956), difference);
            return aim + Math.Atan2(.9 * Math.Sin(difference), .9 * Math.Cos(difference) + .1);
        }
        double bodyYaw = Math.Atan2(facing.X, facing.Z), gunYaw = Math.Atan2(gun.X, gun.Z);
        bodyYaw = Ease(bodyYaw, gunYaw);
        gunYaw -= rawDelta / 4d * Math.PI / 180;
        return (float)(Ease(bodyYaw, gunYaw) * 180 / Math.PI);
    }

    private static (float Delta, bool Clamped) AdaptiveMouse(Vector3 facing, Vector3 gun, float target)
    {
        float baseline = AdaptivePredict(facing, gun, 0), goal = AdaptiveWrap(target - baseline);
        float low = -256, high = 256;
        float maximum = AdaptiveWrap(AdaptivePredict(facing, gun, low) - baseline);
        float minimum = AdaptiveWrap(AdaptivePredict(facing, gun, high) - baseline);
        bool clamped = goal < minimum || goal > maximum;
        goal = Math.Clamp(goal, minimum, maximum);
        for (int i = 0; i < 20; i++)
        {
            float middle = (low + high) / 2;
            if (AdaptiveWrap(AdaptivePredict(facing, gun, middle) - baseline) > goal) low = middle;
            else high = middle;
        }
        return (MathF.Round((low + high) / 2 * 256, MidpointRounding.AwayFromZero) / 256, clamped);
    }
}
