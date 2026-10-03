#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using MphRead.Mods.Input;

namespace MphRead.Mods.Render;
internal static class ModernRenderBenchmark
{
    internal static int Run(string room, string output, int sampleCount = 1200)
    {
        try
        {
            if (sampleCount < 30 || sampleCount > 100000)
                throw new ArgumentOutOfRangeException(nameof(sampleCount), "Use 30–100000 samples.");
            var requestedBackend = GraphicsBackendPolicy.Requested;
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(960, 540);
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            bool modern = ModernGraphicsCompat.Active;
            var identity = modern ? ModernGraphicsCompat.DeviceIdentity
                : (GraphicsBackend.OpenGL, GraphicsApi.GetString(OpenTK.Graphics.OpenGL.StringName.Renderer),
                    GraphicsApi.GetString(OpenTK.Graphics.OpenGL.StringName.Version));
            if (modern) ModernGraphicsCompat.SetVSync(false);
            else window.Context.SwapInterval = 0;
            RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Extreme);
            if (ModernGraphicsCompat.Active) ModernGraphicsCompat.BeginPerformanceSample();
            ModernGraphicsCompat.PerformanceSample? startup = null;
            var scene = new Scene(window.FramebufferSize, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            var results = new List<object>();
            try
            {
                scene.AddPlayer(Hunter.Samus);
                scene.AddRoom(room, GameMode.Battle, playerCount: 1);
                scene.OnLoad();
                GameState.MatchTime = -1;
                for (int i = 0; i < 180; i++) scene.OnSimulationFrame();
                if (ModernGraphicsCompat.Active) startup = ModernGraphicsCompat.EndPerformanceSample();
                foreach (var size in new[] { new Vector2i(1920, 1080), new Vector2i(2560, 1440), new Vector2i(3840, 2160) })
                foreach (int scale in new[] { 75, 100 })
                {
                    RenderOptions.ResolutionScale = scale;
                    // Request the actual pixel size, accounting for a HiDPI window's
                    // logical-to-framebuffer scale. Always report the device's result.
                    var logical = window.ClientSize;
                    var actual = window.FramebufferSize;
                    window.ClientSize = new Vector2i(Math.Max(1, (int)Math.Round(size.X * logical.X / (double)actual.X)),
                        Math.Max(1, (int)Math.Round(size.Y * logical.Y / (double)actual.Y)));
                    NativeWindow.ProcessWindowEvents(false);
                    DesktopGraphicsSession.Resize(window);
                    scene.Size = window.FramebufferSize;
                    scene.OnResize();
                    var frames = new List<double>();
                    var submissions = new List<double>();
                    var presents = new List<double>();
                    ModernGraphicsCompat.PerformanceSample? warmup = null;
                    if (ModernGraphicsCompat.Active) ModernGraphicsCompat.BeginPerformanceSample();
                    for (int i = 0; i < 20 + sampleCount; i++)
                    {
                        if (i == 20 && ModernGraphicsCompat.Active)
                        {
                            warmup = ModernGraphicsCompat.EndPerformanceSample();
                            ModernGraphicsCompat.BeginPerformanceSample();
                        }
                        NativeWindow.ProcessWindowEvents(false);
                        DesktopGraphicsSession.Resize(window);
                        long start = Stopwatch.GetTimestamp();
                        scene.OnDrawFrame();
                        if (!scene.OnRenderFrame()) throw new InvalidOperationException("Scene stopped during benchmark.");
                        if (ModernGraphicsCompat.Active) ModernGraphicsCompat.SubmitPending();
                        double submit = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        GraphicsApi.Finish();
                        double completed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                        long presentStart = Stopwatch.GetTimestamp();
                        DesktopGraphicsSession.Present(window);
                        double presentMs = Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds;
                        scene.AfterRenderFrame();
                        if (i >= 20) { frames.Add(completed); submissions.Add(submit); presents.Add(presentMs); }
                    }
                    ModernGraphicsCompat.PerformanceSample? measurement = ModernGraphicsCompat.Active
                        ? ModernGraphicsCompat.EndPerformanceSample() : null;
                    var sorted = frames.Order().ToArray();
                    results.Add(new { requestedWidth = size.X, requestedHeight = size.Y, width = scene.Size.X, height = scene.Size.Y, scale, samples = frames.Count,
                        warmup, measurement,
                        retainedPackets = scene.RetainedRenderPacketCount,
                        retainedFrameRevision = scene.RetainedRenderFrameRevision,
                        averageCompletedMs = frames.Average(), cpuSubmissionMs = submissions.Average(),
                        averagePresentMs = presents.Average(),
                        averageSurfaceAcquireMs = measurement.HasValue && measurement.Value.SurfaceAcquisitions > 0
                            ? measurement.Value.SurfaceAcquireMs / measurement.Value.SurfaceAcquisitions : (double?)null,
                        p99CompletedMs = sorted[(int)Math.Ceiling(sorted.Length * .99) - 1],
                        p999CompletedMs = sorted[(int)Math.Ceiling(sorted.Length * .999) - 1],
                        onePercentLowFps = 1000 / sorted.Skip((int)(sorted.Length * .99)).Average(),
                        pointOnePercentLowFps = 1000 / sorted.Skip((int)(sorted.Length * .999)).Average(),
                        resources = ModernGraphicsCompat.Active ? ModernGraphicsCompat.LiveResources.ToString() : "OpenGL" });
                    Console.WriteLine($"RENDERBENCH {scene.Size.X}x{scene.Size.Y} scale={scale} completed={frames.Average():F2}ms submit={submissions.Average():F2}ms");
                }
            }
            finally { scene.DoCleanup(); scene.UnloadGl(); }
            string path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                requestedBackend = requestedBackend.ToString(), actualBackend = identity.Item1.ToString(),
                adapter = identity.Item2, driver = identity.Item3,
                platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                room, preset = "Extreme",
                notes = $"Frozen scene; 20 warmup + {sampleCount} samples. Completed time includes synchronous GPU completion and any surface-acquire pacing; present is measured separately. VSync is requested off on both backends, but modern actual PresentMode may fall back to Fifo. Upload submission time includes flushing preceding draws, not GPU transfer duration. Storage estimates cover tracked textures and pooled buffers, not driver VRAM. GPU timestamps unavailable: the pinned native C ABI does not expose timestamp-period conversion. Short runs are smoke checks; longer hardware runs required for release acceptance.",
                startup, results
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("RENDERBENCH PASS " + path);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("RENDERBENCH FAIL " + ex); return 1; }
    }
}
#endif
