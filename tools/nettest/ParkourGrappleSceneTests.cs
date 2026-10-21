using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Mods.Movement;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.NetTest;

// Read-only production diagnostics; the fixture cannot inject an anchor,
// acceptance, nonce, force, timer or contact.
internal interface IGrappleSceneProbe
{
    bool ForceActive { get; }
    bool AcceptedActive { get; }
    ushort RequestNonce { get; }
    Vector3 Anchor { get; }
    float RopeLength { get; }
    Vector3 CanonicalOrigin { get; }
    Vector3 AcceptedAim { get; }
    long AnchorQueries { get; }
    long ForceApplications { get; }
    bool Applied(ulong sceneFrame);
}

internal sealed class GrappleSceneHarness : IDisposable
{
    internal required Scene Scene;
    internal required PlayerEntity Player;
    internal required KeyboardState Keyboard;
    internal required IGrappleSceneProbe Probe;
    internal required Action DisposeScene;
    public void Dispose() => DisposeScene();
}

internal static class ParkourGrappleSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static int _checks;
    private static string _scenario = "";
    private static readonly List<object> Rows = new();
    private static void Check(bool condition, string label)
    { _checks++; if (!condition) throw new InvalidOperationException(label); }
    private static void Tick(GrappleSceneHarness h, bool grapple, bool jump = false, bool morph = false)
    {
        ulong frame = h.Scene.FrameCount;
        SetKey(h.Keyboard, Keys.G, grapple); SetKey(h.Keyboard, Keys.Space, jump);
        SetKey(h.Keyboard, Keys.Tab, morph); h.Scene.OnSimulationFrame();
        Vector3 anchor = h.Probe.Anchor;
        Rows.Add(new { scenario = _scenario, frame, grapple, jump, morph,
            x = h.Player.Position.X, y = h.Player.Position.Y, z = h.Player.Position.Z,
            vx = h.Player.Speed.X, vy = h.Player.Speed.Y, vz = h.Player.Speed.Z,
            accepted = h.Probe.AcceptedActive, forceActive = h.Probe.ForceActive,
            nonce = h.Probe.RequestNonce, anchorX = anchor.X, anchorY = anchor.Y, anchorZ = anchor.Z,
            length = h.Probe.RopeLength, selections = h.Probe.AnchorQueries,
            forces = h.Probe.ForceApplications, applied = h.Probe.Applied(frame) });
        Check(h.Player.Controls.Grapple.IsDown == grapple && h.Player.Controls.Jump.IsDown == jump,
            "production keyboard levels match authored controls");
        Check(h.Player.Health > 0 && h.Player.ModIsInPlay && !NetSession.Active,
            "real living offline owner executes keyboard and native movement");
        Check(float.IsFinite(h.Player.Position.LengthSquared) && float.IsFinite(h.Player.Speed.LengthSquared),
            "production native state remains finite");
    }

    internal static int Run(string? output = null)
    {
        Rows.Clear(); _checks = 0; string? failure = null; int result;
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !DemoRecorder.IsRecording, "fresh offline scene");
            Check(ServerSim.Available(out string reason), reason); Headless.Enter();
            result = RunCases(Load);
        }
        catch (Exception error)
        { failure = _scenario + ": " + error; Console.Error.WriteLine("PARKOUR GRAPPLE SCENE FAIL " + failure); result = 1; }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = result == 0 ? "PASS" : "FAIL",
                checks = _checks, failure, scope = "Offline CPU production keyboard/native collision; initial placement and native actor construction plus labeled same-origin Teleport; no online/replay/rendering qualification", rows = Rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"PARKOUR GRAPPLE SCENE {(result == 0 ? "PASS" : "FAIL")} {_checks} assertions"); return result;
    }

    private sealed class Probe(PlayerEntity player) : IGrappleSceneProbe
    {
        private static readonly PropertyInfo Origin = typeof(PlayerEntity).GetProperty("ModGrappleOrigin", Hidden)!;
        private static readonly FieldInfo Aim = typeof(PlayerEntity).GetField("_gunVec1", Hidden)!;
        private static readonly FieldInfo ForceFrame = typeof(PlayerEntity).GetField("_modGrappleForceFrame", Hidden)!;
        public bool ForceActive => player.ModGrappleOwnsMovement;
        public bool AcceptedActive => player.ModGrappleState.Active;
        public ushort RequestNonce => player.ModGrappleRequestNonce;
        public Vector3 Anchor => player.ModGrappleState.Anchor;
        public float RopeLength => player.ModGrappleState.RopeLength;
        public Vector3 CanonicalOrigin => (Vector3)Origin.GetValue(player)!;
        public Vector3 AcceptedAim => ((Vector3)Aim.GetValue(player)!).Normalized();
        public long AnchorQueries => player.ModGrappleSelectionCount;
        public long ForceApplications => player.ModGrappleForceCount;
        public bool Applied(ulong frame) => (ulong)ForceFrame.GetValue(player)! == frame;
    }

    private static GrappleSceneHarness Load(MovementModel model, string fixture)
        => LoadHunter(Hunter.Samus, model, fixture);

    private static GrappleSceneHarness LoadHunter(Hunter hunter, MovementModel model, string fixture)
    {
        _scenario = hunter + "/" + model + "/" + fixture;
        object?[] arguments = { hunter, model, null };
        Scene scene = (Scene)typeof(TraversalSceneTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, arguments)!;
        try
        {
            PlayerEntity p = scene.Players.Main!;
            p.Controls.Grapple.Key = Keys.G;
            if (fixture == "field-before-safe") ParkourGrappleFixtures.AddFieldBeforeSafeWall(scene);
            if (fixture == "platform-before-safe") ParkourGrappleFixtures.AddPlatformBeforeSafeWall(scene);
            Vector3 seed = new(fixture == "unsafe-static-before-safe" ? -38 : -42, .5f, 25);
            p.Reposition(seed, Vector3.UnitZ, NodeRef.None); p.PrevPosition = seed; p.ModSetSpawnFacing(Vector3.UnitZ);
            for (int i = 0; i < 12; i++) scene.OnSimulationFrame();
            Check(p.Flags1.TestFlag(PlayerFlags1.Standing), "initial fixture settles on real static support");
            return new() { Scene = scene, Player = p, Keyboard = (KeyboardState)arguments[2]!,
                Probe = new Probe(p), DisposeScene = () => MomentumSceneTests.Release(scene) };
        }
        catch { MomentumSceneTests.Release(scene); throw; }
    }

    // Loader uses TraversalSceneTests.Load (actual native keyboard spawning),
    // initial Reposition(-42,.5,25), facing+Z, then12 native settling ticks.
    // Bind the actual new PlayerControls.Grapple toG before running this suite.
    // This factory is a fixture/setup seam, never a force/admission substitute.
    private static int RunCases(Func<MovementModel, string, GrappleSceneHarness> load)
    {
        foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus,
            Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
            using (var h = LoadHunter(hunter, MovementModel.Parkour, "safe-wall"))
            {
                Tick(h, true);
                Check(h.Probe.AcceptedActive && h.Probe.ForceActive && h.Probe.ForceApplications > 0,
                    "all seven native upright Hunters earn an actual safe-wall grapple");
            }
        using (var h = load(MovementModel.Parkour, "safe-wall"))
        {
            CollisionResult nearest = default;
            Vector3 source = h.Probe.CanonicalOrigin, aim = h.Probe.AcceptedAim;
            Check(CollisionDetection.CheckBetweenPoints(source, source + aim * 24,
                TestFlags.Players, h.Scene, ref nearest) && nearest.EntityCollision == null
                && nearest.Slipperiness == 0 && nearest.Terrain != Terrain.Lava
                && !nearest.Flags.TestFlag(CollisionFlags.Damaging), "fixture has an actual nearest safe static polygon");
            Tick(h, true);
            Check(h.Probe.AcceptedActive && h.Probe.ForceActive && h.Probe.RequestNonce != 0,
                "offline fresh physicalG attaches through production admission");
            Check((h.Probe.Anchor - nearest.Position).Length < .001f,
                "accepted anchor is the nearest native hit rather than a farther surface");
            Vector3 anchor = h.Probe.Anchor; float length = h.Probe.RopeLength;
            ushort firstNonce = h.Probe.RequestNonce;
            for (int i = 0; i < 20; i++)
            {
                long queries = h.Probe.AnchorQueries, forces = h.Probe.ForceApplications;
                Vector3 before = h.Player.Position; ulong frame = h.Scene.FrameCount;
                Tick(h, true);
                Check(h.Probe.AnchorQueries == queries, "a repeated held carrier does not choose a new anchor");
                Check(h.Probe.ForceApplications - forces is >= 0 and <= 1, "at most one force application per real fixed tick");
                Check(h.Probe.Anchor == anchor && h.Probe.RopeLength == length,
                    "moving owner cannot reauthor the accepted anchor or rope length");
                // In the clear lane, horizontal native integration must explain
                // all travel. This catches position projection/snap controllers.
                Check(((h.Player.Position - before) - h.Player.Speed / 2).Xz.Length < .001f,
                    "native velocity integration explains clear-lane travel without a position snap");
                Check(h.Player.Position.Z < 38, "fixture stays clear of the anchor wall during no-snap checks");
            }
            ulong release = h.Scene.FrameCount; Tick(h, false);
            Check(!h.Probe.Applied(release) && !h.Probe.ForceActive,
                "physical release removes force permission in the same fixed tick");
            Tick(h, false); Tick(h, true);
            Check(h.Probe.RequestNonce != firstNonce && h.Probe.RequestNonce != 0 && h.Probe.AcceptedActive,
                "release/repress creates a distinct nonzero request and new immediate offline decision");
        }
        foreach (string fixture in new[] { "unsafe-static-before-safe", "platform-before-safe", "field-before-safe" })
            using (var h = load(MovementModel.Parkour, fixture))
            {
                CollisionResult first = default;
                Vector3 source = h.Probe.CanonicalOrigin, aim = h.Probe.AcceptedAim;
                Check(CollisionDetection.CheckBetweenPoints(source, source + aim * 24,
                    TestFlags.Players, h.Scene, ref first), "negative fixture has a real native obstruction");
                if (fixture == "unsafe-static-before-safe")
                    Check(first.EntityCollision == null && first.Flags.TestFlag(CollisionFlags.Damaging),
                        "independent nearest native query witnesses the unsafe static face first");
                else if (fixture == "platform-before-safe")
                    Check(first.EntityCollision != null, "independent nearest native query witnesses entity collision first");
                else
                {
                    bool crossedField = false;
                    foreach (ForceFieldEntity field in h.Scene.GetForceFieldEntities())
                    {
                        if (!field.Active) continue;
                        float denominator = Vector3.Dot(aim, field.Plane.Xyz);
                        if (MathF.Abs(denominator) < .000001f) continue;
                        float distance = (field.Plane.W - Vector3.Dot(source, field.Plane.Xyz)) / denominator;
                        Vector3 relative = source + aim * distance - field.Position;
                        crossedField |= distance > 0 && distance < (first.Position - source).Length
                            && MathF.Abs(Vector3.Dot(relative, field.FieldUpVector)) < field.Height
                            && MathF.Abs(Vector3.Dot(relative, field.FieldRightVector)) < field.Width;
                    }
                    Check(crossedField, "actual active field rectangle crosses the ray before the safe native polygon");
                }
                long queries = h.Probe.AnchorQueries;
                for (int i = 0; i < 25; i++)
                {
                    ulong frame = h.Scene.FrameCount; Tick(h, true);
                    Check(!h.Probe.AcceptedActive && !h.Probe.Applied(frame),
                        "unsafe or native actor first obstruction cannot attach to the safe wall behind it");
                }
                Check(h.Probe.AnchorQueries - queries == 1, "denied held request is consumed instead of raycasting every tick");
                ushort denied = h.Probe.RequestNonce; Tick(h, false); Tick(h, true);
                Check(h.Probe.RequestNonce != denied && h.Probe.AnchorQueries - queries == 2,
                    "only a new physical press may retry a denied anchor request");
            }
        using (var h = load(MovementModel.Parkour, "safe-wall"))
        {
            _scenario += "/held-expiry";
            for (int tick = 0; tick < 205; tick++)
            {
                ulong frame = h.Scene.FrameCount; Tick(h, true);
                if (tick >= GrappleMovementV1.MaxTicks)
                    Check(!h.Probe.Applied(frame) && !h.Probe.ForceActive,
                        "expired physical hold cannot apply force or automatically attach again");
            }
            Check(h.Probe.AnchorQueries == 1 && h.Probe.ForceApplications is > 100 and <= GrappleMovementV1.MaxTicks,
                "real held flight consumes one anchor selection and a bounded earned force window");
        }
        foreach (string native in new[] { "jump", "morph", "teleport" })
            using (var h = load(MovementModel.Parkour, "safe-wall"))
            {
                _scenario += "/native-" + native;
                Tick(h, true); Check(h.Probe.ForceActive, "native priority case first earns a real active rope");
                for (int i = 0; i < 5; i++) Tick(h, true);
                if (native == "teleport")
                {
                    Vector3 origin = h.Player.Position;
                    h.Player.Teleport(origin, h.Player.FacingVector, NodeRef.None);
                    Check(h.Player.Position == origin, "labeled same-origin native Teleport does not relocate test body");
                }
                ulong frame = h.Scene.FrameCount;
                Tick(h, true, jump: native == "jump", morph: native == "morph");
                Check(!h.Probe.Applied(frame) && !h.Probe.ForceActive,
                    "native jump, morph or Teleport owns the cancellation tick");
                if (native == "jump") Check(h.Player.Speed.Y > 0, "native Jump retains its upward impulse");
                for (int i = 0; i < 20; i++)
                {
                    frame = h.Scene.FrameCount; Tick(h, true);
                    Check(!h.Probe.Applied(frame), "same held request cannot revive after native cancellation");
                }
            }
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum,
            MovementModel.Quake3, MovementModel.Traversal })
            using (var h = load(model, "safe-wall"))
                for (int i = 0; i < 30; i++)
                {
                    ulong frame = h.Scene.FrameCount; Tick(h, true);
                    Check(!h.Probe.ForceActive && !h.Probe.Applied(frame), "Classic and other profiles cannot acquire grapple forces");
                }
        Console.WriteLine($"PARKOUR GRAPPLE SCENE PASS {_checks} assertions"); return 0;
    }
}


