using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

// Bounded, opt-in acceptance capture. Real keyboard controls, native life/jump/collision;
// no velocity, position, flags, ability, or health overrides in the trial.
internal static partial class MomentumBalanceSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int SimulationHz = 60;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly string[] Rooms = { "MP3 PROVING GROUND", "MP1 SANCTORUS" };
    private static readonly float[] ObjectiveDistances = { 3, 6, 10 };
    private static int _checks;
    private sealed record Sample(int Tick, float X, float Y, float Z, float VerticalSpeed, float PlanarSpeed,
        bool Grounded, bool UsedJump, bool JumpPressed, bool InferredUpwardJumpPress, bool MomentumLegalJump, float PlanarDisplacement, float PlanarPathDistance);
    private sealed record Capture(Vector3 StartPosition, Vector3 StartFacing, Sample[] Samples, int[] InferredUpwardJumpPressTicks, int[] MomentumLegalJumpTicks, int FirstLandingTick, long SolverSteps,
        int VerticalSeamChecks, int SpeedLimitedSteps, int NeutralGroundBrakingSteps);
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }

    internal static int Run(string? outputPath)
    {
        try
        {
            _checks = 0;
            Require(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay process");
            Require(ServerSim.Available(out string reason), reason);
            Headless.Enter();
            var summaries = new List<object>();
            foreach (string room in Rooms)
            foreach (Hunter hunter in Hunters)
            {
                Capture nativeJump = RunTrial(room, hunter, false, "jump-only", 180, 60);
                Capture momentumJump = RunTrial(room, hunter, true, "jump-only", 180, 60);
                Require(nativeJump.InferredUpwardJumpPressTicks.SequenceEqual(new[] { 1 }), $"{room}/{hunter} native jump calibration");
                Require(nativeJump.FirstLandingTick > 2, $"{room}/{hunter} native solid landing");
                Require(nativeJump.InferredUpwardJumpPressTicks.SequenceEqual(momentumJump.MomentumLegalJumpTicks), "stationary native legal impulse tick parity");
                for (int i = 0; i < nativeJump.Samples.Length; i++)
                {
                    Sample n = nativeJump.Samples[i], m = momentumJump.Samples[i];
                    Require(BitConverter.SingleToInt32Bits(n.Y) == BitConverter.SingleToInt32Bits(m.Y)
                        && BitConverter.SingleToInt32Bits(n.VerticalSpeed) == BitConverter.SingleToInt32Bits(m.VerticalSpeed)
                        && n.Grounded == m.Grounded && n.UsedJump == m.UsedJump,
                        $"{room}/{hunter} stationary jump native Y/velocity bits and flags at tick {i + 1}");
                }
                summaries.Add(Summary(room, hunter, "jump-only", nativeJump, momentumJump, nativeJump.FirstLandingTick));
                foreach (string scenario in new[] { "manual-hop-chain", "running-manual-hop-chain", "forward-release" })
                {
                    int length = scenario == "forward-release" ? 240 : 480;
                    Capture native = RunTrial(room, hunter, false, scenario, length, nativeJump.FirstLandingTick);
                    Capture momentum = RunTrial(room, hunter, true, scenario, length, nativeJump.FirstLandingTick);
                    Require(native.Samples.Max(s => s.PlanarDisplacement) > 1 && momentum.Samples.Max(s => s.PlanarDisplacement) > 1,
                        $"{room}/{hunter}/{scenario} nontrivial genuine native collision-body displacement");
                    if (scenario is "manual-hop-chain" or "running-manual-hop-chain")
                        Require(native.InferredUpwardJumpPressTicks.Length >= 2 && momentum.MomentumLegalJumpTicks.Length >= 2,
                            $"{room}/{hunter} manual chain has repeated actual Momentum native-accepted seam impulses and Native upward press evidence");
                    if (scenario == "running-manual-hop-chain")
                        Require(native.Samples[47].PlanarSpeed > 1 && momentum.Samples[47].PlanarSpeed > 1,
                            $"{room}/{hunter} running chain has actual acquired speed before first jump request");
                    summaries.Add(Summary(room, hunter, scenario, native, momentum, nativeJump.FirstLandingTick));
                    Console.WriteLine($"MOMENTUM BALANCE SCENE PASS {room}/{hunter}/{scenario}: native p50={Percentile(native.Samples.Select(s => s.PlanarSpeed), .5f):F3}, momentum p50={Percentile(momentum.Samples.Select(s => s.PlanarSpeed), .5f):F3}");
                }
            }
            if (outputPath != null)
            {
                string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new {
                    checks = _checks,
                    scope = "asset-backed offline CPU keyboard/input/collision comparison; single living biped, native seeded spawn and intro skip; observational balance evidence, no competitive balance acceptance or device/render/network claim",
                    platform = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    runtime = RuntimeInformation.FrameworkDescription, profileVersion = MomentumProfileV1.MomentumProfileVersion,
                    conditions = new { simulationHz = SimulationHz, renderCadence = "headless; no rendering", playerCount = 1,
                        networkImpairment = "offline; no packets", movementSelection = "pre-load MatchDefinition Native/Momentum rule; diagnostics enabled only for Momentum", spawn = "unchanged seeded native spawn (Rng1 8128, Rng2 0x8128a5a5); 120 native idle settling ticks after introduction End",
                        jumpSchedule = "jump-only Space on tick1; cold manual-hop-chain Space on tick1 then every stationary Native firstLandingTick; running-manual-hop-chain W ticks1-48, first Space tick49 then every same Native period. W on every non-jump chain tick. Both models receive identical fixed input ticks; no automatic landing buffer",
                        coastSchedule = "W ticks1-80; all movement released ticks81-240; no jump",
                        units = "speed = length(native planar Speed) * 30 in world units/second; each native simulation step is 1/60 second",
                        objectives = "first tick reaching planar radial displacement 3/6/10 world units from actual spawn; same objective per pair. Null time means not reached in bounded trial (collision can obstruct route); path distance also reported and is not objective displacement",
                        jumpEvidence = "MomentumLegalJumpTicks uses same-frame nativeJumpAccepted ContactPhase seam with exact native Hunter jump impulse; InferredUpwardJumpPressTicks requires a fresh UsedJump flag plus post-collision upward-velocity increase, explicitly not proof of Native accepted jumps",
                        boundedSamplesPerTrial = 480, sourceIdentity = "record source SHA/dirty diff, CPU and build flags in the invoking evidence manifest" }, summaries
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM BALANCE SCENE PASS {_checks} assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM BALANCE SCENE FAIL " + ex); return 1; }
    }

    private sealed record EnvelopeSolver(string ContactPhase, bool FrictionGrounded, float MoveX, float MoveY,
        float WishSpeed, float IncomingSpeed, float UncappedSpeed, float OutgoingSpeed,
        float? AirAccelerationX, float? AirAccelerationZ, bool SpeedLimited, bool IncomingOverCap);
    private sealed record EnvelopeSample(int Tick, float FacingX, float FacingZ, float RequestedMouseDeltaX, float GatheredMouseDeltaX,
        int CollisionOverflowDelta, int RecoveryFallbackDelta, EnvelopeSolver? Solver);
    private sealed record EnvelopeCapture(Capture Body, EnvelopeSample[] Samples, int IncomingOverCapSteps,
        int NonzeroAirAccelerationSteps, int AimFrames, int FacingChangedFrames,
        int CollisionOverflows, int RecoveryFallbacks);

    // Optional route only. The original four-scenario Run and JSON remain unchanged.
    internal static int RunEnvelope(string outputPath)
    {
        string path = Path.GetFullPath(outputPath);
        if (File.Exists(path))
        {
            Console.Error.WriteLine("MOMENTUM ENVELOPE SCENE FAIL choose a fresh output JSON; prior evidence is retained");
            return 1;
        }
        var calibrations = new List<object>(); var summaries = new List<object>();
        string? failure = null, activeTrial = null;
        try
        {
            _checks = 0;
            Require(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay process");
            Require(ServerSim.Available(out string reason), reason);
            Require(!MphRead.Mods.Input.PointerInput.StylusMode && !MphRead.Mods.Input.PointerDevice.Active
                && InputSettings.MouseSensitivity == 1 && !InputSettings.InvertMouseX,
                "fresh ordinary mouse provider with declared default sensitivity/inversion");
            Headless.Enter();
            foreach (string room in Rooms)
            foreach (Hunter hunter in Hunters)
            {
                activeTrial = $"{room}/{hunter}/stationary-calibration";
                Capture nativeJump = RunTrial(room, hunter, false, "jump-only", 180, 60);
                Capture momentumJump = RunTrial(room, hunter, true, "jump-only", 180, 60);
                Require(nativeJump.FirstLandingTick > 2
                    && nativeJump.InferredUpwardJumpPressTicks.SequenceEqual(new[] { 1 })
                    && momentumJump.MomentumLegalJumpTicks.SequenceEqual(new[] { 1 }), "native grounded jump/landing calibration");
                for (int tick = 0; tick < nativeJump.Samples.Length; tick++)
                {
                    Sample n = nativeJump.Samples[tick], m = momentumJump.Samples[tick];
                    Require(BitConverter.SingleToInt32Bits(n.Y) == BitConverter.SingleToInt32Bits(m.Y)
                        && BitConverter.SingleToInt32Bits(n.VerticalSpeed) == BitConverter.SingleToInt32Bits(m.VerticalSpeed)
                        && n.Grounded == m.Grounded && n.UsedJump == m.UsedJump,
                        "stationary calibration preserves native Y/velocity bits and jump/ground flags");
                }
                int period = nativeJump.FirstLandingTick;
                calibrations.Add(Summary(room, hunter, "jump-only", nativeJump, momentumJump, period));
                foreach (bool left in new[] { false, true })
                {
                    string scenario = left ? "air-turn-hop-left" : "air-turn-hop-right";
                    activeTrial = $"{room}/{hunter}/{scenario}/Native";
                    EnvelopeCapture native = RunEnvelopeTrial(room, hunter, false, left, period);
                    activeTrial = $"{room}/{hunter}/{scenario}/Momentum";
                    EnvelopeCapture momentum = RunEnvelopeTrial(room, hunter, true, left, period);
                    Require(native.Body.Samples[47].PlanarSpeed > 1 && momentum.Body.Samples[47].PlanarSpeed > 1,
                        "actual input acquires running speed before the first requested jump");
                    Require(native.AimFrames > 0 && momentum.AimFrames > 0
                        && native.FacingChangedFrames > 0 && momentum.FacingChangedFrames > 0,
                        "actual gathered relative mouse input turns both native bodies");
                    Require(native.Body.Samples.Max(s => s.PlanarDisplacement) > 1
                        && momentum.Body.Samples.Max(s => s.PlanarDisplacement) > 1, "actual collision bodies traverse");
                    Require(momentum.Body.MomentumLegalJumpTicks.Length >= 2, "Momentum trace contains repeated actual native-accepted jumps");
                    Require(momentum.NonzeroAirAccelerationSteps > 0, "actual nonzero air-acceleration contribution observed before retention limiting");
                    summaries.Add(new {
                        room, hunter = hunter.ToString(), scenario,
                        comparison = Summary(room, hunter, scenario, native.Body, momentum.Body, period),
                        native = EnvelopeDetails(native), momentum = EnvelopeDetails(momentum),
                        capEngaged = momentum.Body.SpeedLimitedSteps > 0,
                        capCoverage = momentum.Body.SpeedLimitedSteps > 0 ? "engaged" : "not reached in fixed observation",
                        capCoverageNote = "No cap requirement or seed/schedule reroll; native geometry/contact can censor this bounded trial. A non-engagement cause is not inferred from query scratch."
                    });
                    Console.WriteLine($"MOMENTUM ENVELOPE SCENE CAPTURE {room}/{hunter}/{scenario}: cap steps={momentum.Body.SpeedLimitedSteps}, incoming over cap={momentum.IncomingOverCapSteps}");
                }
            }
        }
        catch (Exception error) { failure = activeTrial + ": " + error; Console.Error.WriteLine("MOMENTUM ENVELOPE SCENE FAIL " + error); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new {
            checks = _checks, failure, activeTrial, completedEnvelopePairs = summaries.Count, expectedEnvelopePairs = 28,
            scope = "Optional asset-backed fixed-input envelope observation; actual offline Native/Momentum biped input and authored collision; no forced body/life/ability state, no cap calibration or competitive/performance acceptance",
            platform = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription, profileVersion = MomentumProfileV1.MomentumProfileVersion,
            retentionSpeedCap = MomentumProfileV1.RetentionSpeedCap,
            conditions = new {
                simulationHz = 60, rendering = "headless; no achieved display-Hz claim", players = 1,
                network = "offline; no packets", samplesPerEnvelopeArm = 480, calibrationSamplesPerArm = 180,
                seed = "existing helper Rng1 8128 / Rng2 0x8128a5a5, unchanged native spawn and facing",
                schedule = "W ticks1-48; Space tick49 then one-tick pulses every same stationary Native firstLanding period; ticks49-480 hold D with cumulative mouse +4px/tick for right case, A with cumulative mouse -4px/tick for left case; W released; no contact-adaptive input",
                pairedInput = "Both models receive identical fixed keyboard levels/edges and raw cumulative MouseState positions through production snapshots/UpdatePointer/UpdateAimX; mouse sensitivity1, inversionfalse, no stylus; mirrored requests need not produce symmetric map trajectories",
                nativeJumpEvidence = "Native upward press ticks are inferred, not an accepted-jump seam; Momentum uses actual same-frame nativeJumpAccepted contact with exact native jump impulse and preserved Y bits",
                telemetry = "Bounded 480-step arrays only; Native solver cells are null; air contribution is pure uncapped air-step delta before retention limiting; collision counters are actual native deltas, not trace-buffer overflow or resolved-contact normals",
                sourceIdentity = "invoker records source/dirty patch, binaries, CPU and build flags; this functional fixture is not a timing campaign"
            }, calibrations, summaries
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (failure == null) Console.WriteLine($"MOMENTUM ENVELOPE SCENE CAPTURE PASS {_checks} assertions; cap engagement is observational");
        return failure == null ? 0 : 1;
    }

    private static object EnvelopeDetails(EnvelopeCapture capture) => new {
        capture.IncomingOverCapSteps, capture.NonzeroAirAccelerationSteps, capture.AimFrames, capture.FacingChangedFrames,
        capture.CollisionOverflows, capture.RecoveryFallbacks,
        collisionReviewRequired = capture.CollisionOverflows > 0 || capture.RecoveryFallbacks > 0,
        runUpSpeedTick48 = capture.Body.Samples[47].PlanarSpeed,
        runUpGroundedTick48 = capture.Body.Samples[47].Grounded, samples = capture.Samples
    };

    private static EnvelopeCapture RunEnvelopeTrial(string room, Hunter hunter, bool momentum, bool left, int period)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out KeyboardState keyboard, room, matchRule: true);
        try
        {
            PlayerEntity player = scene.Players.Items[0];
            player.Controls.MouseAim = true; player.Controls.KeyboardAim = false;
            Require(!player.Controls.InvertAimX, "ordinary noninverted native mouse binding");
            // Same raw position producer as AndroidInput.MovePointer/PointerCheck; only input samples are written.
            var mouse = (MouseState)typeof(Scene).GetField("_mouseState", Hidden)!.GetValue(scene)!;
            var input = (PlayerEntity.PlayerInput)typeof(PlayerEntity).GetProperty("Input", Hidden)!.GetValue(player)!;
            var setPosition = typeof(MouseState).GetProperty("Position", Hidden | BindingFlags.Public)!.SetMethod!
                .CreateDelegate<Action<MouseState, Vector2>>();
            Vector2 pointer = mouse.Position;
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            Vector3 origin = player.Position, previous = origin, facing = player.FacingVector;
            float previousVertical = player.Speed.Y, path = 0;
            var bodies = new Sample[480]; var samples = new EnvelopeSample[480];
            var inferred = new List<int>(); var accepted = new List<int>();
            long startSteps = scene.DeveloperMomentumSteps;
            int startOverflow = player.ModCollisionContactOverflows, startRecovery = player.ModCollisionRecoveryFallbacks;
            int firstLanding = -1, seam = 0, limited = 0, braking = 0, incomingOverCap = 0, nonzeroAir = 0, aiming = 0, turned = 0;
            bool airborne = false;
            for (int index = 0; index < bodies.Length; index++)
            {
                bool turn = index >= 48, jump = turn && (index - 48) % period == 0;
                setKey(keyboard, Keys.W, !turn); setKey(keyboard, Keys.Space, jump);
                setKey(keyboard, Keys.A, turn && left); setKey(keyboard, Keys.D, turn && !left);
                float requestedMouseDeltaX = turn ? (left ? -4 : 4) : 0;
                pointer.X += requestedMouseDeltaX;
                setPosition(mouse, pointer);
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                int overflowBefore = player.ModCollisionContactOverflows, recoveryBefore = player.ModCollisionRecoveryFallbacks;
                bool usedJumpBefore = player.Flags1.TestFlag(PlayerFlags1.UsedJump);
                scene.OnSimulationFrame();
                Require(player.ModIsInPlay && player.Health > 0 && !player.IsBot && !player.IsAltForm && !NetSession.Active,
                    "envelope trial retains genuine offline living native biped");
                Vector3 p = player.Position, v = player.Speed;
                Require(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
                    && float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), "finite native body");
                Require(player.Controls.Jump.IsPressed == jump && player.Controls.MoveUp.IsDown == !turn
                    && player.Controls.MoveLeft.IsDown == (turn && left) && player.Controls.MoveRight.IsDown == (turn && !left),
                    "production gathered keyboard controls match the fixed schedule");
                Require(input.MouseDeltaX == requestedMouseDeltaX && input.MouseDeltaY == 0,
                    "production snapshots/UpdatePointer gather the exact fixed raw mouse delta");
                bool upward = jump && !usedJumpBefore && player.Flags1.TestFlag(PlayerFlags1.UsedJump)
                    && v.Y > .01f && v.Y > previousVertical + .01f;
                if (upward) inferred.Add(index + 1);
                previousVertical = v.Y;
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                if (airborne && grounded && firstLanding < 0) firstLanding = index + 1;
                airborne |= !grounded;
                path += new Vector2(p.X - previous.X, p.Z - previous.Z).Length; previous = p;
                bool legal = false; EnvelopeSolver? solver = null;
                long steps = scene.DeveloperMomentumSteps - before;
                Require(momentum ? steps is >= 0 and <= 1 : steps == 0, "one eligible Momentum step maximum; Native invokes none");
                if (steps == 1)
                {
                    var s = scene.LastMomentumStep!.Value;
                    Require(s.SimulationFrame == frame && s.Slot == player.SlotIndex, "solver witness belongs to actual body/tick");
                    Require(BitConverter.SingleToInt32Bits(s.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(s.NativeVelocityAfter.Y),
                        "actual horizontal seam preserves native Y bits"); seam++;
                    legal = s.ContactPhase == MomentumContactPhase.LegalJump;
                    if (legal)
                    {
                        Require(jump && BitConverter.SingleToInt32Bits(s.NativeVelocityBefore.Y)
                            == BitConverter.SingleToInt32Bits(Fixed.ToFloat(player.Values.JumpSpeed)), "actual native-accepted Hunter jump impulse");
                        accepted.Add(index + 1);
                    }
                    var incoming = new Vector3(s.NativeVelocityBefore.X * 30, 0, s.NativeVelocityBefore.Z * 30);
                    Vector3 uncapped = MomentumMath.Step(incoming, s.Input, s.Tuning);
                    var expected = MomentumProfileV1.Step(incoming, s.Input);
                    Require(MathF.Abs(s.NativeVelocityAfter.X - expected.Velocity.X / 30) <= .00001f
                        && MathF.Abs(s.NativeVelocityAfter.Z - expected.Velocity.Z / 30) <= .00001f
                        && s.SpeedLimited == expected.SpeedLimited && s.IncomingOverCap == expected.IncomingOverCap,
                        "actual solver diagnostics agree with unchanged profile");
                    float outgoingSpeed = new Vector2(s.NativeVelocityAfter.X, s.NativeVelocityAfter.Z).Length * 30;
                    Require(outgoingSpeed <= MathF.Max(incoming.Length, MomentumProfileV1.RetentionSpeedCap) + .0001f,
                        "non-impulse steering cannot enlarge the incoming-or-retention envelope");
                    if (s.SpeedLimited) limited++;
                    if (s.IncomingOverCap) incomingOverCap++;
                    if (s.Input.Grounded && s.Input.MoveAxes == Vector2.Zero && incoming.Length > .01f && outgoingSpeed < incoming.Length) braking++;
                    float? airX = s.Input.Grounded ? null : uncapped.X - incoming.X;
                    float? airZ = s.Input.Grounded ? null : uncapped.Z - incoming.Z;
                    if (airX.HasValue && MathF.Abs(airX.Value) + MathF.Abs(airZ.GetValueOrDefault()) > .00001f) nonzeroAir++;
                    solver = new(s.ContactPhase.ToString(), s.Input.Grounded, s.Input.MoveAxes.X, s.Input.MoveAxes.Y,
                        s.Input.DesiredSpeed * MathF.Min(1, s.Input.MoveAxes.Length), incoming.Length, uncapped.Length, outgoingSpeed,
                        airX, airZ, s.SpeedLimited, s.IncomingOverCap);
                }
                int overflow = player.ModCollisionContactOverflows - overflowBefore;
                int recovery = player.ModCollisionRecoveryFallbacks - recoveryBefore;
                Require(overflow >= 0 && recovery >= 0, "native collision counters do not reset within a trial");
                if (input.MouseDeltaX != 0) aiming++;
                if ((player.FacingVector - facing).Length > .0001f) turned++;
                bodies[index] = new(index + 1, p.X, p.Y, p.Z, v.Y, new Vector2(v.X, v.Z).Length * 30,
                    grounded, player.Flags1.TestFlag(PlayerFlags1.UsedJump), jump, upward, legal,
                    new Vector2(p.X - origin.X, p.Z - origin.Z).Length, path);
                samples[index] = new(index + 1, player.FacingVector.X, player.FacingVector.Z,
                    requestedMouseDeltaX, input.MouseDeltaX, overflow, recovery, solver);
            }
            Require(!momentum || scene.DeveloperMomentumSteps > startSteps, "actual Momentum integration was exercised");
            var body = new Capture(origin, facing, bodies, inferred.ToArray(), accepted.ToArray(), firstLanding,
                scene.DeveloperMomentumSteps - startSteps, seam, limited, braking);
            return new(body, samples, incomingOverCap, nonzeroAir, aiming, turned,
                player.ModCollisionContactOverflows - startOverflow, player.ModCollisionRecoveryFallbacks - startRecovery);
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static object Summary(string room, Hunter hunter, string scenario, Capture native, Capture momentum, int nativeLanding)
    {
        Require(native.StartPosition == momentum.StartPosition && native.StartFacing == momentum.StartFacing,
            $"{room}/{hunter}/{scenario} paired deterministic actual spawn position and facing");
        object One(Capture capture)
        {
            var speeds = capture.Samples.Select(s => s.PlanarSpeed).ToArray();
            var movingAirSpeeds = capture.Samples.Where(s => !s.Grounded && s.PlanarSpeed > .01f).Select(s => s.PlanarSpeed).ToArray();
            return new {
                sampleCount = speeds.Length,
                startPosition = new[] { capture.StartPosition.X, capture.StartPosition.Y, capture.StartPosition.Z },
                startFacing = new[] { capture.StartFacing.X, capture.StartFacing.Y, capture.StartFacing.Z },
                speed = new { minimum = speeds.Min(), p05 = Percentile(speeds, .05f), p50 = Percentile(speeds, .5f),
                    p95 = Percentile(speeds, .95f), p99 = Percentile(speeds, .99f), maximum = speeds.Max(), mean = speeds.Average() },
                movingAirSpeed = new { sampleCount = movingAirSpeeds.Length,
                    p50 = movingAirSpeeds.Length > 0 ? (float?)Percentile(movingAirSpeeds, .5f) : null,
                    p95 = movingAirSpeeds.Length > 0 ? (float?)Percentile(movingAirSpeeds, .95f) : null,
                    p99 = movingAirSpeeds.Length > 0 ? (float?)Percentile(movingAirSpeeds, .99f) : null },
                runUpSpeedBeforeFirstRequestedJump = scenario == "running-manual-hop-chain" ? (float?)capture.Samples[47].PlanarSpeed : null,
                runUpGroundedAtTick48 = scenario == "running-manual-hop-chain" ? (bool?)capture.Samples[47].Grounded : null,
                groundedTicks = capture.Samples.Count(s => s.Grounded),
                movingTicks = capture.Samples.Count(s => s.PlanarSpeed > .01f),
                objectives = ObjectiveDistances.Select(distance => {
                    Sample? arrival = capture.Samples.FirstOrDefault(s => s.PlanarDisplacement >= distance);
                    return new { distance, reached = arrival != null, firstArrivalTick = arrival?.Tick,
                        timeSeconds = arrival == null ? (float?)null : arrival.Tick / (float)SimulationHz,
                        observationSeconds = capture.Samples.Length / (float)SimulationHz };
                }).ToArray(),
                capture.InferredUpwardJumpPressTicks, capture.MomentumLegalJumpTicks, capture.FirstLandingTick, capture.SolverSteps, capture.VerticalSeamChecks,
                capture.SpeedLimitedSteps, capture.NeutralGroundBrakingSteps,
                ticksWithoutMomentumSolver = capture.Samples.Length - capture.SolverSteps,
                finalPlanarDisplacement = capture.Samples[^1].PlanarDisplacement,
                maximumPlanarDisplacement = capture.Samples.Max(s => s.PlanarDisplacement),
                planarPathDistance = capture.Samples[^1].PlanarPathDistance,
                planarPathTraversalRate = capture.Samples[^1].PlanarPathDistance / (capture.Samples.Length / (float)SimulationHz),
                samples = capture.Samples
            };
        }
        return new { room, hunter = hunter.ToString(), scenario, nativeCalibrationLandingTick = nativeLanding,
            pairedStartPositionDifference = (native.StartPosition - momentum.StartPosition).Length,
            pairedStartFacingDifference = (native.StartFacing - momentum.StartFacing).Length,
            native = One(native), momentum = One(momentum) };
    }

    private static float Percentile(IEnumerable<float> input, float fraction)
    {
        float[] values = input.Order().ToArray();
        float rank = fraction * (values.Length - 1); int lower = (int)rank;
        return values[lower] + (values[Math.Min(lower + 1, values.Length - 1)] - values[lower]) * (rank - lower);
    }

    private static Capture RunTrial(string room, Hunter hunter, bool momentum, string scenario, int length, int hopPeriod)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out KeyboardState keyboard, room, matchRule: true);
        try
        {
            PlayerEntity player = scene.Players.Items[0];
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
            Vector3 origin = player.Position, previous = origin, facing = player.FacingVector; float previousVertical = player.Speed.Y, path = 0;
            var samples = new Sample[length]; var inferred = new List<int>(); var accepted = new List<int>();
            bool airborne = false; int firstLanding = -1, seam = 0, limited = 0, braking = 0;
            long startSteps = scene.DeveloperMomentumSteps;
            for (int index = 0; index < length; index++)
            {
                bool chain = scenario is "manual-hop-chain" or "running-manual-hop-chain";
                int firstJump = scenario == "running-manual-hop-chain" ? 48 : 0;
                bool jump = scenario == "jump-only" ? index == 0 : chain && index >= firstJump && (index - firstJump) % hopPeriod == 0;
                bool forward = chain ? !jump : scenario == "forward-release" && index < 80;
                setKey(keyboard, Keys.Space, jump); setKey(keyboard, Keys.W, forward);
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                bool usedJumpBefore = player.Flags1.TestFlag(PlayerFlags1.UsedJump);
                scene.OnSimulationFrame();
                Require(!NetSession.Active && player.ModIsInPlay && player.Health > 0 && !player.IsBot && !player.IsAltForm,
                    "trial remains genuine offline living native biped");
                Vector3 p = player.Position, v = player.Speed;
                Require(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
                    && float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z), "finite real collision body");
                Require(player.Controls.Jump.IsPressed == jump, "actual native keyboard jump edges follow fixed schedule");
                // Post-collision upward press is only inferred Native evidence: ceiling/pad
                // contact can change the final velocity after native input accepts a jump.
                bool upwardPress = player.Controls.Jump.IsPressed && !usedJumpBefore && player.Flags1.TestFlag(PlayerFlags1.UsedJump)
                    && v.Y > .01f && v.Y > previousVertical + .01f;
                if (upwardPress) inferred.Add(index + 1);
                previousVertical = v.Y;
                bool grounded = player.Flags1.TestFlag(PlayerFlags1.Grounded);
                if (airborne && grounded && firstLanding < 0) firstLanding = index + 1;
                airborne |= !grounded;
                path += new Vector2(p.X - previous.X, p.Z - previous.Z).Length; previous = p;
                samples[index] = new(index + 1, p.X, p.Y, p.Z, v.Y, new Vector2(v.X, v.Z).Length * 30,
                    grounded, player.Flags1.TestFlag(PlayerFlags1.UsedJump), player.Controls.Jump.IsPressed, upwardPress, false,
                    new Vector2(p.X - origin.X, p.Z - origin.Z).Length, path);
                long steps = scene.DeveloperMomentumSteps - before;
                Require(momentum ? steps is >= 0 and <= 1 : steps == 0, "at most one Momentum solver step; Native never invokes it");
                if (steps == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.SimulationFrame == frame && sample.Slot == player.SlotIndex, "diagnostic belongs to actual tick/player");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y),
                        "horizontal solver seam preserves native Y velocity bits"); seam++;
                    if (sample.ContactPhase == MomentumContactPhase.LegalJump)
                    {
                        Require(player.Controls.Jump.IsPressed, "native accepted jump has actual pressed input");
                        Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y)
                            == BitConverter.SingleToInt32Bits(Fixed.ToFloat(player.Values.JumpSpeed)),
                            "native accepted jump seam carries exact Hunter native jump impulse before gravity/collision");
                        accepted.Add(index + 1);
                        samples[index] = samples[index] with { MomentumLegalJump = true };
                    }
                    if (sample.SpeedLimited) limited++;
                    float incoming = new Vector2(sample.NativeVelocityBefore.X, sample.NativeVelocityBefore.Z).Length * 30;
                    float outgoing = new Vector2(sample.NativeVelocityAfter.X, sample.NativeVelocityAfter.Z).Length * 30;
                    Require(outgoing <= MathF.Max(incoming, MomentumProfileV1.GroundSpeed * MomentumProfileV1.RetentionMultiplierCap) + .0001f,
                        "normal movement solver respects existing retention envelope");
                    if (sample.Input.Grounded && sample.Input.MoveAxes == Vector2.Zero && incoming > .01f && outgoing < incoming) braking++;
                }
            }
            Require(!momentum || scene.DeveloperMomentumSteps - startSteps > 0, "Momentum trial has actual solver applications");
            return new(origin, facing, samples, inferred.ToArray(), accepted.ToArray(), firstLanding, scene.DeveloperMomentumSteps - startSteps, seam, limited, braking);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
