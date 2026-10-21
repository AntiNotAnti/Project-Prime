using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Editor;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Utility;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Windows CPU scene gate. Only the initial labeled lane placement is synthetic;
// keyboard, jump acceptance, flight, stance, collision and landing run production paths.
internal static class ParkourTuckSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static int _checks;
    private sealed record Command(bool Crouch = false, bool Jump = false, bool Forward = false, bool Morph = false);
    private sealed record Tick(int Index, float Y, float Z, float VelocityY, bool Standing,
        bool Crouched, bool Tucked, bool Alt, bool Transition, byte Rebases, float Bottom, float Top, float Eye,
        float OrdinaryBottom, float OrdinaryTop, bool AcceptedJump, float? Impulse, float VisualCrouch, float DrawEyeCorrection);
    private sealed record Capture(string Hunter, string Model, string Scenario, Tick[] Ticks);
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }
    private static void Near(float a, float b, string label, float epsilon = .00001f)
        => Check(float.IsFinite(a) && MathF.Abs(a - b) <= epsilon, $"{label}: {a} != {b}");

    internal static int Run(string? output)
    {
        _checks = 0;
        var rows = new List<object>(); var failures = new List<string>();
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording,
                "fresh offline nonrecording process");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            void Case(string name, Action action)
            {
                try { action(); Console.WriteLine("PARKOUR TUCK SCENE PASS " + name); }
                catch (Exception error)
                { failures.Add(name + ": " + error); Console.Error.WriteLine("PARKOUR TUCK SCENE FAIL " + name + ": " + error); }
            }
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
                Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
                Case(hunter + "/normal-jump-tuck-release-and-landing", () => Flight(rows, hunter));
            Case("real-platform-tuck-contact-and-native-carry", () => PlatformLanding(rows));
            Case("raised-top-clearance-and-blocked-release", () => Tunnel(rows));
            Case("repeated-native-jumps-have-no-extra-impulse", () => Repeated(rows));
            foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum,
                MovementModel.Quake3, MovementModel.Traversal })
                Case(model + "/tuck-isolation", () =>
                {
                    Capture capture = CaptureScene(Hunter.Samus, model, "excluded-model", new(-42, .5f, -20),
                        150, (i, _) => new(i >= 36, i == 32));
                    rows.Add(capture);
                    Check(capture.Ticks.All(t => !t.Tucked && t.Rebases == 0), "other models never adopt Parkour tuck or landing pivot");
                    if (model is MovementModel.Native or MovementModel.Momentum or MovementModel.Quake3)
                        Check(capture.Ticks.All(t => !t.Crouched), "standing-only models ignore the same Ctrl input");
                });
            Case("native-alt-form-isolation", () =>
            {
                Capture capture = CaptureScene(Hunter.Samus, MovementModel.Parkour, "native-morph-then-Ctrl-jump",
                    new(-42, .5f, -20), 180, (i, _) => new(i >= 70, i == 90, Morph: i == 8));
                rows.Add(capture);
                Check(capture.Ticks.Any(t => t.Alt && !t.Transition), "real keyboard morph completes native alternate form");
                Check(capture.Ticks.Where(t => t.Alt || t.Transition).All(t => !t.Tucked && !t.Crouched),
                    "native alternate form and transitions cannot acquire tuck");
            });
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = failures.Count == 0 ? "PASS" : "FAIL",
                checks = _checks, failures, scope = "Actual offline CPU keyboard and authored collision; initial lane placement only; no rendering, network or replay qualification", rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"PARKOUR TUCK SCENE {(failures.Count == 0 ? "PASS" : "FAIL")} {_checks} assertions; {failures.Count} failed cases");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void PlatformLanding(List<object> rows)
    {
        object?[] arguments = { Hunter.Samus, MovementModel.Parkour, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity p = scene.Players.Main!; p.Controls.Crouch.Key = Keys.LeftControl;
            var editor = new PlatformEntityEditor { Id = 31000, ModelId = 0, ParentId = -1,
                Position = new Vector3(-42, 3, -20), Up = Vector3.UnitY, Facing = Vector3.UnitZ,
                Active = true, PositionCount = 2, ForwardSpeed = .025f, BackwardSpeed = .025f,
                Flags = PlatformFlags.NoRecoil, BeamId = -1, BeamHitMsgTarget = -1,
                PlayerColMsgTarget = -1, DeadMsgTarget = -1, ScanMsgTarget = -1,
                PositionOffset = Vector3.Zero, ReverseType = 0 };
            for (int i = 0; i < 10; i++)
            {
                editor.Positions.Add(i == 1 ? new Vector3(-42, 5, -20) : editor.Position);
                editor.Rotations.Add(new Vector4(0, 0, 0, 1));
            }
            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                using var writer = new BinaryWriter(stream);
                typeof(Repack).GetMethod("WriteEntity", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { editor, writer });
                bytes = stream.ToArray();
            }
            var platform = new PlatformEntity(Read.ReadStruct<PlatformEntityData>(bytes), "", scene);
            scene.AddEntity(platform);
            Check(platform.EntityCollision[0]?.Collision != null, "serialized native platform supplies real collision");
            p.Reposition(new Vector3(-42, 10, -20), Vector3.UnitZ, NodeRef.None);
            p.PrevPosition = p.Position;
            var ownerField = typeof(PlayerEntity).GetField("_standingEntCol", Hidden)!;
            bool Carried() => ReferenceEquals(ownerField.GetValue(p), platform.EntityCollision[0]);
            Vector3 PlatformPosition() => platform.EntityCollision[0]!.Transform.Row3.Xyz;
            int firstContact = -1, carried = 0, movingCarry = 0; long bypassed = 0;
            bool sawAirborneTuck = false, stoodAfterRelease = false;
            byte initialRebases = p.ModTuckRebases;
            Vector3 previousPlatform = PlatformPosition(), previousPlayer = p.Position;
            float? carriedOffset = null;
            var ticks = new List<object>();
            for (int i = 0; i < 240; i++)
            {
                bool beforeCarried = Carried(); long beforeSteps = scene.DeveloperMomentumSteps;
                SetKey(keyboard, Keys.LeftControl, firstContact < 0);
                scene.OnSimulationFrame();
                Check(p.Health > 0 && p.ModIsInPlay && !p.IsBot && !p.IsAltForm && !NetSession.Active,
                    "real living offline Parkour biped during platform fall and carry");
                bool contact = Carried(); Vector3 platformPosition = PlatformPosition();
                if (beforeCarried) bypassed += scene.DeveloperMomentumSteps - beforeSteps;
                sawAirborneTuck |= !contact && !p.Flags1.TestFlag(PlayerFlags1.Standing) && p.ModTucked;
                if (contact)
                {
                    carried++;
                    Check(p.Flags1.TestFlag(PlayerFlags1.Standing) && !p.ModMomentumEligible,
                        "real native standing EntityCollision excludes horizontal adapter");
                    Check(!p.ModTucked && ((p.ModTuckRebases - initialRebases) & 15) == 1,
                        "platform contact completes exactly one tuck coordinate pivot despite carrier exclusion");
                    if (firstContact < 0)
                    {
                        firstContact = i;
                        Check(sawAirborneTuck && p.ModCrouched && p.Controls.Crouch.IsDown,
                            "physically earned airborne tuck lands as ordinary crouch while Ctrl is still held");
                    }
                    else
                    {
                        Check(!p.Controls.Crouch.IsDown && !p.ModCrouched,
                            "clear native platform permits released Ctrl to restore standing body");
                        stoodAfterRelease = true;
                    }
                    if (beforeCarried && platformPosition != previousPlatform && p.Position != previousPlayer)
                    {
                        movingCarry++;
                        // Native carrier transport retains the body's offset from
                        // its serialized moving collision; the pivot adds no drift.
                        float offset = p.Position.Y - platformPosition.Y;
                        carriedOffset ??= offset;
                        Near(offset, carriedOffset.Value, "native platform transport retains rebased foot anchor", .003f);
                    }
                }
                else if (firstContact >= 0)
                    Check(false, "released standing biped remains on actual platform collision");
                ticks.Add(new { index = i, playerY = p.Position.Y, platformY = platformPosition.Y,
                    contact, crouched = p.ModCrouched, tucked = p.ModTucked, rebases = p.ModTuckRebases,
                    eligible = p.ModMomentumEligible, steps = scene.DeveloperMomentumSteps - beforeSteps });
                previousPlatform = platformPosition; previousPlayer = p.Position;
            }
            Check(firstContact >= 0 && carried > 30 && movingCarry > 20 && stoodAfterRelease && Carried(),
                "bounded real fall establishes sustained moving-platform carry and release-to-stand");
            Check(bypassed == 0, "no momentum adapter steps while already owned by native moving platform");
            rows.Add(new { scenario = "real-platform-tuck-contact-and-native-carry", firstContact, carried,
                movingCarry, bypassed, sawAirborneTuck, stoodAfterRelease, ticks });
        }
        finally { MomentumSceneTests.Release(scene); }
    }

    private static void Flight(List<object> rows, Hunter hunter)
    {
        Vector3 seed = new(-42, .5f, -20);
        Capture tucked = CaptureScene(hunter, MovementModel.Parkour, "jump-then-hold-Ctrl", seed, 170,
            (i, _) => new(i >= 36, i == 32));
        Capture reference = CaptureScene(hunter, MovementModel.Traversal, "ordinary-air-crouch-reference", seed, 170,
            (i, _) => new(i >= 36, i == 32));
        Capture released = CaptureScene(hunter, MovementModel.Parkour, "release-Ctrl-in-open-air", seed, 170,
            (i, _) => new(i >= 36 && i < 40, i == 32));
        rows.Add(new { tucked, reference, released });
        Check(tucked.Ticks[32].AcceptedJump && reference.Ticks[32].AcceptedJump,
            "native normal input accepts jump before Ctrl");
        Check(!tucked.Ticks[35].Standing && !tucked.Ticks[35].Tucked,
            "Ctrl arrives after real takeoff");
        Tick first = tucked.Ticks[36];
        Check(first.Crouched && first.Tucked && !first.Standing, "held Ctrl physically tucks airborne biped");
        for (int i = 36; i < 40; i++)
        {
            Tick t = tucked.Ticks[i], c = reference.Ticks[i];
            Check(t.Tucked && !t.Standing && !c.Standing, "comparison samples remain collision-free airborne");
            Check(BitConverter.SingleToInt32Bits(t.VelocityY) == BitConverter.SingleToInt32Bits(c.VelocityY),
                "tuck preserves exact native vertical speed bits against ordinary crouch");
            Near(t.Y, c.Y, "airborne tuck does not translate origin");
            Near(t.Bottom - t.OrdinaryBottom, ParkourStance.TuckRise, "feet rise by profile constant");
            Near(t.Top - t.OrdinaryTop, ParkourStance.TuckRise, "top rises with short capsule");
            Near(t.Top - t.Bottom, c.Top - c.Bottom, "tuck preserves ordinary crouch height");
        }
        Check(!released.Ticks[40].Tucked && !released.Ticks[40].Crouched && !released.Ticks[40].Standing,
            "clear airborne release restores standing shape immediately");
        Check(released.Ticks[40].VisualCrouch > 0, "air release retains a real cosmetic crouch fade");
        for (int i = 36; i < 46; i++)
            Near(released.Ticks[i].DrawEyeCorrection, 0, "tuck and airborne release fade never create a camera dip");
        Check(tucked.Ticks.Count(t => t.AcceptedJump) == 1, "Ctrl supplies no second jump impulse");
        int landing = Array.FindIndex(tucked.Ticks, 37, t => t.Rebases != 0);
        Check(landing > 40 && !tucked.Ticks[landing - 1].Standing,
            "one landing pivot follows real descending flight");
        Tick landed = tucked.Ticks[landing];
        Check(landed.Standing && landed.Crouched && !landed.Tucked && landed.Rebases == 1,
            "physical contact rebases once into ordinary grounded crouch");
        Near(landed.Bottom, landed.OrdinaryBottom, "landing capsule uses ordinary crouch bottom");
        Near(landed.Top, landed.OrdinaryTop, "landing capsule uses ordinary crouch top");
        Near(landed.Y + landed.Bottom, tucked.Ticks[0].Y + tucked.Ticks[0].Bottom,
            "rebased feet stay on actual authored floor", .002f);
        Tick settled = tucked.Ticks[^1];
        Near(landed.Y + landed.Eye, settled.Y + settled.Eye, "landing eye remains at settled crouch world height", .002f);
        Check(tucked.Ticks.Skip(landing).All(t => t.Rebases == 1 && !t.Tucked),
            "held grounded Ctrl cannot repeat pivot or retuck");
    }

    private static void Tunnel(List<object> rows)
    {
        // Existing authored tunnel: floor y=0, roof y=1.2, lane x=30.
        // Its raised tuck top would penetrate the roof while ordinary crouch fits.
        int release = -1, jump = -1;
        Capture capture = CaptureScene(Hunter.Samus, MovementModel.Parkour, "low-roof-jump-and-release",
            new(30, .5f, -16), 700, (i, p) =>
            {
                if (jump < 0 && p.Position.Z >= -3) jump = i;
                if (release < 0 && p.Position.Z >= -1) release = i;
                return new(release < 0, i == jump, true);
            });
        rows.Add(capture);
        Tick[] underRoof = capture.Ticks.Where(t => t.Z > -3 && t.Z < 8).ToArray();
        Check(underRoof.Length > 10, "ordinary keyboard crouch enters authored tunnel");
        Check(underRoof.All(t => t.Y + t.Top <= 1.22f), "production clearance/collision keeps raised top below authored roof");
        Tick[] released = underRoof.Where(t => release >= 0 && t.Index >= release).ToArray();
        Check(released.Length > 5 && released.All(t => t.Crouched), "release under roof cannot grow to full standing capsule");
        Check(underRoof.Any(t => !t.Tucked && t.Y + t.OrdinaryTop + ParkourStance.TuckRise > 1.22f),
            "ordinary crouch fit does not incorrectly admit a raised tuck top through roof");
    }

    private static void Repeated(List<object> rows)
    {
        Capture capture = CaptureScene(Hunter.Samus, MovementModel.Parkour, "three-normal-input-hops",
            new(-42, .5f, -20), 490,
            (i, _) => new(i % 150 >= 36 && i % 150 < 120, i % 150 == 32 && i < 450));
        rows.Add(capture);
        Tick[] jumps = capture.Ticks.Where(t => t.AcceptedJump).ToArray();
        Check(jumps.Length == 3, "three separated fresh keyboard jumps are accepted exactly once each");
        foreach (Tick jump in jumps) Near(jump.Impulse!.Value, jumps[0].Impulse!.Value, "repeated tuck landing supplies no cumulative jump boost");
        Check(capture.Ticks.Count(t => t.Tucked) > 12 && capture.Ticks[^1].Rebases == 3,
            "each real hop tucks and physically rebases once");
    }

    private static Capture CaptureScene(Hunter hunter, MovementModel model, string scenario,
        Vector3 seed, int length, Func<int, PlayerEntity, Command> choose)
    {
        object?[] arguments = { hunter, model, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        var keyboard = (KeyboardState)arguments[2]!;
        try
        {
            PlayerEntity p = scene.Players.Main!; p.Controls.Crouch.Key = Keys.LeftControl;
            p.Reposition(seed, Vector3.UnitZ, NodeRef.None); p.PrevPosition = seed;
            for (int i = 0; i < 12; i++) scene.OnSimulationFrame();
            Check(p.Flags1.TestFlag(PlayerFlags1.Standing), "initial labeled lane settles on physical support");
            var ticks = new Tick[length];
            for (int i = 0; i < length; i++)
            {
                Command c = choose(i, p);
                SetKey(keyboard, Keys.LeftControl, c.Crouch); SetKey(keyboard, Keys.Space, c.Jump);
                SetKey(keyboard, Keys.W, c.Forward); SetKey(keyboard, Keys.Tab, c.Morph);
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                scene.OnSimulationFrame();
                Check(p.Health > 0 && p.ModIsInPlay && !p.IsBot && !NetSession.Active, "living offline owner input path");
                Check(p.Controls.Crouch.IsDown == c.Crouch && p.Controls.Jump.IsDown == c.Jump,
                    "production keyboard levels match command");
                Check(float.IsFinite(p.Position.LengthSquared) && float.IsFinite(p.Speed.LengthSquared), "finite physical state");
                bool accepted = false; float? impulse = null;
                long delta = scene.DeveloperMomentumSteps - before;
                Check(model == MovementModel.Native ? delta == 0 : delta is >= 0 and <= 1, "at most one adapter step per frame");
                if (delta == 1)
                {
                    var sample = scene.LastMomentumStep!.Value;
                    Check(sample.SimulationFrame == frame && sample.Slot == p.SlotIndex, "diagnostic belongs to actual owned frame");
                    Check(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y)
                        == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "adapter preserves native vertical speed bits");
                    accepted = sample.ContactPhase == MomentumContactPhase.LegalJump;
                    if (accepted) impulse = sample.NativeVelocityBefore.Y;
                }
                ticks[i] = new(i, p.Position.Y, p.Position.Z, p.Speed.Y, p.Flags1.TestFlag(PlayerFlags1.Standing),
                    p.ModCrouched, p.ModTucked, p.IsAltForm, p.IsMorphing || p.IsUnmorphing, p.ModTuckRebases,
                    p.ModBipedBottom, p.ModBipedTop, p.ModBipedEyeOffset,
                    p.ModBipedBottomFor(true, false), p.ModBipedTopFor(true, false), accepted, impulse,
                    (float)typeof(PlayerEntity).GetProperty("ModCrouchVisualAmount", Hidden)!.GetValue(p)!,
                    (float)typeof(PlayerEntity).GetMethod("ModCrouchDrawEyeCorrection", Hidden)!.Invoke(p, new object[] { 1d })!);
            }
            return new(hunter.ToString(), model.ToString(), scenario, ticks);
        }
        finally { MomentumSceneTests.Release(scene); }
    }
}
