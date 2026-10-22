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
    private static string EngineImageKind = "";
    private sealed class Lifetime
    {
        internal required Scene Scene;
        internal required ShotKey Key;
        internal BombEvent? Birth, Terminal;
        internal long BirthDrawIndex = -1, TerminalDrawIndex = -1, RetirementDrawIndex = -1;
        internal uint? RetirementSnapshotFrame;
        internal ulong? RetirementSnapshotFrontier;
        internal bool RetirementSnapshotKeyAbsent;
        internal string? RetirementWitnessStage;
        internal int BodyDrawOpportunities, BodyAttempts;
        internal bool ExpectedAlive, MissingExpectedGhost;
    }
    private sealed class TerminalVisual
    {
        internal required Lifetime Life;
        internal required int EffectId;
        internal required Matrix4 Transform;
        internal readonly List<EffectElementEntry> Elements = new();
    }
    private sealed class EventScope : IDisposable
    {
        public void Dispose() => TerminalContext = null;
    }
    private static readonly Dictionary<ShotKey, Lifetime> Lifetimes = new();
    private static readonly Dictionary<ShotKey, TerminalVisual> Terminals = new();
    private static readonly Dictionary<EffectElementEntry, TerminalVisual> ElementOwners = new();
    private static Lifetime? TerminalContext;
    private static long DrawIndex;
    private static bool Enabled(Scene scene) => !string.IsNullOrEmpty(Output) && NetSession.IsClient && !scene.Services.IsReplica;
    private static Lifetime? GetLife(Scene scene, ShotKey key)
    {
        if (Lifetimes.TryGetValue(key, out var life)) return life;
        if (Lifetimes.Count >= Capacity) { Overflow++; return null; }
        life = new Lifetime { Scene = scene, Key = key };
        Lifetimes.Add(key, life);
        return life;
    }
    internal static void AppliedSnapshot(Scene scene, uint frame, ulong frontier, System.Collections.Generic.IReadOnlyList<BombState> states)
    {
        if (!Enabled(scene)) return;
        var keys = states.Select(s => s.Key).ToHashSet();
        foreach (var life in Lifetimes.Values.Where(l => l.Scene == scene))
            if (!keys.Contains(life.Key) && !(life.Birth is { } birth && NetLifecycleTracker.Newer(birth.Frame, frame)))
            {
                // Retain the first canonical retirement before any presentation,
                // rather than infer a vanished body from a delayed terminal fact.
                if (life.Birth != null && life.ExpectedAlive && life.RetirementSnapshotFrame == null
                    && life.BodyDrawOpportunities == 0 && life.BodyAttempts == 0)
                {
                    life.RetirementSnapshotFrame = frame;
                    life.RetirementSnapshotFrontier = frontier;
                    life.RetirementSnapshotKeyAbsent = true;
                    life.RetirementWitnessStage = "SubsequentSnapshot";
                    life.RetirementDrawIndex = DrawIndex;
                }
                life.ExpectedAlive = false;
            }
        foreach (var state in states)
            if (GetLife(scene, state.Key) is { } life && life.Terminal == null) life.ExpectedAlive = true;
    }
    internal static IDisposable? AcceptedEvent(Scene scene, BombEvent fact, bool birthExpected, bool hasSnapshot, uint snapshotFrame, ulong snapshotFrontier, bool snapshotHasKey)
    {
        if (!Enabled(scene) || fact.Kind is not (BombEventKind.Placed or BombEventKind.Detonated or BombEventKind.Removed)) return null;
        var life = GetLife(scene, fact.State.Key);
        if (life == null) return null;
        if (fact.Kind == BombEventKind.Placed)
        {
            life.Birth = fact; life.BirthDrawIndex = DrawIndex; life.ExpectedAlive = birthExpected;
            if (!birthExpected && hasSnapshot && !snapshotHasKey && snapshotFrame >= fact.Frame
                && life.RetirementSnapshotFrame == null && life.BodyDrawOpportunities == 0 && life.BodyAttempts == 0)
            {
                life.RetirementSnapshotFrame = snapshotFrame;
                life.RetirementSnapshotFrontier = snapshotFrontier;
                life.RetirementSnapshotKeyAbsent = true;
                life.RetirementWitnessStage = "AtBirth";
                life.RetirementDrawIndex = DrawIndex;
            }
            return null;
        }
        life.Terminal = fact; life.TerminalDrawIndex = DrawIndex; life.ExpectedAlive = false;
        if (fact.Kind != BombEventKind.Detonated) return null;
        TerminalContext = life;
        return new EventScope();
    }
    internal static bool CapturingTerminal(Scene scene) => Enabled(scene) && TerminalContext?.Scene == scene;
    internal static void RegisterTerminal(Scene scene, int effectId, Matrix4 transform, IReadOnlyList<EffectElementEntry> elements)
    {
        if (!CapturingTerminal(scene) || TerminalContext is not { } life) return;
        var visual = new TerminalVisual { Life = life, EffectId = effectId, Transform = transform };
        foreach (var element in elements)
        { visual.Elements.Add(element); ElementOwners[element] = visual; }
        Terminals[life.Key] = visual;
    }
    internal static void ElementReleased(EffectElementEntry element)
    {
        if (string.IsNullOrEmpty(Output)) return;
        if (ElementOwners.Remove(element, out var visual)) visual.Elements.Remove(element);
    }
    private static object V(Vector3 v) => new { v.X, v.Y, v.Z };
    private static object State(BombState s) => new { s.Key, Kind = s.Kind.ToString(), s.PlacedFrame,
        Position = V(s.Position), Facing = V(s.Facing), Up = V(s.Up), Speed = V(s.Speed),
        s.Countdown, s.Index, s.Link0, s.Link1, s.Recolor };

    internal static void Observe(Scene scene)
    {
        if (string.IsNullOrEmpty(Output) || !NetSession.IsClient || NetSession.LocalSlot < 0 || NetSession.ConnectionLost) return;
        DrawIndex++;
        var bombs = new List<BombEntity>();
        foreach (BombEntity bomb in scene.GetBombEntities()) bombs.Add(bomb);
        // Count accepted live presentation opportunities even when a missing
        // ghost or a replay view prevents a body capture. Neither can be hidden
        // by later terminal pixels.
        foreach (var life in Lifetimes.Values.Where(l => l.Scene == scene && l.ExpectedAlive))
        {
            life.BodyDrawOpportunities++;
            if (!bombs.Any(b => b.ModReplica && b.ModOwnerKey == life.Key && !b.Flags.TestFlag(BombFlags.Exploded)))
                life.MissingExpectedGhost = true;
        }
        // PNG capture redirects replay/killcam presentation; never pair that PNG with live-world RGB.
        if (KillCam.Presentation(scene) != null || DemoPlayback.Presentation(scene) != null) return;
        if (!Initialized)
        {
            if (File.Exists(Output)) throw new IOException("Live bomb pixel evidence already exists: " + Output);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output))!);
            Directory.CreateDirectory(Output + ".frames");
            // A single-file release has no standalone assembly Location. Hash
            // the deployed assembly when present, otherwise its bundled host,
            // and record which image the witness identifies.
            (EngineSha256, EngineImageKind) = Diagnostics.RuntimeApplicationImage.Capture(typeof(Scene).Assembly);
            Initialized = true;
        }
        foreach (BombEntity bomb in bombs)
        {
            if (!bomb.ModReplica || bomb.Flags.TestFlag(BombFlags.Exploded) || !NetBombs.Current(bomb.ModOwnerKey)) continue;
            ProbeFully(bomb, null);
            if (bomb.BombType == BombType.Lockjaw)
                foreach (uint link in new[] { bomb.ModLink0, bomb.ModLink1 })
                {
                    if (link == 0) continue;
                    BombEntity? target = bombs.FirstOrDefault(b => b.ModReplica && b.ModOwnerKey == (bomb.ModOwnerKey with { ShotId = link })
                        && !b.Flags.TestFlag(BombFlags.Exploded));
                    if (target != null) ProbeFully(bomb, target);
                }
        }
        foreach (var visual in Terminals.Values.Where(v => v.Life.Scene == scene).ToArray())
            ProbeTerminal(visual);
        Save();

        void ProbeFully(BombEntity bomb, BombEntity? target)
        {
            string id = bomb.ModOwnerKey + (target == null ? "/body" : "/link/" + target.ModOwnerKey);
            // Retry unsupported geometry in this same world frame: a short-lived
            // replica may be gone before another render. Definite failures stop.
            while (!Finished.Contains(id) && (!Attempts.TryGetValue(id, out int count) || count < 8))
                Probe(bomb, target);
        }

        void Probe(BombEntity bomb, BombEntity? target)
        {
            string id = bomb.ModOwnerKey + (target == null ? "/body" : "/link/" + target.ModOwnerKey);
            if (Finished.Contains(id)) return;
            if (!Rows.ContainsKey(id) && Rows.Count >= Capacity) { if (Finished.Add(id)) Overflow++; return; }
            int attempt = Attempts.TryGetValue(id, out int previous) ? previous + 1 : 1;
            Attempts[id] = attempt;
            if (target == null && GetLife(scene, bomb.ModOwnerKey) is { } life) life.BodyAttempts++;
            // Bound unsupported camera retries without inventing successful coverage.
            if (attempt > 8) { Finished.Add(id); return; }
            BombState state = bomb.ModCaptureState();
            uint snapshotFrame = (uint)typeof(NetBombWorld).GetField("_snapshotFrame", Private)!.GetValue(scene.Bombs)!;
            BombState? targetState = target?.ModCaptureState();
            var saved = CameraFields.Select(n => typeof(Scene).GetField(n, Private)!.GetValue(scene)).ToArray();
            bool allNodes = scene.ShowAllNodes;
            bool spectatorFreeCamera = SpectatorMode.FreeCamera;
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
                        Frame = NetSession.NetFrame, SnapshotFrame = snapshotFrame, PlayerSnapshotFrame = NetSession.AppliedSnapshotFrame, SceneFrame = frame,
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
                    Frame = NetSession.NetFrame, SnapshotFrame = snapshotFrame, PlayerSnapshotFrame = NetSession.AppliedSnapshotFrame, SceneFrame = frame,
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
                    Frame = NetSession.NetFrame, SnapshotFrame = snapshotFrame, PlayerSnapshotFrame = NetSession.AppliedSnapshotFrame, SceneFrame = frame,
                    Attempts = attempt, Status = "INCOMPLETE", Reason = ex.Message };
            }
            finally
            {
                foreach (var row in flags) row.Key.Flags = row.Value;
                foreach (var row in links) { row.Key.ModLink0 = row.Value.Item1; row.Key.ModLink1 = row.Value.Item2; }
                foreach (var row in draws) ShouldDraw.SetValue(row.Key, row.Value);
                scene.ShowAllNodes = allNodes;
                SpectatorMode.NoteFreeCamera(spectatorFreeCamera);
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

    private static void ProbeTerminal(TerminalVisual visual)
    {
        var life = visual.Life;
        if (life.Birth is not { } birth || life.Terminal is not { Kind: BombEventKind.Detonated } terminal
            || life.BodyDrawOpportunities != 0 || life.BodyAttempts != 0 || life.MissingExpectedGhost) return;
        bool sameDraw = life.BirthDrawIndex == life.TerminalDrawIndex;
        bool retiredBeforeDraw = life.RetirementSnapshotKeyAbsent
            && life.RetirementDrawIndex == life.BirthDrawIndex
            && life.RetirementSnapshotFrame is { } retiredFrame && retiredFrame >= terminal.Frame
            && life.RetirementSnapshotFrontier is { } retiredFrontier && retiredFrontier >= terminal.Sequence;
        if (!sameDraw && !retiredBeforeDraw) return;
        string id = life.Key + "/terminal";
        if (Finished.Contains(id)) return;
        if (!Rows.ContainsKey(id) && Rows.Count >= Capacity) { Finished.Add(id); Overflow++; return; }
        Scene scene = life.Scene;
        var bombs = new List<BombEntity>();
        foreach (var bomb in scene.GetBombEntities()) bombs.Add(bomb);
        while (!Finished.Contains(id) && (!Attempts.TryGetValue(id, out int count) || count < 8))
        {
            int attempt = Attempts.TryGetValue(id, out int previous) ? previous + 1 : 1;
            Attempts[id] = attempt;
            ulong frame = scene.FrameCount;
            var row = new Dictionary<string, object?>
            {
                ["Type"] = "Terminal", ["Key"] = life.Key, ["TargetKey"] = null,
                ["State"] = State(terminal.State), ["BirthSequence"] = birth.Sequence, ["BirthFrame"] = birth.Frame,
                ["TerminalSequence"] = terminal.Sequence, ["TerminalFrame"] = terminal.Frame,
                ["EffectId"] = visual.EffectId, ["EffectPosition"] = V(visual.Transform.Row3.Xyz),
                ["BodyDrawOpportunities"] = life.BodyDrawOpportunities, ["BodyAttempts"] = life.BodyAttempts,
                ["MissingExpectedGhost"] = life.MissingExpectedGhost,
                ["Classification"] = sameDraw ? "BirthTerminalBeforeBodyDraw" : "ExpiredBeforeFirstBodyDraw",
                ["RetirementWitnessStage"] = life.RetirementWitnessStage,
                ["RetirementSnapshotFrame"] = life.RetirementSnapshotFrame,
                ["RetirementSnapshotFrontier"] = life.RetirementSnapshotFrontier,
                ["RetirementSnapshotKeyAbsent"] = life.RetirementSnapshotKeyAbsent,
                ["RetirementDrawIndex"] = life.RetirementDrawIndex,
                ["AppliedBirthDrawIndex"] = life.BirthDrawIndex, ["AppliedTerminalDrawIndex"] = life.TerminalDrawIndex,
                ["CaptureDrawIndex"] = DrawIndex, ["Frame"] = NetSession.NetFrame, ["SceneFrame"] = frame,
                ["SnapshotFrame"] = (uint)typeof(NetBombWorld).GetField("_snapshotFrame", Private)!.GetValue(scene.Bombs)!,
                ["PlayerSnapshotFrame"] = NetSession.AppliedSnapshotFrame, ["Attempts"] = attempt
            };
            Rows[id] = row;
            if (visual.Elements.Count == 0)
            { row["Status"] = "INCOMPLETE"; row["Reason"] = "accepted terminal elements unavailable before pixel capture"; Finished.Add(id); break; }
            var saved = CameraFields.Select(n => typeof(Scene).GetField(n, Private)!.GetValue(scene)).ToArray();
            bool allNodes = scene.ShowAllNodes, spectatorFreeCamera = SpectatorMode.FreeCamera;
            var draws = bombs.ToDictionary(b => b, b => b.ShouldDraw);
            var links = bombs.ToDictionary(b => b, b => (b.ModLink0, b.ModLink1));
            var elements = bombs.SelectMany(b => b.Effect?.Elements ?? new List<EffectElementEntry>())
                .Concat(Terminals.Values.Where(v => v.Life.Scene == scene).SelectMany(v => v.Elements)).Distinct().ToArray();
            var flags = elements.ToDictionary(e => e, e => e.Flags);
            Vector3 point = terminal.State.Position;
            string prefix = string.Join("-", new[] { life.Key.AuthorityEpoch.ToString(), life.Key.MatchId.ToString(),
                life.Key.ShooterSlot.ToString(), life.Key.Generation.ToString(), life.Key.LifeId.ToString(),
                life.Key.ShotId.ToString(), "terminal", attempt.ToString() });
            try
            {
                Vector3? camera = FindCamera(scene, point, attempt);
                if (camera == null)
                { row["Status"] = "INCOMPLETE"; row["Reason"] = "no collision-clear terminal vantage"; continue; }
                scene.ShowAllNodes = true; scene.SetFreeCamera(true);
                Vector3 facing = (point - camera.Value).Normalized();
                Vector3 right = Vector3.Cross(facing, Vector3.UnitY).Normalized();
                Set("_cameraPosition", camera.Value); Set("_cameraFacing", facing); Set("_cameraRight", right);
                Set("_cameraUp", Vector3.Cross(right, facing));
                Suppress(); var before = Capture("before");
                var depth = Depth(scene, point);
                foreach (var element in visual.Elements) element.Flags = flags[element];
                var enabled = Capture("enabled");
                Suppress(); var after = Capture("after");
                int changed = Different(before.Pixels, enabled.Pixels), unstable = Different(before.Pixels, after.Pixels);
                bool depthClear = depth.World >= depth.Fixture - .00001f;
                string status = unstable != 0 || scene.FrameCount != frame ? "INCOMPLETE"
                    : changed > 0 ? "PASS" : depthClear ? "FAIL" : "INCOMPLETE";
                row["Status"] = status; row["Camera"] = V(camera.Value); row["CollisionClear"] = true;
                row["WorldDepth"] = depth.World; row["FixtureDepth"] = depth.Fixture; row["CenterDepthClear"] = depthClear;
                row["SamplePixel"] = new { depth.X, depth.Y }; row["ChangedPixels"] = changed; row["UnstablePixels"] = unstable;
                row["EffectParticles"] = visual.Elements.Sum(e => e.Particles.Count);
                row["Captures"] = new[] { before.Metadata, enabled.Metadata, after.Metadata };
                if (status != "INCOMPLETE") Finished.Add(id);
            }
            catch (Exception ex)
            { row["Status"] = "INCOMPLETE"; row["Reason"] = ex.Message; }
            finally
            {
                foreach (var item in flags) item.Key.Flags = item.Value;
                foreach (var item in links) { item.Key.ModLink0 = item.Value.Item1; item.Key.ModLink1 = item.Value.Item2; }
                foreach (var item in draws) ShouldDraw.SetValue(item.Key, item.Value);
                scene.ShowAllNodes = allNodes; SpectatorMode.NoteFreeCamera(spectatorFreeCamera);
                for (int i = 0; i < CameraFields.Length; i++) typeof(Scene).GetField(CameraFields[i], Private)!.SetValue(scene, saved[i]);
                scene.OnDrawFrame(); scene.OnRenderFrame();
            }
            void Set(string name, Vector3 value) => typeof(Scene).GetField(name, Private)!.SetValue(scene, value);
            void Suppress()
            {
                foreach (var bomb in bombs) { ShouldDraw.SetValue(bomb, false); bomb.ModLink0 = bomb.ModLink1 = 0; }
                foreach (var element in elements) element.Flags &= ~EffElemFlags.DrawEnabled;
            }
            (byte[] Pixels, object Metadata) Capture(string arm)
            {
                scene.OnDrawFrame(); scene.OnRenderFrame();
                byte[] pixels = scene.ReadSceneTarget(out int width, out int height) ?? throw new IOException("Framebuffer read failed");
                string path = Path.GetFullPath(Path.Combine(Output + ".frames", prefix + "-" + arm + ".png"));
                if (!ScreenCapture.Save(scene, path)) throw new IOException("PNG capture failed");
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Terminal bomb probe GL error");
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
        var doc = new { Schema = 1, Source = Environment.GetEnvironmentVariable("PRIME_NET2_SOURCE") ?? "unknown", EngineSha256, EngineImageKind,
            EngineMvid = typeof(Scene).Module.ModuleVersionId.ToString(), ClientSlot = NetSession.LocalSlot,
            Scope = "existing accepted live replica pixels at a diagnostic clear camera; no prior-to-damage assertion",
            PixelThreshold = 2, Overflow, Rows = Rows.Values.ToArray(), DrawIndex,
            Lifecycles = Lifetimes.Values.Select(l => new { l.Key,
                BirthSequence = l.Birth?.Sequence, BirthFrame = l.Birth?.Frame,
                TerminalSequence = l.Terminal?.Sequence, TerminalFrame = l.Terminal?.Frame,
                AppliedBirthDrawIndex = l.BirthDrawIndex, AppliedTerminalDrawIndex = l.TerminalDrawIndex,
                l.BodyDrawOpportunities, l.BodyAttempts, l.MissingExpectedGhost,
                l.RetirementSnapshotFrame, l.RetirementSnapshotFrontier,
                l.RetirementSnapshotKeyAbsent, l.RetirementDrawIndex, l.RetirementWitnessStage }).ToArray() };
        File.WriteAllText(Output!, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
