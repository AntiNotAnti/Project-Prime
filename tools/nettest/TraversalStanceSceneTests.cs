using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Real offline input and native authored collision. Reuses the admitted training
// fixture, with one labeled initial lane placement. No speed, stance, slide timer,
// jump, ownership or collision-acceptance state is manufactured by these tests.
internal static class TraversalStanceSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static int _checks;
    private sealed record Command(bool Forward = false, bool Crouch = false, bool Jump = false);
    private sealed record Tick(int Number, float X, float Y, float Z, float Speed, float BeforeSpeed, float VelocityY,
        bool Standing, bool Crouched, bool CollisionCrouched, bool Sliding, bool CrouchHeld, bool CrouchPressed,
        float Bottom, float Top, float Eye, bool AcceptedJump, float? JumpImpulse);
    private sealed record Capture(string Hunter, string Model, string Scenario, Command[] Commands, Tick[] Ticks);
    private sealed record State(int Index, PlayerEntity Player);
    private static void Require(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float tolerance = .00001f)
        => Require(float.IsFinite(a) && MathF.Abs(a - b) <= tolerance, $"{label}: {a} != {b}");

    internal static int Run(string? output)
    {
        _checks = 0; var rows = new List<object>(); var failures = new List<string>();
        try
        {
            Require(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline nonrecording process");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            void Case(string name, Action work)
            {
                try { work(); Console.WriteLine("TRAVERSAL STANCE SCENE PASS " + name); }
                catch (Exception error) { failures.Add(name + ": " + error); Console.Error.WriteLine("TRAVERSAL STANCE SCENE FAIL " + name + ": " + error); }
            }
            foreach (Hunter hunter in Hunters)
            {
                Case(hunter + "/native-crouch-pose-both-LODs", () =>
                    rows.Add(new { scenario = "native-crouch-pose-both-LODs", pose = TraversalCrouchPoseTests.Capture(hunter) }));
                Case(hunter + "/physical-stance", () =>
                {
                    Command Stance(State s) => new(Crouch: s.Index >= 8 && s.Index < 40);
                    Capture traversal = CaptureScene(hunter, MovementModel.Traversal, "physical-stance",
                        new(42, .5f, 20), Vector3.UnitZ, 60, Stance);
                    Capture native = CaptureScene(hunter, MovementModel.Native, "native-same-Ctrl",
                        new(42, .5f, 20), Vector3.UnitZ, 60, Stance);
                    Capture quake = CaptureScene(hunter, MovementModel.Quake3, "Quake3-same-Ctrl",
                        new(42, .5f, 20), Vector3.UnitZ, 60, Stance);
                    rows.Add(new { scenario = "physical-stance", traversal, native, quake });
                    Tick standing = traversal.Ticks[0]; Tick crouched = traversal.Ticks[12];
                    Require(!standing.Crouched && crouched.Crouched && crouched.CollisionCrouched
                        && !crouched.Sliding && crouched.Top < standing.Top && crouched.Eye < standing.Eye,
                        hunter + " Ctrl changes real physical shape and eye without a stationary slide");
                    Near(crouched.Bottom, standing.Bottom, "crouch retains native foot anchor");
                    Near(crouched.Y + crouched.Bottom, standing.Y + standing.Bottom,
                        "crouching does not raise or sink the actual floor-contact feet");
                    Require(!traversal.Ticks[^1].Crouched
                        && !traversal.Ticks[^1].CollisionCrouched, "clear standing space permits release to stand");
                    foreach (Capture excluded in new[] { native, quake })
                        Require(excluded.Ticks.All(t => !t.Crouched && !t.CollisionCrouched && !t.Sliding)
                            && excluded.Commands.SequenceEqual(traversal.Commands), "Classic and Quake III ignore identical Ctrl input");
                });
                Case(hunter + "/tunnel-clearance", () =>
                {
                    bool released = false;
                    Command Tunnel(State s)
                    { released |= s.Player.Position.Z >= -4; return new(true, !released); }
                    Capture tunnel = CaptureScene(hunter, MovementModel.Traversal, "tunnel-release-under-roof",
                        new(30, .5f, -16), Vector3.UnitZ, 700, Tunnel);
                    rows.Add(new { scenario = "tunnel-clearance", tunnel });
                    Tick[] blocked = tunnel.Ticks.Where(t => t.Z > -3 && t.Z < 8 && !t.CrouchHeld).ToArray();
                    Require(blocked.Length > 5 && blocked.All(t => t.Crouched && t.CollisionCrouched),
                        hunter + " releasing Ctrl under a low ceiling cannot grow the collision shape");
                    Require(blocked.All(t => t.Y + t.Top <= 1.22f && t.Y + t.Bottom >= -.02f),
                        hunter + " actual crouched body fits between the authored floor and roof");
                    Require(tunnel.Ticks.Any(t => t.Z > 11 && !t.Crouched && !t.CollisionCrouched)
                        && tunnel.Ticks.Max(t => t.Z) > 12, "body stands only after earning clearance beyond the tunnel");
                    Require(tunnel.Ticks.All(t => !t.Sliding), "ordinary crouch walk never meets earned slide-entry speed");
                });
            }
            Case("thin-side-rail-blocks-standing-capsule", () =>
            {
                Capture rail = CaptureScene(Hunter.Samus, MovementModel.Traversal, "thin-side-rail",
                    new(38, .5f, 24), Vector3.UnitZ, 220,
                    s => new(s.Index < 60 || s.Index >= 85, s.Index < 60));
                rows.Add(new { scenario = "thin-side-rail", rail });
                Tick[] blocked = rail.Ticks.Where(t => !t.CrouchHeld && t.Z > 26.2f && t.Z < 27.7f).ToArray();
                Require(blocked.Length > 3 && blocked.All(t => t.Crouched),
                    "continuous full-height clearance detects a thin edge between sphere probes");
                Require(rail.Ticks.Any(t => t.Z > 29 && !t.Crouched),
                    "leaving the thin rail permits standing without a persistent crouch latch");
            });
            Case("standing-modes-blocked-by-low-roof", () =>
            {
                foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Quake3 })
                {
                    Capture blocked = CaptureScene(Hunter.Samus, model, "standing-tunnel-control",
                        new(30, .5f, -16), Vector3.UnitZ, 180, _ => new(true, true));
                    rows.Add(new { scenario = "standing-modes-blocked-by-low-roof", blocked });
                    Require(blocked.Ticks.Max(t => t.Z) < -9 && blocked.Ticks.All(t => !t.Crouched),
                        "same held Ctrl does not let an excluded standing model enter the low tunnel");
                }
            });
            Case("earned-slide-friction-and-finite-window", () =>
            {
                int entry = -1; bool releasedRun = false;
                Command Slide(State s)
                {
                    releasedRun |= s.Player.Position.Z >= -.5f;
                    if (entry < 0 && releasedRun && s.Player.Position.Z > 8
                        && s.Player.Flags1.TestFlag(PlayerFlags1.Standing)
                        && s.Player.Speed.Xz.Length * 30 >= TraversalMovementProfileV2.SlideMinimumSpeed)
                        entry = s.Index;
                    return new(!releasedRun, entry >= 0);
                }
                Capture slide = CaptureScene(Hunter.Samus, MovementModel.Traversal, "earned-surf-to-slide",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 450, Slide);
                Capture standing = CaptureScene(Hunter.Samus, MovementModel.Traversal, "standing-friction-control",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 450,
                    s => slide.Commands[s.Index] with { Crouch = false });
                rows.Add(new { scenario = "earned-slide-friction-and-finite-window", entry, slide, standing });
                Require(entry >= 0, "actual run-up and native surf descent earn slide-entry speed on solid landing");
                Tick start = slide.Ticks[entry];
                Require(start.CrouchPressed && start.Crouched && start.Sliding
                    && start.BeforeSpeed >= TraversalMovementProfileV2.SlideMinimumSpeed,
                    "fresh grounded Ctrl consumes only an actually earned entry speed");
                Require(start.Speed <= start.BeforeSpeed + .00001f, "entering a slide creates no bonus speed");
                Near(start.BeforeSpeed, standing.Ticks[entry].BeforeSpeed, "paired friction controls share actual entry velocity");
                Require(slide.Ticks[entry + 5].Speed > standing.Ticks[entry + 5].Speed + .01f,
                    "actual grounded slide retains more momentum than standing friction");
                int slidingTicks = slide.Ticks.Count(t => t.Sliding);
                Require(slidingTicks > 5 && slidingTicks <= TraversalMovementProfileV2.SlideTicks,
                    "actual slide has a finite at-most-48-tick lifetime");
                Require(slide.Ticks.Skip(entry + TraversalMovementProfileV2.SlideTicks + 1)
                    .All(t => t.CrouchHeld && !t.Sliding), "holding Ctrl cannot refresh an expired slide");
                Require(slide.Ticks.All(t => !t.AcceptedJump), "slide entry creates no native jump impulse");

                bool oldStandingApplied = false;
                Capture staleStanding = CaptureScene(Hunter.Samus, MovementModel.Traversal, "earned-slide-old-standing-adoption",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 450, s =>
                    {
                        if (s.Index == entry + 5)
                        {
                            Require(s.Player.ModCrouched && s.Player.ModSliding,
                                "stale-standing regression begins from an actual earned active slide");
                            // Exercise the production owner snapshot-stance adopter, not
                            // a backing flag write. Full packet ownership is a separate suite.
                            s.Player.ModApplyReplicatedCrouch(false, isLocal: true);
                            Require(s.Player.ModCrouched && s.Player.ModSliding,
                                "delayed standing adoption cannot cancel newer local crouch/slide prediction");
                            oldStandingApplied = true;
                        }
                        return slide.Commands[s.Index];
                    });
                rows.Add(new { scenario = "earned-slide-old-standing-adoption", oldStandingApplied, staleStanding });
                Require(oldStandingApplied && staleStanding.Ticks.SequenceEqual(slide.Ticks),
                    "old owner stance adoption neither cancels nor refreshes the earned slide trajectory");

                int jumpTick = entry + 4;
                Capture jump = CaptureScene(Hunter.Samus, MovementModel.Traversal, "slide-jump",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 450,
                    s => slide.Commands[s.Index] with { Jump = s.Index == jumpTick });
                Capture nativeJump = CaptureScene(Hunter.Samus, MovementModel.Traversal, "standing-jump-control",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 450,
                    s => slide.Commands[s.Index] with { Crouch = false, Jump = s.Index == jumpTick });
                rows.Add(new { scenario = "slide-jump-has-no-boost", jump, nativeJump });
                Tick accepted = jump.Ticks[jumpTick]; Tick controlJump = nativeJump.Ticks[jumpTick];
                Require(accepted.AcceptedJump && controlJump.AcceptedJump && !accepted.Sliding,
                    "actual slide jump cancels the slide window");
                Near(accepted.JumpImpulse!.Value, controlJump.JumpImpulse!.Value,
                    "crouch slide preserves the native flat-ground jump impulse");

                Capture fresh = CaptureScene(Hunter.Samus, MovementModel.Traversal, "slide-fresh-press",
                    new(-10, 8.5f, -20), Vector3.UnitZ, 450,
                    s => slide.Commands[s.Index] with { Crouch = s.Index != entry + 3 && slide.Commands[s.Index].Crouch });
                rows.Add(new { scenario = "slide-fresh-press", fresh });
                Require(!fresh.Ticks[entry + 3].Sliding && fresh.Ticks[entry + 4].CrouchPressed,
                    "release ends the first slide and rearms a fresh input edge");
                if (fresh.Ticks[entry + 4].BeforeSpeed >= TraversalMovementProfileV2.SlideMinimumSpeed)
                    Require(fresh.Ticks[entry + 4].Sliding, "fresh press can reenter only with remaining earned speed");
                else Require(!fresh.Ticks[entry + 4].Sliding, "fresh press below threshold cannot manufacture a replacement slide");
            });
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL",
                checks = _checks, failures, profileVersion = TraversalMovementProfileV2.ProfileVersion,
                scope = "Actual offline CPU keyboard and authored collision; native spawn/intro skip and one initial lane placement; all measured speeds, stance, clearance and slide lifetime are production-derived; no physical device, GL, multiplayer or replay qualification", rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"TRAVERSAL STANCE SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Capture CaptureScene(Hunter hunter, MovementModel model, string scenario,
        Vector3 seed, Vector3 facing, int length, Func<State, Command> choose)
    {
        object?[] arguments = { hunter, model, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity p = scene.Players.Main!; p.Controls.Crouch.Key = Keys.LeftControl;
            Require(p.Speed.X == 0 && p.Speed.Z == 0, "native spawn has no fabricated horizontal speed");
            p.Reposition(seed, facing, NodeRef.None); p.PrevPosition = seed;
            for (int index = 0; index < 12; index++) scene.OnSimulationFrame();
            Require(p.Flags1.TestFlag(PlayerFlags1.Standing), "initial fixture lane settles on real support");
            var commands = new Command[length]; var ticks = new Tick[length]; bool wasCrouch = false;
            for (int index = 0; index < length; index++)
            {
                Command c = choose(new(index, p)); commands[index] = c;
                SetKey(keyboard, Keys.W, c.Forward); SetKey(keyboard, Keys.LeftControl, c.Crouch);
                SetKey(keyboard, Keys.Space, c.Jump);
                float beforeSpeed = p.Speed.Xz.Length * 30;
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                scene.OnSimulationFrame();
                Require(!NetSession.Active && p.ModIsInPlay && p.Health > 0 && !p.IsBot && !p.IsAltForm,
                    "genuine living offline owned biped");
                Require(p.Controls.Crouch.IsDown == c.Crouch && p.Controls.Crouch.IsPressed == (c.Crouch && !wasCrouch)
                    && p.Controls.MoveUp.IsDown == c.Forward && p.Controls.Jump.IsDown == c.Jump,
                    "actual production keyboard levels and fresh crouch edges");
                Require(float.IsFinite(p.Position.LengthSquared) && float.IsFinite(p.Speed.LengthSquared),
                    "finite real native body and velocity");
                bool accepted = false; float? jumpImpulse = null;
                long delta = scene.DeveloperMomentumSteps - before;
                Require(model == MovementModel.Native ? delta == 0 : delta is >= 0 and <= 1,
                    "one eligible horizontal adapter; none for Classic");
                if (delta == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.SimulationFrame == frame && sample.Slot == p.SlotIndex,
                        "diagnostic belongs to actual frame and body");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y)
                        == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "horizontal stance solver preserves native Y bits");
                    accepted = sample.ContactPhase == MomentumContactPhase.LegalJump;
                    if (accepted) jumpImpulse = sample.NativeVelocityBefore.Y;
                }
                ticks[index] = new(index + 1, p.Position.X, p.Position.Y, p.Position.Z, p.Speed.Xz.Length * 30,
                    beforeSpeed, p.Speed.Y, p.Flags1.TestFlag(PlayerFlags1.Standing), p.ModCrouched,
                    p.ModCollisionCrouched, p.ModSliding, c.Crouch, p.Controls.Crouch.IsPressed,
                    p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset, accepted, jumpImpulse);
                wasCrouch = c.Crouch;
            }
            return new(hunter.ToString(), model.ToString(), scenario, commands, ticks);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
