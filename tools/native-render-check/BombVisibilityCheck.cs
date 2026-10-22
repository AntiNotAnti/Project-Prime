using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

// Opt-in local GPU fixture, independent of UDP reliability evidence. It uses the
// canonical replica constructor and production body/effect/trail draw paths.
sealed class BombVisibilityCheck
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly Scene _scene;
    readonly string _directory;
    readonly List<(BombEntity Bomb, BombState State)> _bombs = new();
    readonly Vector3 _target, _positive, _negative;
    readonly Vector3 _wall;
    static readonly PropertyInfo Draw = typeof(EntityBase).GetProperty("ShouldDraw")!;
    static readonly MethodInfo Apply = typeof(BombEntity).GetMethod("ModApplyState", Private)!;
    static readonly string[] CameraFields = { "_cameraMode", "_cameraPosition", "_cameraFacing", "_cameraRight", "_cameraUp", "_freeCam", "_inputMode", "_cameraFov", "_viewModelFov" };
    public object? Evidence { get; private set; }

    public BombVisibilityCheck(Scene scene, string directory)
    {
        _scene = scene; _directory = directory;
        if (Directory.Exists(directory)) throw new IOException("Bomb GPU evidence directory already exists: " + directory);
        Directory.CreateDirectory(directory);
        (_target, _positive, _negative, _wall) = FindCameras();
        var spawn = typeof(BombEntity).GetMethod("SpawnReplica", BindingFlags.Static | BindingFlags.NonPublic)!;
        Add(0, BombType.MorphBall, 1, 0, _target, 0, 0);
        Add(1, BombType.Stinglarva, 1, 0, _target, 0, 0);
        Add(3, BombType.Lockjaw, 1, 0, _target - Vector3.UnitX * .8f, 0, 0);
        Add(3, BombType.Lockjaw, 2, 1, _target + Vector3.UnitX * .8f, 1, 0);
        Add(3, BombType.Lockjaw, 3, 2, _target + Vector3.UnitZ * 1.4f, 1, 2);
        void Add(int slot, BombType kind, uint sequence, byte index, Vector3 position, uint link0, uint link1)
        {
            var state = new BombState(new ShotKey(1, 1, slot, 1, 1, sequence), kind,
                (uint)scene.FrameCount, position, Vector3.UnitZ, Vector3.UnitY, Vector3.Zero, 1800, index, link0, link1, 0);
            if (!state.IsValid) throw new InvalidDataException("Invalid GPU fixture state.");
            var bomb = (BombEntity?)spawn.Invoke(null, new object[] { scene.Players.Items[slot], state, scene });
            if (bomb == null) throw new InvalidOperationException("GPU fixture exhausted bomb pool.");
            _bombs.Add((bomb, state));
        }
    }

    (Vector3, Vector3, Vector3, Vector3) FindCameras()
    {
        // Use real room collision to choose a clear positive view and a wall
        // between the same fixture and the negative camera. No synthetic depth.
        foreach (float height in new[] { 1.5f, 2.5f, .8f })
        foreach (Vector3 direction in new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitX })
        {
            Vector3 target = _scene.Players.Main.Position + Vector3.UnitY * height;
            Vector3 positive = target + direction * 5;
            CollisionResult clear = default, wall = default;
            if (CollisionDetection.CheckBetweenPoints(target, positive, TestFlags.Beams, _scene, ref clear)) continue;
            if (!CollisionDetection.CheckBetweenPoints(target, target - direction * 80, TestFlags.Beams, _scene, ref wall)) continue;
            Vector3 negative = wall.Position - direction * 2;
            if ((negative - target).LengthSquared < 9) continue;
            bool blocked = true;
            foreach (Vector3 offset in new[] { Vector3.Zero, -Vector3.UnitX * .8f, Vector3.UnitX * .8f, Vector3.UnitZ * 1.4f })
            {
                CollisionResult hit = default;
                blocked &= CollisionDetection.CheckBetweenPoints(negative, target + offset, TestFlags.Beams, _scene, ref hit);
            }
            if (blocked) return (target, positive, negative, wall.Position);
        }
        throw new InvalidOperationException("No collision-clear positive/collision-blocked wall camera found; bomb visibility is unexercised.");
    }

    public void Run()
    {
        var savedCamera = CameraFields.Select(n => typeof(Scene).GetField(n, Private)!.GetValue(_scene)).ToArray();
        bool allNodes = _scene.ShowAllNodes;
        var effectFlags = _bombs.SelectMany(b => b.Bomb.Effect?.Elements ?? new List<EffectElementEntry>())
            .ToDictionary(e => e, e => e.Flags);
        var effectSnapshot = _bombs.Select(b => new { key = b.State.Key.ToString(), effectId = b.Bomb.Effect?.EffectId,
            elements = b.Bomb.Effect?.Elements.Select(e => new { e.EffectName, e.ElementName, flags = e.Flags.ToString(), e.CreationTime,
                e.ExpirationTime, e.Expired, particleCount = e.Particles.Count }).ToArray() }).ToArray();
        var cases = new List<object>();
        var failures = new List<string>();
        ulong frame = _scene.FrameCount;
        try
        {
            _scene.ShowAllNodes = true;
            _scene.SetFreeCamera(true);
            for (int i = 0; i < _bombs.Count; i++)
                Check("body-" + i, i, null);
            Check("edge-2-1", 3, 1u);
            Check("edge-3-1", 4, 1u);
            Check("edge-3-2", 4, 2u);
            if (_scene.FrameCount != frame) failures.Add("simulation advanced during frozen GPU check");
            Evidence = new { schema = 1, scope = "local canonical replica GPU fixture; UDP sequencing audited separately",
                emitterInitialization = "production canonical replica initialization/Process and normal native effect draw pass; no fixture priming",
                status = failures.Count == 0 ? "PASS" : "FAIL", simulationFrame = frame,
                matchState = _scene.GameState.MatchState.ToString(), _scene.GameState.MenuPause, _scene.GameState.DialogPause,
                processFrame = _scene.ProcessFrame, effectFrame = typeof(Scene).GetField("_effectFrame", Private)!.GetValue(_scene),
                positiveCamera = V(_positive), negativeCamera = V(_negative), wallIntersection = V(_wall),
                pixelThreshold = 2, effectSnapshot, fixtures = _bombs.Select(b => new { key = b.State.Key.ToString(), kind = b.State.Kind.ToString(),
                    position = V(b.State.Position), b.State.Index, b.State.Link0, b.State.Link1 }).ToArray(), cases, failures };
            File.WriteAllText(Path.Combine(_directory, "evidence.json"), JsonSerializer.Serialize(Evidence, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            if (failures.Count != 0) throw new InvalidDataException("Bomb GPU check failed: " + string.Join("; ", failures));
        }
        finally
        {
            foreach (var pair in effectFlags) pair.Key.Flags = pair.Value;
            foreach (var row in _bombs)
            {
                Apply.Invoke(row.Bomb, new object[] { row.State });
                Draw.SetValue(row.Bomb, true);
                typeof(BombEntity).GetMethod("ModRemoveReplica", Private)!.Invoke(row.Bomb, null);
            }
            _scene.ShowAllNodes = allNodes;
            for (int i = 0; i < CameraFields.Length; i++) typeof(Scene).GetField(CameraFields[i], Private)!.SetValue(_scene, savedCamera[i]);
            _scene.OnDrawFrame(); _scene.OnRenderFrame();
        }

        void Suppress()
        {
            foreach (var row in _bombs)
            {
                Draw.SetValue(row.Bomb, false);
                row.Bomb.Effect?.SetDrawEnabled(false);
                Apply.Invoke(row.Bomb, new object[] { row.State with { Link0 = 0, Link1 = 0 } });
            }
        }
        void Enable(int index, uint? edge)
        {
            var row = _bombs[index];
            Draw.SetValue(row.Bomb, true);
            if (edge.HasValue)
                Apply.Invoke(row.Bomb, new object[] { row.State with { Link0 = edge.Value, Link1 = 0 } });
            else if (row.Bomb.Effect != null)
                foreach (var element in row.Bomb.Effect.Elements) element.Flags = effectFlags[element];
        }
        void Check(string name, int index, uint? edge)
        {
            foreach (bool occluded in new[] { false, true })
            {
                string label = name + (occluded ? "-wall" : "-visible");
                Camera(occluded ? _negative : _positive);
                Suppress(); var before = Capture(label + "-suppressed-before");
                var row = _bombs[index];
                Vector3 samplePosition = row.State.Position;
                if (edge.HasValue)
                {
                    var target = _bombs.Single(b => b.State.Key == (row.State.Key with { ShotId = edge.Value }));
                    samplePosition = (samplePosition + target.State.Position) / 2;
                }
                var depth = Depth(samplePosition);
                bool geometry = occluded ? depth.Sample > 0 && depth.Sample < depth.Expected - .00001f
                    : depth.Sample > depth.Expected - .00001f;
                Enable(index, edge); var enabled = Capture(label + "-enabled");
                Suppress(); var after = Capture(label + "-suppressed-after");
                int unstable = Different(before.Pixels, after.Pixels);
                int changed = Different(before.Pixels, enabled.Pixels);
                bool pass = geometry && unstable == 0 && (occluded ? changed == 0 : changed > 0);
                if (!pass) failures.Add(label + $": changedPixels={changed}, unstablePixels={unstable}, geometry={geometry}");
                cases.Add(new { name, occluded, key = row.State.Key.ToString(), targetKey = edge.HasValue
                    ? (row.State.Key with { ShotId = edge.Value }).ToString() : null,
                    status = pass ? "PASS" : "FAIL", geometryVerified = geometry, worldDepth = depth.Sample, fixtureDepth = depth.Expected, changedPixels = changed, unstablePixels = unstable,
                    captures = new[] { before.Metadata, enabled.Metadata, after.Metadata } });
            }
        }
    }

    void Camera(Vector3 position)
    {
        Vector3 facing = (_target - position).Normalized();
        Vector3 right = Vector3.Cross(facing, Vector3.UnitY).Normalized();
        void Set(string name, Vector3 value) => typeof(Scene).GetField(name, Private)!.SetValue(_scene, value);
        Set("_cameraPosition", position); Set("_cameraFacing", facing);
        Set("_cameraRight", right); Set("_cameraUp", Vector3.Cross(right, facing));
    }

    (byte[] Pixels, object Metadata) Capture(string name)
    {
        _scene.OnDrawFrame(); _scene.OnRenderFrame();
        byte[] pixels = _scene.ReadSceneTarget(out int width, out int height)
            ?? throw new InvalidDataException("Bomb GPU framebuffer readback failed.");
        if (pixels.Length != width * height * 3 || width <= 0 || height <= 0)
            throw new InvalidDataException("Bomb GPU framebuffer dimensions invalid.");
        string path = Path.Combine(_directory, name + ".png");
        if (!ScreenCapture.Save(_scene, path)) throw new IOException("Bomb GPU PNG write failed.");
        var error = GL.GetError();
        if (error != ErrorCode.NoError) throw new InvalidOperationException("Bomb GPU GL error: " + error);
        return (pixels, new { path, width, height, rgbSha256 = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant(),
            pngSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() });
    }
    (float Sample, float Expected) Depth(Vector3 point)
    {
        // A wall-negative with an empty/culled framebuffer must not pass merely
        // because both images were empty. Read actual world depth at this key's
        // projected body/edge center and require geometry in front of it.
        Vector4 clip = Vector4.TransformRow(new Vector4(point, 1), _scene.ViewMatrix * _scene.PerspectiveMatrix);
        if (clip.W <= 0) throw new InvalidDataException("Bomb GPU sample behind camera.");
        Vector3 ndc = clip.Xyz / clip.W;
        var size = (Vector2i)typeof(Scene).GetField("_targetSize", Private)!.GetValue(_scene)!;
        int x = (int)((ndc.X + 1) * .5f * size.X), y = (int)((ndc.Y + 1) * .5f * size.Y);
        if (x < 0 || y < 0 || x >= size.X || y >= size.Y) throw new InvalidDataException("Bomb GPU sample outside framebuffer.");
        var values = new float[1];
        int framebuffer = (int)typeof(Scene).GetField("_frameBuffer", Private)!.GetValue(_scene)!;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        GL.ReadPixels(x, y, 1, 1, PixelFormat.DepthComponent, PixelType.Float, values);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return (values[0], (ndc.Z + 1) * .5f);
    }
    static int Different(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) throw new InvalidDataException("GPU capture size changed.");
        int count = 0;
        for (int i = 0; i < left.Length; i += 3)
            if (Math.Abs(left[i] - right[i]) > 2 || Math.Abs(left[i + 1] - right[i + 1]) > 2 || Math.Abs(left[i + 2] - right[i + 2]) > 2) count++;
        return count;
    }
    static float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };
}
