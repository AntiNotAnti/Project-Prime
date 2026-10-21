using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Formats.Collision;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Real offline keyboard and static authored wall faces.
// Only initial lane placement is synthetic. Later stale invocations inspect the
// frame fence without modifying frame, body, velocity, contact or input state.
internal static class ParkourWallstrafeSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly MethodInfo Apply = typeof(PlayerEntity).GetMethod("ModApplyParkourWallstrafe", Hidden)!;
    private static readonly FieldInfo Attempt = typeof(PlayerEntity).GetField("_modWallstrafeAttempt", Hidden)!;
    private static readonly FieldInfo Contacts = typeof(PlayerEntity).GetField("_collisionScratch", Hidden)!;
    private static int _checks;
    private sealed record Command(bool Forward = true, int Strafe = 1, bool Crouch = false, bool Jump = false);
    private sealed record Tick(int Index, ulong Frame, float X, float Y, float Z, float VelocityX,
        float VelocityY, float VelocityZ, bool Applied, bool Lateral, bool Standing, bool Crouched, bool Sliding, bool Tucked, bool StaticBoundaryFace, bool CornerFaces);
    private sealed record Capture(string Scenario, string Model, Tick[] Ticks);
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float epsilon = .0001f)
        => Check(float.IsFinite(a) && float.IsFinite(b) && MathF.Abs(a - b) <= epsilon, $"{label}: {a} != {b}");
    private static bool SameBits(Vector3 a, Vector3 b)
        => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
        && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
        && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

    internal static int Run(string? output)
    {
        _checks = 0; var rows = new List<object>(); var failures = new List<string>();
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline nonrecording process");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            void Case(string name, Action work)
            {
                try { work(); Console.WriteLine("PARKOUR WALLSTRAFE SCENE PASS " + name); }
                catch (Exception error)
                { failures.Add(name + ": " + error); Console.Error.WriteLine("PARKOUR WALLSTRAFE SCENE FAIL " + name + ": " + error); }
            }
            Case("real-wall-gain-and-mirror", () =>
            {
                Capture east = CaptureScene("east-W-D", MovementModel.Parkour, new(42, .5f, 20),
                    new(3, 0, 1), 1, 130, _ => new());
                Capture west = CaptureScene("west-W-A", MovementModel.Parkour, new(-42, .5f, 20),
                    new(-3, 0, 1), -1, 130, _ => new());
                Capture legacy = CaptureScene("Traversal-east-control", MovementModel.Traversal, new(42, .5f, 20),
                    new(3, 0, 1), 1, 130, _ => new());
                rows.Add(new { east, west, legacy });
                Tick[] earned = east.Ticks.Where(t => t.Applied).ToArray();
                Check(earned.Length >= 5, "normal keyboard motion earns repeated actual static wall impulses");
                Check(earned.All(t => t.X > 46 && t.X < 48 && t.Lateral && t.StaticBoundaryFace),
                    "gain witness belongs to physically reached east wall face");
                Check(east.Ticks.TakeWhile(t => t.X < 46).All(t => !t.Applied),
                    "approaching wall earns no proximity/grace impulse");
                Check(legacy.Ticks.All(t => !t.Applied), "Traversal control never applies Parkour wall impulse");
                Check(earned.Any(t => t.VelocityZ > TraversalMovementProfileV1.GroundSpeed / 30f),
                    "native wall clipping plus tangent gain earns speed above ordinary ground speed");
                Check(earned.All(t => t.VelocityZ * 30 <= WallstrafeMovementV1.MaxTangentSpeed + .001f),
                    "real wall gain remains bounded by profile tangent cap");
                Check(east.Ticks[^1].Z > legacy.Ticks[^1].Z + .1f,
                    "physically earned Parkour wall gain travels farther than same-input Traversal control");
                Check(west.Ticks.Count(t => t.Applied) == earned.Length, "mirrored static wall earns same number of impulses");
                for (int i = 0; i < east.Ticks.Length; i++)
                {
                    Tick a = east.Ticks[i], b = west.Ticks[i];
                    Check(!b.Applied || b.StaticBoundaryFace, "west gain has actual static plane witness");
                    Check(a.Applied == b.Applied, "mirrored gain witness agrees each tick");
                    Near(a.X, -b.X, "mirror body X"); Near(a.Z, b.Z, "mirror tangent travel");
                    Near(a.VelocityX, -b.VelocityX, "mirror normal speed");
                    Near(a.VelocityZ, b.VelocityZ, "mirror tangent speed");
                    Check(BitConverter.SingleToInt32Bits(a.VelocityY) == BitConverter.SingleToInt32Bits(b.VelocityY),
                        "mirror preserves native Y velocity bits");
                    Check(BitConverter.SingleToInt32Bits(a.VelocityY) == BitConverter.SingleToInt32Bits(legacy.Ticks[i].VelocityY),
                        "wall acceleration preserves native flat-lane Y against same-input control");
                }
            });
            Case("no-wall-head-on-no-strafe-away-and-crouch", () =>
            {
                var controls = new[]
                {
                    CaptureScene("no-wall", MovementModel.Parkour, new(0, .5f, 20), new(3, 0, 1), 1, 80, _ => new()),
                    // Facing (1,0,-1), native right=(-facing.Z,0,facing.X): W+D is head-on +X.
                    CaptureScene("head-on-W-D", MovementModel.Parkour, new(42, .5f, 20), new(1, 0, -1), 1, 130, _ => new()),
                    CaptureScene("no-strafe", MovementModel.Parkour, new(42, .5f, 20), new(3, 0, 1), 1, 130, _ => new(Strafe: 0)),
                    CaptureScene("away-from-wall", MovementModel.Parkour, new(46, .5f, 20), new(-3, 0, 1), -1, 80, _ => new()),
                    CaptureScene("grounded-crouch", MovementModel.Parkour, new(42, .5f, 20), new(3, 0, 1), 1, 240, _ => new(Crouch: true))
                };
                rows.Add(controls);
                foreach (Capture capture in controls)
                    Check(capture.Ticks.All(t => !t.Applied), capture.Scenario + " cannot earn wallstrafe gain");
                Check(controls[1].Ticks.Any(t => t.Lateral) && controls[2].Ticks.Any(t => t.Lateral),
                    "head-on and no-strafe controls reach actual wall contact");
                Check(controls[4].Ticks.Any(t => t.Lateral && t.Standing && t.Crouched),
                    "ground crouch exclusion is tested at actual wall contact");
            });
            Case("departure-corner-and-airborne-tuck", () =>
            {
                Capture departure = CaptureScene("leave-wall", MovementModel.Parkour, new(46, .5f, 20),
                    new(3, 0, 1), 1, 160, i => i < 90 ? new() : new(Forward: false));
                Capture corner = CaptureScene("east-front-corner", MovementModel.Parkour, new(42, .5f, 34),
                    new(3, 0, 1), 1, 220, _ => new());
                Capture tuck = CaptureScene("jump-then-tuck-at-wall", MovementModel.Parkour, new(46, .5f, 20),
                    new(3, 0, 1), 1, 150, i => new(Jump: i == 70, Crouch: i >= 72 && i < 92));
                rows.Add(new { departure, corner, tuck });
                Check(departure.Ticks.Take(90).Any(t => t.Applied), "departure starts from earned wall gain");
                Check(departure.Ticks.Skip(90).All(t => !t.Applied), "released forward cannot retain wall gain");
                Check(departure.Ticks.Skip(100).Any(t => !t.Lateral), "normal movement physically leaves wall");
                Tick[] cornerTicks = corner.Ticks.Where(t => t.CornerFaces).ToArray();
                Check(cornerTicks.Length > 0, "corner control reaches two actual perpendicular static boundary planes");
                Check(cornerTicks.All(t => !t.Applied), "two-wall corner cannot gain tangent impulse");
                Tick[] tucked = tuck.Ticks.Where(t => t.Tucked && !t.Standing && t.StaticBoundaryFace).ToArray();
                Check(tucked.Length > 0, "jump then crouch earns airborne tuck at actual wall");
                Check(tucked.Any(t => t.Applied), "airborne tuck remains eligible for bounded wallstrafe");
            });
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum,
                MovementModel.Quake3, MovementModel.Traversal })
                Case(model + "/legacy-isolation", () =>
                {
                    Capture capture = CaptureScene("legacy-same-wall-input", model, new(42, .5f, 20),
                        new(3, 0, 1), 1, 130, _ => new());
                    rows.Add(capture);
                    Check(capture.Ticks.Any(t => t.Lateral), "same keyboard control physically reaches wall");
                    Check(capture.Ticks.All(t => !t.Applied), "legacy movement cannot apply Parkour gain");
                });
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL", checks = _checks,
                failures, scope = "Offline CPU keyboard, authored training walls, one initial placement; stale completed-contact invocation checks frame fence; no renderer/network/replay qualification", rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"PARKOUR WALLSTRAFE SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Capture CaptureScene(string scenario, MovementModel model, Vector3 seed, Vector3 facing,
        int strafeSign, int length, Func<int, Command> choose)
    {
        object?[] arguments = { Hunter.Samus, model, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity p = scene.Players.Main!; p.Controls.Crouch.Key = Keys.LeftControl;
            p.Reposition(seed, facing.Normalized(), NodeRef.None); p.PrevPosition = seed;
            p.ModSetSpawnFacing(facing.Normalized());
            for (int i = 0; i < 12; i++) scene.OnSimulationFrame();
            Check(p.Flags1.TestFlag(PlayerFlags1.Standing), "initial labeled lane settles on native physical support");
            var ticks = new Tick[length];
            for (int i = 0; i < length; i++)
            {
                Command c = choose(i); int strafe = c.Strafe * strafeSign;
                SetKey(keyboard, Keys.W, c.Forward); SetKey(keyboard, Keys.A, strafe < 0);
                SetKey(keyboard, Keys.D, strafe > 0); SetKey(keyboard, Keys.LeftControl, c.Crouch);
                SetKey(keyboard, Keys.Space, c.Jump);
                ulong frame = scene.FrameCount; scene.OnSimulationFrame();
                Check(p.Health > 0 && p.ModIsInPlay && !p.IsBot && !p.IsAltForm && !NetSession.Active,
                    "living offline owned biped input path");
                Check(p.Controls.MoveUp.IsDown == c.Forward && p.Controls.MoveRight.IsDown == (strafe > 0)
                    && p.Controls.MoveLeft.IsDown == (strafe < 0) && p.Controls.Crouch.IsDown == c.Crouch,
                    "real production keyboard movement levels");
                Check(float.IsFinite(p.Position.LengthSquared) && float.IsFinite(p.Speed.LengthSquared), "finite native body and velocity");
                bool applied = p.ModParkourWallstrafeApplied(frame);
                if (applied)
                {
                    Vector3 speed = p.Speed, position = p.Position;
                    Check((ulong)Attempt.GetValue(p)! == frame, "actual production wall attempt owns completed simulation frame");
                    Apply.Invoke(p, null); Apply.Invoke(p, null);
                    Check(SameBits(p.Speed, speed), "later repeated stale-contact calls preserve exact native XYZ velocity bits");
                    Check(SameBits(p.Position, position), "stale-contact calls never translate body");
                    Check((ulong)Attempt.GetValue(p)! == frame, "stale calls cannot rearm completed-frame attempt");
                }
                bool boundaryFace = false, eastFace = false, frontFace = false;
                float radius = Fixed.ToFloat(p.Values.BipedColRadius);
                foreach (CollisionResult hit in (CollisionResult[])Contacts.GetValue(p)!)
                {
                    if (hit.Field0 != 0 || hit.EntityCollision != null || MathF.Abs(hit.Plane.Y) > .001f) continue;
                    float gap = MathF.Abs(Vector3.Dot(p.Position, hit.Plane.Xyz) - radius - hit.Plane.W);
                    if (gap > .025f) continue;
                    bool east = hit.Plane.X < -.99f && MathF.Abs(hit.Plane.Z) < .001f && p.Position.X > 47;
                    bool west = hit.Plane.X > .99f && MathF.Abs(hit.Plane.Z) < .001f && p.Position.X < -47;
                    bool front = hit.Plane.Z < -.99f && MathF.Abs(hit.Plane.X) < .001f && p.Position.Z > 39;
                    boundaryFace |= east || west || front; eastFace |= east; frontFace |= front;
                }
                Check(!applied || boundaryFace, "every applied impulse has current static boundary face and plane-distance witness");
                ticks[i] = new(i, frame, p.Position.X, p.Position.Y, p.Position.Z, p.Speed.X, p.Speed.Y, p.Speed.Z,
                    applied, p.Flags1.TestFlag(PlayerFlags1.CollidingLateral), p.Flags1.TestFlag(PlayerFlags1.Standing), p.ModCrouched, p.ModSliding, p.ModTucked, boundaryFace, eastFace && frontFace);
            }
            return new(scenario, model.ToString(), ticks);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
