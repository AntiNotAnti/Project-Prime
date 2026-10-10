using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render
{
    public static class OpenGlBackendCheck
    {
        public static int Run()
        {
            int failures = 0;
            foreach (GraphicsPlatform platform in Enum.GetValues<GraphicsPlatform>())
            {
                CheckDefault(platform, GraphicsBackend.OpenGL, ref failures);
                CheckSupported(platform, GraphicsBackend.Auto, true, ref failures);
                CheckSupported(platform, GraphicsBackend.OpenGL, true, ref failures);
                CheckSupported(platform, GraphicsBackend.DirectX12, false, ref failures);
                CheckSupported(platform, GraphicsBackend.Vulkan, false, ref failures);
                CheckSupported(platform, GraphicsBackend.Metal, false, ref failures);
                Check(GraphicsBackendPolicy.Resolve(platform, GraphicsBackend.Auto) == GraphicsBackend.OpenGL
                    && GraphicsBackendPolicy.Resolve(platform, GraphicsBackend.OpenGL) == GraphicsBackend.OpenGL
                    && GraphicsBackendPolicy.ModernBackendsFor(platform).Count == 0,
                    $"{platform} resolves Auto to OpenGL and exposes no modern choices", ref failures);
            }

            CheckAlias("auto", GraphicsBackend.Auto, ref failures);
            CheckAlias("d3d12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("directx12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("vk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("moltenvk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("metal", GraphicsBackend.Metal, ref failures);
            CheckAlias("opengl", GraphicsBackend.OpenGL, ref failures);
            GraphicsBackend[] choices = GraphicsBackendPolicy.RendererChoices();
            Check(choices.Length == 1 && choices[0] == GraphicsBackend.OpenGL,
                "only OpenGL is exposed to the settings UI", ref failures);

            foreach (string retired in new[] { "dx12", "vulkan", "metal" })
            {
                bool refused = false;
                try { GraphicsBackendPolicy.Configure(retired); }
                catch (PlatformNotSupportedException) { refused = true; }
                Check(refused && GraphicsBackendPolicy.Resolved == GraphicsBackend.OpenGL,
                    $"retired renderer '{retired}' cannot become active", ref failures);
            }
            GraphicsBackendPolicy.Configure("auto");
            Check(GraphicsBackendPolicy.Resolved == GraphicsBackend.OpenGL
                && GraphicsBackendPolicy.Requested == GraphicsBackend.OpenGL
                && !GraphicsBackendPolicy.ModernGameplayRequested
                && !GraphicsBackendPolicy.StartupGuardMatches("vulkan", GraphicsBackend.Vulkan),
                "Auto and stale startup guards cannot select a modern renderer", ref failures);
            GraphicsBackendPolicy.Configure("opengl");
            CheckGeometry(ref failures);
            CheckSurfaceLifecycle(ref failures);
            CheckLowLatencyFoundation(ref failures);
#if !MPHREAD_SERVER
            CheckLegacyUniformCache(ref failures);
#endif

            Console.WriteLine(failures == 0
                ? "RENDERBACKENDS all policy cases pass"
                : $"RENDERBACKENDS {failures} case(s) FAILED");
            return failures;
        }

        private static void CheckDefault(GraphicsPlatform platform, GraphicsBackend expected, ref int failures)
        {
            GraphicsBackend actual = GraphicsBackendPolicy.DefaultFor(platform);
            Check(actual == expected, $"{platform} default = {expected} (actual {actual})", ref failures);
        }

        private static void CheckSupported(GraphicsPlatform platform, GraphicsBackend backend,
            bool expected, ref int failures)
        {
            bool actual = GraphicsBackendPolicy.IsSupported(platform, backend);
            Check(actual == expected, $"{platform} {(expected ? "supports" : "rejects")} {backend}", ref failures);
        }

        private static void CheckAlias(string value, GraphicsBackend expected, ref int failures)
        {
            bool parsed = GraphicsBackendPolicy.TryParse(value, out GraphicsBackend actual);
            Check(parsed && actual == expected, $"'{value}' parses as {expected}", ref failures);
        }

        private static void CheckGeometry(ref int failures)
        {
            var batch = new LegacyGeometryBatch();
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.Quads);
            for (int i = 0; i < 4; i++)
            {
                batch.AddVertex(new OpenTK.Mathematics.Vector3(i, 0, 0),
                    OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                    OpenTK.Mathematics.Vector3.Zero, hasOwnColor: true);
            }
            batch.End();
            Check(batch.VertexCount == 4
                && batch.TriIndices.Count == 6
                && batch.TriIndices[0] == 0 && batch.TriIndices[1] == 1 && batch.TriIndices[2] == 2
                && batch.TriIndices[3] == 0 && batch.TriIndices[4] == 2 && batch.TriIndices[5] == 3,
                "legacy quad becomes two indexed triangles", ref failures);
            Check(batch.Vertices.Count == 4 * LegacyGeometryBatch.FloatsPerVertex,
                "legacy vertex layout is stable", ref failures);

            batch.Clear();
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.TriangleStrip);
            for (int i = 0; i < 4; i++)
            {
                batch.AddVertex(new OpenTK.Mathematics.Vector3(i, 0, 0),
                    OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                    OpenTK.Mathematics.Vector3.Zero, hasOwnColor: false);
            }
            batch.End();
            Check(batch.TriIndices.Count == 6
                && batch.TriIndices[0] == 0 && batch.TriIndices[1] == 1 && batch.TriIndices[2] == 2
                && batch.TriIndices[3] == 2 && batch.TriIndices[4] == 1 && batch.TriIndices[5] == 3,
                "triangle strip preserves alternating winding", ref failures);

            batch.Clear();
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.LineLoop);
            for (int i = 0; i < 3; i++)
            {
                batch.AddVertex(new OpenTK.Mathematics.Vector3(i, 0, 0),
                    OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                    OpenTK.Mathematics.Vector3.Zero, hasOwnColor: false);
            }
            batch.End();
            Check(batch.LineIndices.Count == 6
                && batch.LineIndices[4] == 2 && batch.LineIndices[5] == 0,
                "line loop closes explicitly", ref failures);
            batch.Clear();
            batch.Begin(OpenTK.Graphics.OpenGL.PrimitiveType.Lines);
            for (int i = 0; i < 5; i++) batch.AddVertex(new(i, 0, 0),
                OpenTK.Mathematics.Vector4.One, OpenTK.Mathematics.Vector3.UnitZ,
                OpenTK.Mathematics.Vector3.Zero, true);
            batch.End();
            Check(batch.LineIndices.Count == 4 && batch.LineIndices[3] == 3,
                "independent lines ignore incomplete trailing vertex", ref failures);
        }

        private static void CheckSurfaceLifecycle(ref int failures)
        {
            Check(!ModernSurfaceLifecyclePolicy.CanConfigure(0, 720, true)
                    && !ModernSurfaceLifecyclePolicy.CanConfigure(1280, 0, true)
                    && !ModernSurfaceLifecyclePolicy.CanConfigure(1280, 720, false)
                    && ModernSurfaceLifecyclePolicy.CanConfigure(1280, 720, true),
                "zero-sized/unavailable surfaces suspend configuration until restore", ref failures);
            Check(ModernSurfaceLifecyclePolicy.AcquireAction(
                    SurfaceAcquireCondition.Success, hasTexture: true, attempt: 0)
                    == SurfaceAcquireAction.UseTexture
                && ModernSurfaceLifecyclePolicy.AcquireAction(
                    SurfaceAcquireCondition.Timeout, hasTexture: false, attempt: 0)
                    == SurfaceAcquireAction.SkipFrame,
                "surface success presents and transient timeout skips one frame", ref failures);
            Check(ModernSurfaceLifecyclePolicy.AcquireAction(
                    SurfaceAcquireCondition.Outdated, hasTexture: false, attempt: 0)
                    == SurfaceAcquireAction.Reconfigure
                && ModernSurfaceLifecyclePolicy.AcquireAction(
                    SurfaceAcquireCondition.Outdated, hasTexture: false, attempt: 1)
                    == SurfaceAcquireAction.Fail,
                "outdated surface gets one bounded reconfigure attempt", ref failures);
            Check(ModernSurfaceLifecyclePolicy.AcquireAction(
                    SurfaceAcquireCondition.Lost, hasTexture: false, attempt: 0)
                    == SurfaceAcquireAction.RecreateSurface
                && ModernSurfaceLifecyclePolicy.AcquireAction(
                    SurfaceAcquireCondition.Lost, hasTexture: false, attempt: 1)
                    == SurfaceAcquireAction.Fail,
                "lost surface gets one bounded recreation attempt", ref failures);
            Check(ModernSurfaceLifecyclePolicy.PresentModeChangeRequiresReconfigure(
                    oldVsync: true, newVsync: false, 1280, 720, surfaceAvailable: true)
                && !ModernSurfaceLifecyclePolicy.PresentModeChangeRequiresReconfigure(
                    oldVsync: true, newVsync: true, 1280, 720, surfaceAvailable: true)
                && !ModernSurfaceLifecyclePolicy.PresentModeChangeRequiresReconfigure(
                    oldVsync: true, newVsync: false, 0, 0, surfaceAvailable: true),
                "present-mode changes reconfigure only a live drawable surface", ref failures);
        }

        private sealed class RecordingLowLatencyProvider : ILowLatencyProvider
        {
            internal readonly List<(ulong Frame, LowLatencyMarker Marker)> Markers = new();
            internal LowLatencyMode Configured;
            internal int Waits;
            internal bool Disposed;
            public string Name => "test";
            public bool Supported { get; }
            internal RecordingLowLatencyProvider(bool supported) => Supported = supported;
            public void Configure(LowLatencyMode mode) => Configured = mode;
            public void WaitForFrame(ulong frameId) => Waits++;
            public void Mark(ulong frameId, LowLatencyMarker marker) => Markers.Add((frameId, marker));
            public void Dispose() => Disposed = true;
        }

        private static void CheckLowLatencyFoundation(ref int failures)
        {
            var provider = new RecordingLowLatencyProvider(supported: true);
            using (var session = new LowLatencySession(provider))
            {
                session.Configure(LowLatencyMode.Enabled);
                ulong frame = session.BeginFrame();
                session.WaitForFrame(frame);
                foreach (LowLatencyMarker marker in new[]
                {
                    LowLatencyMarker.InputSample,
                    LowLatencyMarker.SimulationStart,
                    LowLatencyMarker.SimulationEnd,
                    LowLatencyMarker.RenderSubmitStart,
                    LowLatencyMarker.RenderSubmitEnd,
                    LowLatencyMarker.PresentStart,
                    LowLatencyMarker.PresentEnd
                })
                    session.Mark(frame, marker);

                LowLatencyStatus status = session.Status;
                Check(provider.Configured == LowLatencyMode.Enabled
                    && provider.Waits == 1
                    && provider.Markers.Count == 7
                    && provider.Markers[0].Marker == LowLatencyMarker.InputSample
                    && provider.Markers[^1].Marker == LowLatencyMarker.PresentEnd
                    && status.DroppedMarkers == 0 && status.IncompleteFrames == 0,
                    "backend-neutral latency provider receives one ordered marker vocabulary",
                    ref failures);

                ulong second = session.BeginFrame();
                session.Mark(second, LowLatencyMarker.RenderSubmitStart);
                bool rejected = !session.Mark(second, LowLatencyMarker.SimulationStart);
                session.CancelFrame(second);
                Check(rejected && session.Status.DroppedMarkers == 1,
                    "out-of-order latency markers are contained without touching gameplay",
                    ref failures);
            }

            var unsupported = new RecordingLowLatencyProvider(supported: false);
            using (var session = new LowLatencySession(unsupported))
            {
                session.Configure(LowLatencyMode.Boost);
                ulong frame = session.BeginFrame();
                session.Mark(frame, LowLatencyMarker.PresentStart);
                session.Mark(frame, LowLatencyMarker.PresentEnd);
                Check(session.Status.RequestedMode == LowLatencyMode.Boost
                    && session.Status.ActiveMode == LowLatencyMode.Disabled
                    && unsupported.Markers.Count == 0,
                    "unsupported low-latency provider degrades to a no-op", ref failures);
            }
        }

#if !MPHREAD_SERVER
        private static void CheckLegacyUniformCache(ref int failures)
        {
            var cache = new LegacyGlUniformCache();
            cache.UseProgram(7);
            cache.BeginSample();
            bool first = cache.Submit(3, 11);
            bool duplicate = cache.Submit(3, 11);
            bool changed = cache.Submit(3, 12);
            bool vectorFirst = cache.Submit4(4, new OpenTK.Mathematics.Vector4(1, 2, 3, 4));
            bool vectorDuplicate = cache.Submit4(4, new OpenTK.Mathematics.Vector4(1, 2, 3, 4));
            var identity = OpenTK.Mathematics.Matrix4.Identity;
            bool matrixFirst = cache.SubmitMatrix4(5, false, in identity);
            bool matrixDuplicate = cache.SubmitMatrix4(5, false, in identity);
            cache.NoteUncachedWrite();
            LegacyUniformSample sample = cache.EndSample();
            Check(first && !duplicate && changed && vectorFirst && !vectorDuplicate
                && matrixFirst && !matrixDuplicate
                && sample.Requested == 8 && sample.Submitted == 5 && sample.Skipped == 3,
                "legacy GL cache suppresses exact scalar/vector/matrix duplicates and measures arrays",
                ref failures);

            cache.UseProgram(8);
            cache.Submit(9, 1f);
            cache.Submit3(10, OpenTK.Mathematics.Vector3.One);
            cache.SubmitMatrix4(11, false, in identity);
            cache.InvalidateFloatArray();
            cache.InvalidateVector3Array();
            cache.InvalidateMatrix4Array();
            Check(cache.Submit(9, 1f)
                && cache.Submit3(10, OpenTK.Mathematics.Vector3.One)
                && cache.SubmitMatrix4(11, false, in identity),
                "array uploads invalidate overlapping cached single uniform values",
                ref failures);
            bool otherProgram = cache.Submit(3, 12);
            cache.UseProgram(7);
            bool originalStillCached = !cache.Submit(3, 12);
            cache.InvalidateAll();
            bool invalidationResubmits = cache.Submit(3, 12);
            cache.ResetContext();
            bool noProgramPassesThrough = cache.Submit(3, 12);
            Check(otherProgram && originalStillCached && invalidationResubmits
                && noProgramPassesThrough && cache.CurrentProgram == 0,
                "legacy GL cache is program-local and invalidates safely at ownership boundaries",
                ref failures);
        }

#endif

        private static void Check(bool success, string name, ref int failures)
        {
            Console.WriteLine($"RENDERBACKENDS {(success ? "PASS" : "FAIL")} {name}");
            if (!success) failures++;
        }
    }
}
