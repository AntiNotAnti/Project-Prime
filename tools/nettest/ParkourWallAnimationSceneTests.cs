using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Initial labeled lane placement, then actual keyboard/collision/controller
// witnesses. A native same-origin Teleport is the only midflight API fixture.
// No accepted cue, contact, controller, body, velocity or clock injection.
internal static class ParkourWallAnimationSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly FieldInfo Contacts = typeof(PlayerEntity).GetField("_collisionScratch", Hidden)!;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly List<object> Rows = new();
    private static readonly HashSet<string> Coverage = new();
    private static readonly HashSet<string> BodyCoverage = new();
    private static int _checks;
    private static string _scenario = "";
    private sealed record Tick(int TickIndex, ulong Frame, Vector3 Position, Vector3 Speed,
        byte Cue, float Amount, bool Run, bool Kick, bool Gain, bool Standing, bool Tucked, int Remaining);
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(_scenario + ": " + message); }
    private static void Near(float a, float b, string label, float tolerance = .0003f)
        => Check(float.IsFinite(a) && float.IsFinite(b) && MathF.Abs(a - b) <= tolerance, $"{label}: {a} versus {b}");
    private static int Kind(byte cue) => cue is >= 1 and <= 12 ? (cue - 1) / 4 + 1 : 0;
    private static Vector3 Normal(byte cue) => cue is >= 1 and <= 12 ? (((cue - 1) % 4) switch
        { 0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ }) : Vector3.Zero;

    internal static int Run(string? output = null)
    {
        _checks = 0; Rows.Clear(); Coverage.Clear(); BodyCoverage.Clear();
        ParkourAnimationSceneTests.BeginWallProbes(); var failures = new List<string>();
        int priorCap = FrameTiming.FrameRateCap;
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording, "fresh offline CPU owner");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter(); FrameTiming.FrameRateCap = 60;
            void Case(string name, Action action)
            {
                _scenario = name;
                try { action(); Console.WriteLine("PARKOUR WALL ANIMATION SCENE PASS " + name); }
                catch (Exception error) { failures.Add(name + ": " + error); Console.Error.WriteLine("PARKOUR WALL ANIMATION SCENE FAIL " + name + ": " + error); }
            }
            foreach (Hunter hunter in Hunters)
            {
                foreach (string action in new[] { "wallstrafe", "air-wallstrafe", "wallrun", "wallkick", "tuck-wallstrafe" })
                    Case(hunter + "/" + action + "/native-mirror", () =>
                    {
                        Tick[] east = Positive(hunter, action, 1);
                        Tick[] west = Positive(hunter, action, -1);
                        Check(east.Length == west.Length, "mirrored trajectories retain the same actual fixed-tick interval");
                        for (int i = 0; i < east.Length; i++)
                        {
                            Tick a = east[i], b = west[i];
                            Near(a.Position.X, -b.Position.X, "mirrored native X"); Near(a.Position.Y, b.Position.Y, "mirrored native height");
                            Near(a.Position.Z, b.Position.Z, "mirrored native tangent travel");
                            Near(a.Speed.X, -b.Speed.X, "mirrored normal velocity"); Near(a.Speed.Y, b.Speed.Y, "mirrored native vertical velocity");
                            Near(a.Speed.Z, b.Speed.Z, "mirrored tangent velocity");
                            Check(Kind(a.Cue) == Kind(b.Cue) && a.Amount == b.Amount && a.Run == b.Run
                                && a.Kick == b.Kick && a.Tucked == b.Tucked && a.Remaining == b.Remaining,
                                "mirrored native movement earns matching cue family, weight, stance and controller budget");
                        }
                    });
                Case(hunter + "/held-takeoff-is-not-kick", () => HeldJump(hunter));
            }
            foreach (string action in new[] { "release", "morph", "teleport", "corner", "no-wall", "head-on", "no-strafe", "ground-crouch" })
                Case("Samus/" + action, () => Exclusion(action));
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3, MovementModel.Traversal })
                Case(model + "/historical-isolation", () => Historical(model));
            Check(Coverage.Count == 70, "all7 Hunters earn five real wall actions on both mirrored wall normals");
            Check(BodyCoverage.Count == 140, "both native LODs visibly animate every earned Hunter/action/wall-side combination");
            Check(ParkourAnimationSceneTests.WallAllocationProbeCount == 4,
                "four real strafe/run/kick/tuck native pose samples each execute20k warmed zero-allocation scopes");
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally { FrameTiming.FrameRateCap = priorCap; }
        int checks = _checks + ParkourAnimationSceneTests.WallProbeChecks;
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL", checks,
                movementChecks = _checks, poseChecks = ParkourAnimationSceneTests.WallProbeChecks, failures,
                coverage = Coverage.Order().ToArray(), bodyCoverage = BodyCoverage.Order().ToArray(),
                scope = "CPU actual7-Hunter/two-LOD keyboard/native collision on mirrored authored walls; real takeoff/freshkick/tuck/at-cap contact and native same-originTeleport; render-only paired owner cues, exact native state/palette restoration; no injected controller/cue, GL, UDP, replay or performance qualification",
                rows = Rows, poseRows = ParkourAnimationSceneTests.WallProbeRows }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"PARKOUR WALL ANIMATION SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Scene Scene;
        internal readonly PlayerEntity Player;
        internal readonly KeyboardState Keyboard;
        internal readonly int Side;
        internal Fixture(Hunter hunter, int side, bool approach = false, float z = 10,
            MovementModel movement = MovementModel.Parkour, Vector3? facing = null, bool noWall = false)
        {
            Side = side;
            object?[] args = { hunter, movement, null };
            Scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
            try
            {
                Player = Scene.Players.Main!; Keyboard = (KeyboardState)args[2]!;
                Player.Controls.Crouch.Key = Keys.LeftControl; Player.Controls.Morph.Key = Keys.Tab;
                Vector3 heading = (facing ?? new Vector3(side * 3, 0, 1)).Normalized();
                Vector3 seed = new(noWall ? 0 : side * (approach ? 42 : 46), .5f, z);
                Player.Reposition(seed, heading, NodeRef.None); Player.PrevPosition = seed; Player.ModSetSpawnFacing(heading);
                for (int i = 0; i < 12; i++) Scene.OnSimulationFrame();
                Check(Player.Flags1.TestFlag(PlayerFlags1.Standing), "initial labeled lane settles on actual native floor");
            }
            catch { MomentumSceneTests.Release(Scene); throw; }
        }
        internal ulong Step(bool forward = true, bool strafe = true, bool jump = false, bool crouch = false, bool morph = false)
        {
            SetKey(Keyboard, Keys.W, forward); SetKey(Keyboard, Keys.D, strafe && Side > 0);
            SetKey(Keyboard, Keys.A, strafe && Side < 0); SetKey(Keyboard, Keys.Space, jump);
            SetKey(Keyboard, Keys.LeftControl, crouch); SetKey(Keyboard, Keys.Tab, morph);
            ulong frame = Scene.FrameCount; Scene.OnSimulationFrame();
            Check(Player.Controls.MoveUp.IsDown == forward && Player.Controls.Jump.IsDown == jump
                && Player.Controls.Crouch.IsDown == crouch && Player.Controls.MoveRight.IsDown == (strafe && Side > 0)
                && Player.Controls.MoveLeft.IsDown == (strafe && Side < 0), "real production keyboard movement levels");
            Check(Player.Health > 0 && Player.ModIsInPlay && !NetSession.Active
                && float.IsFinite(Player.Position.LengthSquared) && float.IsFinite(Player.Speed.LengthSquared), "finite living actual offline native owner");
            return frame;
        }
        internal bool Face(Vector3 normal)
        {
            float radius = Fixed.ToFloat(Player.Values.BipedColRadius);
            foreach (CollisionResult hit in (CollisionResult[])Contacts.GetValue(Player)!)
                if (hit.EntityCollision == null && hit.Field0 == 0 && hit.Slipperiness == 0 && hit.Terrain != Terrain.Lava
                    && !hit.Flags.TestFlag(CollisionFlags.Damaging) && hit.Plane.Xyz == normal
                    && MathF.Abs(Vector3.Dot(Player.Position, normal) - hit.Plane.W - radius) <= .03f) return true;
            return false;
        }
        public void Dispose() => MomentumSceneTests.Release(Scene);
    }

    private static Tick[] Positive(Hunter hunter, string action, int side)
    {
        using var f = new Fixture(hunter, side, approach: action == "wallstrafe", z: action == "tuck-wallstrafe" ? 20 : 10);
        PlayerEntity p = f.Player; string key = hunter + "/" + action + "/" + side;
        var ticks = new List<Tick>(); int requestKick = -1, kickTick = -1, earned = 0, gain = 0, cap = 0;
        bool[] changed = new bool[2]; bool finished = false;
        for (int i = 0; i < 150; i++)
        {
            if (action == "wallkick" && requestKick < 0 && p.ModParkourWallrunActive) requestKick = i + 4;
            bool takeoff = action == "tuck-wallstrafe" ? i == 70 : action != "wallstrafe" && i == 32;
            bool crouch = action == "tuck-wallstrafe" && i >= 72 && i < 92;
            if (action == "wallkick" && kickTick >= 0 || action == "wallstrafe" && earned >= 55
                || action == "tuck-wallstrafe" && i >= 100) finished = true;
            ulong frame = f.Step(forward: !finished, strafe: !finished,
                jump: takeoff || i == requestKick, crouch: crouch);
            ParkourAnimationPose pose = p.ModGetParkourAnimationPose();
            bool kick = p.ModParkourWallkickApplied(frame), applied = p.ModParkourWallstrafeApplied(frame);
            if (kick) { Check(kickTick < 0 && i == requestKick && p.Controls.Jump.IsPressed, "one actual fresh keyboard kick event"); kickTick = i; }
            int kind = Kind(pose.WallCue);
            int wanted = action switch { "wallrun" => 2, "wallkick" => 3, _ => 1 };
            bool witness = action switch
            {
                "wallstrafe" => p.Flags1.TestFlag(PlayerFlags1.Standing) && applied,
                "wallrun" => p.ModParkourWallrunActive,
                "wallkick" => kickTick >= 0 && i - kickTick < 12,
                "air-wallstrafe" => !p.Flags1.TestFlag(PlayerFlags1.Standing) && !p.ModTucked
                    && !p.ModParkourWallrunActive && p.ModParkourWallrunRemaining == 0 && applied,
                _ => p.ModTucked && !p.Flags1.TestFlag(PlayerFlags1.Standing) && applied
            };
            if (witness)
            {
                Check(kind == wanted && pose.WallAmount == 1 && pose.Active
                    && pose.WallNormal == new Vector3(-side, 0, 0) && Normal(pose.WallCue) == pose.WallNormal,
                    "accepted native wall movement supplies the exact family/axis cue and live bounded amount");
                Check(pose.MantleProgress == 0 && pose.GrappleAmount == 0, "wall picture cannot fabricate mantle or grapple ownership");
                Check(pose.WallTucked == (action == "tuck-wallstrafe"), "only real airborne tuck marks the wall picture tucked");
                if (wanted != 3) Check(f.Face(pose.WallNormal), "positive run/strafe has a current actual safe static plane witness");
                if (wanted == 3) Check(!p.ModParkourWallrunActive && p.ModParkourWallrunRemaining == 0,
                    "actual kick visual follows the consumed native run without restoring airborne budget");
                earned++; gain += applied ? 1 : 0;
                if (earned <= 3 || earned % 8 == 0 || wanted == 3)
                {
                    bool[] sample = ParkourAnimationSceneTests.InspectWallFrame(f.Scene, p, f.Keyboard, pose, action, i);
                    for (int lod = 0; lod < 2; lod++) changed[lod] |= sample[lod];
                }
            }
            if (action == "tuck-wallstrafe" && p.ModTucked && !p.Flags1.TestFlag(PlayerFlags1.Standing)
                && f.Face(new(-side, 0, 0))
                && MathF.Abs(p.Speed.Z) * PlayerEntity.NativeVelocityUnitsPerSecond >= WallstrafeMovementV1.MaxTangentSpeed - .001f)
            {
                cap++;
                Check(kind == 1 && pose.WallAmount == 1,
                    "genuine continued at-cap airborne tucked wallstrafe retains its current contact picture");
            }
            if (action == "wallkick" && kickTick >= 0 && i - kickTick >= 12)
                Check(kind != 3, "kick visual ends after its declared12 completed ticks without another actual kick");
            ticks.Add(new(i, frame, p.Position, p.Speed, pose.WallCue, pose.WallAmount,
                p.ModParkourWallrunActive, kick, applied, p.Flags1.TestFlag(PlayerFlags1.Standing), p.ModTucked, p.ModParkourWallrunRemaining));
            Rows.Add(new { key, tick = i, frame, pose.WallCue, pose.WallAmount, pose.WallTucked,
                x = p.Position.X, y = p.Position.Y, z = p.Position.Z, run = p.ModParkourWallrunActive,
                vx = p.Speed.X, vy = p.Speed.Y, vz = p.Speed.Z, pose.Seconds,
                normalX = pose.WallNormal.X, normalY = pose.WallNormal.Y, normalZ = pose.WallNormal.Z,
                remaining = p.ModParkourWallrunRemaining, kick, gain = applied, witness });
        }
        Check(earned > 1 && changed.All(value => value), "all positive wall scenarios earn visible native poses on both actual LODs");
        if (action == "wallstrafe") Check(gain > 1, "ground fixture independently earns repeated actual wall acceleration");
        if (action == "tuck-wallstrafe") Check(cap > 0, "actual native airtuck trajectory reaches a sustained tangent-cap interval");
        if (action == "wallkick") Check(kickTick > 32 && earned == 12, "exact12-tick kick picture begins at the actual fresh kick event");
        Coverage.Add(key); for (int lod = 0; lod < 2; lod++) if (changed[lod]) BodyCoverage.Add(key + "/LOD" + lod);
        return ticks.ToArray();
    }

    private static void HeldJump(Hunter hunter)
    {
        using var f = new Fixture(hunter, 1); bool run = false;
        for (int i = 0; i < 110; i++)
        {
            ulong frame = f.Step(jump: i >= 32); var pose = f.Player.ModGetParkourAnimationPose();
            run |= f.Player.ModParkourWallrunActive;
            Check(!f.Player.ModParkourWallkickApplied(frame) && Kind(pose.WallCue) != 3,
                "holding original takeoff never fabricates a kick event or kick picture");
        }
        Check(run, "held-Jump negative genuinely earned an airborne wallrun first");
    }

    private static void Exclusion(string action)
    {
        using var f = new Fixture(Hunter.Samus, 1, z: action == "corner" ? 34 : 10,
            noWall: action == "no-wall", facing: action == "head-on" ? new Vector3(1, 0, -1) : null);
        PlayerEntity p = f.Player; bool earned = false, corner = false, morphed = false; int cancel = -1;
        for (int i = 0; i < 130; i++)
        {
            if (cancel < 0 && p.ModParkourWallrunActive && action is "release" or "morph" or "teleport") cancel = i + 4;
            bool stopped = cancel >= 0 && i >= cancel;
            if (action == "teleport" && i == cancel)
            {
                Vector3 origin = p.Position; int remaining = p.ModParkourWallrunRemaining;
                p.Teleport(origin, p.FacingVector, NodeRef.None);
                Check(p.Position == origin && p.ModParkourWallrunRemaining == remaining,
                    "same-origin native Teleport cancels presentation without body relocation or budget refill");
                Check(p.ModGetParkourAnimationPose().WallCue == 0,
                    "native Teleport retires old-wall presentation immediately before another input tick");
            }
            bool jump = i == (action == "corner" ? 8 : 32) && action != "ground-crouch";
            ulong frame = f.Step(forward: !stopped, strafe: !stopped && action != "no-strafe", jump: jump,
                crouch: action == "ground-crouch", morph: action == "morph" && i == cancel);
            earned |= p.ModParkourWallrunActive; morphed |= p.IsAltForm || p.IsMorphing || p.IsUnmorphing;
            var pose = p.ModGetParkourAnimationPose();
            bool corners = f.Face(-Vector3.UnitX) && f.Face(-Vector3.UnitZ); corner |= corners;
            if (stopped || corners || action is "no-wall" or "head-on" or "no-strafe" or "ground-crouch")
            {
                Check(pose.WallCue == 0 && pose.WallAmount == 0 && !p.ModParkourWallkickApplied(frame),
                    "actual input/geometry/lifecycle exclusion cannot retain a wall picture or manufacture a kick");
                if (i % 30 == 0 && !p.IsAltForm && !p.IsMorphing && !p.IsUnmorphing)
                    ParkourAnimationSceneTests.InspectWallFrame(f.Scene, p, f.Keyboard, pose, "excluded-" + action, i);
            }
        }
        if (action is "release" or "morph" or "teleport") Check(earned && cancel > 32, "lifecycle exclusion follows an actually earned wallrun");
        if (action == "morph") Check(morphed, "native keyboard morph reaches the real form transition");
        if (action == "corner") Check(earned && corner, "corner exclusion reaches both actual perpendicular static wall faces after a run");
    }

    private static void Historical(MovementModel movement)
    {
        using var f = new Fixture(Hunter.Samus, 1, movement: movement); bool wall = false;
        for (int i = 0; i < 100; i++)
        {
            f.Step(jump: i == 32); wall |= f.Face(-Vector3.UnitX);
            var pose = f.Player.ModGetParkourAnimationPose();
            Check(pose.WallCue == 0 && pose.WallAmount == 0, "native and historical movement selections never gain wall presentation");
            if (i % 30 == 0) ParkourAnimationSceneTests.InspectWallFrame(f.Scene, f.Player, f.Keyboard, pose, "historical-" + movement, i);
        }
        Check(wall, "historical profile comparison physically reaches the same real static wall");
    }
}
