using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Existing authored course + actual native AI geometry policy. Neutral coast is
// an explicitly seeded-cap human-body fixture, not an AI-earned speed claim.
internal static class MomentumBotNavigationTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static object? _partial;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static float[] V(Vector3 value) => new[] { value.X, value.Y, value.Z };
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    internal static int Run(string? outputPath)
    {
        var results = new List<object>(); var failures = new List<string>();
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay navigation fixture");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            foreach (Hunter hunter in Hunters)
            foreach (bool momentum in new[] { false, true })
            foreach (bool overshoot in new[] { false, true })
            {
                string label = $"{hunter}/{(momentum ? "Momentum" : "Native")}/{(overshoot ? "one-step-veto-entry" : "first-veto")}";
                try { _partial = null; results.Add(Capture(hunter, momentum, overshoot)); Console.WriteLine("MOMENTUM BOT NAV PASS " + label); }
                catch (Exception ex) { failures.Add(label + ": " + ex.Message); results.Add(new { label, failure = ex.ToString(), partial = _partial }); Console.Error.WriteLine("MOMENTUM BOT NAV CASE FAIL " + label + ": " + ex.Message); }
            }
            Write(outputPath, results, failures.Count == 0 ? null : string.Join("\n", failures));
            Console.WriteLine($"MOMENTUM BOT NAV {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions, {failures.Count} failed cases");
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex) { Write(outputPath, results, ex.ToString()); Console.Error.WriteLine("MOMENTUM BOT NAV FAIL " + ex); return 1; }
    }

    private static object Capture(Hunter hunter, bool momentum, bool overshoot)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out _, MomentumCollisionSceneTests.Room, matchRule: true);
        try
        {
            var p = scene.Players.Items[0];
            var probe = p.AiData.GetType().GetMethod("TacticalCanMove", Hidden)!;
            bool CanMove(Vector3 direction) => (bool)probe.Invoke(p.AiData, new object[] { direction })!;
            void Place(Vector3 position, Vector3 facing)
            {
                // Each query placement is a declared geometry fixture. This is
                // outside the measured neutral-braking interval below.
                p.Reposition(position, facing, p.NodeRef); p.PrevPosition = position; p.Speed = Vector3.Zero;
            }
            float bottom = p.Values.MinPickupHeight / 4096f, radius = p.Values.BipedColRadius / 4096f;
            var probes = new List<object>();
            void Query(string name, Vector3 position, Vector3 direction, bool expected)
            {
                Place(position, direction);
                bool actual = CanMove(direction); probes.Add(new { name, position = V(position), direction = V(direction), actual, expected });
                _partial = new { hunter = hunter.ToString(), momentum, probes };
                Check(actual == expected, $"{hunter}/{name}: actual native TacticalCanMove={actual}, expected={expected}");
            }
            Query("safe-floor", new(-48, -bottom, -12), Vector3.UnitZ, true);
            Query("wall-body-and-head", new(-48, -bottom, 7), Vector3.UnitZ, false);
            Query("low-step", new(-16, -bottom, -1), Vector3.UnitZ, true);
            Query("ramp-up", new(0, 4f / 6 - bottom, 0), -Vector3.UnitZ, true);
            Query("ramp-down", new(0, 4f / 6 - bottom, 0), Vector3.UnitZ, true);
            Query("narrow-corridor", new(30.25f, -bottom, -4), Vector3.UnitZ, true);
            // Suspended initial query position isolates the real head-height ray
            // against the ceiling's vertical front. It is not an earned jump.
            Vector3 headFixture = new(16, 1.7f, -1);
            Place(headFixture, Vector3.UnitZ);
            CollisionResult bodyRay = default, headRay = default;
            bool bodyBlocked = CollisionDetection.CheckBetweenPoints(headFixture.AddY(.65f), headFixture.AddY(.65f) + Vector3.UnitZ * 1.5f, TestFlags.None, scene, ref bodyRay);
            bool headBlocked = CollisionDetection.CheckBetweenPoints(headFixture.AddY(1.35f), headFixture.AddY(1.35f) + Vector3.UnitZ * 1.5f, TestFlags.None, scene, ref headRay);
            Check(!bodyBlocked && headBlocked && !CanMove(Vector3.UnitZ), "actual head-height probe rejects obstruction missed by body-height ray");
            probes.Add(new { name = "head-only suspended geometry fixture", position = V(headFixture), bodyBlocked, headBlocked });

            // Ramp top: y2 at z-8, falling to the separate room floor y0.
            // Find the live policy's first veto from the supported side, rather
            // than hardcoding a guessed threshold or changing the probe length.
            var scan = new List<object>();
            Vector3? lastSafe = null, firstUnsafe = null;
            for (int i = 0; i <= 100; i++)
            {
                float z = -5.5f - i * .025f;
                float surface = (4 - z) / 6;
                Place(new Vector3(0, surface - bottom + .05f, z), -Vector3.UnitZ);
                for (int tick = 0; tick < 12; tick++) scene.OnSimulationFrame();
                Check(p.Health > 0 && Finite(p.Position), "native course scan settles living finite body");
                bool allowed = CanMove(-Vector3.UnitZ);
                scan.Add(new { position = V(p.Position), allowed, standing = p.Flags1.TestFlag(PlayerFlags1.Standing) });
                if (allowed) lastSafe = p.Position;
                else { firstUnsafe = p.Position; break; }
            }
            _partial = new { hunter = hunter.ToString(), momentum, probes, scan };
            Check(lastSafe.HasValue && firstUnsafe.HasValue && firstUnsafe.Value.Z > -8,
                "actual AI floor query detects the authored top ledge before center crosses it");
            float seededSpeed = MomentumProfileV1.RetentionSpeedCap / 30;
            if (overshoot)
            {
                // Renderer makes the next AI decision after entity physics;
                // ProcessInput consumes it on the following tick. A sampled
                // veto may therefore first observe a threshold crossed during
                // the preceding movement step. This arm is a conservative
                // cap/2 entry-offset placement, not an AI-earned trajectory.
                Vector3 entry = firstUnsafe!.Value - Vector3.UnitZ * (seededSpeed / 2);
                float surface = (4 - entry.Z) / 6;
                Place(entry.WithY(surface - bottom + .05f), -Vector3.UnitZ);
                for (int tick = 0; tick < 12; tick++) scene.OnSimulationFrame();
            }
            Vector3 start = p.Position;
            Check(!CanMove(-Vector3.UnitZ), "actual motor policy would veto outward input at measured start");
            p.Speed = -Vector3.UnitZ * seededSpeed; // Explicit initial cap seed; no further speed writes.
            float minimumZ = start.Z, maximumPlanar = seededSpeed, maximumDisplacement = 0;
            int airborneTicks = 0, supportedPositiveAirPhases = 0, supportedPositiveGroundPhases = 0, overflows = p.ModCollisionContactOverflows, fallbacks = p.ModCollisionRecoveryFallbacks;
            long steps = scene.DeveloperMomentumSteps;
            var trace = new List<object>();
            for (int tick = 0; tick < 180; tick++)
            {
                Vector3 before = p.Position;
                ulong frame = scene.FrameCount;
                bool standingBefore = p.Flags1.TestFlag(PlayerFlags1.Standing);
                bool standingPreviousBefore = p.Flags1.TestFlag(PlayerFlags1.StandingPrevious);
                float verticalBefore = p.Speed.Y;
                scene.OnSimulationFrame();
                MomentumStepSample? diagnostic = scene.LastMomentumStep is { } current
                    && current.Slot == p.SlotIndex && current.SimulationFrame == frame ? current : null;
                if (momentum && p.ModMomentumEligible)
                    Check(diagnostic.HasValue, "eligible neutral coast has exact current-frame local solver diagnostic");
                if (standingBefore && diagnostic.HasValue && diagnostic.Value.NativeVelocityBefore.Y > 0)
                {
                    if (diagnostic.Value.ContactPhase == MomentumContactPhase.Airborne) supportedPositiveAirPhases++;
                    if (diagnostic.Value.Input.Grounded) supportedPositiveGroundPhases++;
                }
                Check(Finite(p.Position) && Finite(p.Speed) && p.Health > 0, "finite living native neutral coast");
                Check(!p.Controls.MoveUp.IsDown && !p.Controls.MoveDown.IsDown && !p.Controls.MoveLeft.IsDown && !p.Controls.MoveRight.IsDown && !p.Controls.Jump.IsDown && !p.Controls.Jump.IsPressed,
                    "actual keyboard movement/jump input remains neutral throughout measured braking");
                Check(p.ModCollisionContactOverflows == overflows && p.ModCollisionRecoveryFallbacks == fallbacks,
                    "no collision overflow/recovery fallback masks navigation result");
                float planar = MathF.Sqrt(p.Speed.X * p.Speed.X + p.Speed.Z * p.Speed.Z);
                maximumPlanar = MathF.Max(maximumPlanar, planar);
                minimumZ = MathF.Min(minimumZ, p.Position.Z);
                maximumDisplacement = MathF.Max(maximumDisplacement, (p.Position - before).Length);
                if (!p.Flags1.TestFlag(PlayerFlags1.Standing)) airborneTicks++;
                if (tick < 24 || tick % 12 == 0) trace.Add(new { tick, position = V(p.Position), speed = V(p.Speed),
                    standing = p.Flags1.TestFlag(PlayerFlags1.Standing), standingBefore, standingPreviousBefore, verticalBefore,
                    contactPhase = diagnostic?.ContactPhase.ToString(), solverGroundFriction = diagnostic?.Input.Grounded,
                    solverIncomingY = diagnostic?.NativeVelocityBefore.Y, solverOutgoingY = diagnostic?.NativeVelocityAfter.Y,
                    jumpDown = p.Controls.Jump.IsDown, jumpPressed = p.Controls.Jump.IsPressed, centerEdgeGap = p.Position.Z + 8,
                    bodyEdgeMargin = p.Position.Z + 8 - radius });
            }
            float coast = start.Z - minimumZ, centerGap = minimumZ + 8, bodyMargin = centerGap - radius;
            _partial = new { hunter = hunter.ToString(), momentum, arm = overshoot ? "one-step-veto-entry" : "first-veto", start = V(start), lastSafe = V(lastSafe!.Value),
                firstUnsafe = V(firstUnsafe!.Value), seededSpeed, coast, centerGap, bodyMargin, airborneTicks, supportedPositiveAirPhases, supportedPositiveGroundPhases, maximumPlanar, maximumDisplacement, probes, scan, trace };
            if (momentum)
                Check(supportedPositiveGroundPhases > 0 && supportedPositiveAirPhases == 0,
                    $"native standing uphill residual Y retains ground friction (ground={supportedPositiveGroundPhases}, air={supportedPositiveAirPhases})");
            Check(coast <= 1.125f, $"actual neutral cap coast exceeds existing conservative budget: {coast}");
            Check(bodyMargin >= 0, $"selected floor probe leaves negative biped edge margin: {bodyMargin} (start={start}, minimumZ={minimumZ})");
            Check(p.Position.Z > -8 && p.Position.Y + bottom > 1.5f && p.Flags1.TestFlag(PlayerFlags1.Standing),
                "actual neutral braking remains supported on upper ramp; no ledge fall");
            Check(maximumPlanar <= seededSpeed + .00001f, "neutral braking never manufactures incoming speed");
            Check(momentum ? scene.DeveloperMomentumSteps > steps : scene.DeveloperMomentumSteps == 0,
                "native/Momentum braking uses selected ordinary movement path");
            return _partial!;
        }
        finally { MomentumSceneTests.Release(scene); }
    }
    private static void Write(string? outputPath, List<object> results, string? failure)
    {
        if (outputPath == null) return;
        string path = Path.GetFullPath(outputPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks, failure,
            scope = "actual authored collision + TacticalCanMove; explicit initial v1-cap seed into neutral human-body braking, not AI-earned speed or autonomous path selection",
            conditions = new { hunters = 7, modes = new[] { "Native", "Momentum" }, ledgeZ = -8, ledgeTop = 2,
                nativeProbeDistance = 1.5f, momentumFloorProbeDistance = .5f + MomentumProfileV1.RetentionSpeedCap / MomentumProfileV1.Tuning.GroundFriction,
                neutralCoastBudget = 1.125f, noMeasuredControlOrSpeedWrites = true,
                arms = new[] { "first-veto", "one-step-veto-entry: explicit cap/2 initial offset, not autonomous AI latency proof" },
                floorScope = "authored two-unit ramp-to-base-floor drop; native ray acceptance determines threshold, not an unbounded void",
                navigationPolicy = "Native probes unchanged; eligible Momentum floor lookahead includes body radius plus cap/friction stopping distance; wall/head rays unchanged" }, results },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
