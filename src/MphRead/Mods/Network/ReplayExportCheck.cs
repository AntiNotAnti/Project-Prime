using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using MphRead.Mods.Input;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
#if !ANDROID
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;
#endif

namespace MphRead.Mods.Network;

internal static class ReplayExportCheck
{
    private static string SourceHash(string path)
    { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    internal static int Run(string path, string output, bool ownerPresentation = false, bool channelCurves = false)
    {
        if (path == "synthetic")
        {
            string fixture = Path.Combine(Path.GetTempPath(), "prime-render-replay-" + Guid.NewGuid().ToString("N") + ".ppdemo");
            try
            {
                ReplayWorldCoverageCheck.Write(fixture, "MP3 PROVING GROUND", GameMode.Battle, new Vector3(0, 1, 0));
                return Run(fixture, output, ownerPresentation, channelCurves);
            }
            finally { File.Delete(fixture); }
        }

#if ANDROID
        return 1;
#else
        Directory.CreateDirectory(output);
        var settings = Render.DesktopGlContext.Settings(background: true);
        settings.ClientSize = new Vector2i(640, 480);
        using var window = new NativeWindow(settings); using var graphics = new MphRead.Mods.Render.DesktopGraphicsSession(window);
        var shell = new Scene(settings.ClientSize, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, initializeRuntime: false);
        bool previousCollision = ReplayCamera.TrackCollisionAvoidance;
        string originalSource = path;
        string originalHash = "";
        var exportMetrics = new List<object>();
        try
        {
            if (channelCurves) originalHash = SourceHash(path);
            if (ownerPresentation || channelCurves)
            {
                string copy = Path.Combine(output, channelCurves ? "curve-source.ppdemo" : "synthetic-owner-source.ppdemo");
                File.Copy(path, copy, overwrite: true); path = copy;
                if (ownerPresentation) WriteSyntheticOwnerPictures(path);
            }
            if (!DemoPlayback.Join(path)) throw new InvalidDataException(DemoPlayback.LastError);
            shell.OnSimulationFrame();
            if (DemoPlayback.LastFrame < (channelCurves ? 68u : 8u))
                throw new InvalidDataException(channelCurves
                    ? "Curve acceptance requires at least 68 frames for surrounding camera controls."
                    : "Export acceptance requires at least 8 frames.");
            uint start = Math.Min(ownerPresentation ? 12u : 350u,
                DemoPlayback.LastFrame - (channelCurves ? 36u : 8u)), end = start + 6;
            if (channelCurves)
            {
                if (start < 30 || end + 30 > DemoPlayback.LastFrame)
                    throw new InvalidDataException("Curve acceptance requires at least 68 frames for surrounding camera controls.");
                ReplayController.Seek(start, resume: false);
                while (ReplayController.IsSeeking) shell.OnSimulationFrame();
                ReplayCamera.SetMode(ReplayCameraMode.FirstPerson);
                shell.OnDrawFrame(); shell.OnRenderFrame();
                var anchor = DemoPlayback.PresentationScene!.CurveAcceptanceCamera();
                ReplayCamera.EnsureTrack(); ReplayCamera.Track.Clear();
                uint[] frames = { start - 30, start, start + 2, end + 30 };
                float[] fovs = { 1.1f, 1.2f, 1.4f, 1.15f };
                for (int i = 0; i < frames.Length; i++)
                {
                    var key = anchor with { Frame = frames[i], Position = anchor.Position + new Vector3(i * .01f, i * .006f, 0),
                        Rotation = (Quaternion.FromAxisAngle(Vector3.UnitY, i * .04f) * anchor.Rotation).Normalized(),
                        Fov = fovs[i], Roll = i * .025f,
                        Curves = new(ReplayCameraChannelMode.Smooth, ReplayCameraChannelEase.None,
                            ReplayCameraChannelMode.Smooth, ReplayCameraChannelEase.None,
                            ReplayCameraChannelMode.Smooth, ReplayCameraChannelEase.None) };
                    if (!ReplayCamera.Track.Put(key)) throw new InvalidDataException(ReplayCamera.Track.LastError);
                }
                if (!ReplayCamera.Track.Save(path)) throw new InvalidDataException(ReplayCamera.Track.LastError);
                ReplayCamera.TrackCollisionAvoidance = false;
            }
            foreach (int fps in Replay.ReplayExportRates.Supported)
            {
                string[]? reference = null;
                for (int pass = 0; pass < 2; pass++)
                {
                    string directory = Path.Combine(output, $"{fps}-{pass}");
                    string pattern = Path.Combine(directory, "frame_%08d.png"), movie = Path.Combine(directory, "replay.mp4");
                    var job = new ReplayVideoExportManifest(2, Path.GetFullPath(path), start, end, 1280, 720, fps,
                        true, false, channelCurves, pattern, movie,
                        $"-y -loglevel error -framerate {fps} -i \"{pattern}\" -c:v libx264 -pix_fmt yuv420p \"{movie}\"");
                    var expectedMode = channelCurves ? ReplayCameraMode.Free : ownerPresentation ? ReplayCameraMode.FirstPerson : ReplayCameraMode.Orbit;
                    ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation); ReplayCamera.SetMode(expectedMode);
                    if (channelCurves)
                    {
                        ReplayCamera.PlayTrack = true;
                        // A paused ordinary preview and the export use the same
                        // frozen world and evaluator.
                        ReplayController.Seek(start, resume: false);
                        while (ReplayController.IsSeeking) shell.OnSimulationFrame();
                        var preview = DemoPlayback.PresentationScene!;
                        shell.OnDrawFrame(); shell.OnRenderFrame();
                        if (!ReplayCamera.Track.Sample(start, out var previewKey, ReplayCamera.TrackConstantSpeed)
                            || !preview.CurveAcceptanceMatches(previewKey)) throw new InvalidDataException("Live curve preview differs from the canonical evaluator.");
                    }
                    long exportBegan = Stopwatch.GetTimestamp();
                    if (!ReplayVideoExporter.Start(job)) throw new InvalidDataException(ReplayVideoExporter.Status);
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    while (ReplayVideoExporter.Active && DateTime.UtcNow < deadline)
                    {
                        if (ReplayVideoExporter.State == ReplayExportState.Encoding) { Thread.Sleep(5); continue; }
                        shell.OnSimulationFrame();
                        if (ReplayController.IsSeeking) continue;
                        Scene scene = DemoPlayback.PresentationScene!;
                        string before = ReplayStateHash.Compute(scene);
                        shell.OnDrawFrame(); shell.OnRenderFrame();
                        if (channelCurves && (!ReplayCamera.Track.Sample(scene.ReplayPresentationFrame, out var curve, ReplayCamera.TrackConstantSpeed)
                            || !scene.CurveAcceptanceMatches(curve)))
                            throw new InvalidDataException("Rendered orientation/lens curve differs from the preview evaluator.");
                        if (ownerPresentation && ReplayOwnerExport.Ready && scene.OwnerExportPreparedForDraw
                            && !scene.ReplayOwnerProvenance.StartsWith("Owner captured", StringComparison.Ordinal))
                            throw new InvalidDataException("The exact first-person export did not consume the frozen owner picture: " + scene.ReplayOwnerProvenance);
                        if (ReplayStateHash.Compute(scene) != before) throw new InvalidDataException("Rendering changed gameplay state.");
                        ReplayVideoExporter.AfterSceneDraw(scene);
                        if (pass == 1) Thread.Sleep(3); // intentionally change wall-clock/render cadence
                    }
                    if (ReplayVideoExporter.State != ReplayExportState.Completed || !File.Exists(movie))
                        throw new InvalidDataException("Export did not complete: " + ReplayVideoExporter.Status);
                    if (ReplayCamera.Mode != expectedMode || ReplayCamera.Profile != ReplayPresentationProfile.Presentation)
                        throw new InvalidDataException("Export did not restore the replay camera.");
                    int expected = checked((int)new ReplayExportSampler(start, end, fps).Count);
                    if (ReplayVideoExporter.FramesWritten != expected) throw new InvalidDataException("Wrong export sample count.");
                    var files = Directory.GetFiles(directory, "frame_*.png").OrderBy(p => p).ToArray();
                    if (files.Length != expected)
                        throw new InvalidDataException("PNG sequence contains missing or stale frames; use a fresh acceptance output directory.");
                    string[] hashes = files.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
                    foreach (string file in files)
                    {
                        byte[] png = File.ReadAllBytes(file);
                        if (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) != 1280
                            || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) != 720)
                            throw new InvalidDataException("Export used window dimensions instead of the requested native target.");
                    }
                    if (reference != null && !reference.SequenceEqual(hashes)) throw new InvalidDataException($"{fps} FPS export depends on prior seek or wall-clock cadence.");
                    if (fps is 120 or 240 && hashes.Distinct().Count() <= 7) throw new InvalidDataException($"{fps} FPS duplicated 60 Hz pictures.");
                    reference = hashes;
                    var metrics = new { Fps = fps, Pass = pass, Frames = expected,
                        WallMilliseconds = Stopwatch.GetElapsedTime(exportBegan).TotalMilliseconds,
                        ReplayVideoExporter.ReadbackMilliseconds, ReplayVideoExporter.PngEncodeMilliseconds,
                        ReplayVideoExporter.MaximumPngFrames };
                    exportMetrics.Add(metrics);
                    Console.WriteLine("[replayexport] metrics " + JsonSerializer.Serialize(metrics));
                }
            }
            File.WriteAllText(Path.Combine(output, "export-metrics.json"), JsonSerializer.Serialize(new
            {
                Schema = 1, SourceSha256 = SourceHash(path), ChannelCurves = channelCurves, OwnerPresentation = ownerPresentation,
                Scope = "Local diagnostic CPU/driver readback and PNG encode/write wall times; no isolated GPU execution or physical-display pacing claim.",
                Rows = exportMetrics
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (channelCurves)
            {
                if (originalHash != SourceHash(originalSource))
                    throw new InvalidDataException("Curve acceptance changed its original source recording.");
                Console.WriteLine("[replayexport] CURVES PASS: opt-in SQUAD/spherical-cubic orientation and monotone FOV/roll, live preview/export evaluator equality, repeated identical 24/30/48/60/90/120/144/240 FPS PNGs and real video encoding, fractional samples and unchanged gameplay/source hashes.");
                return 0;
            }
            if (ownerPresentation)
            {
                Console.WriteLine("[replayexport] OWNER PASS: synthetic 240Hz camera/gun/animation/cosmetic sidecar against real ROM assets; identical repeated 24/30/48/60/90/120/144/240 FPS PNGs, real video encoding, cold seek/cadence independence and gameplay hash invariance.");
                return 0;
            }
            // A queue owns the renderer and encoder through successful process exit.
            ReplayExportQueue.ClearPending();
            var queued = new ReplayVideoExportManifest[3];
            for (int i = 0; i < queued.Length; i++)
            {
                string directory = Path.Combine(output, "queue-" + i);
                string pattern = Path.Combine(directory, "frame_%08d.png"), movie = Path.Combine(directory, "replay.mp4");
                queued[i] = new(2, Path.GetFullPath(path), start, end, 640, 360, 60, true, false, false, pattern, movie,
                    $"-y -loglevel error -framerate 60 -i \"{pattern}\" -c:v libx264 -pix_fmt yuv420p \"{movie}\"");
                if (i == 2) queued[i] = queued[i] with { Segments = new[] {
                    new ReplayVideoSegment(start, start + 2, "A", ReplaySegmentCamera.Chase),
                    new ReplayVideoSegment(start + 4, end, "B", ReplaySegmentCamera.FirstPerson) } };
                ReplayExportQueue.Enqueue(queued[i]);
            }
            var queueDeadline = DateTime.UtcNow.AddSeconds(60);
            bool observedEncoding = false;
            while ((ReplayVideoExporter.Active || ReplayExportQueue.PendingCount > 0) && DateTime.UtcNow < queueDeadline)
            {
                ReplayExportQueue.Pump();
                if (ReplayVideoExporter.State == ReplayExportState.Encoding)
                { observedEncoding = true; if (ReplayVideoExporter.Rendering) throw new InvalidDataException("Renderer overlaps encoder."); Thread.Sleep(5); continue; }
                shell.OnSimulationFrame(); if (ReplayController.IsSeeking) continue;
                shell.OnDrawFrame(); shell.OnRenderFrame(); ReplayVideoExporter.AfterSceneDraw(DemoPlayback.PresentationScene!);
            }
            if (!observedEncoding || queued.Any(job => !File.Exists(job.SuggestedOutput))
                || ReplayVideoExporter.Active || ReplayExportQueue.PendingCount != 0 || ReplayVideoExporter.FramesWritten != 6)
                throw new InvalidDataException("Export queue/reel did not drain successfully: " + ReplayVideoExporter.Status);
            ReplayCamera.SetMode(ReplayCameraMode.Orbit);
            if (!ReplayVideoExporter.Start(queued[0])) throw new InvalidDataException(ReplayVideoExporter.Status);
            ReplayVideoExporter.Cancel();
            if (!SpinWait.SpinUntil(() => !ReplayVideoExporter.Active, TimeSpan.FromSeconds(5)))
                throw new InvalidDataException("Cancelled PNG worker did not release output ownership.");
            if (ReplayVideoExporter.Active || ReplayCamera.Mode != ReplayCameraMode.Orbit
                || ReplayVideoExporter.State != ReplayExportState.Cancelled) throw new InvalidDataException("Cancelled rendering retained state.");
            // Cancellation releases export ownership, but its requested seek
            // still belongs to the replay player. Let that real restore finish
            // before starting the next independent render job.
            for (int warm = 0; warm < 10000 && ReplayController.IsSeeking; warm++) shell.OnSimulationFrame();
            if (ReplayController.IsSeeking || !DemoPlayback.IsActive
                || DemoPlayback.PresentationScene is not { } resumed || !DemoPlayback.Owns(resumed)
                || DemoPlayback.PlaybackPath == null || !ReplayPathComparer.Same(DemoPlayback.PlaybackPath, path))
                throw new InvalidDataException("Cancelled render seek did not restore ready playback for the same replay.");
            // Native 4K composite includes HUD without a 4K window.
            ReplayCamera.SetProfile(ReplayPresentationProfile.Faithful); ReplayCamera.SetMode(ReplayCameraMode.FirstPerson);
            string hudDirectory = Path.Combine(output, "4k-hud");
            var hud = new ReplayVideoExportManifest(2, Path.GetFullPath(path), start, start + 1, 3840, 2160, 60,
                false, false, false, Path.Combine(hudDirectory, "frame_%08d.png"), Path.Combine(hudDirectory, "replay.mp4"),
                $"-y -loglevel error -framerate 60 -i \"{Path.Combine(hudDirectory, "frame_%08d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{Path.Combine(hudDirectory, "replay.mp4")}\"");
            if (!ReplayVideoExporter.Start(hud)) throw new InvalidDataException(ReplayVideoExporter.Status);
            var hudDeadline = DateTime.UtcNow.AddSeconds(60);
            while (ReplayVideoExporter.Active && DateTime.UtcNow < hudDeadline)
            {
                if (ReplayVideoExporter.State == ReplayExportState.Encoding) { Thread.Sleep(5); continue; }
                shell.OnSimulationFrame(); if (ReplayController.IsSeeking) continue;
                shell.OnDrawFrame(); shell.OnRenderFrame(); ReplayVideoExporter.AfterSceneDraw(DemoPlayback.PresentationScene!);
            }
            if (ReplayVideoExporter.State != ReplayExportState.Completed) throw new InvalidDataException(ReplayVideoExporter.Status);
            byte[] composite = File.ReadAllBytes(Path.Combine(hudDirectory, "frame_00000000.png"));
            if (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(composite.AsSpan(16)) != 3840
                || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(composite.AsSpan(20)) != 2160)
                throw new InvalidDataException("HUD composite did not use the 4K offscreen target.");
            Console.WriteLine("[replayexport] PASS: native 720p/4K HUD targets, repeated identical 24/30/48/60/90/120/144/240 FPS samples, fractional frames, gameplay invariance, seek/cadence independence, serialized three-job queue, multi-segment reel and render cancellation.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[replayexport] FAIL: " + ex); return 1; }
        finally { ReplayVideoExporter.Cancel(); DemoPlayback.Stop(); ReplayCamera.TrackCollisionAvoidance = previousCollision; shell.DoCleanup(); shell.UnloadGl(); }
#endif
    }

#if !ANDROID
    private static void WriteSyntheticOwnerPictures(string path)
    {
        using var replica = new PassiveReplayScene(path, new Vector2i(1280, 720));
        var track = new ReplayOwnerTrack(128);
        while (replica.Session.CurrentFrame <= 20 && replica.Step())
        {
            var player = replica.Scene.Players.Main;
            if (replica.Session.CurrentFrame < 10 || replica.State.Match is not { } match
                || !replica.State.TryGetPlayer(player.SlotIndex, out var state) || state.Health == 0) continue;
            Vector3 facing = player.CameraInfo.Facing;
            if (!float.IsFinite(facing.LengthSquared) || facing.LengthSquared < .0001f) facing = Vector3.UnitZ;
            facing = facing.Normalized(); var right = Vector3.Cross(facing, Vector3.UnitY).Normalized();
            var up = Vector3.Cross(right, facing).Normalized();
            for (int i = 0; i < 4; i++)
            {
                double frame = replica.Session.CurrentFrame + i / 4d;
                Vector3 eye = player.CameraInfo.Position + right * (float)(Math.Sin(frame * .2) * .05);
                var pose = new ReplayOwnerPose(replica.Session.RecordingPresentationFrame(frame), frame / 60,
                    match.MatchId, match.AuthorityEpoch, (byte)player.SlotIndex, state.SlotGeneration, state.LifeId,
                    Matrix4.LookAt(eye, eye + facing, up), MathHelper.DegreesToRadians(78), MathHelper.DegreesToRadians(78),
                    eye + facing * .4f + right * .2f - up * .2f, facing, up)
                    { Aspect = 16f / 9, Visual = player.ModCaptureOwnerVisual() };
                if (!track.Add(pose)) throw new InvalidDataException("Synthetic owner picture was invalid.");
            }
        }
        if (track.Count < 32) throw new InvalidDataException("Insufficient owner fixture pictures.");
        track.Save(path);
    }
#endif
}
