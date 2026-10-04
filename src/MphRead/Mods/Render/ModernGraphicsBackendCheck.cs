using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render
{
    public static class ModernGraphicsBackendCheck
    {
        public static int Run()
        {
            int failures = 0;
            CheckDefault(GraphicsPlatform.Windows, GraphicsBackend.DirectX12, ref failures);
            CheckDefault(GraphicsPlatform.MacOS, GraphicsBackend.Metal, ref failures);
            CheckDefault(GraphicsPlatform.Linux, GraphicsBackend.Vulkan, ref failures);
            CheckDefault(GraphicsPlatform.Android, GraphicsBackend.Vulkan, ref failures);

            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.DirectX12, true, ref failures);
            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Windows, GraphicsBackend.Metal, false, ref failures);

            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.Metal, true, ref failures);
            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.MacOS, GraphicsBackend.DirectX12, false, ref failures);

            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.Metal, false, ref failures);
            CheckSupported(GraphicsPlatform.Linux, GraphicsBackend.DirectX12, false, ref failures);

            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.Vulkan, true, ref failures);
            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.Metal, false, ref failures);
            CheckSupported(GraphicsPlatform.Android, GraphicsBackend.DirectX12, false, ref failures);

            CheckAlias("d3d12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("directx12", GraphicsBackend.DirectX12, ref failures);
            CheckAlias("vk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("moltenvk", GraphicsBackend.Vulkan, ref failures);
            CheckAlias("metal", GraphicsBackend.Metal, ref failures);
            CheckAlias("opengl", GraphicsBackend.OpenGL, ref failures);
            Check(GraphicsBackendPolicy.StartupGuardMatches("DirectX12", GraphicsBackend.DirectX12)
                && GraphicsBackendPolicy.StartupGuardMatches("vulkan", GraphicsBackend.Vulkan)
                && !GraphicsBackendPolicy.StartupGuardMatches("OpenGL", GraphicsBackend.DirectX12)
                && !GraphicsBackendPolicy.StartupGuardMatches("broken", GraphicsBackend.DirectX12),
                "startup recovery fence matches only the failed modern backend", ref failures);
            CheckGeometry(ref failures);
            CheckSurfaceLifecycle(ref failures);
            CheckLowLatencyFoundation(ref failures);
#if !MPHREAD_SERVER
            CheckUniformCompatibility(ref failures);
            CheckLegacyUniformCache(ref failures);
            CheckResourceCompatibility(ref failures);
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

        private static void CheckUniformCompatibility(ref int failures)
        {
            var state = new ModernGraphicsCompatState();
            int vertex = state.CreateShader(OpenTK.Graphics.OpenGL.ShaderType.VertexShader);
            state.ShaderSource(vertex, "uniform mat4 proj_mtx; void main() { }");
            state.CompileShader(vertex);
            int fragment = state.CreateShader(OpenTK.Graphics.OpenGL.ShaderType.FragmentShader);
            state.ShaderSource(fragment,
                "uniform int mat_mode; uniform float shift_table[64]; void main() { }");
            state.CompileShader(fragment);
            int program = state.CreateProgram();
            state.AttachShader(program, vertex);
            state.AttachShader(program, fragment);
            state.LinkProgram(program);
            state.GetProgram(program, OpenTK.Graphics.OpenGL.GetProgramParameterName.LinkStatus,
                out int linked);
            Check(linked == 1, "compat shader/program IDs link without GL objects", ref failures);

            int mode = state.GetUniformLocation(program, "mat_mode");
            int shift = state.GetUniformLocation(program, "shift_table");
            int missing = state.GetUniformLocation(program, "definitely_missing");
            Check(mode > 0 && shift > 0 && missing == -1,
                "compat uniform locations preserve declared/missing behavior", ref failures);

            state.UseProgram(program);
            state.Uniform1(mode, 7);
            state.GetUniform(program, mode, out int stored);
            Check(stored == 7, "compat uniform writes remain program-local and readable", ref failures);
        }

        private static void CheckResourceCompatibility(ref int failures)
        {
            var state = new ModernGraphicsResourceState();

            int texture = state.GenTexture();
            state.ActiveTexture(OpenTK.Graphics.OpenGL.TextureUnit.Texture0);
            state.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, texture);
            byte[] pixels =
            {
                1, 2, 3, 4,   5, 6, 7, 8,
                9, 10, 11, 12, 13, 14, 15, 16
            };
            state.TexImage2D(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                OpenTK.Graphics.OpenGL.PixelInternalFormat.Rgba, 2, 2,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, pixels);
            state.GetTexLevelParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 0,
                OpenTK.Graphics.OpenGL.GetTextureParameter.TextureWidth, out int width);
            state.GetTexLevelParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 0,
                OpenTK.Graphics.OpenGL.GetTextureParameter.TextureHeight, out int height);
            Check(width == 2 && height == 2 && state.IsTexture(texture),
                "compat texture allocation preserves size and object identity", ref failures);

            byte[] replacement = { 101, 102, 103, 104 };
            state.TexSubImage2D(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                1, 0, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, replacement);
            byte[]? stored = state.Texture(texture).Pixels;
            Check(stored != null && stored.Length == 16
                && stored[4] == 101 && stored[5] == 102
                && stored[6] == 103 && stored[7] == 104,
                "compat texture sub-image updates the addressed texel", ref failures);

            byte[] beforeInvalidUpdate = (byte[])state.Texture(texture).Pixels!.Clone();
            bool rejectedRowOverflow = false, rejectedShortSource = false;
            try
            {
                state.TexSubImage2D(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                    1, 0, 2, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                    OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, new byte[8]);
            }
            catch (ArgumentOutOfRangeException) { rejectedRowOverflow = true; }
            try
            {
                state.TexSubImage2D(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                    0, 0, 1, 2, OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                    OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, new byte[4]);
            }
            catch (ArgumentException) { rejectedShortSource = true; }
            Check(rejectedRowOverflow && rejectedShortSource
                && beforeInvalidUpdate.AsSpan().SequenceEqual(state.Texture(texture).Pixels),
                "invalid sub-images fail before changing any texels", ref failures);

            state.Texture(texture).Dirty = false; // Simulate a completed upload.
            state.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                (OpenTK.Graphics.OpenGL.TextureParameterName)0x84FE, 64);
            Check(state.Texture(texture).Anisotropy == 16 && !state.Texture(texture).Dirty
                && state.Texture(texture).SamplerDirty,
                "sampler edits clamp anisotropy without invalidating texture contents", ref failures);
            state.GenerateMipmap(OpenTK.Graphics.OpenGL.GenerateMipmapTarget.Texture2D);
            Check(state.Texture(texture).HasMipmaps && state.Texture(texture).MipmapsDirty
                && !state.Texture(texture).Dirty,
                "mipmap requests preserve the uploaded base level", ref failures);

            state.Texture(texture).SamplerDirty = false;
            state.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                (OpenTK.Graphics.OpenGL.TextureParameterName)0x84FE, 64);
            state.TexParameter(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D,
                OpenTK.Graphics.OpenGL.TextureParameterName.TextureMinFilter, state.Texture(texture).MinFilter);
            Check(!state.Texture(texture).SamplerDirty,
                "unchanged effective sampler settings preserve the native sampler", ref failures);

            int framebuffer = state.GenFramebuffer();
            state.BindFramebuffer(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer, framebuffer);
            state.FramebufferTexture2D(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.ColorAttachment0, texture);
            int mrtTexture1 = state.GenTexture();
            int mrtTexture2 = state.GenTexture();
            state.FramebufferTexture2D(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.ColorAttachment1, mrtTexture1);
            state.FramebufferTexture2D(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.ColorAttachment2, mrtTexture2);
            Check(state.Framebuffer(framebuffer).ColorTexture == texture
                    && state.Framebuffer(framebuffer).ColorTexture1 == mrtTexture1
                    && state.Framebuffer(framebuffer).ColorTexture2 == mrtTexture2,
                "compat framebuffer tracks three MRT color attachments", ref failures);
            Check(state.CheckFramebufferStatus(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer)
                    == OpenTK.Graphics.OpenGL.FramebufferErrorCode.FramebufferComplete,
                "compat framebuffer completes with MRT color attachments", ref failures);

            int renderbuffer = state.GenRenderbuffer();
            state.BindRenderbuffer(OpenTK.Graphics.OpenGL.RenderbufferTarget.Renderbuffer, renderbuffer);
            state.RenderbufferStorage(OpenTK.Graphics.OpenGL.RenderbufferTarget.Renderbuffer,
                OpenTK.Graphics.OpenGL.RenderbufferStorage.Depth24Stencil8, 2, 2);
            state.FramebufferRenderbuffer(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.DepthStencilAttachment,
                OpenTK.Graphics.OpenGL.RenderbufferTarget.Renderbuffer, renderbuffer);
            state.GetFramebufferAttachmentParameter(
                OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.DepthStencilAttachment,
                OpenTK.Graphics.OpenGL.FramebufferParameterName.FramebufferAttachmentDepthSize,
                out int depthBits);
            Check(depthBits == 24,
                "compat depth/stencil attachment reports 24 depth bits", ref failures);

            state.FramebufferTexture2D(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.ColorAttachment0, 0);
            Check(state.IsFramebufferTexture(texture),
                "detached render targets retain their sampling origin", ref failures);
            state.FramebufferTexture2D(OpenTK.Graphics.OpenGL.FramebufferTarget.Framebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.ColorAttachment0, texture);

            state.DeleteTexture(mrtTexture1);
            state.DeleteTexture(mrtTexture2);
            Check(state.Framebuffer(framebuffer).ColorTexture1 == 0
                    && state.Framebuffer(framebuffer).ColorTexture2 == 0,
                "deleting MRT textures clears secondary framebuffer references", ref failures);
            state.DeleteTexture(texture);
            Check(!state.IsTexture(texture) && state.Framebuffer(framebuffer).ColorTexture == 0,
                "deleting texture clears framebuffer attachment references", ref failures);
        }
#endif

        private static void Check(bool success, string name, ref int failures)
        {
            Console.WriteLine($"RENDERBACKENDS {(success ? "PASS" : "FAIL")} {name}");
            if (!success) failures++;
        }
    }
}
