using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// Opt-in diagnostic: measure existing accepted replica pixels, never inject state.
internal static class NetBombVisibilityProbe
{
    private const int Capacity = 512;
    private static readonly string? Output = Environment.GetEnvironmentVariable("PRIME_NET2_BOMB_VISIBILITY");
    private static readonly Dictionary<string, object> Rows = new();
    private static readonly Dictionary<string, int> Attempts = new();
    private static readonly HashSet<string> Finished = new();
    private static long Overflow;
    private static bool Initialized;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly PropertyInfo ShouldDraw = typeof(EntityBase).GetProperty("ShouldDraw")!;
    private static readonly string[] CameraFields = { "_cameraMode", "_cameraPosition", "_cameraFacing", "_cameraRight", "_cameraUp", "_freeCam", "_inputMode", "_cameraFov", "_viewModelFov" };
    private static string EngineSha256 = "";
    private static object V(Vector3 v) => new { v.X, v.Y, v.Z };
    private static object State(BombState s) => new { s.Key, Kind = s.Kind.ToString(), s.PlacedFrame,
        Position = V(s.Position), Facing = V(s.Facing), Up = V(s.Up), Speed = V(s.Speed),
        s.Countdown, s.Index, s.Link0, s.Link1, s.Recolor };

    internal static void Observe(Scene scene)
    {
        if (string.IsNullOrEmpty(Output) || !NetSession.IsClient || NetSession.LocalSlot < 0 || NetSession.ConnectionLost) return;
        if (!Initialized)
        {
            if (File.Exists(Output)) throw new IOException("Live bomb pixel evidence already exists: " + Output);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output))!);
            Directory.CreateDirectory(Output + ".frames");
            EngineSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))).ToLowerInvariant();
            Initialized = true;
        }
        var bombs = scene.GetBombEntities().ToArray();
        foreach (BombEntity bomb in bombs)
        {
            if (!bomb.ModReplica || bomb.Flags.TestFlag(BombFlags.Exploded) || !NetBombs.Current(bomb.ModOwnerKey)) continue;
            Probe(bomb, null);
            if (bomb.BombType == BombType.Lockjaw)
                foreach (uint link in new[] { bomb.ModLink0, bomb.ModLink1 })
                {
                    if (link == 0) continue;
                    BombEntity? target = bombs.FirstOrDefault(b => b.ModReplica && b.ModOwnerKey == (bomb.ModOwnerKey with { ShotId = link })
                        && !b.Flags.TestFlag(BombFlags.Exploded));
                    if (target != null) Probe(bomb, target);
                }
        }
        Save();

        void Probe(BombEntity bomb, BombEntity? target)
        {
            string id = bomb.ModOwnerKey + (target == null ? "/body" : "/link/" + target.ModOwnerKey);
            if (Finished.Contains(id)) return;
            if (!Rows.ContainsKey(id) && Rows.Count >= Capacity) { if (Finished.Add(id)) Overflow++; return; }
            int attempt = Attempts.TryGetValue(id, out int previous) ? previous + 1 : 1;
            Attempts[id] = attempt;
            // Bound unsupported camera retries without inventing successful coverage.
            if (attempt > 8) { Finished.Add(id); return; }
            BombState state = bomb.ModCaptureState();
            BombState? targetState = target?.ModCaptureState();
            var saved = CameraFields.Select(n => typeof(Scene).GetField(n, Private)!.GetValue(scene)).ToArray();
            bool allNodes = scene.ShowAllNodes;
            var draws = bombs.ToDictionary(b => b, b => b.ShouldDraw);
            var flags = bombs.SelectMany(b => b.Effect?.Elements ?? new List<EffectElementEntry>()).ToDictionary(e => e, e => e.Flags);
            var links = bombs.ToDictionary(b => b, b => (b.ModLink0, b.ModLink1));
            ulong frame = scene.FrameCount;
            Vector3 point = target == null ? bomb.Position : (bomb.Position + target.Position) / 2;
            string prefix = string.Join("-", new[] { state.Key.AuthorityEpoch.ToString(), state.Key.MatchId.ToString(), state.Key.ShooterSlot.ToString(),
                state.Key.Generation.ToString(), state.Key.LifeId.ToString(), state.Key.ShotId.ToString(), target?.ModOwnerKey.ShotId.ToString() ?? "body", attempt.ToString() });
            try
            {
                Vector3? camera = FindCamera(scene, point, attempt);
                if (camera == null)
                {
                    Rows[id] = new { Type = target == null ? "Body" : "Link", Key = state.Key, TargetKey = targetState?.Key,
                        State = State(state), TargetState = targetState.HasValue ? State(targetState.Value) : null,
                        Frame = NetSession.NetFrame, SnapshotFrame = NetSession.AppliedSnapshotFrame, SceneFrame = frame,
                        Attempts = attempt, Status = "INCOMPLETE", Reason = "no collision-clear vantage" };
                    return;
                }
                scene.ShowAllNodes = true; scene.SetFreeCamera(true);
                Vector3 facing = (point - camera.Value).Normalized();
                Vector3 right = Vector3.Cross(facing, Vector3.UnitY).Normalized();
                Set("_cameraPosition", camera.Value); Set("_cameraFacing", facing); Set("_cameraRight", right); Set("_cameraUp", Vector3.Cross(right, facing));
                Suppress(); var before = Capture("before");
                var depth = Depth(scene, point);
                ShouldDraw.SetValue(bomb, true);
                if (target == null && bomb.Effect != null)
                    foreach (EffectElementEntry element in bomb.Effect.Elements) element.Flags = flags[element];
                else if (target != null) { bomb.ModLink0 = target.ModOwnerKey.ShotId; bomb.ModLink1 = 0; }
                var enabled = Capture("enabled");
                Suppress(); var after = Capture("after");
                int changed = Different(before.Pixels, enabled.Pixels), unstable = Different(before.Pixels, after.Pixels);
                bool depthClear = depth.World >= depth.Fixture - .00001f;
                string status = unstable != 0 || scene.FrameCount != frame ? "INCOMPLETE"
                    : changed > 0 ? "PASS" : depthClear ? "FAIL" : "INCOMPLETE";
                Rows[id] = new { Type = target == null ? "Body" : "Link", Key = state.Key, TargetKey = targetState?.Key,
                    State = State(state), TargetState = targetState.HasValue ? State(targetState.Value) : null,
                    Frame = NetSession.NetFrame, SnapshotFrame = NetSession.AppliedSnapshotFrame, SceneFrame = frame,
                    Attempts = attempt, Status = status, Camera = V(camera.Value), CollisionClear = true,
                    WorldDepth = depth.World, FixtureDepth = depth.Fixture, CenterDepthClear = depthClear,
                    SamplePixel = new { depth.X, depth.Y }, ChangedPixels = changed, UnstablePixels = unstable,
                    EffectParticles = bomb.Effect?.Elements.Sum(e => e.Particles.Count) ?? 0,
                    Captures = new[] { before.Metadata, enabled.Metadata, after.Metadata } };
                if (status != "INCOMPLETE") Finished.Add(id);
            }
            catch (Exception ex)
            {
                Rows[id] = new { Type = target == null ? "Body" : "Link", Key = state.Key, TargetKey = targetState?.Key,
                    State = State(state), TargetState = targetState.HasValue ? State(targetState.Value) : null,
                    Frame = NetSession.NetFrame, SnapshotFrame = NetSession.AppliedSnapshotFrame, SceneFrame = frame,
                    Attempts = attempt, Status = "INCOMPLETE", Reason = ex.Message };
            }
            finally
            {
                foreach (var row in flags) row.Key.Flags = row.Value;
                foreach (var row in links) { row.Key.ModLink0 = row.Value.Item1; row.Key.ModLink1 = row.Value.Item2; }
                foreach (var row in draws) ShouldDraw.SetValue(row.Key, row.Value);
                scene.ShowAllNodes = allNodes;
                for (int i = 0; i < CameraFields.Length; i++) typeof(Scene).GetField(CameraFields[i], Private)!.SetValue(scene, saved[i]);
                scene.OnDrawFrame(); scene.OnRenderFrame();
            }
            void Set(string name, Vector3 value) => typeof(Scene).GetField(name, Private)!.SetValue(scene, value);
            void Suppress()
            {
                foreach (BombEntity row in bombs) { ShouldDraw.SetValue(row, false); row.Effect?.SetDrawEnabled(false); row.ModLink0 = row.ModLink1 = 0; }
            }
            (byte[] Pixels, object Metadata) Capture(string arm)
            {
                scene.OnDrawFrame(); scene.OnRenderFrame();
                byte[] pixels = scene.ReadSceneTarget(out int width, out int height) ?? throw new IOException("Framebuffer read failed");
                string path = Path.GetFullPath(Path.Combine(Output + ".frames", prefix + "-" + arm + ".png"));
                if (!ScreenCapture.Save(scene, path)) throw new IOException("PNG capture failed");
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Live bomb probe GL error");
                return (pixels, new { Path = path, Width = width, Height = height,
                    RgbSha256 = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant(),
                    PngSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() });
            }
        }
    }

    private static Vector3? FindCamera(Scene scene, Vector3 point, int attempt)
    {
        var directions = new[] { Vector3.UnitZ, Vector3.UnitX, -Vector3.UnitZ, -Vector3.UnitX,
            (Vector3.UnitZ + Vector3.UnitY).Normalized(), (Vector3.UnitX + Vector3.UnitY).Normalized(),
            (-Vector3.UnitZ + Vector3.UnitY).Normalized(), (-Vector3.UnitX + Vector3.UnitY).Normalized() };
        for (int i = 0; i < directions.Length; i++)
        {
            Vector3 camera = point + directions[(i + attempt - 1) % directions.Length] * 4;
            CollisionResult result = default;
            if (!CollisionDetection.CheckBetweenPoints(point, camera, TestFlags.Beams, scene, ref result)) return camera;
        }
        return null;
    }
    private static (float World, float Fixture, int X, int Y) Depth(Scene scene, Vector3 point)
    {
        Vector4 clip = Vector4.TransformRow(new Vector4(point, 1), scene.ViewMatrix * scene.PerspectiveMatrix);
        if (clip.W <= 0) throw new InvalidDataException("Sample behind camera");
        Vector3 ndc = clip.Xyz / clip.W;
        var size = (Vector2i)typeof(Scene).GetField("_targetSize", Private)!.GetValue(scene)!;
        int x = (int)((ndc.X + 1) * .5f * size.X), y = (int)((ndc.Y + 1) * .5f * size.Y);
        if (x < 0 || y < 0 || x >= size.X || y >= size.Y) throw new InvalidDataException("Sample outside framebuffer");
        var values = new float[1];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (int)typeof(Scene).GetField("_frameBuffer", Private)!.GetValue(scene)!);
        GL.ReadPixels(x, y, 1, 1, PixelFormat.DepthComponent, PixelType.Float, values);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return (values[0], (ndc.Z + 1) * .5f, x, y);
    }
    private static int Different(byte[] left, byte[] right)
    {
        if (left.Length != right.Length || left.Length % 3 != 0) throw new InvalidDataException("Capture dimensions changed");
        int count = 0;
        for (int i = 0; i < left.Length; i += 3)
            if (Math.Abs(left[i] - right[i]) > 2 || Math.Abs(left[i + 1] - right[i + 1]) > 2 || Math.Abs(left[i + 2] - right[i + 2]) > 2) count++;
        return count;
    }
    private static void Save()
    {
        var doc = new { Schema = 1, Source = Environment.GetEnvironmentVariable("PRIME_NET2_SOURCE") ?? "unknown", EngineSha256,
            EngineMvid = typeof(Scene).Module.ModuleVersionId.ToString(), ClientSlot = NetSession.LocalSlot,
            Scope = "existing accepted live replica pixels at a diagnostic clear camera; no prior-to-damage assertion",
            PixelThreshold = 2, Overflow, Rows = Rows.Values.ToArray() };
        File.WriteAllText(Output!, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
