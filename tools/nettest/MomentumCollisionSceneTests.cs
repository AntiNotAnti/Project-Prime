using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Original authored geometry, loaded through CustomRooms/MapPacker. Live biped
// collision is the native sphere-between-points CheckCollision pass, not the
// clearance/recovery three-sphere helper and not a capsule sweep.
internal static class MomentumCollisionSceneTests
{
    internal const string Room = "TEST MOMENTUM COLLISION";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static object? _lastCapture;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly string[] Cases = { "wall", "oblique-slide", "corner", "low-step", "down-ramp", "up-ramp", "ledge", "wall-overlap", "ceiling", "narrow-corridor", "reversal", "slip1", "slip3" };
    private static void Require(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    internal static int Run(string? outputPath)
    {
        var results = new List<object>();
        var failures = new List<string>();
        try
        {
            _checks = 0; _lastCapture = null;
            Require(!NetSession.Active && !DemoPlayback.IsActive, "offline non-replay process");
            Require(ServerSim.Available(out string reason), reason);
            Headless.Enter();
            foreach (Hunter hunter in Hunters)
            foreach (bool momentum in new[] { false, true })
            foreach (bool seed in new[] { false, true })
            foreach (string scenario in Cases)
            {
                string label = $"{hunter}/{(momentum ? "Momentum" : "Native")}/{scenario}/{(seed ? "seeded-cap" : "earned-input")}";
                try
                {
                    _lastCapture = null;
                    results.Add(Capture(hunter, momentum, seed, scenario));
                    Console.WriteLine("MOMENTUM COLLISION PASS " + label);
                }
                catch (Exception ex)
                {
                    failures.Add(label + ": " + ex.Message);
                    results.Add(new { label, failure = ex.ToString(), partial = _lastCapture });
                    Console.Error.WriteLine("MOMENTUM COLLISION CASE FAIL " + label + ": " + ex.Message);
                }
            }
            Write(outputPath, results, failures.Count == 0 ? null : string.Join("\n", failures));
            Console.WriteLine($"MOMENTUM COLLISION {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions, {failures.Count} failed traces");
            return failures.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Write(outputPath, results, ex.ToString());
            Console.Error.WriteLine("MOMENTUM COLLISION FAIL " + ex);
            return 1;
        }
    }

    private static object Capture(Hunter hunter, bool momentum, bool seed, string scenario)
    {
        Scene scene = MomentumSceneTests.Load(hunter, momentum, out var keyboard, Room);
        try
        {
            var p = scene.Players.Items[0];
            var setKey = typeof(KeyboardState).GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
                .CreateDelegate<Action<KeyboardState, Keys, bool>>();
            float x = scenario switch { "corner" => -38, "low-step" => -16, "down-ramp" or "up-ramp" or "ledge" => 0,
                "ceiling" => 16, "narrow-corridor" => 30.25f, "slip1" => 42, "slip3" => 54, _ => -48 };
            Vector3 heading = scenario is "oblique-slide" or "corner"
                ? Vector3.Normalize(new Vector3(1, 0, 1)) : scenario is "up-ramp" or "ledge" ? -Vector3.UnitZ : Vector3.UnitZ;
            float y = scenario is "down-ramp" or "ledge" ? 2.1f : .1f;
            Vector3 start = new(x, y, scenario is "down-ramp" or "ledge" ? -7 : scenario == "up-ramp" ? 6 : -6);
            p.Reposition(start, heading, NodeRef.None);
            p.PrevPosition = start;
            p.Speed = Vector3.Zero;
            // Real collision settles each lane before controls are driven. Slipperiness
            // is never written: only authored surface contact may install it.
            for (int i = 0; i < 12; i++) scene.OnSimulationFrame();
            start = p.Position;
            if (scenario == "wall-overlap")
            {
                // A 0.01-unit fixture overlap, resolved by the actual native
                // collision frame. No call to the dormant recovery helper.
                Vector3 overlap = new(x, p.Position.Y, 8 - p.Values.BipedColRadius / 4096f + .01f);
                p.Reposition(overlap, heading, NodeRef.None); p.PrevPosition = overlap;
                start = p.Position;
            }
            // For wall stress, a real accepted jump supplies native airborne ownership;
            // only the planar speed and near-contact placement are fixture seeds.
            // This brings the 1.5x budget to contact before grounded friction decays it.
            if (seed && scenario is "wall" or "oblique-slide")
            {
                setKey(keyboard, Keys.Space, true);
                scene.OnSimulationFrame();
                setKey(keyboard, Keys.Space, false);
                Require(p.Speed.Y > 0, "stress setup earns native upward jump impulse");
                Vector3 nearWall = new(x, 1, 8 - p.Values.BipedColRadius / 4096f - .03f);
                p.Reposition(nearWall, heading, NodeRef.None); p.PrevPosition = nearWall;
                start = p.Position;
            }
            if (seed) p.Speed = (heading * (MomentumProfileV1.RetentionSpeedCap / 30)).WithY(p.Speed.Y);
            long steps = scene.DeveloperMomentumSteps;
            int overflow = p.ModCollisionContactOverflows, recovery = p.ModCollisionRecoveryFallbacks;
            float bodyRadius = p.Values.BipedColRadius / 4096f;
            float bodyTop = p.Values.MaxPickupHeight / 4096f;
            float bodyBottom = p.Values.MinPickupHeight / 4096f;
            FieldInfo slipField = typeof(PlayerEntity).GetField("_slipperiness", Hidden)!;
            int expectedSlip = scenario == "slip1" ? 1 : scenario == "slip3" ? 3 : 0;
            bool slipContact = false, bypass = false, resumed = false, obstacle = false, reversed = false;
            bool leftSupport = false, landedAfterLedge = false;
            object? ceilingContactEvidence = null;
            float maxZ = p.Position.Z, minY = p.Position.Y, maxY = p.Position.Y, maxDisplacement = 0;
            float minX = p.Position.X, maxX = p.Position.X;
            object? capContactEvidence = null;
            var trace = new List<object>();
            int duration = scenario is "wall" or "oblique-slide" or "corner" or "slip1" or "slip3" ? 240 : 120;
            for (int tick = 0; tick < duration; tick++)
            {
                bool reverse = scenario == "reversal" && tick >= 45;
                // Neutral on the seeded contact tick exposes the native normal response
                // without the ordinary Native input impulse applied later in that tick.
                bool neutralContact = tick == 0 && seed && scenario is "wall" or "oblique-slide";
                setKey(keyboard, Keys.W, !reverse && !neutralContact);
                setKey(keyboard, Keys.S, reverse);
                setKey(keyboard, Keys.Space, scenario == "ceiling" && tick == 70);
                int beforeSlip = Convert.ToInt32(slipField.GetValue(p));
                long beforeSteps = scene.DeveloperMomentumSteps;
                Vector3 before = p.Position;
                Vector3 incoming = p.Speed;
                ulong frame = scene.FrameCount;
                scene.OnSimulationFrame();
                Vector3 delta = p.Position - before;
                if (tick == 0 && seed && scenario is "wall" or "oblique-slide")
                {
                    float cap = MomentumProfileV1.RetentionSpeedCap / 30;
                    var sample = scene.LastMomentumStep;
                    float solverIncoming = sample.HasValue ? sample.Value.NativeVelocityBefore.Xz.Length : 0;
                    float solverOutput = sample.HasValue ? sample.Value.NativeVelocityAfter.Xz.Length : 0;
                    bool contact = MathF.Abs(p.Position.Z + bodyRadius - 8) <= .002f
                        && MathF.Abs(p.Speed.Z) < .00001f;
                    Require(contact, $"first seeded tick activates native wall normal response (incoming={incoming}, delta={delta}, after={p.Speed}, position={p.Position}, gap={8 - bodyRadius - p.Position.Z})");
                    if (momentum)
                    {
                        Require(sample.HasValue && sample.Value.SimulationFrame == frame, "cap contact has current solver sample");
                        Require(MathF.Abs(solverIncoming - cap) <= .00001f && MathF.Abs(solverOutput - cap) <= .00001f,
                            "maximum legitimate v1 velocity reaches native wall collision");
                    }
                    capContactEvidence = new { tick, incomingPlanar = incoming.Xz.Length,
                        solverIncoming, solverOutput, postCollisionPlanar = p.Speed.Xz.Length,
                        normalVelocityAfter = p.Speed.Z, wallGapAfter = 8 - bodyRadius - p.Position.Z, contact };
                }
                Require(Finite(p.Position) && Finite(p.Speed), $"{hunter}/{scenario}: finite world state");
                Require(p.Health > 0, $"{hunter}/{scenario}: alive");
                Require(p.Position.Y + bodyBottom >= -.002f && p.Position.Y <= 10.1f, $"{hunter}/{scenario}: floor/world vertical bound {p.Position}");
                Require(p.Position.Z + bodyRadius <= 8.002f && MathF.Abs(p.Position.X) + bodyRadius <= 64.002f, $"{hunter}/{scenario}: authored world nonpenetration {p.Position}");
                Require(delta.Length <= 1.0f, $"{hunter}/{scenario}: bounded actual tick displacement {delta.Length}");
                Require(p.ModCollisionContactOverflows == overflow && p.ModCollisionRecoveryFallbacks == recovery,
                    $"{hunter}/{scenario}: no contact overflow or recovery fallback");
                if (scenario == "ceiling" && incoming.Y > 0 && p.Speed.Y <= 0
                    && before.Z > bodyRadius && before.Z < 7 - bodyRadius)
                {
                    // The native swept query can reject a predicted penetration before
                    // leaving an exactly tangent end position. Prove interception of
                    // that upward path, rather than demand a fabricated final overlap.
                    float intendedTop = before.Y + bodyTop + incoming.Y / 2;
                    bool intercepted = intendedTop > 3 && p.Position.Y + bodyTop <= 3.002f;
                    obstacle |= intercepted;
                    ceilingContactEvidence = new { tick, incomingY = incoming.Y, afterY = p.Speed.Y,
                        intendedTop, actualTop = p.Position.Y + bodyTop, intercepted };
                }
                maxZ = MathF.Max(maxZ, p.Position.Z); minY = MathF.Min(minY, p.Position.Y); maxY = MathF.Max(maxY, p.Position.Y);
                minX = MathF.Min(minX, p.Position.X); maxX = MathF.Max(maxX, p.Position.X);
                maxDisplacement = MathF.Max(maxDisplacement, delta.Length);
                bool standing = p.Flags1.TestFlag(PlayerFlags1.Standing);
                leftSupport |= scenario == "ledge" && !standing && p.Speed.Y < 0;
                landedAfterLedge |= leftSupport && standing && p.Position.Y + bodyBottom <= .002f;
                int slip = Convert.ToInt32(slipField.GetValue(p));
                slipContact |= expectedSlip != 0 && slip == expectedSlip;
                if (expectedSlip != 0 && beforeSlip == expectedSlip && momentum)
                {
                    Require(scene.DeveloperMomentumSteps == beforeSteps, "actual slippery surface bypasses Momentum");
                    bypass = true;
                }
                resumed |= bypass && beforeSlip == 0 && scene.DeveloperMomentumSteps > beforeSteps;
                obstacle |= scenario switch {
                    "wall" or "oblique-slide" or "wall-overlap" => p.Position.Z > 6.8f && MathF.Abs(delta.Z) < .01f,
                    "corner" => p.Position.X > -30.8f && MathF.Abs(delta.X) < .01f,
                    "low-step" => p.Position.Z > 0 && p.Position.Y + bodyBottom >= .248f,
                    "down-ramp" => p.Position.Y < start.Y - .5f,
                    "up-ramp" => p.Position.Y > start.Y + .5f,
                    "ledge" => landedAfterLedge,
                    "ceiling" => obstacle,
                    "narrow-corridor" => p.Position.Z > 1,
                    "reversal" => tick > 45 && delta.Z < -.01f,
                    _ => slipContact };
                reversed |= reverse && delta.Z < -.01f;
                if (scenario == "narrow-corridor") Require(p.Position.X - bodyRadius >= 28.998f && p.Position.X + bodyRadius <= 31.502f, $"corridor walls contain body tick={tick} position={p.Position}");
                if (scenario == "corner") Require(p.Position.X + bodyRadius <= -29.998f, $"corner return wall nonpenetration tick={tick} position={p.Position}");
                if (scenario == "ceiling" && p.Position.Z > bodyRadius && p.Position.Z < 7 - bodyRadius)
                    Require(p.Position.Y + bodyTop <= 3.002f, $"ceiling contains native biped upper extent tick={tick} position={p.Position}");
                if (tick % 6 == 0 || scenario == "ceiling") trace.Add(new { tick, position = new[] { p.Position.X, p.Position.Y, p.Position.Z }, speed = new[] { p.Speed.X, p.Speed.Y, p.Speed.Z }, slip, solverSteps = scene.DeveloperMomentumSteps - steps });
            }
            _lastCapture = new { hunter = hunter.ToString(), momentum, seed, scenario,
                start = new[] { start.X, start.Y, start.Z }, end = new[] { p.Position.X, p.Position.Y, p.Position.Z },
                maxZ, minY, maxY, minX, maxX, bodyRadius, bodyTop, ceilingContactEvidence, trace };
            Require(obstacle, $"{hunter}/{scenario}: authored contact or traversal activated (start={start}, end={p.Position}, maxZ={maxZ}, speed={p.Speed}, facing={p.FacingVector}, W={p.Controls.MoveUp.IsDown})");
            Require((p.Position - start).Length > (scenario == "wall-overlap" ? .005f : .1f), "actual requested input or native overlap correction displaced body");
            if (scenario == "oblique-slide") Require(maxX - minX > 2, "oblique wall contact permits tangential travel");
            if (scenario == "reversal") Require(reversed, "input earns reverse displacement");
            if (expectedSlip != 0)
            {
                Require(slipContact, "actual authored contact installs requested slipperiness");
                if (momentum) Require(bypass && resumed, "slippery native ownership then normal surface Momentum resume");
            }
            Require(momentum ? scene.DeveloperMomentumSteps > steps : scene.DeveloperMomentumSteps == steps, "profile activation matches requested mode");
            return new { hunter = hunter.ToString(), mode = momentum ? "Momentum" : "Native", scenario,
                velocitySource = seed ? "explicit initial planar speed seed at v1 1.5x retention cap; not earned input" : "earned keyboard input from rest",
                initialSeedSpeed = seed ? MomentumProfileV1.RetentionSpeedCap / 30 : 0,
                maxZ, minY, maxY, minX, maxX, maxDisplacement, obstacle, slipContact, bypass, resumed, leftSupport, landedAfterLedge, capContactEvidence, ceilingContactEvidence,
                contactOverflows = p.ModCollisionContactOverflows - overflow, recoveryFallbacks = p.ModCollisionRecoveryFallbacks - recovery, trace };
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static void Write(string? path, List<object> results, string? failure)
    {
        if (path == null) return;
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { scope = "asset-backed offline keyboard/native biped collision on original authored course; no GL/network/replay acceptance",
            conditions = new { room = Room, hunters = 7, modes = new[] { "Native", "Momentum" }, scenarios = Cases,
                velocitySources = new[] { "earned-input", "explicit initial 1.5x v1 cap seed" },
                carrier = "not covered: fixture contains static authored room geometry, no native standing EntityCollision carrier" },
            checks = _checks, failure, lastCapture = _lastCapture, results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
