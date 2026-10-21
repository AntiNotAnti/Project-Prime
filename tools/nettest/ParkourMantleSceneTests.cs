using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead;
using MphRead.Entities;
using MphRead.Editor;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Utility;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Actual offline keyboard and authored collision. Only the initial labeled lane
// placement is synthetic. The Teleport case invokes the production lifecycle API
// at the unchanged origin; no in-flight position/speed/contact/timer writes.
internal static class ParkourMantleSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly FieldInfo Contacts = typeof(PlayerEntity).GetField("_collisionScratch", Hidden)!;
    private static readonly FieldInfo Seed = typeof(PlayerEntity).GetField("_modMantleSeed", Hidden)!;
    private static readonly FieldInfo SeedBlocked = typeof(PlayerEntity).GetField("_modMantleSeedBlocked", Hidden)!;
    private static readonly FieldInfo ContactFrame = typeof(PlayerEntity).GetField("_modParkourContactFrame", Hidden)!;
    private static readonly FieldInfo SeedNormal = typeof(PlayerEntity).GetField("_modMantleSeedNormal", Hidden)!;
    private static readonly FieldInfo SeedPlane = typeof(PlayerEntity).GetField("_modMantleSeedPlane", Hidden)!;
    private static int _checks;
    private sealed record Tick(int Index, [property: JsonIgnore] Vector3 Position,
        [property: JsonIgnore] Vector3 Velocity, bool Standing, bool Front,
        bool Forward, bool JumpPressed, bool Active, bool Applied, bool Completed, int Ticks,
        bool NativeJump, bool Alt, bool Transition, bool SafeTop, int Admission,
        bool SeedBefore, bool SeedBlockedBefore, ulong ContactFrameBefore,
        float SeedNormalX, float SeedNormalY, float SeedNormalZ, float SeedPlaneBefore,
        bool ActorContact, bool ActorActive)
    {
        public float[] PositionXyz => new[] { Position.X, Position.Y, Position.Z };
        public float[] VelocityXyz => new[] { Velocity.X, Velocity.Y, Velocity.Z };
    }
    private sealed record Capture(string Scenario, string Hunter, string Model, int Contact, int Request,
        int Abort, Tick[] Ticks);
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }

    internal static int Run(string? output = null)
    {
        _checks = 0; var rows = new List<object>(); var failures = new List<string>();
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording, "fresh offline scene");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            void Case(string name, Action action)
            {
                try { action(); Console.WriteLine("PARKOUR MANTLE SCENE PASS " + name); }
                catch (Exception error)
                { failures.Add(name + ": " + error); Console.Error.WriteLine("PARKOUR MANTLE SCENE FAIL " + name + ": " + error); }
            }
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
                Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
                Case(hunter + "/fresh-contact-climb", () =>
                {
                    Capture capture = Trace(hunter, MovementModel.Parkour, -30, "fresh"); rows.Add(capture);
                    Check(capture.Contact >= 0 && capture.Request > capture.Contact,
                        "fresh request follows actual native front-face contact");
                    Tick[] applied = capture.Ticks.Where(t => t.Applied).ToArray();
                    Check(applied.Length > 1 && applied.Length <= MantleMovementV1.Ticks,
                        $"actual mantle movement is bounded to at most 24 fixed ticks (observed {applied.Length}, request {capture.Request})");
                    Check(capture.Ticks.Count(t => t.Completed) == 1,
                        "one fresh request completes exactly one mantle");
                    Check(capture.Ticks[capture.Request].JumpPressed && !capture.Ticks[capture.Request].NativeJump,
                        "accepted fresh mantle owns its Jump without a simultaneous native jump");
                    float originY = capture.Ticks[Math.Max(0, applied[0].Index - 1)].Position.Y;
                    Check(applied.All(t => t.Position.Y - originY <= MantleMovementV1.MaxRise + .03f),
                        "physical route remains within the 1.25-unit rise bound");
                    Check(capture.Ticks.Any(t => t.Standing && t.SafeTop && t.Position.Z > 17),
                        "native collision physically lands on the broad safe static top");
                    foreach (Tick t in applied)
                    {
                        Vector3 previous = capture.Ticks[t.Index - 1].Position;
                        Check((t.Position - previous).Length < .35f,
                            "native velocity integration advances the route in bounded steps");
                    }
                    Check(capture.Ticks.All(t => t.Ticks is >= 0 and <= MantleMovementV1.Ticks),
                        "controller tick counter stays within its fixed budget");
                });
            foreach (var fixture in new[] { (X: -18f, Name: "roof"), (X: -6f, Name: "too-high"),
                (X: 6f, Name: "narrow-support"), (X: 18f, Name: "thin-side-rail") })
                Case(fixture.Name + "/reject", () =>
                {
                    Capture capture = Trace(Hunter.Samus, MovementModel.Parkour, fixture.X, "fresh"); rows.Add(capture);
                    Check(capture.Contact >= 0 && capture.Request > capture.Contact,
                        "negative fixture earns actual front contact and a fresh keyboard request");
                    Check(capture.Ticks.All(t => !t.Applied && !t.Active && !t.Completed),
                        "obstructed, excessive or unsupported route never starts a mantle");
                });
            foreach (string input in new[] { "held-jump", "no-forward" })
                Case(input + "/reject", () =>
                {
                    Capture capture = Trace(Hunter.Samus, MovementModel.Parkour, -30, input); rows.Add(capture);
                    Check(capture.Ticks.All(t => !t.Applied && !t.Active && !t.Completed),
                        "held Jump or released Forward cannot initiate a mantle");
                    if (input == "no-forward") Check(capture.Contact >= 0 && capture.Request > capture.Contact,
                        "no-Forward case first physically earns the same front contact");
                });
            foreach (string actor in new[] { "field-contact", "field-route" })
                Case(actor + "/native-actor-reject", () =>
                {
                    Capture capture = Trace(Hunter.Samus, MovementModel.Parkour, -30, actor); rows.Add(capture);
                    Check(capture.Contact >= 0 && capture.Request > capture.Contact,
                        "actor negative earns actual keyboard contact before its fresh request");
                    Check(capture.Ticks.All(t => !t.Applied && !t.Active && !t.Completed),
                        "active native actor cannot seed or obstruct an admitted static mantle route");
                    Tick request = capture.Ticks[capture.Request];
                    Check(request.JumpPressed && request.NativeJump && request.Velocity.Y > .01f,
                        "actor admission refusal preserves the ordinary native fresh Jump");
                    if (actor == "field-contact")
                        Check(capture.Ticks.Take(capture.Request).Any(t => t.ActorContact)
                            && request.SeedBlockedBefore,
                            "native forcefield contact veto is physically earned rather than injected");
                    else
                        Check(request.SeedBefore && !request.SeedBlockedBefore
                            && !capture.Ticks.Take(capture.Request).Any(t => t.ActorContact),
                            "route-only actor starts from a genuine static wall seed without touching the raised field");
                });
            Case("native-Unlock-API-clears-route-actor", () =>
            {
                Capture capture = Trace(Hunter.Samus, MovementModel.Parkour, -30, "field-unlocked"); rows.Add(capture);
                Check(capture.Ticks.All(t => !t.ActorActive)
                    && capture.Ticks.Count(t => t.Completed) == 1
                    && capture.Ticks.Count(t => t.Applied) > 1,
                    "initial native Unlock API retires the identical field and restores the positive mantle");
            });
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum,
                MovementModel.Quake3, MovementModel.Traversal })
                Case(model + "/isolation", () =>
                {
                    Capture capture = Trace(Hunter.Samus, model, -30, "fresh"); rows.Add(capture);
                    Check(capture.Contact >= 0 && capture.Request > capture.Contact,
                        "excluded model earns contact and the same fresh keyboard request");
                    Check(capture.Ticks.All(t => !t.Active && !t.Applied && !t.Completed),
                        "Classic and historical movement selections cannot mantle");
                    Check(capture.Ticks[capture.Request].Velocity.Y > .01f,
                        "excluded model retains the native fresh Jump");
                });
            foreach (string action in new[] { "release", "morph", "teleport" })
                Case(action + "/cancel", () =>
                {
                    Capture capture = Trace(Hunter.Samus, MovementModel.Parkour, -30, action); rows.Add(capture);
                    Check(capture.Abort > capture.Request && capture.Ticks.Take(capture.Abort).Any(t => t.Active && t.Applied),
                        "cancellation follows an actually earned and advancing mantle");
                    Check(capture.Ticks.Skip(capture.Abort).All(t => !t.Active && !t.Applied && !t.Completed),
                        "release, native morph or native Teleport immediately retires mantle assistance");
                    if (action == "morph") Check(capture.Ticks.Any(t => t.Alt || t.Transition),
                        "real keyboard morph reaches the native form path");
                });
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL",
                checks = _checks, failures, scope = "Windows CPU production keyboard and authored static collision; initial actor construction and native Unlock plus one labeled same-origin native Teleport API; no renderer, network or replay qualification", rows },
                new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        Console.WriteLine($"PARKOUR MANTLE SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Capture Trace(Hunter hunter, MovementModel model, float x, string scenario)
    {
        object?[] arguments = { hunter, model, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity p = scene.Players.Main!;
            ForceFieldEntity? actor = null;
            if (scenario is "field-contact" or "field-route" or "field-unlocked")
            {
                bool contactActor = scenario == "field-contact";
                var editor = new ForceFieldEntityEditor { Id = 31001, ForceFieldType = 9,
                    Position = new Vector3(-30, contactActor ? .75f : 2, contactActor ? 16.8f : 17.7f),
                    Up = Vector3.UnitY, Facing = -Vector3.UnitZ, Width = contactActor ? 2 : 2.5f,
                    Height = contactActor ? 2 : .3f, Active = true };
                byte[] bytes;
                using (var stream = new MemoryStream())
                {
                    using var writer = new BinaryWriter(stream);
                    typeof(Repack).GetMethod("WriteEntity", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, new object[] { editor, writer });
                    bytes = stream.ToArray();
                }
                actor = new ForceFieldEntity(Read.ReadStruct<ForceFieldEntityData>(bytes), "", scene);
                scene.AddEntity(actor);
                Check(actor.Active && actor.Data.Type == 9, "serialized production no-lock native forcefield starts active");
                if (scenario == "field-unlocked")
                {
                    actor.HandleMessage(new MessageInfo(Message.Unlock, p, actor, 0, 0, scene.FrameCount, scene.FrameCount));
                    Check(!actor.Active, "initial production Unlock message disables actor collision");
                }
            }
            Vector3 seed = new(x, .5f, 14);
            p.Reposition(seed, Vector3.UnitZ, NodeRef.None); p.PrevPosition = seed; p.ModSetSpawnFacing(Vector3.UnitZ);
            for (int i = 0; i < 12; i++) scene.OnSimulationFrame();
            Check(p.Flags1.TestFlag(PlayerFlags1.Standing), "initial labeled lane rests on real floor");
            int contact = -1, request = -1, abort = -1, activeTicks = 0;
            bool finished = false; var ticks = new Tick[130];
            for (int i = 0; i < ticks.Length; i++)
            {
                if (p.ModParkourMantleActive) activeTicks++;
                if (activeTicks > 0 && !p.ModParkourMantleActive) finished = true;
                if (request < 0 && contact >= 0) request = contact + 5;
                if (abort < 0 && activeTicks >= 3 && scenario is "release" or "morph" or "teleport")
                {
                    abort = i;
                    if (scenario == "teleport")
                    {
                        Vector3 origin = p.Position;
                        p.Teleport(origin, p.FacingVector, NodeRef.None);
                        Check(p.Position == origin, "labeled native Teleport cancellation leaves origin unchanged");
                        Check(!p.ModParkourMantleActive, "native Teleport API cancels mantle immediately");
                    }
                }
                bool forward = !finished && abort < 0 && !(scenario == "no-forward" && contact >= 0);
                bool jump = scenario == "held-jump" || i == request;
                SetKey(keyboard, Keys.W, forward); SetKey(keyboard, Keys.Space, jump);
                SetKey(keyboard, Keys.Tab, scenario == "morph" && i == abort);
                ulong frame = scene.FrameCount; long steps = scene.DeveloperMomentumSteps;
                bool seedBefore = (bool)Seed.GetValue(p)!, seedBlockedBefore = (bool)SeedBlocked.GetValue(p)!;
                ulong contactFrameBefore = (ulong)ContactFrame.GetValue(p)!;
                Vector3 seedNormal = (Vector3)SeedNormal.GetValue(p)!;
                float seedPlaneBefore = (float)SeedPlane.GetValue(p)!;
                scene.OnSimulationFrame();
                Check(p.Health > 0 && p.ModIsInPlay && !NetSession.Active, "living offline owned keyboard player");
                Check(p.Controls.MoveUp.IsDown == forward && p.Controls.Jump.IsDown == jump,
                    "production keyboard levels match authored input");
                Check(float.IsFinite(p.Position.LengthSquared) && float.IsFinite(p.Speed.LengthSquared), "finite native motion");
                bool front = false;
                float radius = Fixed.ToFloat(p.Values.BipedColRadius);
                foreach (CollisionResult hit in (CollisionResult[])Contacts.GetValue(p)!)
                {
                    if (hit.Field0 != 0 || hit.EntityCollision != null || hit.Slipperiness != 0
                        || hit.Flags.TestFlag(CollisionFlags.Damaging) || hit.Terrain == Terrain.Lava) continue;
                    if (hit.Plane.Z < -.999f && MathF.Abs(hit.Plane.W + 17) < .001f
                        && MathF.Abs(Vector3.Dot(p.Position, hit.Plane.Xyz) - hit.Plane.W - radius) <= .03f)
                        front = true;
                }
                bool actorContact = false;
                if (actor?.Active == true)
                {
                    Vector3 actorBetween = p.Volume.SpherePosition - actor.Position;
                    float actorGap = MathF.Abs(Vector3.Dot(p.Position, actor.Plane.Xyz) - actor.Plane.W) - radius;
                    actorContact = MathF.Abs(actorGap) <= .03f
                        && MathF.Abs(Vector3.Dot(actorBetween, actor.FieldUpVector)) <= actor.Height
                        && MathF.Abs(Vector3.Dot(actorBetween, actor.FieldRightVector)) <= actor.Width;
                }
                if ((front || actorContact) && contact < 0) contact = i;
                CollisionResult support = default;
                Vector3 foot = p.Position.AddY(p.ModBipedBottom);
                bool safeTop = CollisionDetection.CheckBetweenPoints(foot.AddY(.06f), foot.AddY(-.06f),
                    TestFlags.Players, scene, ref support) && support.EntityCollision == null
                    && support.Field0 == 0 && support.Slipperiness == 0
                    && !support.Flags.TestFlag(CollisionFlags.Damaging) && support.Terrain != Terrain.Lava
                    && support.Plane.Xyz == Vector3.UnitY && MathF.Abs(support.Plane.W - 1) < .001f;
                bool nativeJump = scene.DeveloperMomentumSteps > steps
                    && scene.LastMomentumStep!.Value.ContactPhase == MomentumContactPhase.LegalJump;
                ticks[i] = new(i, p.Position, p.Speed, p.Flags1.TestFlag(PlayerFlags1.Standing), front,
                    forward, p.Controls.Jump.IsPressed, p.ModParkourMantleActive,
                    p.ModParkourMantleApplied(frame), p.ModParkourMantleCompleted(frame), p.ModParkourMantleTicks,
                    nativeJump, p.IsAltForm, p.IsMorphing || p.IsUnmorphing, safeTop,
                    p.ModParkourMantleAdmission, seedBefore, seedBlockedBefore, contactFrameBefore,
                    seedNormal.X, seedNormal.Y, seedNormal.Z, seedPlaneBefore,
                    actorContact, actor?.Active == true);
            }
            return new(scenario, hunter.ToString(), model.ToString(), contact, request, abort, ticks);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
