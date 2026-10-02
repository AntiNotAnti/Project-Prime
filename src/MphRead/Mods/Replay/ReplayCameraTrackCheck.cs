using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay
{
    internal static class ReplayCameraTrackCheck
    {
        public static void Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "replay-camera-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string replay = Path.Combine(directory, "sample.ppdemo");
            try
            {
                byte[] source = { 1, 2, 3, 4 };
                File.WriteAllBytes(replay, source);
                var track = new ReplayCameraTrack();
                var start = new ReplayCameraKeyframe(10, Vector3.Zero, Quaternion.Identity, 1, -1);
                var end = new ReplayCameraKeyframe(30, new Vector3(10, 4, 2), Quaternion.FromAxisAngle(Vector3.UnitY, MathF.PI / 2), 2, 3);
                Require(track.Put(end) && track.Put(start), "insert");
                Require(track.Keys[0] == start, "sorted keys");
                Require(track.Sample(20, out var mid) && (mid.Position - new Vector3(5, 2, 1)).Length < 0.0001f && Math.Abs(mid.Fov - 1.5f) < 0.0001f, "linear position/FOV");
                var facing = Vector3.Transform(-Vector3.UnitZ, mid.Rotation);
                Require((facing - new Vector3(-MathF.Sqrt(0.5f), 0, -MathF.Sqrt(0.5f))).Length < 0.0001f, "spherical orientation");
                Require(mid.LookAtSlot == -1 && track.Sample(30, out var last) && last.LookAtSlot == 3, "look-at boundary");
                Require(track.Sample(20, out var paused) && paused == mid, "paused frame stable");
                var held = new ReplayCameraTrack();
                Require(held.Put(start with { Interpolation = ReplayCameraInterpolation.Hold })
                    && held.Put(end) && held.Sample(29, out var heldPose)
                    && heldPose.Position == start.Position
                    && held.Sample(30, out heldPose) && heldPose.Position == end.Position,
                    "hold interpolation remains fixed until the destination key");
                Require(track.Sample(20, out var constantMid, constantSpeed: true)
                    && float.IsFinite(constantMid.Position.X)
                    && float.IsFinite(constantMid.Position.Y)
                    && float.IsFinite(constantMid.Position.Z),
                    "constant-speed spline sample finite");
                Require(track.TrySegmentFrom(10, constantSpeed: true, out var segment)
                    && segment.EndFrame == 30 && segment.Seconds > 0
                    && segment.Distance > 0 && segment.AverageSpeed > 0 && segment.PeakSpeed > 0,
                    "camera segment diagnostics expose duration, distance and speed");

                var continuous = new ReplayCameraTrack();
                var c0 = new ReplayCameraKeyframe(0, Vector3.Zero, Quaternion.Identity, 1);
                var c1 = c0 with { Frame = 60, Position = new Vector3(10, 0, 0) };
                var c2 = c0 with { Frame = 120, Position = new Vector3(20, 0, 0) };
                ReplayCameraKeyframe beforeKey = default;
                ReplayCameraKeyframe atKey = default;
                ReplayCameraKeyframe afterKey = default;
                Require(continuous.Put(c0) && continuous.Put(c1) && continuous.Put(c2)
                    && continuous.Sample(59, out beforeKey, constantSpeed: true)
                    && continuous.Sample(60, out atKey, constantSpeed: true)
                    && continuous.Sample(61, out afterKey, constantSpeed: true),
                    "continuous spline fixture");
                float intoKey = (atKey.Position - beforeKey.Position).Length;
                float outOfKey = (afterKey.Position - atKey.Position).Length;
                Require(intoKey > 0.01f && outOfKey > 0.01f
                    && Math.Abs(intoKey - outOfKey) / Math.Max(intoKey, outOfKey) < 0.35f,
                    "spline easing preserves velocity through interior keyframes");
                Require(track.Sample(0, out var first) && first == start with { Frame = 0 }
                    && track.Sample(uint.MaxValue, out last) && last == end with { Frame = uint.MaxValue },
                    "endpoint pose clamps while retaining requested presentation frame");
                foreach (Vector3 direction in new[] { Vector3.UnitX, -Vector3.UnitZ, new Vector3(1, 2, 3).Normalized(), Vector3.UnitY })
                    Require((Vector3.Transform(-Vector3.UnitZ, ReplayCameraTrack.FacingRotation(direction)) - direction).Length < 0.0001f, "capture orientation");
                Require(track.Save(replay), "save");
                byte[] good = File.ReadAllBytes(replay + ".camera");
                var loaded = new ReplayCameraTrack();
                Require(loaded.Load(replay) && loaded.Keys.SequenceEqual(track.Keys), "round trip");
                Require(File.ReadAllBytes(replay).SequenceEqual(source), "replay unchanged");
                Require(loaded.Put(start with { Position = Vector3.UnitX }) && loaded.Keys.Count == 2, "replace frame");
                Require(!loaded.Put(start with { Fov = float.NaN }) && !loaded.Put(start with { Rotation = default }), "finite normalized validation");
                var bulk = new ReplayCameraTrack();
                Require(bulk.Put(start) && bulk.Put(start with { Frame = 20 }) && bulk.Put(end),
                    "bulk removal fixture");
                Require(bulk.RemoveMany(new uint[] { 10, 30 }) && bulk.Keys.Count == 1 && bulk.Keys[0].Frame == 20,
                    "bulk removal deletes every selected key");
                Require(!bulk.RemoveMany(new uint[] { 20, 99 }) && bulk.Keys.Count == 1 && bulk.Keys[0].Frame == 20,
                    "bulk removal is all-or-nothing when a selected frame is missing");
                loaded.Clear();
                for (uint i = 0; i < ReplayCameraTrack.MaxKeys; i++) Require(loaded.Put(start with { Frame = i }), "capacity insert");
                Require(!loaded.Put(start with { Frame = 100 }) && loaded.Put(start), "capacity and replacement");
                Require(loaded.Remove(10) && !loaded.Remove(100), "remove");
                byte[] damaged = (byte[])good.Clone(); damaged[25] ^= 1;
                File.WriteAllBytes(replay + ".camera", damaged);
                Require(!loaded.Load(replay) && loaded.Keys.Count == 0, "checksum rejects entire track");
                File.WriteAllBytes(replay + ".camera", good[..12]);
                Require(!loaded.Load(replay), "truncation");
                File.WriteAllBytes(replay + ".camera", new byte[10000]);
                Require(!loaded.Load(replay), "bounded size");
                File.WriteAllBytes(replay + ".camera", good);
                File.AppendAllText(replay, "changed");
                Require(!loaded.Load(replay), "source binding");

                string virtualClip = Path.Combine(directory, "stable.ppclip");
                File.WriteAllText(virtualClip, JsonSerializer.Serialize(new ReplayVirtualClipDocument(
                    1, replay, 10, 30, DateTime.UtcNow, "Stable clip")));
                var virtualTrack = new ReplayCameraTrack();
                Require(virtualTrack.Put(start) && virtualTrack.Save(virtualClip), "virtual track save");
                string disposableCache = Path.Combine(directory, ".virtual-cache", "stable.ppdemo");
                Directory.CreateDirectory(Path.GetDirectoryName(disposableCache)!);
                File.WriteAllBytes(disposableCache, new byte[] { 9, 8, 7 });
                File.Delete(disposableCache);
                var rebuiltTrack = new ReplayCameraTrack();
                Require(rebuiltTrack.Load(virtualClip) && rebuiltTrack.Keys.SequenceEqual(virtualTrack.Keys),
                    "virtual camera sidecar did not survive cache deletion/rebuild");
                File.WriteAllText(virtualClip, JsonSerializer.Serialize(new ReplayVirtualClipDocument(
                    1, replay, 11, 30, DateTime.UtcNow, "Changed range")));
                Require(!rebuiltTrack.Load(virtualClip), "virtual camera track accepted a different clip range");
                Require(!loaded.Save(Path.Combine(directory, "missing.ppdemo")), "missing source");
                Require(Directory.GetFiles(directory, "*.tmp").Length == 0, "temporary cleanup");
                ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation);
                ReplayCamera.PlayTrack = true;
                ReplayCamera.SetProfile(ReplayPresentationProfile.Faithful);
                Require(!ReplayCamera.PlayTrack, "faithful disables track");
                Console.WriteLine("[replaycheck] camera: interpolation, persistence, bounds, corruption and profiles passed");
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("camera: " + message); }
    }
}
