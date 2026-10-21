using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Testing-only, opt-in real Scene rendering. This deliberately does not inherit
// RenderWindow: its normal Reveal path shows a window. No OS input is sent and
// no window is shown/focused, even when a hidden driver fails to produce pixels.
internal static class ParkourAnimationRenderCheck
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string Room = "TRAVERSAL TRAINING";
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly Action<KeyboardState, Keys, bool> SetKey = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden)!.CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Pose(float MantleProgress, float MantleWeight, float GrappleAmount,
        float[] RopeDirection, double Seconds, byte WallCue, float WallAmount, float[] WallNormal, bool WallTucked);
    private sealed record Image(string File, string RgbSha256, string PngSha256, int Width, int Height);
    private sealed record Pixels(byte[] Rgb, Image Metadata);
    private sealed record Canonical(Vector3 Position, Vector3 Previous, Vector3 Speed, object? Collision,
        Vector3 Eye, Vector3 Target, Matrix4 View, Vector3 Muzzle, Vector3 AimPosition, Vector3 Gun,
        object? Grapple, object? SourceFacts, string NativeClocks, string WallControllers, Pose Pose, ulong Frame);
    private static readonly FieldInfo[] WallControllerFields = typeof(PlayerEntity).GetFields(Hidden)
        .Where(f => !f.FieldType.IsArray && (f.Name.StartsWith("_modWall", StringComparison.Ordinal)
            || f.Name.StartsWith("_modParkourWall", StringComparison.Ordinal)
            || f.Name == "_modParkourIncomingContactSpeed")).OrderBy(f => f.Name).ToArray();
    private sealed class Fixture : IDisposable
    {
        internal readonly Scene Scene;
        internal readonly PlayerEntity Player;
        internal readonly KeyboardState Keyboard = SyntheticInput.CreateKeyboard();
        internal readonly Vector3 CameraOffset;
        internal Fixture(Hunter hunter, string scenario, Vector2i size, bool west = false)
        {
            Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
            Scene = new Scene(size, Keyboard, SyntheticInput.CreateMouse(), _ => { }, () => { });
            try
            {
                Scene.AddPlayer(hunter); Scene.AddRoom(Room, GameMode.Battle, playerCount: 1);
                new MatchDefinition { RoomKey = Room, Mode = GameMode.Battle,
                    MovementModel = MovementModel.Parkour, TimeLimitSeconds = 0, PointGoal = 10000 }
                    .ApplyInitialConfiguration(Scene.GameState);
                Set(Scene, "_developerMomentumDiagnostics", true);
                Scene.OnLoad(); Scene.OnResize(); InputMode(Scene);
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Player = Scene.Players.Main;
                foreach (var binding in Player.Controls.All) { binding.Type = ButtonType.Key; binding.Key = Keys.Unknown; }
                Player.Controls.MoveUp.Key = Keys.W; Player.Controls.Jump.Key = Keys.Space;
                Player.Controls.MoveLeft.Key = Keys.A; Player.Controls.MoveRight.Key = Keys.D;
                Player.Controls.Crouch.Key = Keys.LeftControl;
                Player.Controls.Grapple.Key = Keys.G; Player.Controls.Shoot.Key = Keys.Enter;
                Player.Controls.MouseAim = false; Player.Controls.KeyboardAim = true;
                SetKey(Keyboard, Keys.Enter, true);
                for (int tick = 0; tick < 600 && !Player.LoadFlags.TestFlag(LoadFlags.Spawned); tick++) Step();
                SetKey(Keyboard, Keys.Enter, false);
                Require(Player.LoadFlags.TestFlag(LoadFlags.Spawned), "real keyboard shoot-to-spawn");
                Scene.CameraSequences.Current?.End();
                for (int tick = 0; tick < 120; tick++) Step();
                bool wall = scenario.StartsWith("wall", StringComparison.Ordinal);
                float side = west ? -1 : 1;
                Vector3 initial = scenario == "mantle" ? new(-30, .5f, 14)
                    : scenario == "grapple" ? new(-42, .5f, 25)
                    : new(side * (scenario == "wallstrafe" ? 42 : 46), .5f, scenario == "walltuck" ? 20 : 10);
                Vector3 facing = wall ? new Vector3(side * 3, 0, 1).Normalized() : Vector3.UnitZ;
                // Explicit initial lane seam only; all subsequent travel, contact,
                // admission, phase and animation time come from production input.
                Player.Reposition(initial, facing, NodeRef.None); Player.PrevPosition = initial;
                Invoke(Player, "ModSetSpawnFacing", facing);
                for (int tick = 0; tick < 12; tick++) Step();
                Require(Player.Flags1.TestFlag(PlayerFlags1.Standing), "initial native floor support");
                Require(!Player.IsBot && Player.Health > 0 && !Headless.Active && !NetSession.Active,
                    "living offline native keyboard owner and real renderer");
                Require(!(bool)Get(Player, "ModParkourMantleActive")!
                    && !(bool)Get(Get(Player, "ModGrappleState")!, "Active")!,
                    "initial fixture has no fabricated or retained active movement controller");
                CameraOffset = ChooseCamera(Scene, Player, wall ? -side : 0);
            }
            catch { Release(Scene); throw; }
        }
        internal void Step()
        {
            InputMode(Scene); GamepadContexts.Focused = true;
            Require(FrameTiming.Advance(FrameTiming.StepSeconds) == 1, "one fixed60Hz simulation step");
            Scene.OnSimulationFrame();
        }
        public void Dispose() => Release(Scene);
    }

    private static int _checks;
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidDataException("Parkour animation GL: " + message); }
    private static Type Engine(string name) => typeof(Scene).Assembly.GetType(name, throwOnError: true)!;
    private static object? Get(object value, string name)
        => value.GetType().GetProperty(name, Hidden)?.GetValue(value)
            ?? value.GetType().GetField(name, Hidden)?.GetValue(value);
    private static void Set(object value, string name, object state)
    {
        if (value.GetType().GetField(name, Hidden) is { } field) field.SetValue(value, state);
        else (value.GetType().GetProperty(name, Hidden) ?? throw new MissingMemberException(name)).SetValue(value, state);
    }
    private static object? Invoke(object value, string name, params object?[] arguments)
        => (value.GetType().GetMethod(name, Hidden) ?? throw new MissingMethodException(name)).Invoke(value, arguments);
    private static void InputMode(Scene scene)
    {
        FieldInfo mode = typeof(Scene).GetField("_inputMode", Hidden)!;
        mode.SetValue(scene, Enum.Parse(mode.FieldType, "All"));
    }
    private static Pose Picture(PlayerEntity player)
    {
        object pose = Invoke(player, "ModGetParkourAnimationPose")!;
        Vector3 rope = (Vector3)Get(pose, "RopeDirection")!;
        Vector3 wall = (Vector3)Get(pose, "WallNormal")!;
        return new((float)Get(pose, "MantleProgress")!, (float)Get(pose, "MantleWeight")!,
            (float)Get(pose, "GrappleAmount")!, new[] { rope.X, rope.Y, rope.Z }, (double)Get(pose, "Seconds")!,
            (byte)Get(pose, "WallCue")!, (float)Get(pose, "WallAmount")!,
            new[] { wall.X, wall.Y, wall.Z }, (bool)Get(pose, "WallTucked")!);
    }
    private static string WallControllers(PlayerEntity player)
    {
        static string Float(float value) => BitConverter.SingleToInt32Bits(value).ToString("x8");
        return string.Join("|", WallControllerFields.Select(field => field.Name + "=" + (field.GetValue(player) switch
        {
            Vector3 vector => Float(vector.X) + "," + Float(vector.Y) + "," + Float(vector.Z),
            float value => Float(value),
            double value => BitConverter.DoubleToInt64Bits(value).ToString("x16"),
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        })));
    }
    private static string Clock(PlayerEntity player, string field)
    {
        var info = (AnimationInfo)Get(Get(player, field)!, "AnimInfo")!;
        return string.Join("/", info.Index.Concat(info.PrevIndex).Concat(info.Frame).Concat(info.FrameCount)
            .Concat(info.Step).Concat(info.Flags.Select(flag => (int)flag))
            .Concat(new[] { info.Node.Slot, info.Material.Slot, info.Texture.Slot, info.Texcoord.Slot }));
    }
    private static Canonical CaptureCanonical(Fixture fixture)
    {
        PlayerEntity p = fixture.Player; object camera = Get(p, "CameraInfo")!;
        object?[] source = { (Vector3)Get(p, "_gunVec1")!, null };
        Require((bool)typeof(PlayerEntity).GetMethod("ModTryCaptureCustomPrecombatFacts", Hidden)!
            .Invoke(p, source)!, "canonical source-time weapon facts are available");
        return new(p.Position, p.PrevPosition, p.Speed, Invoke(p, "ModCaptureCollisionState"),
            (Vector3)Get(camera, "Position")!, (Vector3)Get(camera, "Target")!, (Matrix4)Get(camera, "ViewMatrix")!,
            (Vector3)Get(p, "_muzzlePos")!, (Vector3)Get(p, "_aimPosition")!, (Vector3)Get(p, "_gunVec1")!,
            Get(p, "ModGrappleState"), source[1], Clock(p, "_bipedModel1") + ":" + Clock(p, "_bipedModel2"),
            WallControllers(p), Picture(p), fixture.Scene.FrameCount);
    }
    private static bool Equal(Canonical a, Canonical b)
        => a.Position == b.Position && a.Previous == b.Previous && a.Speed == b.Speed
            && Equals(a.Collision, b.Collision) && a.Eye == b.Eye && a.Target == b.Target && a.View == b.View
            && a.Muzzle == b.Muzzle && a.AimPosition == b.AimPosition && a.Gun == b.Gun
            && Equals(a.Grapple, b.Grapple) && Equals(a.SourceFacts, b.SourceFacts)
            && a.NativeClocks == b.NativeClocks && a.WallControllers == b.WallControllers && a.Frame == b.Frame
            && a.Pose.MantleProgress == b.Pose.MantleProgress && a.Pose.MantleWeight == b.Pose.MantleWeight
            && a.Pose.GrappleAmount == b.Pose.GrappleAmount && a.Pose.Seconds == b.Pose.Seconds
            && a.Pose.RopeDirection.SequenceEqual(b.Pose.RopeDirection)
            && a.Pose.WallCue == b.Pose.WallCue && a.Pose.WallAmount == b.Pose.WallAmount
            && a.Pose.WallTucked == b.Pose.WallTucked && a.Pose.WallNormal.SequenceEqual(b.Pose.WallNormal);
    private static Vector3 ChooseCamera(Scene scene, PlayerEntity player, float interiorX = 0)
    {
        Vector3 aim = player.Position + Vector3.UnitY * .65f;
        foreach (Vector3 offset in new[] { new Vector3(3, 1.2f, 3), new(-3, 1.2f, 3),
            new(3, 1.2f, -3), new(-3, 1.2f, -3), new(4, 1, 0), new(-4, 1, 0) })
        {
            // A camera clear at the approach seed must remain on the arena side
            // when the body reaches the x=+/-48 boundary later in the sequence.
            if (interiorX != 0 && offset.X * interiorX <= 0) continue;
            CollisionResult hit = default;
            if (!CollisionDetection.CheckBetweenPoints(aim, aim + offset, TestFlags.Beams, scene, ref hit)) return offset;
        }
        throw new InvalidDataException("No unobstructed external fixture camera");
    }
    private static void Camera(Fixture fixture)
    {
        Scene scene = fixture.Scene; Vector3 aim = fixture.Player.Position + Vector3.UnitY * .65f;
        Vector3 camera = aim + fixture.CameraOffset, forward = (aim - camera).Normalized();
        Vector3 right = Vector3.Cross(forward, Vector3.UnitY).Normalized();
        scene.SetFreeCamera(true); scene.ShowAllNodes = true;
        Set(scene, "_cameraPosition", camera); Set(scene, "_cameraFacing", forward);
        Set(scene, "_cameraRight", right); Set(scene, "_cameraUp", Vector3.Cross(right, forward));
        Set(scene, "_cameraFov", MathHelper.DegreesToRadians(60));
        Set(scene, "_viewModelFov", MathHelper.DegreesToRadians(60)); InputMode(scene);
    }
    private static void HiddenWindow(NativeWindow window)
        => Require(!window.IsVisible && !window.IsFocused, "context remains hidden and unfocused; no fallback reveal");
    private static Pixels Draw(Fixture fixture, NativeWindow window, string file)
    {
        HiddenWindow(window); fixture.Scene.OnDrawFrame();
        Require(fixture.Scene.OnRenderFrame(), "actual Scene renderer produced a frame");
        byte[] pixels = fixture.Scene.ReadSceneTarget(out int width, out int height)
            ?? throw new InvalidDataException("Hidden GL scene framebuffer readback unavailable");
        Require(width > 0 && height > 0 && pixels.Length == width * height * 3, "valid offscreen RGB framebuffer");
        Require(!File.Exists(file) && ScreenCapture.Save(fixture.Scene, file), "fresh native PNG capture");
        Require(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "native draw has no GL error"); HiddenWindow(window);
        return new(pixels, new(file, Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(), width, height));
    }
    private static int Difference(Pixels a, Pixels b)
    {
        Require(a.Metadata.Width == b.Metadata.Width && a.Metadata.Height == b.Metadata.Height,
            "counterfactual framebuffer dimensions are identical");
        int changed = 0;
        for (int i = 0; i < a.Rgb.Length; i += 3)
            if (Math.Abs(a.Rgb[i] - b.Rgb[i]) > 2 || Math.Abs(a.Rgb[i + 1] - b.Rgb[i + 1]) > 2
                || Math.Abs(a.Rgb[i + 2] - b.Rgb[i + 2]) > 2) changed++;
        return changed;
    }
    private static object Capture(Fixture fixture, NativeWindow window, FieldInfo suppress,
        string folder, int number, List<object> frames, out int posePixels)
    {
        Camera(fixture); string stem = Path.Combine(folder, number.ToString("D4"));
        // Warm the normal preparation path before taking the canonical fence.
        fixture.Scene.OnDrawFrame(); Require(fixture.Scene.OnRenderFrame(), "warm actual body draw");
        Canonical before = CaptureCanonical(fixture); PlayerFlags2 flags = fixture.Player.Flags2;
        bool prior = (bool)suppress.GetValue(null)!; Pixels positive, native, hidden, repeat;
        Require(!prior, "overlay suppression is disabled outside the test's frozen counterfactual");
        try
        {
            suppress.SetValue(null, false); positive = Draw(fixture, window, stem + ".png");
            suppress.SetValue(null, true); native = Draw(fixture, window, stem + "-native-pose.png");
            suppress.SetValue(null, false);
            Set(fixture.Player, nameof(PlayerEntity.Flags2), flags | PlayerFlags2.HideModel);
            try { hidden = Draw(fixture, window, stem + "-body-hidden.png"); }
            finally { Set(fixture.Player, nameof(PlayerEntity.Flags2), flags); }
            repeat = Draw(fixture, window, stem + "-repeat.png");
        }
        finally { suppress.SetValue(null, prior); Set(fixture.Player, nameof(PlayerEntity.Flags2), flags); }
        Require(positive.Rgb.SequenceEqual(repeat.Rgb), "same-tick positive/repeat RGB is exact");
        Require(Equal(before, CaptureCanonical(fixture)) && flags == fixture.Player.Flags2,
            "frozen counterfactuals preserve motion, collision, eye, shot anchors, native clocks and accepted state");
        int bodyPixels = Difference(positive, hidden); posePixels = Difference(positive, native);
        Require(bodyPixels >= 20, "native Hunter body contributes positive isolated pixels");
        if (before.Pose.MantleWeight == 0 && before.Pose.GrappleAmount == 0 && before.Pose.WallAmount == 0)
            Require(posePixels == 0, "inactive overlay retains the native body picture");
        frames.Add(new { number, simulationFrame = before.Frame, file = Path.GetFileName(positive.Metadata.File),
            durationSeconds = 1d / 30, pose = before.Pose });
        return new { number, simulationFrame = before.Frame, pose = before.Pose, bodyPixels, posePixels,
            repeatRgbExact = true, canonicalStateUnchanged = true, positive = positive.Metadata,
            nativePose = native.Metadata, bodyHidden = hidden.Metadata, repeat = repeat.Metadata };
    }
    private static bool FrontContact(PlayerEntity player)
    {
        float radius = Fixed.ToFloat(player.Values.BipedColRadius);
        foreach (CollisionResult hit in (CollisionResult[])Get(player, "_collisionScratch")!)
            if (hit.Field0 == 0 && hit.EntityCollision == null && hit.Slipperiness == 0
                && !hit.Flags.TestFlag(CollisionFlags.Damaging) && hit.Terrain != Terrain.Lava
                && hit.Plane.Z < -.999f && MathF.Abs(hit.Plane.W + 17) < .001f
                && MathF.Abs(Vector3.Dot(player.Position, hit.Plane.Xyz) - hit.Plane.W - radius) <= .03f) return true;
        return false;
    }
    private static object Scenario(Hunter hunter, string scenario, NativeWindow window, FieldInfo suppress,
        string shots, Vector2i size)
    {
        string folder = Path.Combine(shots, hunter.ToString().ToLowerInvariant(), scenario);
        Directory.CreateDirectory(folder); var pictures = new List<object>(); var frames = new List<object>();
        using var fixture = new Fixture(hunter, scenario, size);
        int contact = -1, request = -1, finished = -1, activeTicks = 0, visiblePoseFrames = 0, picture = 0;
        bool wasActive = false, complete = false;
        string manifest = Path.Combine(folder, "frames.json");
        try
        {
            pictures.Add(Capture(fixture, window, suppress, folder, picture++, frames, out _));
            for (int tick = 0; tick < (scenario == "mantle" ? 100 : 46); tick++)
            {
                bool forward = scenario == "mantle" && finished < 0;
                bool jump = scenario == "mantle" && tick == request;
                bool grapple = scenario == "grapple" && tick < 32;
                SetKey(fixture.Keyboard, Keys.W, forward); SetKey(fixture.Keyboard, Keys.Space, jump);
                SetKey(fixture.Keyboard, Keys.G, grapple); fixture.Step();
                Require(fixture.Player.Controls.MoveUp.IsDown == forward && fixture.Player.Controls.Jump.IsDown == jump
                    && fixture.Player.Controls.Grapple.IsDown == grapple, "production controls consume the exact authored keys");
                Require(fixture.Player.Health > 0 && !fixture.Player.IsAltForm && !fixture.Player.IsMorphing,
                    "actual living native biped remains eligible");
                bool active = scenario == "mantle" ? (bool)Get(fixture.Player, "ModParkourMantleActive")!
                    : (bool)Get(Get(fixture.Player, "ModGrappleState")!, "Active")!;
                if (active) activeTicks++;
                if (wasActive && !active && finished < 0) finished = tick;
                wasActive = active;
                if (scenario == "mantle" && contact < 0 && FrontContact(fixture.Player))
                { contact = tick; request = tick + 5; }
                if (scenario == "grapple" && tick >= 32)
                    Require(Picture(fixture.Player).GrappleAmount == 0, "physical G release immediately retires the visual grapple");
                if (tick % 2 == 0)
                {
                    Pose pose = Picture(fixture.Player);
                    pictures.Add(Capture(fixture, window, suppress, folder, picture++, frames, out int changed));
                    if ((pose.MantleWeight > .25f || pose.GrappleAmount > .25f) && changed >= 8) visiblePoseFrames++;
                }
                if (scenario == "mantle" && finished >= 0 && tick - finished >= 12) break;
            }
            Require(activeTicks > 1, "actual production admission earns multiple active ticks");
            Require(finished >= 0, "action reaches its native completion or physical-release boundary");
            Require(visiblePoseFrames >= 2, "multiple admitted animation pictures visibly differ from the same-state native pose");
            if (scenario == "grapple") Require((int)Get(fixture.Player, "ModGrappleForceCount")! > 0,
                "actual G admission earned owner force applications before physical release");
            complete = true;
            return new { hunter = hunter.ToString(), scenario, contact, request, finished, activeTicks,
                visiblePoseFrames, frameManifest = manifest, pictures };
        }
        finally
        {
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { status = complete ? "PASS" : "FAIL_OR_INCOMPLETE",
                fps = 30, frames, pictures,
                scope = "Private native positive frames at every second fixed tick, plus an establishing frame; encode only verified sequences; no elapsed/performance claim" }, Json));
        }
    }
    private static bool BoundaryContact(PlayerEntity player, Vector3 normal)
    {
        float radius = Fixed.ToFloat(player.Values.BipedColRadius);
        foreach (CollisionResult hit in (CollisionResult[])Get(player, "_collisionScratch")!)
            if (hit.Field0 == 0 && hit.EntityCollision == null && hit.Slipperiness == 0
                && !hit.Flags.TestFlag(CollisionFlags.Damaging) && hit.Terrain != Terrain.Lava
                && hit.Plane.Xyz == normal && MathF.Abs(hit.Plane.W + 48) < .001f
                && MathF.Abs(Vector3.Dot(player.Position, normal) - hit.Plane.W - radius) <= .03f) return true;
        return false;
    }
    private static int WallKind(byte cue) => cue is >= 1 and <= 12 ? (cue - 1) / 4 + 1 : 0;
    private static Vector3 CueNormal(byte cue) => cue is >= 1 and <= 12 ? ((cue - 1) % 4) switch
    {
        0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitZ, _ => -Vector3.UnitZ
    } : Vector3.Zero;
    private static object WallScenario(Hunter hunter, string scenario, bool west, NativeWindow window,
        FieldInfo suppress, string shots, Vector2i size)
    {
        string side = west ? "west" : "east";
        string folder = Path.Combine(shots, hunter.ToString().ToLowerInvariant(), scenario + "-" + side);
        Directory.CreateDirectory(folder); var pictures = new List<object>(); var frames = new List<object>();
        using var fixture = new Fixture(hunter, scenario, size, west);
        PlayerEntity player = fixture.Player; Vector3 normal = west ? Vector3.UnitX : -Vector3.UnitX;
        int targetKind = scenario == "wallrun" ? 2 : scenario == "wallkick" ? 3 : 1;
        int length = scenario == "wallstrafe" ? 180 : scenario == "walltuck" ? 110 : 150;
        int kick = -1, kickCount = 0, runTicks = 0, appliedStrafeTicks = 0, contactTicks = 0;
        int activeTicks = 0, visiblePoseFrames = 0, tuckedPoseFrames = 0, picture = 0;
        bool complete = false; string manifest = Path.Combine(folder, "frames.json");
        void KeysFor(bool moving, bool jump, bool tuck)
        {
            SetKey(fixture.Keyboard, Keys.W, moving); SetKey(fixture.Keyboard, Keys.A, moving && west);
            SetKey(fixture.Keyboard, Keys.D, moving && !west); SetKey(fixture.Keyboard, Keys.Space, jump);
            SetKey(fixture.Keyboard, Keys.LeftControl, tuck); SetKey(fixture.Keyboard, Keys.G, false);
        }
        bool Target(Pose pose) => WallKind(pose.WallCue) == targetKind
            && (scenario != "walltuck" || pose.WallTucked) && pose.WallAmount > .25f;
        void PictureAt(int tick)
        {
            if (tick % 2 != 0) return;
            Pose pose = Picture(player);
            pictures.Add(Capture(fixture, window, suppress, folder, picture++, frames, out int changed));
            if (Target(pose) && changed >= 8) visiblePoseFrames++;
            if (pose.WallTucked && pose.WallAmount > .25f && changed >= 8) tuckedPoseFrames++;
        }
        try
        {
            Require(Picture(player).WallCue == 0, "neutral initial wall lane earns no proximity-only wall cue");
            pictures.Add(Capture(fixture, window, suppress, folder, picture++, frames, out _));
            for (int tick = 0; tick < length; tick++)
            {
                if (scenario == "wallkick" && kick < 0 && (bool)Get(player, "ModParkourWallrunActive")!)
                    kick = tick + 4;
                bool moving = scenario != "wallkick" || kick < 0 || tick <= kick;
                bool jump = scenario == "walltuck" ? tick == 70
                    : (scenario is "wallrun" or "wallkick") && (tick == 32 || tick == kick);
                bool tuck = scenario == "walltuck" && tick is >= 72 and <= 91;
                KeysFor(moving, jump, tuck); ulong frame = fixture.Scene.FrameCount; fixture.Step();
                Require(player.Controls.MoveUp.IsDown == moving && player.Controls.MoveLeft.IsDown == (moving && west)
                    && player.Controls.MoveRight.IsDown == (moving && !west) && player.Controls.Jump.IsDown == jump
                    && player.Controls.Crouch.IsDown == tuck && !player.Controls.Grapple.IsDown,
                    "production controls consume exact W/side/fresh Space/Ctrl wall commands");
                Require(player.Health > 0 && !player.IsAltForm && !player.IsMorphing
                    && float.IsFinite(player.Position.LengthSquared) && float.IsFinite(player.Speed.LengthSquared),
                    "wall course retains living finite native biped motion");
                bool run = (bool)Get(player, "ModParkourWallrunActive")!;
                bool kicked = (bool)Invoke(player, "ModParkourWallkickApplied", frame)!;
                bool applied = (bool)Invoke(player, "ModParkourWallstrafeApplied", frame)!;
                bool contact = BoundaryContact(player, normal);
                if (run) runTicks++; if (kicked) kickCount++; if (applied) appliedStrafeTicks++;
                if (contact) contactTicks++;
                Pose pose = Picture(player); int kind = WallKind(pose.WallCue);
                Require(pose.WallCue <= 12 && float.IsFinite(pose.WallAmount) && pose.WallAmount is >= 0 and <= 1,
                    "wall cue and amount stay within their presentation contract");
                if (pose.WallAmount > 0)
                {
                    Require(kind > 0 && pose.WallNormal.SequenceEqual(new[] { CueNormal(pose.WallCue).X,
                        CueNormal(pose.WallCue).Y, CueNormal(pose.WallCue).Z }),
                        "published wall normal matches the exact cue's world side");
                    Require(pose.MantleWeight == 0 && pose.GrappleAmount == 0,
                        "wall picture does not fabricate concurrent mantle or grapple");
                    if (kind == 2) Require(run, "wallrun picture is earned by the actual active controller");
                    if (kind == 3) Require(kickCount == 1, "kick tail follows one actual accepted fresh-Space kick");
                }
                if (Target(pose))
                {
                    activeTicks++;
                    Require(pose.WallNormal.SequenceEqual(new[] { normal.X, normal.Y, normal.Z }),
                        "selected course action uses the physically approached east/west boundary");
                    if (targetKind != 3) Require(contact, "strafe/run picture has actual safe static boundary contact");
                }
                PictureAt(tick);
                if (scenario == "wallkick" && kick >= 0 && tick > kick + 24 && pose.WallAmount == 0) break;
            }
            // Release only through the real keyboard and native completed ticks.
            // This also records the retired picture after the bounded kick tail.
            KeysFor(false, false, false);
            for (int tick = 0; tick < 14; tick++) { fixture.Step(); PictureAt(tick); }
            Require(Picture(player).WallAmount == 0 && Picture(player).WallCue == 0,
                "physical wall-input release retires the wall picture");
            Require(contactTicks > 1 && activeTicks > 1 && visiblePoseFrames >= 2,
                "actual repeated wall admission earns multiple visible same-state pixel differences");
            if (scenario == "wallstrafe" || scenario == "walltuck")
                Require(appliedStrafeTicks > 1, "course earned actual native wallstrafe impulses before stable cap");
            if (scenario is "wallrun" or "wallkick") Require(runTicks > 1,
                "fresh takeoff earned actual airborne wallrun ticks");
            if (scenario == "wallkick") Require(kick > 32 && kickCount == 1,
                "released takeoff plus one fresh active-run Space accepts exactly one wallkick");
            else Require(kickCount == 0, "nonkick course never fabricates a kick");
            if (scenario == "walltuck") Require(tuckedPoseFrames >= 2,
                "actual airborne Ctrl earns several visible tucked wallstrafe pictures");
            complete = true;
            return new { hunter = hunter.ToString(), scenario, side, kick, kickCount, runTicks, appliedStrafeTicks,
                contactTicks, activeTicks, visiblePoseFrames, tuckedPoseFrames, frameManifest = manifest, pictures };
        }
        finally
        {
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { status = complete ? "PASS" : "FAIL_OR_INCOMPLETE",
                fps = 30, scenario, side, frames, pictures,
                scope = "Private native wall frames every second fixed tick plus establishing frame; actual W/side/fresh Space/Ctrl with one initial placement only; no elapsed/performance claim" }, Json));
        }
    }
    private static void Release(Scene scene)
    {
        Type commitType = Engine("MphRead.Mods.Launcher.PostMatchCommitContext");
        object commit = commitType.GetMethod("Capture", Static)!.Invoke(null,
            new object[] { scene.GameState, (byte)0, SaveWhen.Never, 0L })!;
        try { scene.DoCleanup(); }
        finally
        {
            try { scene.UnloadGl(); }
            finally
            {
                Invoke(commit, "Commit"); Invoke(commit, "Release");
                Engine("MphRead.ForegroundSceneBinding").GetMethod("TryRelease", Static)!.Invoke(null, new[] { commit });
            }
        }
    }

    // Program wiring is intentionally separate from this testing-only owner.
    // Proposed invocation: -parkouranimationcheck -hunter all -scenario both
    // -shots <fresh directory> -output <fresh JSON> [-size 800x600].
    // -scenario walls selects strafe/run/kick, keeping default both unchanged.
    // -wall-side east|west|both optionally mirrors wall courses; walltuck is opt-in.
    internal static int Run(string[] args)
    {
        _checks = 0;
        string Value(string name, string fallback)
        {
            int at = Array.IndexOf(args, name);
            if (at < 0) return fallback;
            if (at + 1 == args.Length) throw new ArgumentException("Missing " + name + " value");
            return args[at + 1];
        }
        if (!args.Contains("-output") || !args.Contains("-shots"))
            throw new ArgumentException("-parkouranimationcheck requires explicit fresh -output and -shots paths");
        string output = Path.GetFullPath(Value("-output", ""));
        string shots = Path.GetFullPath(Value("-shots", ""));
        string binaryRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (output.StartsWith(binaryRoot, StringComparison.OrdinalIgnoreCase)
            || (shots + Path.DirectorySeparatorChar).StartsWith(binaryRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Keep GL outputs outside the frozen binary directory");
        if (File.Exists(output) || Directory.Exists(shots)) throw new IOException("Preserve prior GL attempt; choose fresh outputs");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); Directory.CreateDirectory(shots);
        var rows = new List<object>(); string status = "FAIL", failure = ""; object? driver = null; int exit = 1;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This fixture permits Windows only");
            Require(!Headless.Active && !NetSession.Active && !DemoPlayback.IsActive, "fresh nonheadless offline process");
            string selected = Value("-hunter", "all"), scenario = Value("-scenario", "both");
            Hunter[] hunters = selected == "all" ? Hunters : new[] { Enum.Parse<Hunter>(selected, true) };
            Require(hunters.All(h => Hunters.Contains(h)), "only the seven native Hunters are admitted");
            Require(scenario is "both" or "mantle" or "grapple" or "walls" or "wallstrafe" or "wallrun"
                or "wallkick" or "walltuck", "known actual-input scenario");
            string wallSide = Value("-wall-side", "east");
            Require(wallSide is "east" or "west" or "both", "known authored wall side");
            string[] dimensions = Value("-size", "800x600").Split('x');
            var size = new Vector2i(int.Parse(dimensions[0]), int.Parse(dimensions[1]));
            Require(size.X is >= 640 and <= 1920 && size.Y is >= 480 and <= 1080, "bounded native capture dimensions");
            Engine("MphRead.ConsoleSetup").GetMethod("Run", Static)!.Invoke(null, null);
            Paths.UpdatePaths(); Paths.ChooseMphPath(); Paths.ChooseFhPath();
            MphRead.Mods.MapGen.CustomRooms.MapDirectory = Path.Combine(shots, "generated-training-map");
            Directory.CreateDirectory(MphRead.Mods.MapGen.CustomRooms.MapDirectory);
            // Match nettest's authored course, copied from committed build content;
            // map generation may create only private derived runtime outputs.
            File.Copy(Path.Combine(AppContext.BaseDirectory, "maps", "traversal-training", "map.json"),
                Path.Combine(MphRead.Mods.MapGen.CustomRooms.MapDirectory, "map.json"));
            MphRead.Mods.MapGen.CustomRooms.UserMapDirectory = Path.Combine(shots, "empty-user-maps");
            MphRead.Mods.MapGen.CustomRooms.RuntimeNamespace = "parkour-animation-gl";
            MphRead.Mods.MapGen.CustomRooms.GenerateMissing(Room);
            Type context = Engine("MphRead.Mods.Render.DesktopGlContext");
            var settings = (NativeWindowSettings)context.GetMethod("Settings", Static)!.Invoke(null, new object[] { true })!;
            settings.ClientSize = size; settings.StartVisible = false; settings.StartFocused = false;
            settings.Title = "Parkour hidden native animation verification";
            using var window = new NativeWindow(settings);
            using var graphics = (IDisposable)Activator.CreateInstance(Engine("MphRead.Mods.Render.DesktopGraphicsSession"),
                Hidden, null, new object[] { window }, null)!;
            HiddenWindow(window);
            driver = new { vendor = GL.GetString(StringName.Vendor), renderer = GL.GetString(StringName.Renderer),
                version = GL.GetString(StringName.Version), hidden = true, focused = false };
            using var input = new DisplayPoseInputIsolation();
            RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Original); RenderOptions.Shadows = ShadowQuality.Off;
            RenderOptions.PlayerOutline = PlayerOutlineStyle.Off; RenderOptions.ResolutionScale = 100;
            RenderOptions.ShowFps = false; RenderOptions.ShowCustomCosmetics = false;
            RenderOptions.TextureFiltering = false; RenderOptions.TextureMipmaps = false;
            RenderOptions.TextureAnisotropy = 1;
            FrameTiming.FrameRateCap = 60; FrameTiming.Reset(); FrameTiming.ResetDiagnostics();
            FieldInfo suppress = Engine("MphRead.Mods.Render.ParkourPoseOverlay").GetField("SuppressForCheck", Static)!;
            Require(!(bool)suppress.GetValue(null)!, "default native pose counterfactual seam is disabled");
            string[] actions = scenario == "both" ? new[] { "mantle", "grapple" }
                : scenario == "walls" ? new[] { "wallstrafe", "wallrun", "wallkick" } : new[] { scenario };
            foreach (Hunter hunter in hunters)
            foreach (string action in actions)
            {
                Require(input.Isolated, "physical input polling remains scoped out of the authored keyboard fixture");
                if (action.StartsWith("wall", StringComparison.Ordinal))
                {
                    foreach (bool west in wallSide == "both" ? new[] { false, true } : new[] { wallSide == "west" })
                        rows.Add(WallScenario(hunter, action, west, window, suppress, shots, size));
                }
                else rows.Add(Scenario(hunter, action, window, suppress, shots, size));
            }
            HiddenWindow(window); status = "PASS"; exit = 0;
        }
        catch (Exception error)
        {
            Exception cause = error is TargetInvocationException { InnerException: { } inner } ? inner : error;
            failure = cause.ToString();
            if (cause is PlatformNotSupportedException || cause.Message.Contains("No active primary display", StringComparison.Ordinal)
                || cause.Message.Contains("WGL: The driver does not appear to support OpenGL", StringComparison.Ordinal))
            { status = "BLOCKED_GL_ENVIRONMENT"; exit = 3; }
            Console.Error.WriteLine("PARKOUR ANIMATION GL " + status + " " + failure);
        }
        finally
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { schema = 1, status, failure, checks = _checks,
                scope = "Windows hidden unfocused native GL Scene; real W/side/Space/G/Ctrl admission and native bodies; initial lane placement only; frozen render-only overlay suppression/body-hidden controls; no OS-focus/UI automation, imported assets, physical-device, Mac, timing or performance acceptance",
                driver, simulationHz = 60, frameSequenceFps = 30, nativeClientMvid = typeof(Scene).Module.ModuleVersionId,
                clientSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))).ToLowerInvariant(),
                harnessSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ParkourAnimationRenderCheck).Assembly.Location))).ToLowerInvariant(),
                sourceCommit = Environment.GetEnvironmentVariable("PRIME_TEST_SOURCE_SHA"), rows }, Json));
        }
        Console.WriteLine($"PARKOUR ANIMATION GL {status} {_checks} assertions; {rows.Count} Hunter/action scenarios");
        return exit;
    }
}
