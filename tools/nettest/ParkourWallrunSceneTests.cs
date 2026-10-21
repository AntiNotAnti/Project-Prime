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
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Parkour v3 controller acceptance through production keyboard and collision.
// Real keyboard/native collision; only initial fixture placement. One labeled
// lifecycle case invokes native Teleport at the unchanged current origin; no
// direct midflight body, speed, flags, contact or timer writes.
internal static class ParkourWallrunSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Budget = 45, JumpTick = 32;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly FieldInfo Contacts = typeof(PlayerEntity).GetField("_collisionScratch", Hidden)!;
    private static readonly FieldInfo Gravity = typeof(PlayerEntity).GetField("_gravity", Hidden)!;
    private static int _checks;
    private sealed record Command(bool Forward = true, int Strafe = 1, bool Jump = false, bool Crouch = false);
    private sealed record Tick(int Index, float X, float Y, float Z, float Vx, float Vy, float Vz,
        bool Standing, bool Active, int Remaining, bool Kick, bool JumpPressed, bool NativeJump,
        bool EastFace, bool FrontFace, bool SafeFloor, float Gravity, float NativeGravity, float StoredGravity);
    private sealed record Capture(string Scenario, string Model, Command[] Commands, Tick[] Ticks);
    private static void Check(bool condition, string label)
    { _checks++; if (!condition) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float epsilon = .0001f)
        => Check(float.IsFinite(a) && MathF.Abs(a - b) <= epsilon, $"{label}: {a} != {b}");

    internal static int Run(string? output)
    {
        _checks = 0; var rows = new List<object>(); var failures = new List<string>();
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording, "fresh offline scene");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            void Case(string name, Action action)
            {
                try { action(); Console.WriteLine("PARKOUR WALLRUN SCENE PASS " + name); }
                catch (Exception error)
                { failures.Add(name + ": " + error); Console.Error.WriteLine("PARKOUR WALLRUN SCENE FAIL " + name + ": " + error); }
            }
            Case("earned-airborne-run-and-fixed-budget", () => BudgetAndGravity(rows));
            Case("release-loss-and-corner-cancel", () => Cancellation(rows));
            Case("native-Teleport-API-cancels-run-without-budget-refill", () => TeleportCancellation(rows));
            Case("fresh-kick-versus-held-jump-and-static-refill", () => KickAndRefill(rows));
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum,
                MovementModel.Quake3, MovementModel.Traversal })
                Case(model + "/isolation", () =>
                {
                    Capture capture = Trace("same-wall-jump-input", model, 130, new(46, .5f, 20),
                        (i, _) => new(Jump: i == JumpTick));
                    rows.Add(capture);
                    Check(capture.Ticks.Any(t => t.EastFace && !t.Standing), "excluded-model keyboard jump reaches actual wall");
                    Check(capture.Ticks.All(t => !t.Active && !t.Kick), "other models never run or kick");
                });
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL",
                checks = _checks, failures, scope = "Windows CPU keyboard and static authored training collision; initial placement plus one same-origin native Teleport API lifecycle check (not a map trigger); no renderer or multiplayer qualification", rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"PARKOUR WALLRUN SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void BudgetAndGravity(List<object> rows)
    {
        Capture run = Trace("wall-jump-approach-and-budget", MovementModel.Parkour, 180, new(46, .5f, 10),
            (i, _) => new(Jump: i == JumpTick));
        Capture ordinary = Trace("Traversal-native-gravity-control", MovementModel.Traversal, run.Commands.Length,
            new(46, .5f, 10), (i, _) => run.Commands[i]);
        rows.Add(new { run, ordinary });
        Check(run.Ticks[JumpTick].NativeJump, "ordinary fresh keyboard Jump earns takeoff");
        int first = Array.FindIndex(run.Ticks, t => t.Active);
        Check(first >= JumpTick && first < JumpTick + 30, "real airborne wall contact activates bounded run");
        Check(run.Ticks.Take(JumpTick).All(t => !t.Active && t.Remaining == Budget),
            "standing wall contact cannot spend or activate run budget");
        Tick[] airborne = run.Ticks.Skip(first).TakeWhile(t => !t.Standing).ToArray();
        Check(airborne.Length > Budget, "fixture stays physically airborne through full run budget");
        int spent = 0;
        for (int i = first; i < first + airborne.Length; i++)
        {
            Tick t = run.Ticks[i]; int previous = run.Ticks[i - 1].Remaining;
            Check(previous - t.Remaining is 0 or 1, "budget decreases no more than once per real fixed tick");
            if (t.Remaining < previous)
            {
                spent++;
                Near(t.Gravity, t.NativeGravity * .35f, "spent tick applies exactly 0.35 native gravity", .000001f);
                Near(t.StoredGravity, t.NativeGravity, "wallrun leaves the persistent native gravity owner unchanged", .000001f);
            }
            Check(t.Remaining is >= 0 and <= Budget, "budget stays within fixed profile window");
            Check(!t.Kick, "single released takeoff edge does not produce automatic kick");
        }
        Check(spent == Budget && airborne.Any(t => t.Remaining == 0 && !t.Active),
            "exactly 45 earned reduced-gravity ticks exhaust airborne run");
        Tick[] fullRun = run.Ticks.Where(t => t.Active && t.Remaining > 0).ToArray();
        Check(fullRun.Length >= 30 && fullRun.All(t => t.EastFace && !t.Standing),
            "run samples have current real static face and physical airborne state");
        // Native Y may rise only from the original jump. Reduced gravity must
        // remain a descent-rate change rather than resetting or boosting Y.
        foreach (Tick t in fullRun.Where(t => t.Index > first))
            Check(t.Vy <= run.Ticks[t.Index - 1].Vy + .00001f, "run never injects upward velocity");
        int sample = first + 12;
        Check(run.Ticks[sample].Vy > ordinary.Ticks[sample].Vy && run.Ticks[sample].Y > ordinary.Ticks[sample].Y,
            "wall run earns less native gravity and greater airborne height than identical Traversal input");
    }

    private static void Cancellation(List<object> rows)
    {
        int release = -1;
        Capture released = Trace("release-forward-and-physically-leave-wall", MovementModel.Parkour, 140,
            new(46, .5f, 10), (i, p) =>
            {
                if (release < 0 && p.ModParkourWallrunActive) release = i + 6;
                return new(Forward: release < 0 || i < release, Jump: i == JumpTick);
            });
        rows.Add(released);
        Check(release > JumpTick && released.Ticks.Take(release).Any(t => t.Active), "release starts from earned run");
        Check(released.Ticks.Skip(release).All(t => !t.Active && !t.Kick), "released forward cancels run and cannot kick");
        Check(released.Ticks.Skip(release + 8).Any(t => !t.EastFace && !t.Standing), "native strafe motion earns actual airborne loss of wall");
        Tick[] afterRelease = released.Ticks.Skip(release).TakeWhile(t => !t.Standing).ToArray();
        Check(afterRelease.Length > 5 && afterRelease.All(t => t.Remaining == afterRelease[0].Remaining),
            "release/loss stops spending without refilling remaining airborne budget");
        int crouch = -1;
        Capture crouched = Trace("active-run-crouch-exclusion", MovementModel.Parkour, 140,
            new(46, .5f, 10), (i, p) =>
            {
                if (crouch < 0 && p.ModParkourWallrunActive) crouch = i + 6;
                return new(Jump: i == JumpTick, Crouch: crouch >= 0 && i >= crouch);
            });
        rows.Add(crouched);
        Check(crouch > JumpTick && crouched.Ticks.Take(crouch).Any(t => t.Active), "crouch exclusion begins from actual active run");
        Tick[] lowAir = crouched.Ticks.Skip(crouch).TakeWhile(t => !t.Standing).ToArray();
        Check(lowAir.Length > 5 && lowAir.All(t => !t.Active && !t.Kick), "held Ctrl excludes crouch/tuck from run and kick");
        Check(lowAir.All(t => t.Remaining == lowAir[0].Remaining), "stance cleanup cannot mint replacement airborne budget");
        Capture corner = Trace("earned-east-front-corner", MovementModel.Parkour, 120, new(46, .5f, 34),
            (i, _) => new(Jump: i == 8));
        rows.Add(corner);
        Check(corner.Ticks.Any(t => t.Active), "corner approach first earns a wall run");
        Tick[] corners = corner.Ticks.Where(t => t.EastFace && t.FrontFace && !t.Standing).ToArray();
        Check(corners.Length > 0, "native airborne motion reaches two perpendicular boundary faces");
        Check(corners.All(t => !t.Active && !t.Kick), "actual corner cannot retain run or emit kick");
    }

    private static void TeleportCancellation(List<object> rows)
    {
        // API lifecycle regression, not a real teleport-map-trigger test. The
        // native controller earns the run and spends its budget before this
        // one production API call. Destination is the unchanged current origin.
        int teleport = -1, remainingAtTeleport = -1, calls = 0;
        Capture capture = Trace("same-origin-native-Teleport-API-lifecycle", MovementModel.Parkour,
            180, new(46, .5f, 10), (i, p) =>
            {
                if (teleport < 0 && p.ModParkourWallrunActive
                    && p.ModParkourWallrunRemaining <= Budget - 3)
                {
                    Check(!p.Flags1.TestFlag(PlayerFlags1.Standing)
                        && p.ModParkourWallrunRemaining > 0,
                        "native Teleport API begins during a real active run with at least three spent ticks");
                    teleport = i; remainingAtTeleport = p.ModParkourWallrunRemaining;
                    Vector3 origin = p.Position;
                    p.Teleport(p.Position, p.FacingVector, NodeRef.None);
                    calls++;
                    Check(p.Position == origin, "same-origin production Teleport does not relocate test body");
                    Check(!p.ModParkourWallrunActive,
                        "native Teleport API immediately cancels active wallrun");
                    Check(p.ModParkourWallrunRemaining == remainingAtTeleport,
                        "native Teleport cancellation preserves the actually spent airborne budget");
                }
                // Release forward in the first post-API input sample so this
                // lifecycle test cannot immediately earn a fresh wall attach.
                return teleport >= 0 ? new(Forward: false, Strafe: 0)
                    : new(Jump: i == JumpTick);
            });
        rows.Add(new { scenario = "native-Teleport-API-lifecycle-not-map-trigger", teleport,
            remainingAtTeleport, calls, capture });
        Check(calls == 1 && teleport > JumpTick && remainingAtTeleport is > 0 and <= Budget - 3,
            "exactly one native API call follows physically earned budget expenditure");
        Check(capture.Ticks.Take(teleport).Any(t => t.Active && t.Remaining <= Budget - 3),
            "pre-API trace independently witnesses earned run and spent budget");
        Tick[] airborne = capture.Ticks.Skip(teleport).TakeWhile(t => !t.Standing).ToArray();
        Check(airborne.Length > 5, "native API cancellation leaves a measurable genuine airborne interval");
        Check(airborne.All(t => !t.Active && !t.Kick && t.Remaining == remainingAtTeleport),
            "native Teleport cannot refill spent budget or retain run/kick during the remaining flight");
        Check(capture.Commands.Skip(teleport).All(c => !c.Forward && !c.Jump),
            "real keyboard forward and Jump stay released after API cancellation");
    }

    private static void KickAndRefill(List<object> rows)
    {
        int kick = -1;
        Capture fresh = Trace("fresh-wallkick-then-safe-static-landing", MovementModel.Parkour, 300,
            new(46, .5f, 10), (i, p) =>
            {
                if (kick < 0 && p.ModParkourWallrunActive) kick = i + 4;
                return i > kick && kick >= 0 ? new(Forward: false, Strafe: 0)
                    : new(Jump: i == JumpTick || i == kick);
            });
        Capture held = Trace("hold-original-Jump-through-run", MovementModel.Parkour, 115, new(46, .5f, 10),
            (i, _) => new(Jump: i >= JumpTick));
        rows.Add(new { fresh, held, kick });
        Check(kick > JumpTick && fresh.Ticks[kick - 1].Active, "fresh kick follows actual airborne wallrun and released takeoff");
        Tick t = fresh.Ticks[kick];
        Check(t.Kick && t.JumpPressed && !t.NativeJump && !t.Active && t.Remaining == 0,
            "fresh active-wall Jump consumes one kick and entire budget instead of native double jump");
        Check(-t.Vx * 30 >= 8 - .001f && (t.Vy - t.Gravity / 2) * 30 >= 9 - .001f,
            "east-wall kick earns velocity floors before the same-tick native gravity step");
        Near(t.Vz, fresh.Ticks[kick - 1].Vz, "wallkick retains wall tangent", .03f);
        Check(fresh.Ticks.Count(s => s.Kick) == 1, "single fresh press yields exactly one wallkick");
        Check(held.Ticks.Any(s => s.Active) && held.Ticks.All(s => !s.Kick),
            "held original Jump runs without automatically kicking");
        int landing = Array.FindIndex(fresh.Ticks, kick + 1, s => s.Standing && s.SafeFloor);
        Check(landing > kick + 5, "kick trajectory physically lands on genuine safe static support");
        Check(fresh.Ticks.Skip(kick).Take(landing - kick).All(s => s.Remaining == 0),
            "spent airborne budget cannot refill from grace flags or wall loss");
        Check(fresh.Ticks[landing].Remaining == Budget && !fresh.Ticks[landing].Active,
            "only actual current safe static floor landing refills fixed 45-tick budget");
    }

    private static Capture Trace(string scenario, MovementModel model, int length, Vector3 seed,
        Func<int, PlayerEntity, Command> choose)
    {
        object?[] arguments = { Hunter.Samus, model, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity p = scene.Players.Main!; p.Controls.Crouch.Key = Keys.LeftControl;
            p.Reposition(seed, new Vector3(3, 0, 1).Normalized(), NodeRef.None); p.PrevPosition = seed;
            p.ModSetSpawnFacing(new Vector3(3, 0, 1).Normalized());
            for (int i = 0; i < 12; i++) scene.OnSimulationFrame();
            Check(p.Flags1.TestFlag(PlayerFlags1.Standing), "labeled initial lane settles on physical support");
            var ticks = new Tick[length]; var commands = new Command[length];
            for (int i = 0; i < length; i++)
            {
                Command c = choose(i, p); commands[i] = c;
                SetKey(keyboard, Keys.W, c.Forward); SetKey(keyboard, Keys.D, c.Strafe > 0);
                SetKey(keyboard, Keys.A, c.Strafe < 0); SetKey(keyboard, Keys.Space, c.Jump);
                SetKey(keyboard, Keys.LeftControl, c.Crouch);
                ulong frame = scene.FrameCount; long before = scene.DeveloperMomentumSteps;
                scene.OnSimulationFrame();
                Check(p.Health > 0 && p.ModIsInPlay && !p.IsAltForm && !NetSession.Active, "living offline native keyboard biped");
                Check(p.Controls.Jump.IsDown == c.Jump && p.Controls.MoveUp.IsDown == c.Forward,
                    "production keyboard levels match authored command");
                Check(float.IsFinite(p.Position.LengthSquared) && float.IsFinite(p.Speed.LengthSquared), "finite native trajectory");
                bool nativeJump = scene.DeveloperMomentumSteps > before
                    && scene.LastMomentumStep!.Value.ContactPhase == MomentumContactPhase.LegalJump;
                bool east = false, front = false, floor = false;
                float radius = Fixed.ToFloat(p.Values.BipedColRadius);
                foreach (CollisionResult hit in (CollisionResult[])Contacts.GetValue(p)!)
                {
                    if (hit.Field0 != 0 || hit.EntityCollision != null || hit.Slipperiness != 0
                        || hit.Flags.TestFlag(CollisionFlags.Damaging) || hit.Terrain == Terrain.Lava) continue;
                    if (MathF.Abs(hit.Plane.Y) < .001f)
                    {
                        float gap = MathF.Abs(Vector3.Dot(p.Position, hit.Plane.Xyz) - radius - hit.Plane.W);
                        if (gap > .025f) continue;
                        east |= hit.Plane.X < -.99f && p.Position.X > 47;
                        front |= hit.Plane.Z < -.99f && p.Position.Z > 39;
                    }
                    else if (hit.Plane.Y > .9999f && MathF.Abs(hit.Plane.W) < .001f)
                        floor |= MathF.Abs(p.Position.Y + p.ModBipedBottom) < .025f;
                }
                ticks[i] = new(i, p.Position.X, p.Position.Y, p.Position.Z, p.Speed.X, p.Speed.Y, p.Speed.Z,
                    p.Flags1.TestFlag(PlayerFlags1.Standing), p.ModParkourWallrunActive, p.ModParkourWallrunRemaining,
                    p.ModParkourWallkickApplied(frame), p.Controls.Jump.IsPressed, nativeJump, east, front, floor,
                    p.ModParkourAppliedGravity, Fixed.ToFloat(p.Values.BipedGravity), (float)Gravity.GetValue(p)!);
            }
            return new(scenario, model.ToString(), commands, ticks);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
