#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// End-to-end modern-window smoke test: GLFW NoAPI window, native WebGPU
    /// surface, texture upload, compatibility Begin/End geometry, readback and
    /// present. It intentionally needs no game files.
    /// </summary>
    internal static class ModernGraphicsWindowCheck
    {
        internal static int Run(string? renderer)
        {
            try
            {
                GraphicsBackendPolicy.Configure(renderer ?? "auto");
                GraphicsBackend backend = GraphicsBackendPolicy.Resolved;
                if (!GraphicsBackendPolicy.IsModern(backend))
                {
                    Console.Error.WriteLine("[renderwindowcheck] choose a modern renderer");
                    return 2;
                }

                NativeWindowSettings settings = DesktopGlContext.Settings(background: true);
                settings.ClientSize = new Vector2i(96, 64);
                settings.StartVisible = false;
                settings.Title = "Project Prime modern renderer smoke";
                using var window = new NativeWindow(settings);
                DesktopGlContext.InstallErrorCallback();
                GLFW.SwapInterval(1); // A NoAPI window has no current GL context.
                if (GLFW.GetError(out _) != OpenTK.Windowing.GraphicsLibraryFramework.ErrorCode.NoContext)
                    throw new InvalidOperationException("Expected GLFW NoContext callback was not delivered.");
                Console.WriteLine("[renderwindowcheck] native GLFW error returns safely PASS");
                GraphicsTimingPolicy.Enable();
                ModernGraphicsCompat.Initialize(window, backend);
                try
                {
                    ModernGraphicsCompat.Resize(96, 64);
                    int enabledTextureLimit = ModernGraphicsCompat.QueryEnabledTextureLimitForCheck();
                    if (GraphicsApi.GetInteger(GetPName.MaxTextureSize) != enabledTextureLimit
                        || GraphicsApi.GetInteger(GetPName.MaxRenderbufferSize) != enabledTextureLimit)
                        throw new InvalidOperationException("Compatibility graphics limits disagree with the enabled device limits.");
                    Console.WriteLine($"[renderwindowcheck] enabled texture/render-target limit {enabledTextureLimit} PASS");
                    ModernGraphicsCompat.ValidateGeneratedShaders();
                    ModernGraphicsCompat.ValidateVisibilityShadersForCheck();
                    GraphicsApi.Viewport(0, 0, 96, 64);
                    GraphicsApi.ClearColor(0f, 0f, 0f, 1f);
                    GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);

                    int texture = GraphicsApi.GenTexture();
                    GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                    GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,
                        TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,
                        TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    byte[] red =
                    {
                        255, 0, 0, 255, 255, 0, 0, 255,
                        255, 0, 0, 255, 255, 0, 0, 255
                    };
                    GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                        PixelInternalFormat.Rgba, 2, 2, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, red);
                    GraphicsApi.Enable(EnableCap.Texture2D);
                    GraphicsApi.Disable(EnableCap.DepthTest);
                    GraphicsApi.Disable(EnableCap.CullFace);
                    GraphicsApi.Disable(EnableCap.Blend);
                    GraphicsApi.Color4(1f, 1f, 1f, 1f);

                    GraphicsApi.Begin(PrimitiveType.TriangleStrip);
                    GraphicsApi.TexCoord2(1, 0); GraphicsApi.Vertex3( 1,  1, 0);
                    GraphicsApi.TexCoord2(0, 0); GraphicsApi.Vertex3(-1,  1, 0);
                    GraphicsApi.TexCoord2(1, 1); GraphicsApi.Vertex3( 1, -1, 0);
                    GraphicsApi.TexCoord2(0, 1); GraphicsApi.Vertex3(-1, -1, 0);
                    GraphicsApi.End();

                    byte[] pixel = new byte[4];
                    GraphicsApi.ReadPixels(48, 32, 1, 1,
                        PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                    ModernGraphicsCompat.Present();
                    GraphicsApi.DeleteTexture(texture);

                    bool redPixel = pixel[0] >= 220 && pixel[1] <= 24
                        && pixel[2] <= 24 && pixel[3] >= 220;
                    if (!redPixel)
                    {
                        Console.Error.WriteLine(
                            $"[renderwindowcheck] FAIL launcher rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]})");
                        return 1;
                    }

                    RunSurfaceLifecycleCheck();
                    RunScissorBoundsCheck();
                    RunCopyFormatCheck();
                    RunDeviceRecoveryCheck();
                    RunTrackedStorageCheck();
                    RunMipmapCheck();
                    AuthoredRgbaMipGpuCheck.Verify();
                    RunLargeTextureMipmapCheck();
                    RunProgressiveTexturePromotionCheck();
                    RunReadbackOrientationCheck();
                    ModernGraphicsCompat.BeginPerformanceSample();
                    TextureUpdateCheck.Verify();
                    var uploads = ModernGraphicsCompat.EndPerformanceSample();
                    if (uploads.TextureUploadBytes <= 0 || uploads.TextureUploadSubmissionMs < 0)
                        throw new InvalidOperationException("Texture upload performance counters failed.");
                    RunAdvancedShaderCheck();
                    ModernPbrLayerCheck.Verify();
                    ModernGraphicsCompat.BeginPerformanceSample();
                    RunWireframeCheck();
                    var pipelines = ModernGraphicsCompat.EndPerformanceSample();
                    if (pipelines.PipelinesCreated <= 0 || pipelines.LongestPipelineCreationMs <= 0)
                        throw new InvalidOperationException("Pipeline performance counters failed.");
                    RunLifetimeCheck();
                    ModernGraphicsCompat.VerifyRetainedAtlasLifetimeForCheck();
                    ModernGraphicsCompat.ValidateResourceAccountingForCheck();
                    ModernGraphicsCompat.VerifyGpuVisibilityForCheck();
                    ModernGraphicsCompat.VerifyGpuFrameTimingForCheck();
                    byte[] worldPixels = RunWorldCompositeCheck();
                    bool topLeftGreen = worldPixels[0] <= 30 && worldPixels[1] >= 220
                        && worldPixels[2] <= 30 && worldPixels[3] >= 220;
                    int blend = ModernGraphicsCompat.SurfaceUsesSrgb ? 188 : 128;
                    bool topRightBlend = Math.Abs(worldPixels[4] - blend) < 18
                        && Math.Abs(worldPixels[5] - blend) < 18
                        && worldPixels[6] <= 35 && worldPixels[7] >= 220;
                    bool bottomBlue = worldPixels[8] <= 30 && worldPixels[9] <= 30
                        && worldPixels[10] >= 220 && worldPixels[11] >= 220;
                    bool whiteout = worldPixels[12] >= 220 && worldPixels[13] >= 220
                        && worldPixels[14] >= 220 && worldPixels[15] >= 220;
                    bool linearBlend = worldPixels[16] >= 120 && worldPixels[16] <= 136
                        && worldPixels[17] >= 120 && worldPixels[17] <= 136
                        && worldPixels[18] <= 20 && worldPixels[19] >= 220;
                    bool blitTop = worldPixels[20] <= 30 && worldPixels[21] >= 220
                        && worldPixels[22] <= 30 && worldPixels[23] >= 220;
                    bool blitBottom = worldPixels[24] <= 30 && worldPixels[25] <= 30
                        && worldPixels[26] >= 220 && worldPixels[27] >= 220;
                    if (!topLeftGreen || !topRightBlend || !bottomBlue || !whiteout
                        || !linearBlend || !blitTop || !blitBottom)
                    {
                        Console.Error.WriteLine(
                            $"[renderwindowcheck] FAIL world/rtt "
                            + $"topLeft=rgba({worldPixels[0]},{worldPixels[1]},{worldPixels[2]},{worldPixels[3]}) "
                            + $"topRight=rgba({worldPixels[4]},{worldPixels[5]},{worldPixels[6]},{worldPixels[7]}) "
                            + $"bottom=rgba({worldPixels[8]},{worldPixels[9]},{worldPixels[10]},{worldPixels[11]}) "
                            + $"whiteout=rgba({worldPixels[12]},{worldPixels[13]},{worldPixels[14]},{worldPixels[15]}) "
                            + $"linearBlend=rgba({worldPixels[16]},{worldPixels[17]},{worldPixels[18]},{worldPixels[19]}) "
                            + $"blitTop=rgba({worldPixels[20]},{worldPixels[21]},{worldPixels[22]},{worldPixels[23]}) "
                            + $"blitBottom=rgba({worldPixels[24]},{worldPixels[25]},{worldPixels[26]},{worldPixels[27]})");
                        return 1;
                    }

                    UiOverlayCompositeCheck.Run(96, 64);
                    Console.WriteLine("[renderwindowcheck] fullscreen UI composite integration PASS");
                    RunFailedRecoveryFallbackCheck();
                    RunRendererRestartCheck(window, backend);
                    Console.WriteLine(
                        $"[renderwindowcheck] PASS backend={GraphicsBackendPolicy.DisplayName(backend)} "
                        + $"launcher=rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]}) "
                        + $"worldTop=rgba({worldPixels[0]},{worldPixels[1]},{worldPixels[2]},{worldPixels[3]}) "
                        + $"worldBlend=rgba({worldPixels[4]},{worldPixels[5]},{worldPixels[6]},{worldPixels[7]}) "
                        + $"worldBottom=rgba({worldPixels[8]},{worldPixels[9]},{worldPixels[10]},{worldPixels[11]}) "
                        + $"whiteout=rgba({worldPixels[12]},{worldPixels[13]},{worldPixels[14]},{worldPixels[15]}) "
                        + $"linearBlend=rgba({worldPixels[16]},{worldPixels[17]},{worldPixels[18]},{worldPixels[19]}) "
                        + $"blitTop=rgba({worldPixels[20]},{worldPixels[21]},{worldPixels[22]},{worldPixels[23]}) "
                        + $"blitBottom=rgba({worldPixels[24]},{worldPixels[25]},{worldPixels[26]},{worldPixels[27]})");
                    return 0;
                }
                finally
                {
                    ModernGraphicsCompat.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[renderwindowcheck] FAIL {ex.GetType().Name}: {ex.Message}");
                // Recoverable native surface outcomes are logged to the recent
                // diagnostic ring. Include its tail so an unavailable drawable
                // cannot be mistaken for a texture/readback pixel failure.
                var diagnostics = new System.Text.StringBuilder();
                DebugLog.AppendRecent(diagnostics);
                const int maximumDiagnosticCharacters = 8192;
                int start = Math.Max(0, diagnostics.Length - maximumDiagnosticCharacters);
                if (diagnostics.Length != 0)
                    Console.Error.WriteLine(diagnostics.ToString(start, diagnostics.Length - start));
                return 1;
            }
        }

        private static void RunSurfaceLifecycleCheck()
        {
            // A minimized/temporarily unavailable drawable is represented to
            // the renderer as a zero-sized surface. It must suspend cleanly,
            // preserve the requested present mode, and resume on restore.
            ModernGraphicsCompat.SetVSync(true);
            ModernGraphicsCompat.Resize(0, 0);
            ModernGraphicsCompat.Present();
            ModernGraphicsCompat.SetVSync(false);
            string unpacedMode = ModernGraphicsCompat.ActivePresentMode;
            ModernGraphicsCompat.Resize(96, 64);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(.2f, .4f, .8f, 1f);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            byte[] restored = new byte[4];
            GraphicsApi.ReadPixels(48, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, restored);
            ModernGraphicsCompat.Present();

            bool blockingMatchesMode = ModernGraphicsCompat.ActivePresentMode is "Immediate" or "Mailbox"
                ? !ModernGraphicsCompat.PresentationBlocks
                : ModernGraphicsCompat.PresentationBlocks;
            if (restored[0] < 35 || restored[0] > 70
                || restored[1] < 85 || restored[1] > 120
                || restored[2] < 190 || restored[2] > 220
                || !blockingMatchesMode)
                throw new InvalidOperationException(
                    $"Surface restore/present-mode lifecycle failed: mode={unpacedMode} "
                    + $"rgba({restored[0]},{restored[1]},{restored[2]},{restored[3]}).");

            ModernGraphicsCompat.SetVSync(true);
            Console.WriteLine(
                $"[renderwindowcheck] zero-size restore and present-mode switching PASS "
                + $"unpaced={unpacedMode} vsync={ModernGraphicsCompat.ActivePresentMode}");
        }

        private static unsafe void RunRendererRestartCheck(NativeWindow window, GraphicsBackend backend)
        {
            byte[] overlay = { 255, 0, 0, 255 };
            fixed (byte* pixels = overlay) UiOverlay.Upload((nint)pixels, 1, 1);
            UiOverlay.Visible = true;
            UiOverlay.Release();
            if (UiOverlay.HasFrame || UiOverlay.Visible)
                throw new InvalidOperationException("UI overlay retained state after renderer-owned release.");
            // Teardown may be reached after a context/surface has already gone.
            // Shutdown is intentionally idempotent, then the same host window
            // must be able to create a fresh renderer and present again.
            ModernGraphicsCompat.Shutdown();
            ModernGraphicsCompat.Shutdown();
            ModernGraphicsCompat.Initialize(window, backend);
            ModernGraphicsCompat.Resize(96, 64);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0f, .75f, .25f, 1f);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            byte[] pixel = new byte[4];
            GraphicsApi.ReadPixels(48, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            ModernGraphicsCompat.Present();
            if (pixel[0] > 16 || pixel[1] < 175 || pixel[2] < 45)
                throw new InvalidOperationException(
                    $"Renderer restart did not recover a presentable surface: "
                    + $"rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]}).");
            // Same-size upload must allocate in the new renderer generation.
            fixed (byte* pixels = overlay) UiOverlay.Upload((nint)pixels, 1, 1);
            UiOverlay.Visible = true;
            UiOverlay.Draw(96, 64);
            GraphicsApi.ReadPixels(48, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            if (pixel[0] < 220 || pixel[1] > 24 || pixel[2] > 24)
                throw new InvalidOperationException("UI overlay did not upload/draw after renderer restart.");
            UiOverlay.Release();
            // Terminal recovery failure must also be able to forget IDs without
            // attempting any graphics operation on the failed facade.
            UiOverlay.ForgetRendererResources();
            Console.WriteLine("[renderwindowcheck] idempotent shutdown and renderer restart PASS");
        }

        private static void RunWireframeCheck()
        {
            int texture = GraphicsApi.GenTexture(), framebuffer = GraphicsApi.GenFramebuffer();
            int list = GraphicsApi.GenLists(1);
            GraphicsApi.PushAttrib(AttribMask.AllAttribBits);
            try
            {
                GraphicsApi.UseProgram(0);
                GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                    32, 32, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, texture, 0);
                GraphicsApi.Viewport(0, 0, 32, 32);
                foreach (var cap in new[] { EnableCap.DepthTest, EnableCap.CullFace, EnableCap.Blend,
                    EnableCap.Texture2D, EnableCap.ScissorTest, EnableCap.StencilTest, EnableCap.AlphaTest })
                    GraphicsApi.Disable(cap);
                GraphicsApi.ColorMask(true, true, true, true);
                GraphicsApi.Color4(1f, 1f, 1f, 1f);
                GraphicsApi.NewList(list, ListMode.Compile);
                GraphicsApi.Begin(PrimitiveType.Triangles);
                GraphicsApi.Vertex3(-.8f, -.8f, 0); GraphicsApi.Vertex3(.8f, -.8f, 0); GraphicsApi.Vertex3(0, .8f, 0);
                GraphicsApi.End(); GraphicsApi.EndList();
                foreach (var mode in new[] { OpenTK.Graphics.OpenGL.PolygonMode.Line, OpenTK.Graphics.OpenGL.PolygonMode.Fill, OpenTK.Graphics.OpenGL.PolygonMode.Line })
                {
                    GraphicsApi.ClearColor(0, 0, 0, 1);
                    GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                    GraphicsApi.PolygonMode(TriangleFace.FrontAndBack, mode);
                    GraphicsApi.CallList(list);
                    byte[] pixels = new byte[32 * 32 * 4];
                    GraphicsApi.ReadPixels(0, 0, 32, 32, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    int lit = 0;
                    for (int at = 0; at < pixels.Length; at += 4) if (pixels[at] > 200) lit++;
                    bool center = pixels[(12 * 32 + 16) * 4] > 200;
                    if (mode == OpenTK.Graphics.OpenGL.PolygonMode.Line ? center || lit < 20 || lit > 150 : !center || lit < 200)
                        throw new InvalidOperationException($"Wireframe/fill mismatch: {mode} lit={lit} center={center}");
                }
            }
            finally
            {
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GraphicsApi.DeleteLists(list, 1);
                GraphicsApi.DeleteFramebuffer(framebuffer);
                GraphicsApi.DeleteTexture(texture);
                GraphicsApi.PopAttrib();
            }
            Console.WriteLine("[renderwindowcheck] wireframe/fill display-list switching PASS");
        }

        private static void RunFailedRecoveryFallbackCheck()
        {
            // The successful-reconstruction case above has already used the
            // one retry. A second loss must stay controlled and permit GL.
            ModernGraphicsCompat.DestroyDeviceForCheck();
            try { GraphicsApi.Viewport(0, 0, 96, 64); }
            catch (InvalidOperationException) when (ModernGraphicsCompat.RecoveryFailure != null) { }
            if (ModernGraphicsCompat.RecoveryFailure == null)
                throw new InvalidOperationException("Repeated device loss did not enter controlled fallback.");
            ModernGraphicsCompat.Shutdown();
            GraphicsBackendPolicy.UseCompatibilityFallback("forced repeated device loss acceptance check");
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(96, 64);
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            GraphicsApi.ClearColor(0, 1, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            byte[] pixel = new byte[4];
            GraphicsApi.ReadPixels(1, 1, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            if (pixel[0] > 10 || pixel[1] < 245 || pixel[2] > 10)
                throw new InvalidOperationException("Fresh OpenGL context failed after device recovery failure.");
            AuthoredRgbaMipGpuCheck.Verify();
            DesktopGraphicsSession.Present(window);
            Console.WriteLine("[renderwindowcheck] failed recovery to fresh OpenGL context PASS");
        }

        private static void RunScissorBoundsCheck()
        {
            int texture = GraphicsApi.GenTexture(), framebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.PushAttrib(AttribMask.AllAttribBits);
            try
            {
                GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, texture, 0);
                GraphicsApi.UseProgram(0);
                GraphicsApi.Disable(EnableCap.Texture2D);
                GraphicsApi.Disable(EnableCap.DepthTest);
                GraphicsApi.Disable(EnableCap.Blend);
                GraphicsApi.Disable(EnableCap.CullFace);
                GraphicsApi.ColorMask(true, true, true, true);
                foreach (bool window in new[] { false, true })
                {
                    int width = window ? 96 : 4, height = window ? 64 : 4;
                    GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, window ? 0 : framebuffer);
                    GraphicsApi.Viewport(0, 0, width, height);
                    foreach (bool clear in new[] { false, true })
                    foreach (var rectangle in new[] { (-1, -1, 2, 2), (0, 0, 0, 4), (width + 1, 0, 4, 4) })
                    {
                        GraphicsApi.Disable(EnableCap.ScissorTest);
                        GraphicsApi.ClearColor(0, 0, 1, 1);
                        GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                        GraphicsApi.Enable(EnableCap.ScissorTest);
                        GraphicsApi.Scissor(rectangle.Item1, rectangle.Item2, rectangle.Item3, rectangle.Item4);
                        if (clear)
                        {
                            GraphicsApi.ClearColor(1, 0, 0, 1);
                            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                        }
                        else
                        {
                            GraphicsApi.Color4(1f, 0f, 0f, 1f);
                            GraphicsApi.Begin(PrimitiveType.Quads);
                            GraphicsApi.Vertex3(-1, -1, 0); GraphicsApi.Vertex3(1, -1, 0);
                            GraphicsApi.Vertex3(1, 1, 0); GraphicsApi.Vertex3(-1, 1, 0);
                            GraphicsApi.End();
                        }
                        byte[] pixels = new byte[width * height * 4];
                        GraphicsApi.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                        for (int i = 0; i < width * height; i++)
                        {
                            bool red = rectangle.Item1 == -1 && i == 0;
                            if (pixels[i * 4] != (red ? 255 : 0) || pixels[i * 4 + 2] != (red ? 0 : 255))
                                throw new InvalidOperationException($"Scissor clipping failed: window={window} clear={clear} rect={rectangle} pixel={i}.");
                        }
                    }
                }
                Console.WriteLine("[renderwindowcheck] empty/offscreen scissor draws and clears PASS");
            }
            finally
            {
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GraphicsApi.DeleteFramebuffer(framebuffer);
                GraphicsApi.DeleteTexture(texture);
                GraphicsApi.PopAttrib();
            }
        }

        private static void RunCopyFormatCheck()
        {
            int source = GraphicsApi.GenTexture(), destination = GraphicsApi.GenTexture();
            int sourceFbo = GraphicsApi.GenFramebuffer(), destinationFbo = GraphicsApi.GenFramebuffer();
            GraphicsApi.PushAttrib(AttribMask.AllAttribBits);
            try
            {
                GraphicsApi.Disable(EnableCap.ScissorTest);
                GraphicsApi.ColorMask(true, true, true, true);
                GraphicsApi.BindTexture(TextureTarget.Texture2D, source);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, 4, 4, 0,
                    PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, sourceFbo);
                GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, source, 0);
                GraphicsApi.BindTexture(TextureTarget.Texture2D, destination);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFbo);
                GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, destination, 0);
                foreach (bool window in new[] { false, true })
                {
                    GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFbo);
                    GraphicsApi.ClearColor(0, 0, 1, 1);
                    GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                    GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, window ? 0 : sourceFbo);
                    GraphicsApi.ClearColor(0.75f, 0.25f, 0, 1);
                    GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                    GraphicsApi.BindTexture(TextureTarget.Texture2D, destination);
                    // Copy-to-texture must ignore draw-state scissor, even
                    // when a shader is required for format conversion.
                    GraphicsApi.Enable(EnableCap.ScissorTest);
                    GraphicsApi.Scissor(0, 0, 0, 0);
                    GraphicsApi.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 1, 1, 0, 0, 2, 2);
                    GraphicsApi.Disable(EnableCap.ScissorTest);
                    GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFbo);
                    byte[] pixels = new byte[64];
                    GraphicsApi.ReadPixels(0, 0, 4, 4, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                    for (int i = 0; i < 16; i++)
                    {
                        bool copied = i % 4 is 1 or 2 && i / 4 is 1 or 2;
                        int red = copied ? 191 : 0, green = copied ? 64 : 0, blue = copied ? 0 : 255;
                        if (Math.Abs(pixels[i * 4] - red) > 1 || Math.Abs(pixels[i * 4 + 1] - green) > 1
                            || Math.Abs(pixels[i * 4 + 2] - blue) > 1)
                            throw new InvalidOperationException($"Copy format conversion failed: window={window} pixel={i}.");
                    }
                }
                foreach (bool window in new[] { false, true })
                {
                    GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFbo);
                    GraphicsApi.Disable(EnableCap.ScissorTest);
                    GraphicsApi.ClearColor(0, 0, 1, 1);
                    GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                    GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, window ? 0 : sourceFbo);
                    GraphicsApi.Enable(EnableCap.ScissorTest);
                    GraphicsApi.Scissor(-1, -1, 2, 2);
                    GraphicsApi.BlitFramebuffer(0, 0, 4, 4, 0, 0, 4, 4,
                        ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                    GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, destinationFbo);
                    byte[] blitPixels = new byte[64];
                    GraphicsApi.ReadPixels(0, 0, 4, 4, PixelFormat.Rgba, PixelType.UnsignedByte, blitPixels);
                    for (int i = 0; i < 16; i++)
                        if (Math.Abs(blitPixels[i * 4] - (i == 0 ? 191 : 0)) > 1
                            || blitPixels[i * 4 + 2] != (i == 0 ? 0 : 255))
                            throw new InvalidOperationException($"Scissored framebuffer blit failed at pixel {i}.");
                }
                Console.WriteLine("[renderwindowcheck] HDR/surface copy conversion and scissored blit PASS");
            }
            finally
            {
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GraphicsApi.DeleteFramebuffer(sourceFbo); GraphicsApi.DeleteFramebuffer(destinationFbo);
                GraphicsApi.DeleteTexture(source); GraphicsApi.DeleteTexture(destination);
                GraphicsApi.PopAttrib();
            }
        }

        private static void RunReadbackOrientationCheck()
        {
            int texture = GraphicsApi.GenTexture(), framebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, texture, 0);
            GraphicsApi.Disable(EnableCap.ScissorTest);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.ClearColor(0, 0, 1, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.Enable(EnableCap.ScissorTest);
            GraphicsApi.Scissor(1, 0, 2, 2);
            GraphicsApi.ClearColor(1, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            byte[] pixels = new byte[64];
            GraphicsApi.ReadPixels(0, 0, 4, 4, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int at = (y * 4 + x) * 4;
                bool red = y < 2 && x is 1 or 2;
                if (pixels[at] != (red ? 255 : 0) || pixels[at + 2] != (red ? 0 : 255))
                    throw new InvalidOperationException($"Scissored clear/bottom-up readback mismatch at {x},{y}.");
            }
            GraphicsApi.Disable(EnableCap.ScissorTest);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.DeleteFramebuffer(framebuffer);
            GraphicsApi.DeleteTexture(texture);
            Console.WriteLine("[renderwindowcheck] scissored clear and bottom-up readback PASS");
        }

        private static void RunTrackedStorageCheck()
        {
            var before = ModernGraphicsCompat.TrackedStorageCapacity;
            int texture = GraphicsApi.GenTexture();
            try
            {
                GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                    32, 16, 0, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[32 * 16 * 4]);
                GraphicsApi.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                ModernGraphicsCompat.EnsureBoundTextureResident();
                var during = ModernGraphicsCompat.TrackedStorageCapacity;
                long expected = TextureStorageMath.Bytes(32, 16, 6, 4, false);
                if (during.TextureBytes - before.TextureBytes != expected)
                    throw new InvalidOperationException($"Tracked mip capacity delta differs: {during.TextureBytes - before.TextureBytes} != {expected}.");
            }
            finally
            {
                GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
                GraphicsApi.DeleteTexture(texture);
            }
            if (ModernGraphicsCompat.TrackedStorageCapacity.TextureBytes != before.TextureBytes)
                throw new InvalidOperationException("Tracked native texture capacity did not return after deletion.");
            Console.WriteLine("[renderwindowcheck] native mip capacity/delete accounting PASS");
        }

        private static void RunLifetimeCheck()
        {
            // Warm caches before measuring: bounded pipeline retention is intentional.
            // Unlike GameWindow.Run, this hidden NativeWindow has no event
            // loop. Pump nonblocking native events at each frame boundary so
            // Cocoa/WSI can retire presented drawables during rapid resizes.
            NativeWindow.ProcessWindowEvents(false);
            RunMipmapCheck();
            NativeWindow.ProcessWindowEvents(false);
            var baseline = ModernGraphicsCompat.LiveResources;
            for (int i = 0; i < 120; i++)
            {
                NativeWindow.ProcessWindowEvents(false);
                ModernGraphicsCompat.Resize(96 + i % 12, 64 + i % 12);
                try { RunMipmapCheck(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Resize/resource lifetime iteration {i} at {96 + i % 12}x{64 + i % 12}: {ex.Message}", ex);
                }
                NativeWindow.ProcessWindowEvents(false);
                var after = ModernGraphicsCompat.LiveResources;
                if (after.Textures > baseline.Textures || after.Renderbuffers > baseline.Renderbuffers
                    || after.Geometry > baseline.Geometry || after.Pipelines > baseline.Pipelines
                    || after.Programs > baseline.Programs || after.Lists > baseline.Lists
                    || after.Views > baseline.Views || after.Samplers > baseline.Samplers
                    || after.Buffers > baseline.Buffers || after.ShaderModules > baseline.ShaderModules
                    || after.Surfaces > baseline.Surfaces || after.BindGroups > baseline.BindGroups)
                    throw new InvalidOperationException($"Resource growth after iteration {i}: {baseline} -> {after}");
            }
            ModernGraphicsCompat.Resize(96, 64);
            Console.WriteLine($"[renderwindowcheck] 120-cycle resize/resource lifetime PASS {baseline}");
        }

        private static void RunDeviceRecoveryCheck()
        {
            var authored = AuthoredRgbaMipFixtures.Decode(
                AuthoredRgbaMipFixtures.Levels(7, 5, TextureAssetChannel.Normal),
                TextureAssetClass.Hunter, TextureAssetChannel.Normal, 8192);
            int authoredTexture = GraphicsApi.GenTexture();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, authoredTexture);
            TextureAssetManager.UploadPreparedBound(authored, repeat: false,
                new TextureSamplerDescriptor(true, true, true, 1, 0));
            int texture = GraphicsApi.GenTexture();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 0, 255, 0, 255 });
            int list = GraphicsApi.GenLists(1);
            GraphicsApi.NewList(list, ListMode.Compile);
            GraphicsApi.Begin(PrimitiveType.Triangles);
            GraphicsApi.TexCoord2(0, 0);
            GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.Vertex3(3, -1, 0);
            GraphicsApi.Vertex3(-1, 3, 0);
            GraphicsApi.End();
            GraphicsApi.EndList();
            GraphicsApi.CallList(list);
            ModernGraphicsCompat.Present();
            int generation = ModernGraphicsCompat.DeviceGeneration;
            ModernGraphicsCompat.DestroyDeviceForCheck();
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.CallList(list);
            byte[] pixel = new byte[4];
            GraphicsApi.ReadPixels(48, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            if (ModernGraphicsCompat.DeviceGeneration != generation + 1 || pixel[1] < 220 || pixel[0] > 24)
                throw new InvalidOperationException("Device reconstruction did not restore texture/display-list contents.");
            AuthoredRgbaMipGpuCheck.VerifyTexture(authoredTexture, authored, mipmaps: true);
            ModernGraphicsCompat.Present();
            GraphicsApi.DeleteLists(list, 1);
            GraphicsApi.DeleteTexture(texture);
            GraphicsApi.DeleteTexture(authoredTexture);
            Console.WriteLine("[renderwindowcheck] device reconstruction and resource restoration PASS");
        }

        private static void RunAdvancedShaderCheck()
        {
            int texture = GraphicsApi.GenTexture();
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 0, 0, 255 });
            int post = Link(GraphicsPipelineShader.VertexSource, GraphicsPipelineShader.FragmentSource);
            GraphicsApi.UseProgram(post);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(post, "tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(post, "gamma_value"), 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(post, "contrast_value"), 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(post, "saturation_value"), 1f);
            GraphicsApi.Uniform2(GraphicsApi.GetUniformLocation(post, "texel"), 1f / 96, 1f / 64);
            for (int aa = 0; aa <= 4; aa++)
            {
                GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(post, "aa_mode"), aa);
                DrawTexturedQuad(-1, -1, 1, 1, 0);
                byte[] pixel = new byte[4];
                GraphicsApi.ReadPixels(48, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                if (pixel[0] < 245 || pixel[1] > 10 || pixel[2] > 10)
                    throw new InvalidOperationException($"Post-process AA mode {aa} changed a flat red field: {string.Join(",", pixel)}.");
            }
            int pbr = Link(DeferredPbrShader.VertexSource, DeferredPbrShader.FragmentSource);
            GraphicsApi.UseProgram(pbr);
            Matrix4 identity = Matrix4.Identity;
            foreach (string name in new[] { "proj_mtx", "view_mtx", "view_inv_mtx", "tex_mtx" })
                GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(pbr, name), false, ref identity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(pbr, "mtx_stack"), 32, false, IdentityStack());
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "use_override"), 1);
            GraphicsApi.Uniform4(GraphicsApi.GetUniformLocation(pbr, "override_color"), 1f, 0f, 0f, 1f);
            GraphicsApi.Normal3(0, 0, 1);
            GraphicsApi.Color4(1f, 1f, 1f, 1f);
            for (int mode = 1; mode <= 3; mode++)
            {
                GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "gbuffer_mode"), mode);
                DrawTexturedQuad(-1, -1, 1, 1, 0);
                byte[] pixel = new byte[4];
                GraphicsApi.ReadPixels(48, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                bool valid = mode == 1 ? pixel[0] > 245 && pixel[1] < 10 && pixel[2] < 10
                    : mode == 2 ? pixel[0] > 120 && pixel[1] > 120 && pixel[2] > 245
                    : pixel[0] < 10 && pixel[1] > 145 && pixel[2] < 10;
                if (!valid) throw new InvalidOperationException($"PBR target {mode} failed: {string.Join(",", pixel)}.");
            }
            // Read the actual material target, not just the colored surface:
            // built-in skins must retain distinct finishes on modern backends.
            (float Metal, float Rough)[] finishes = { (0.78f, 0.30f), (0.85f, 0.38f),
                (0.05f, 0.24f), (0.55f, 0.42f), (0.12f, 0.68f), (0.35f, 0.28f) };
            for (int skin = 1; skin <= finishes.Length; skin++)
            {
                GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "cosmetic_skin"), skin);
                DrawTexturedQuad(-1, -1, 1, 1, 0);
                byte[] pixel = new byte[4];
                GraphicsApi.ReadPixels(48, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                var expected = finishes[skin - 1];
                if (Math.Abs(pixel[0] / 255f - expected.Metal) > 0.015f
                    || Math.Abs(pixel[1] / 255f - expected.Rough) > 0.015f)
                    throw new InvalidOperationException($"Cosmetic material {skin} lost its PBR finish: {string.Join(",", pixel)}.");
            }
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "cosmetic_skin"), 0);
            int normalList = GraphicsApi.GenLists(1);
            GraphicsApi.NewList(normalList, ListMode.Compile);
            DrawTexturedQuad(-1, -1, 1, 1, 0);
            GraphicsApi.EndList();
            GraphicsApi.Normal3(1, 0, 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "gbuffer_mode"), 2);
            GraphicsApi.CallList(normalList);
            byte[] inheritedNormal = new byte[4];
            GraphicsApi.ReadPixels(48, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, inheritedNormal);
            if (inheritedNormal[0] < 245 || inheritedNormal[1] < 120 || inheritedNormal[2] is < 120 or > 135)
                throw new InvalidOperationException("Display list did not inherit its draw-time normal.");
            GraphicsApi.DeleteLists(normalList, 1);
            GraphicsApi.Normal3(0, 0, 1);
            ModernGraphicsCompat.Resize(0, 0);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            DrawTexturedQuad(-1, -1, 1, 1, 0);
            ModernGraphicsCompat.Present();
            ModernGraphicsCompat.Resize(96, 64);
            ModernGraphicsCompat.Present();
            GraphicsApi.UseProgram(0);
            GraphicsApi.DeleteProgram(pbr);
            GraphicsApi.DeleteProgram(post);
            GraphicsApi.DeleteTexture(texture);
        }

        private static void RunMipmapCheck()
        {
            // This check also runs after shader/foreground fixtures. Establish
            // the sampled draw state instead of inheriting their texture mode.
            GraphicsApi.UseProgram(0);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.Enable(EnableCap.Texture2D);
            foreach (var cap in new[] { EnableCap.DepthTest, EnableCap.CullFace,
                EnableCap.Blend, EnableCap.ScissorTest, EnableCap.StencilTest,
                EnableCap.AlphaTest })
                GraphicsApi.Disable(cap);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.Color4(1f, 1f, 1f, 1f);
            GraphicsApi.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);
            int texture = GraphicsApi.GenTexture();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            byte[] checker = new byte[8 * 8 * 4];
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int offset = (y * 8 + x) * 4;
                checker[offset + ((x + y) % 2 == 0 ? 0 : 2)] = 255;
                checker[offset + 3] = 255;
            }
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                8, 8, 0, PixelFormat.Rgba, PixelType.UnsignedByte, checker);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.LinearMipmapLinear);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GraphicsApi.TexParameter(TextureTarget.Texture2D, (TextureParameterName)0x84FE, 16);
            GraphicsApi.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord2(128, 0); GraphicsApi.Vertex3(1, 1, 0);
            GraphicsApi.TexCoord2(0, 0); GraphicsApi.Vertex3(-1, 1, 0);
            GraphicsApi.TexCoord2(128, 128); GraphicsApi.Vertex3(1, -1, 0);
            GraphicsApi.TexCoord2(0, 128); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();
            byte[] pixel = new byte[4];
            GraphicsApi.ReadPixels(48, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            int expected = 128; // UI sampling preserves encoded UI colors on an sRGB surface.
            if (Math.Abs(pixel[0] - expected) > 8 || pixel[1] > 8
                || Math.Abs(pixel[2] - expected) > 8 || pixel[3] < 245)
                throw new InvalidOperationException($"Mipmap minification failed: {string.Join(",", pixel)}.");
            ModernGraphicsCompat.Present();
            GraphicsApi.DeleteTexture(texture);
        }

        private static void RunLargeTextureMipmapCheck()
        {
            const int width = 4096, height = 256;
            int texture = GraphicsApi.GenTexture();
            try
            {
                byte[] pixels = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int at = (y * width + x) * 4;
                    bool red = (((x >> 4) + (y >> 4)) & 1) == 0;
                    pixels[at] = red ? (byte)255 : (byte)0;
                    pixels[at + 2] = red ? (byte)0 : (byte)255;
                    pixels[at + 3] = 255;
                }

                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GraphicsApi.Viewport(0, 0, 96, 64);
                GraphicsApi.UseProgram(0);
                GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                    PixelInternalFormat.Rgba8, width, height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
                GraphicsApi.GenerateMipmap(GenerateMipmapTarget.Texture2D);

                GraphicsApi.ClearColor(0, 0, 0, 1);
                GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                GraphicsApi.Enable(EnableCap.Texture2D);
                GraphicsApi.Disable(EnableCap.DepthTest);
                GraphicsApi.Disable(EnableCap.CullFace);
                GraphicsApi.Disable(EnableCap.Blend);
                GraphicsApi.Color4(1f, 1f, 1f, 1f);
                GraphicsApi.Begin(PrimitiveType.TriangleStrip);
                GraphicsApi.TexCoord2(256, 0); GraphicsApi.Vertex3( 1,  1, 0);
                GraphicsApi.TexCoord2(0, 0); GraphicsApi.Vertex3(-1,  1, 0);
                GraphicsApi.TexCoord2(256, 16); GraphicsApi.Vertex3( 1, -1, 0);
                GraphicsApi.TexCoord2(0, 16); GraphicsApi.Vertex3(-1, -1, 0);
                GraphicsApi.End();

                foreach ((int x, int y) in new[] { (24,16), (48,32), (72,48) })
                {
                    byte[] pixel = new byte[4];
                    GraphicsApi.ReadPixels(x, y, 1, 1,
                        PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                    if (Math.Abs(pixel[0] - 128) > 18 || pixel[1] > 10
                        || Math.Abs(pixel[2] - 128) > 18 || pixel[3] < 245)
                        throw new InvalidOperationException(
                            $"4K mip minification failed at {x},{y}: {string.Join(",", pixel)}.");
                }
                ModernGraphicsCompat.Present();
                Console.WriteLine("[renderwindowcheck] 4096-wide staged mip chain PASS");
            }
            finally
            {
                GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
                GraphicsApi.DeleteTexture(texture);
            }
        }

        private static void RunProgressiveTexturePromotionCheck()
        {
            const int width = 2048, height = 1536;
            int texture = GraphicsApi.GenTexture();
            try
            {
                GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                    PixelInternalFormat.Rgba8, 2, 2, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte,
                    new byte[]
                    {
                        255,0,0,255, 255,0,0,255,
                        255,0,0,255, 255,0,0,255
                    });
                ModernGraphicsCompat.EnsureBoundTextureResident();
                if (ModernGraphicsCompat.NativeTextureSizeForCheck(texture) != (2, 2))
                    throw new InvalidOperationException("Progressive upload fixture did not establish the live fallback texture.");

                byte[] replacement = new byte[width * height * 4];
                for (int at = 0; at < replacement.Length; at += 4)
                {
                    replacement[at + 1] = 255;
                    replacement[at + 3] = 255;
                }
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                    PixelInternalFormat.Rgba8, width, height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, replacement);
                GraphicsApi.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                GraphicsApi.GenerateMipmap(GenerateMipmapTarget.Texture2D);

                if (ModernGraphicsCompat.PendingTextureUploadCount != 1
                    || ModernGraphicsCompat.NativeTextureSizeForCheck(texture) != (2, 2))
                {
                    throw new InvalidOperationException(
                        "Large replacement did not retain the complete live fallback while promotion began.");
                }

                var pendingStorage = ModernGraphicsCompat.TrackedStorageCapacity;
                long pendingCapacity = TextureStorageMath.Bytes(width, height,
                    1 + (int)Math.Floor(Math.Log2(Math.Max(width, height))), 4, false);
                if (pendingStorage.TextureBytes < pendingCapacity + 2 * 2 * 4)
                    throw new InvalidOperationException("Tracked native texture capacity omitted a pending replacement or its retained fallback.");

                // 2048 RGBA rows are 8 KiB each. A 4 MiB frame budget therefore
                // uploads 512 rows, so this 1536-row image must take three
                // presentation boundaries before it becomes live.
                ModernGraphicsCompat.Present();
                if (ModernGraphicsCompat.PendingTextureUploadCount != 1
                    || ModernGraphicsCompat.NativeTextureSizeForCheck(texture) != (2, 2))
                    throw new InvalidOperationException("Progressive texture swapped after the first chunk.");
                ModernGraphicsCompat.Present();
                if (ModernGraphicsCompat.PendingTextureUploadCount != 1
                    || ModernGraphicsCompat.NativeTextureSizeForCheck(texture) != (2, 2))
                    throw new InvalidOperationException("Progressive texture swapped after the second chunk.");
                ModernGraphicsCompat.Present();
                if (ModernGraphicsCompat.PendingTextureUploadCount != 1
                    || ModernGraphicsCompat.NativeTextureSizeForCheck(texture) != (2, 2))
                    throw new InvalidOperationException("Progressive texture swapped before its mip chain was complete.");

                // Mips are deliberately one level per presentation after the
                // base image. Bound the loop so a stalled promotion is a test
                // failure rather than an infinite renderer check.
                int mipFrames = 0;
                while (ModernGraphicsCompat.PendingTextureUploadCount != 0 && mipFrames++ < 16)
                    ModernGraphicsCompat.Present();
                if (ModernGraphicsCompat.PendingTextureUploadCount != 0
                    || ModernGraphicsCompat.NativeTextureSizeForCheck(texture) != (width, height))
                {
                    throw new InvalidOperationException(
                        "Progressive texture did not atomically replace the fallback after its mip chain.");
                }
                Console.WriteLine($"[renderwindowcheck] multi-frame large texture promotion PASS mipFrames={mipFrames}");
            }
            finally
            {
                GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
                GraphicsApi.DeleteTexture(texture);
            }
        }

        private static byte[] RunWorldCompositeCheck()
        {
            int world = Link(MphRead.Shaders.VertexShader, MphRead.Shaders.FragmentShader);
            int rtt = Link(MphRead.Shaders.RttVertexShader, MphRead.Shaders.RttFragmentShader);
            int shift = Link(MphRead.Shaders.RttVertexShader, MphRead.Shaders.ShiftFragmentShader);
            int cel = Link(MphRead.Shaders.RttVertexShader, MphRead.Shaders.CelFragmentShader);
            int playerOutline = Link(PlayerOutlineShader.VertexSource, PlayerOutlineShader.Source);
            int toneMap = Link(GraphicsToneMapShader.VertexSource, GraphicsToneMapShader.FragmentSource);

            int color = GraphicsApi.GenTexture();
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                64, 64, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);

            int depth = GraphicsApi.GenTexture();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, depth);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                PixelInternalFormat.Depth24Stencil8, 64, 64, 0,
                PixelFormat.DepthStencil, PixelType.UnsignedInt248, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);

            int framebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment,
                TextureTarget.Texture2D, depth, 0);
            if (GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                != FramebufferErrorCode.FramebufferComplete)
            {
                throw new InvalidOperationException("World compatibility framebuffer did not complete.");
            }

            GraphicsApi.Viewport(0, 0, 64, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.ClearStencil(0);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit
                | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            GraphicsApi.UseProgram(world);
            GraphicsApi.Disable(EnableCap.Texture2D);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Enable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(true);
            GraphicsApi.DepthFunc(DepthFunction.Less);
            GraphicsApi.Disable(EnableCap.CullFace);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_light"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "show_colors"), 1);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "fog_enable"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_mode"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "texgen_mode"), 0);
            Matrix4 identity = Matrix4.Identity;
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "proj_mtx"), false, ref identity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "view_mtx"), false, ref identity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "view_inv_mtx"), false, ref identity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "tex_mtx"), false, ref identity);
            float[] stack = IdentityStack();
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "mtx_stack"),
                32, false, stack);

            // Far red control surface. Everything below should beat this in
            // the depth buffer; the later yellow surface should not.
            GraphicsApi.Color4(1, 0, 0, 1);
            DrawQuad(-0.9f, -0.9f, 0.9f, 0.9f, 0.55f);

            // Top half: a real sampled world texture, not just vertex colour.
            int greenTexture = GraphicsApi.GenTexture();
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, greenTexture);
            byte[] green =
            {
                0, 255, 0, 255, 0, 255, 0, 255,
                0, 255, 0, 255, 0, 255, 0, 255
            };
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                2, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, green);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.Enable(EnableCap.Texture2D);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 1);
            GraphicsApi.Color4(1, 1, 1, 1);
            DrawTexturedQuad(-0.9f, 0.0f, 0.9f, 0.9f, -0.45f);

            // Bottom half: untextured blue.
            GraphicsApi.Disable(EnableCap.Texture2D);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 0);
            GraphicsApi.Color4(0, 0, 1, 1);
            DrawQuad(-0.9f, -0.9f, 0.9f, 0.0f, -0.45f);

            // This is farther than both near halves and must fail depth.
            GraphicsApi.Color4(1, 1, 0, 1);
            DrawQuad(-0.9f, -0.9f, 0.9f, 0.9f, 0.75f);

            // Blend 50% red over only the top-right quadrant. Preserve target
            // alpha with ColorMask, matching several game overlay paths.
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.ColorMask(true, true, true, false);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 0.5f);
            GraphicsApi.Color4(1, 0, 0, 1);
            DrawQuad(0.0f, 0.0f, 0.9f, 0.9f, -0.7f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 1f);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Enable(EnableCap.DepthTest);
            byte[] linearBlendPixel = new byte[4];
            GraphicsApi.ReadPixels(48, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, linearBlendPixel);
            GraphicsApi.DeleteTexture(greenTexture);

            // Exercise the real cel program in its copy-free arrangement:
            // the finished world color remains immutable on unit 0 while the
            // outline resolves directly into a second color attachment.
            int celResolved = GraphicsApi.GenTexture();
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, celResolved);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                64, 64, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            int celFramebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, celFramebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, celResolved, 0);
            if (GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                != FramebufferErrorCode.FramebufferComplete)
            {
                throw new InvalidOperationException("Cel compatibility framebuffer did not complete.");
            }

            GraphicsApi.UseProgram(cel);
            GraphicsApi.ActiveTexture(TextureUnit.Texture1);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, depth);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "depth_tex"), 1);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "texel_w"), 1f / 64f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "texel_h"), 1f / 64f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "outline"), 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "near_plane"), 0.0625f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "far_plane"), 10000f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "depth_quantum"), 1f / 16777216f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(cel, "probe"), 0);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Disable(EnableCap.CullFace);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3( 1,  1, 0);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(-1,  1, 0);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3( 1, -1, 0);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();
            byte[] celCenter = new byte[4];
            GraphicsApi.ReadPixels(32, 16, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, celCenter);
            if (celCenter[2] < 200 || celCenter[3] < 220)
                throw new InvalidOperationException(
                    $"Cel compatibility pass corrupted interior color rgba({celCenter[0]},{celCenter[1]},{celCenter[2]},{celCenter[3]}).");
            GraphicsApi.ActiveTexture(TextureUnit.Texture1);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.UseProgram(rtt);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(false);
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, celResolved);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "alpha"), 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "use_mask"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "view_width"), 96f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "view_height"), 64f);
            GraphicsApi.Uniform4(GraphicsApi.GetUniformLocation(rtt, "fade_color"), Vector4.Zero);

            GraphicsApi.Color4(1, 1, 1, 1);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3( 1,  1, 0);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(-1,  1, 0);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3( 1, -1, 0);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();

            byte[] pixels = new byte[28];
            byte[] sample = new byte[4];
            GraphicsApi.ReadPixels(24, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 0, 4);
            GraphicsApi.ReadPixels(72, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 4, 4);
            GraphicsApi.ReadPixels(48, 16, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 8, 4);
            ModernGraphicsCompat.Present();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteFramebuffer(celFramebuffer);
            GraphicsApi.DeleteTexture(celResolved);

            // Second frame: replay-preview style scaled framebuffer blit using
            // independent read/draw bindings.
            GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.BlitFramebuffer(0, 0, 64, 64,
                0, 0, 48, 64, ClearBufferMask.ColorBufferBit,
                BlitFramebufferFilter.Linear);
            GraphicsApi.ReadPixels(24, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 20, 4);
            GraphicsApi.ReadPixels(24, 16, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 24, 4);
            ModernGraphicsCompat.Present();

            // Third frame: run the real disruption/whiteout program through
            // its array uniforms. A table of ones with white_fac=1 must
            // produce white regardless of the source scene colour.
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.UseProgram(shift);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "shift_idx"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "shift_fac"), 0f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "lerp_fac"), 0f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "shift_table"),
                64, new float[64]);
            float[] whiteTable = new float[192];
            Array.Fill(whiteTable, 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "white_table"),
                192, whiteTable);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "white_fac"), 1f);
            GraphicsApi.Color4(1, 1, 1, 1);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3( 1,  1, 0);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(-1,  1, 0);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3( 1, -1, 0);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();
            GraphicsApi.ReadPixels(48, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 12, 4);
            Array.Copy(linearBlendPixel, 0, pixels, 16, 4);
            ModernGraphicsCompat.Present();

            // Fourth frame: execute the custom player-outline program, not the
            // world fallback. The second opaque mask column borders a
            // transparent third column, so its interior-facing texel must
            // survive as an inward outline.
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            int outlineMask = GraphicsApi.GenTexture();
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, outlineMask);
            byte[] outlinePixels = new byte[4 * 4 * 4];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int p = (y * 4 + x) * 4;
                    bool opaque = x != 2;
                    outlinePixels[p + 0] = opaque ? (byte)255 : (byte)0;
                    outlinePixels[p + 1] = 0;
                    outlinePixels[p + 2] = 0;
                    outlinePixels[p + 3] = opaque ? (byte)255 : (byte)0;
                }
            }
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                4, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, outlinePixels);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GraphicsApi.UseProgram(playerOutline);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(playerOutline, "mask_tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(playerOutline, "outline_step_x"), 0.25f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(playerOutline, "outline_step_y"), 0.25f);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Disable(EnableCap.CullFace);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3( 1,  1, 0);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(-1,  1, 0);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3( 1, -1, 0);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();
            byte[] outlineEdge = new byte[4];
            GraphicsApi.ReadPixels(36, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, outlineEdge);
            if (outlineEdge[0] < 220 || outlineEdge[1] > 20
                || outlineEdge[2] > 20 || outlineEdge[3] < 220)
            {
                throw new InvalidOperationException(
                    $"Player-outline compatibility pass missed edge rgba({outlineEdge[0]},{outlineEdge[1]},{outlineEdge[2]},{outlineEdge[3]}).");
            }
            ModernGraphicsCompat.Present();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteTexture(outlineMask);

            // Fifth frame: render a genuinely overbright scene into RGBA16F,
            // then resolve it through the dedicated ACES tone-map program.
            int hdrTexture = GraphicsApi.GenTexture();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, hdrTexture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f,
                32, 32, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            int hdrFramebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, hdrFramebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hdrTexture, 0);
            if (GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("HDR compatibility framebuffer did not complete.");
            int plainDepth = GraphicsApi.GenTexture();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, plainDepth);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24,
                32, 32, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, plainDepth, 0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, hdrTexture);
            GraphicsApi.Viewport(0, 0, 32, 32);
            GraphicsApi.DepthMask(true);
            GraphicsApi.Disable(EnableCap.StencilTest);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GraphicsApi.UseProgram(world);
            GraphicsApi.Enable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(true);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_light"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "show_colors"), 1);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "fog_enable"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 1f);
            Matrix4 hdrIdentity = Matrix4.Identity;
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "proj_mtx"), false, ref hdrIdentity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "view_mtx"), false, ref hdrIdentity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "view_inv_mtx"), false, ref hdrIdentity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "mtx_stack"), false, ref hdrIdentity);
            // The attachment remains bound on unit 0. Active feedback must fail
            // before native submission; switching sampling off must render safely.
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 1);
            bool feedbackRejected = false;
            try { DrawQuad(-1, -1, 1, 1, 0); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("WebGPU feedback hazard"))
            {
                feedbackRejected = ex.Message.Contains("unit=0") && ex.Message.Contains("ColorAttachment");
            }
            if (!feedbackRejected) throw new InvalidOperationException("Active attachment feedback was not rejected.");
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 0);
            GraphicsApi.Color4(4f, 1f, 0.25f, 1f);
            DrawQuad(-1, -1, 1, 1, 0);
            // Sampler edits and expansion to a mip chain must retain GPU-only
            // HDR contents, including values above one.
            GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.Repeat);
            GraphicsApi.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.Disable(EnableCap.StencilTest);
            GraphicsApi.UseProgram(toneMap);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, hdrTexture);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(toneMap, "hdr_tex"), 0);
            GraphicsApi.Color4(1, 1, 1, 1);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3( 1,  1, 0);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(-1,  1, 0);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3( 1, -1, 0);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();
            byte[] hdrResolved = new byte[4];
            GraphicsApi.ReadPixels(48, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, hdrResolved);
            bool srgb = ModernGraphicsCompat.SurfaceUsesSrgb;
            if (hdrResolved[0] < 235 || hdrResolved[1] < (srgb ? 220 : 175) || hdrResolved[1] > (srgb ? 245 : 220)
                || hdrResolved[2] < (srgb ? 150 : 80) || hdrResolved[2] > (srgb ? 180 : 120) || hdrResolved[3] < 220)
            {
                throw new InvalidOperationException(
                    $"HDR tone-map compatibility resolve unexpected rgba({hdrResolved[0]},{hdrResolved[1]},{hdrResolved[2]},{hdrResolved[3]}).");
            }
            ModernGraphicsCompat.Present();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteFramebuffer(hdrFramebuffer);
            GraphicsApi.DeleteTexture(plainDepth);
            GraphicsApi.DeleteTexture(hdrTexture);

            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteFramebuffer(framebuffer);
            GraphicsApi.DeleteTexture(depth);
            GraphicsApi.DeleteTexture(color);
            GraphicsApi.DeleteProgram(world);
            GraphicsApi.DeleteProgram(rtt);
            GraphicsApi.DeleteProgram(shift);
            GraphicsApi.DeleteProgram(cel);
            GraphicsApi.DeleteProgram(playerOutline);
            GraphicsApi.DeleteProgram(toneMap);
            return pixels;
        }

        private static void DrawQuad(float left, float bottom, float right, float top, float z)
        {
            GraphicsApi.Begin(PrimitiveType.Quads);
            GraphicsApi.Vertex3(left, bottom, z);
            GraphicsApi.Vertex3(right, bottom, z);
            GraphicsApi.Vertex3(right, top, z);
            GraphicsApi.Vertex3(left, top, z);
            GraphicsApi.End();
        }

        private static void DrawTexturedQuad(float left, float bottom, float right, float top, float z)
        {
            GraphicsApi.Begin(PrimitiveType.Quads);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(left, bottom, z);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3(right, bottom, z);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3(right, top, z);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(left, top, z);
            GraphicsApi.End();
        }

        private static int Link(string vertexSource, string fragmentSource)
        {
            int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
            GraphicsApi.ShaderSource(vertex, vertexSource);
            GraphicsApi.CompileShader(vertex);
            GraphicsApi.GetShader(vertex, ShaderParameter.CompileStatus, out int vertexOk);
            int fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
            GraphicsApi.ShaderSource(fragment, fragmentSource);
            GraphicsApi.CompileShader(fragment);
            GraphicsApi.GetShader(fragment, ShaderParameter.CompileStatus, out int fragmentOk);
            if (vertexOk == 0 || fragmentOk == 0)
                throw new InvalidOperationException("Compatibility shader bookkeeping did not compile.");

            int program = GraphicsApi.CreateProgram();
            GraphicsApi.AttachShader(program, vertex);
            GraphicsApi.AttachShader(program, fragment);
            GraphicsApi.LinkProgram(program);
            GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            GraphicsApi.DetachShader(program, vertex);
            GraphicsApi.DetachShader(program, fragment);
            GraphicsApi.DeleteShader(vertex);
            GraphicsApi.DeleteShader(fragment);
            if (linked == 0) throw new InvalidOperationException("Compatibility program did not link.");
            return program;
        }

        private static float[] IdentityStack()
        {
            var result = new float[32 * 16];
            for (int m = 0; m < 32; m++)
            {
                int b = m * 16;
                result[b + 0] = 1;
                result[b + 5] = 1;
                result[b + 10] = 1;
                result[b + 15] = 1;
            }
            return result;
        }
    }
}
#endif
