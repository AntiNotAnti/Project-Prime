using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
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
    internal static int Run(string path, string output)
    {
        if (path == "synthetic")
        {
            string fixture = Path.Combine(Path.GetTempPath(), "prime-render-replay-" + Guid.NewGuid().ToString("N") + ".ppdemo");
            try
            {
                ReplayWorldCoverageCheck.Write(fixture, "MP3 PROVING GROUND", GameMode.Battle, new Vector3(0, 1, 0));
                return Run(fixture, output);
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
        try
        {
            if (!DemoPlayback.Join(path)) throw new InvalidDataException(DemoPlayback.LastError);
            shell.OnSimulationFrame();
            uint start = Math.Min(350, DemoPlayback.LastFrame - 8), end = start + 6;
            foreach (int fps in Replay.ReplayExportRates.Supported)
            {
                string[]? reference = null;
                for (int pass = 0; pass < 2; pass++)
                {
                    string directory = Path.Combine(output, $"{fps}-{pass}");
                    string pattern = Path.Combine(directory, "frame_%08d.png"), movie = Path.Combine(directory, "replay.mp4");
                    var job = new ReplayVideoExportManifest(2, Path.GetFullPath(path), start, end, 1280, 720, fps,
                        true, false, false, pattern, movie,
                        $"-y -loglevel error -framerate {fps} -i \"{pattern}\" -c:v libx264 -pix_fmt yuv420p \"{movie}\"");
                    ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation); ReplayCamera.SetMode(ReplayCameraMode.Orbit);
                    if (!ReplayVideoExporter.Start(job)) throw new InvalidDataException(ReplayVideoExporter.Status);
                    if (!ReplayVideoExporter.SuppressGameHud)
                        throw new InvalidDataException("Clean export did not suppress the game HUD while rendering.");
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    while (ReplayVideoExporter.Active && DateTime.UtcNow < deadline)
                    {
                        if (ReplayVideoExporter.State == ReplayExportState.Encoding) { Thread.Sleep(5); continue; }
                        shell.OnSimulationFrame();
                        if (ReplayController.IsSeeking) continue;
                        Scene scene = DemoPlayback.PresentationScene!;
                        string before = ReplayStateHash.Compute(scene);
                        shell.OnDrawFrame(); shell.OnRenderFrame();
                        if (ReplayStateHash.Compute(scene) != before) throw new InvalidDataException("Rendering changed gameplay state.");
                        // Match Android's foreground call shape: the shell has
                        // delegated drawing, but has no replay sample timestamp.
                        if (!double.IsNaN(shell.ReplayPresentationFrame))
                            throw new InvalidDataException("The export regression requires an unstamped foreground shell.");
                        ReplayVideoExporter.AfterSceneDraw(shell);
                        if (pass == 1) Thread.Sleep(3); // intentionally change wall-clock/render cadence
                    }
                    if (ReplayVideoExporter.State != ReplayExportState.Completed || !File.Exists(movie))
                        throw new InvalidDataException("Export did not complete: " + ReplayVideoExporter.Status);
                    if (ReplayVideoExporter.SuppressGameHud)
                        throw new InvalidDataException("Completed export retained game HUD suppression.");
                    if (ReplayCamera.Mode != ReplayCameraMode.Orbit || ReplayCamera.Profile != ReplayPresentationProfile.Presentation)
                        throw new InvalidDataException("Export did not restore the replay camera.");
                    int expected = checked((int)new ReplayExportSampler(start, end, fps).Count);
                    if (ReplayVideoExporter.FramesWritten != expected) throw new InvalidDataException("Wrong export sample count.");
                    var files = Directory.GetFiles(directory, "frame_*.png").OrderBy(p => p).ToArray();
                    string[] hashes = files.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
                    foreach (string file in files)
                    {
                        byte[] png = File.ReadAllBytes(file);
                        if (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) != 1280
                            || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) != 720)
                            throw new InvalidDataException("Export used window dimensions instead of the requested native target.");
                    }
                    if (reference != null && !reference.SequenceEqual(hashes)) throw new InvalidDataException($"{fps} FPS export depends on prior seek or wall-clock cadence.");
                    if (fps == 120 && hashes.Distinct().Count() <= 7) throw new InvalidDataException("120 FPS duplicated 60 Hz pictures.");
                    reference = hashes;
                }
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
                shell.OnDrawFrame(); shell.OnRenderFrame(); ReplayVideoExporter.AfterSceneDraw(shell);
            }
            if (!observedEncoding || queued.Any(job => !File.Exists(job.SuggestedOutput))
                || ReplayVideoExporter.Active || ReplayExportQueue.PendingCount != 0 || ReplayVideoExporter.FramesWritten != 6)
                throw new InvalidDataException("Export queue/reel did not drain successfully: " + ReplayVideoExporter.Status);
            ReplayCamera.SetMode(ReplayCameraMode.Orbit);
            if (!ReplayVideoExporter.Start(queued[0])) throw new InvalidDataException(ReplayVideoExporter.Status);
            ReplayVideoExporter.Cancel();
            if (ReplayVideoExporter.Active || ReplayCamera.Mode != ReplayCameraMode.Orbit
                || ReplayVideoExporter.State != ReplayExportState.Cancelled || ReplayVideoExporter.SuppressGameHud)
                throw new InvalidDataException("Cancelled rendering retained state.");
            // Native 4K composite includes HUD without a 4K window.
            ReplayCamera.SetProfile(ReplayPresentationProfile.Faithful); ReplayCamera.SetMode(ReplayCameraMode.FirstPerson);
            string hudDirectory = Path.Combine(output, "4k-hud");
            var hud = new ReplayVideoExportManifest(2, Path.GetFullPath(path), start, start + 1, 3840, 2160, 60,
                false, false, false, Path.Combine(hudDirectory, "frame_%08d.png"), Path.Combine(hudDirectory, "replay.mp4"),
                $"-y -loglevel error -framerate 60 -i \"{Path.Combine(hudDirectory, "frame_%08d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{Path.Combine(hudDirectory, "replay.mp4")}\"");
            if (!ReplayVideoExporter.Start(hud)) throw new InvalidDataException(ReplayVideoExporter.Status);
            if (ReplayVideoExporter.SuppressGameHud)
                throw new InvalidDataException("HUD export unexpectedly suppressed the game HUD.");
            var hudDeadline = DateTime.UtcNow.AddSeconds(60);
            while (ReplayVideoExporter.Active && DateTime.UtcNow < hudDeadline)
            {
                if (ReplayVideoExporter.State == ReplayExportState.Encoding) { Thread.Sleep(5); continue; }
                shell.OnSimulationFrame(); if (ReplayController.IsSeeking) continue;
                shell.OnDrawFrame(); shell.OnRenderFrame(); ReplayVideoExporter.AfterSceneDraw(shell);
            }
            if (ReplayVideoExporter.State != ReplayExportState.Completed) throw new InvalidDataException(ReplayVideoExporter.Status);
            byte[] composite = File.ReadAllBytes(Path.Combine(hudDirectory, "frame_00000000.png"));
            if (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(composite.AsSpan(16)) != 3840
                || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(composite.AsSpan(20)) != 2160)
                throw new InvalidDataException("HUD composite did not use the 4K offscreen target.");
            Console.WriteLine("[replayexport] PASS: foreground-shell capture, native 720p/4K HUD targets, repeated identical 24/30/48/60/90/120/144 FPS samples, fractional frames, gameplay invariance, seek/cadence independence, serialized three-job queue, multi-segment reel and render cancellation.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[replayexport] FAIL: " + ex); return 1; }
        finally { ReplayVideoExporter.Cancel(); DemoPlayback.Stop(); shell.DoCleanup(); shell.UnloadGl(); }
#endif
    }
}
