#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>Asset-free pixel fixture using the production world and PBR shaders.</summary>
internal static class ModernPbrLayerCheck
{
    internal static void Verify()
    {
        using var diagnosticScope = ModernGraphicsDevice.BeginLayeredPbrShaderDiagnosticScopeForCheck();
        try { VerifyScoped(); }
        catch (Exception ex)
        {
            ShaderDiagnosticPolicy.WriteException(Console.Error, "layered-pbr outer", ex);
            throw;
        }
    }

    private static void VerifyScoped()
    {
        ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=state-snapshot START");
        int previousProgram = GraphicsApi.GetInteger(GetPName.CurrentProgram);
        int previousReadFramebuffer = GraphicsApi.GetInteger(GetPName.ReadFramebufferBinding);
        int previousDrawFramebuffer = GraphicsApi.GetInteger(GetPName.DrawFramebufferBinding);
        bool previousTextureEnabled = GraphicsApi.IsEnabled(EnableCap.Texture2D);
        int[] previousViewport = new int[4];
        GraphicsApi.GetInteger(GetPName.Viewport, previousViewport);
        GraphicsApi.PushAttrib(AttribMask.AllAttribBits);
        try
        {
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=state-snapshot DONE");
            VerifyPixels();
        }
        catch (Exception ex)
        {
            ShaderDiagnosticPolicy.WriteException(Console.Error, "layered-pbr before state restoration", ex);
            throw;
        }
        finally
        {
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=state-restore START");
            // Attribute stacks include texture enables/bindings and viewport,
            // but programs and framebuffer bindings need explicit restoration.
            GraphicsApi.UseProgram(previousProgram);
            GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousReadFramebuffer);
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDrawFramebuffer);
            GraphicsApi.PopAttrib();
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=state-restore DONE");
        }
        ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=state-verification START");
        int[] restoredViewport = new int[4];
        GraphicsApi.GetInteger(GetPName.Viewport, restoredViewport);
        if (GraphicsApi.GetInteger(GetPName.CurrentProgram) != previousProgram
            || GraphicsApi.GetInteger(GetPName.ReadFramebufferBinding) != previousReadFramebuffer
            || GraphicsApi.GetInteger(GetPName.DrawFramebufferBinding) != previousDrawFramebuffer
            || GraphicsApi.IsEnabled(EnableCap.Texture2D) != previousTextureEnabled)
            throw new InvalidOperationException("PBR pixel fixture did not restore its caller's render state.");
        for (int i = 0; i < previousViewport.Length; i++)
            if (restoredViewport[i] != previousViewport[i])
                throw new InvalidOperationException("PBR pixel fixture did not restore its caller's viewport.");
        ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=state-verification DONE");
    }

    private static void VerifyPixels()
    {
        ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=setup START");
        int world = Link(Shaders.VertexShader, Shaders.FragmentShader);
        int post = Link(GraphicsPipelineShader.VertexSource, GraphicsPipelineShader.FragmentSource);
        int pbr = Link(DeferredPbrShader.VertexSource, DeferredPbrShader.FragmentSource);
        int toneMap = Link(GraphicsToneMapShader.VertexSource, GraphicsToneMapShader.FragmentSource);
        int scene = ColorTexture(8, new byte[] { 51, 102, 153, 255 });
        int resolved = ColorTexture(4, new byte[] { 0, 0, 0, 255 });
        int output = ColorTexture(8, new byte[] { 0, 0, 0, 255 });
        int albedo = ColorTexture(1, new byte[] { 224, 64, 32, 255 });
        int normal = ColorTexture(1, new byte[] { 128, 128, 255, 255 });
        int material = ColorTexture(1, new byte[] { 0, 153, 0, 255 });
        int cutout = ColorTexture(1, new byte[] { 255, 255, 255, 254 });
        int hdrSource = FloatColorTexture(1, new float[] { 4f, 2f, 0.5f, 1f });
        int hdrResolved = FloatColorTexture(4, new float[] { 0, 0, 0, 1 });
        int hdrScene = FloatColorTexture(8, new float[] { 0, 0, 0, 1 });
        int ownedDepth = DepthTexture(4);
        int forwardDepth = DepthTexture(8);
        int depthColor = ColorTexture(4, new byte[] { 0, 0, 0, 255 });
        int sceneFbo = Framebuffer(scene, forwardDepth);
        int resolveFbo = Framebuffer(resolved);
        int outputFbo = Framebuffer(output);
        int depthFbo = Framebuffer(depthColor, ownedDepth);
        try
        {
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=setup DONE");
            GraphicsApi.Disable(EnableCap.CullFace);
            GraphicsApi.Disable(EnableCap.AlphaTest);
            GraphicsApi.Disable(EnableCap.ScissorTest);
            GraphicsApi.Disable(EnableCap.StencilTest);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.DepthMask(true);
            GraphicsApi.Disable(EnableCap.Blend);

            // Reduced G-buffer depth contains an opaque surface. The larger
            // forward depth deliberately contains only clear depth at resolve.
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=clear-01 START");
            GraphicsApi.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=clear-01 DONE");
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, depthFbo);
            GraphicsApi.Viewport(0, 0, 4, 4);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=clear-02 START");
            GraphicsApi.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=clear-02 DONE");
            ConfigureWorld(world);
            GraphicsApi.Enable(EnableCap.DepthTest);
            GraphicsApi.DepthFunc(DepthFunction.Less);
            GraphicsApi.Color4(1, 1, 1, 1);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-01 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-01 DONE");

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, resolveFbo);
            GraphicsApi.Viewport(0, 0, 4, 4);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(false);
            ConfigurePost(post, scene, ownedDepth, albedo, normal, material, 2);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-02 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-02 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-01 START");
            byte[] background = Pixel(2, 2);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-01 DONE");
            if (Math.Abs(background[0] - 51) + Math.Abs(background[1] - 102)
                + Math.Abs(background[2] - 153) < 20)
                throw new InvalidOperationException("PBR resolve did not use the reduced G-buffer's readable depth.");

            GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, resolveFbo);
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, sceneFbo);
            GraphicsApi.BlitFramebuffer(0, 0, 4, 4, 0, 0, 8, 8,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
            GraphicsApi.Viewport(0, 0, 8, 8);
            ConfigureWorld(world);
            GraphicsApi.Enable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(true);
            GraphicsApi.DepthFunc(DepthFunction.Less);

            // Foreground uses another projection, as a weapon model does.
            Matrix4 foregroundProjection = Matrix4.CreateScale(1f, 0.9f, 0.7f);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "proj_mtx"),
                false, ref foregroundProjection);
            GraphicsApi.Color4(0, 0, 1, 1);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-03 START");
            Quad(-1, 0, -0.5f);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-03 DONE");
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 0.5f);
            GraphicsApi.Color4(1, 0, 0, 1);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-04 START");
            Quad(0, 1, -0.5f);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-04 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-02 START");
            byte[] expectedTransparent = Pixel(6, 4);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-02 DONE");

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, outputFbo);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(false);
            ConfigurePost(post, scene, forwardDepth, albedo, normal, material, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-05 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-05 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-03 START");
            byte[] foreground = Pixel(2, 4);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-03 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-04 START");
            byte[] transparent = Pixel(6, 4);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-04 DONE");
            if (foreground[0] > 3 || foreground[1] > 3 || foreground[2] < 252)
                throw new InvalidOperationException("Background PBR contaminated the foreground projection layer.");
            if (transparent[3] < 252)
                throw new InvalidOperationException("Final opaque presentation did not close the transparent layer's alpha.");
            for (int c = 0; c < 3; c++)
                if (Math.Abs(transparent[c] - expectedTransparent[c]) > 2)
                {
                    ReportTransparentMismatch(expectedTransparent, transparent, c);
                    throw new InvalidOperationException("Final presentation relit or attenuated a transparent layer after PBR.");
                }
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(post, "gamma_value"), 2f);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-06 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-06 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-05 START");
            byte[] gradedTransparent = Pixel(6, 4);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-05 DONE");
            for (int c = 0; c < 3; c++)
                if (Math.Abs(gradedTransparent[c]
                    - (int)Math.Round(Math.Sqrt(expectedTransparent[c] / 255.0) * 255)) > 2)
                    throw new InvalidOperationException("Final gamma presentation was skipped or applied twice.");

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, resolveFbo);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hdrResolved, 0);
            GraphicsApi.Viewport(0, 0, 4, 4);
            ConfigurePost(post, hdrSource, ownedDepth, albedo, normal, material, 2);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-07 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-07 DONE");
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, sceneFbo);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, hdrScene, 0);
            GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, resolveFbo);
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, sceneFbo);
            GraphicsApi.BlitFramebuffer(0, 0, 4, 4, 0, 0, 8, 8,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, outputFbo);
            GraphicsApi.Viewport(0, 0, 8, 8);
            GraphicsApi.UseProgram(toneMap);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, hdrScene);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(toneMap, "hdr_tex"), 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-08 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-08 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-06 START");
            byte[] hdrPixel = Pixel(6, 4);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-06 DONE");
            // ACES of a value clamped to one is only ~205 in this linear RGBA8
            // target. This proves the early resolve/copy preserved >1 to the
            // dedicated final tone map, rather than tone mapping/clamping twice.
            if (hdrPixel[0] <= 225 || hdrPixel[3] < 252)
                throw new InvalidOperationException("HDR PBR color was truncated during the opaque scene copy.");

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, depthFbo);
            GraphicsApi.Viewport(0, 0, 4, 4);
            GraphicsApi.ClearColor(0, 0.5f, 0, 1);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=clear-03 START");
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=clear-03 DONE");
            ConfigureWorld(pbr);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, cutout);
            GraphicsApi.Color4(1, 1, 1, 1);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "use_texture"), 1);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "use_override"), 1);
            GraphicsApi.Uniform4(GraphicsApi.GetUniformLocation(pbr, "override_color"), 1f, 0f, 0f, 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(pbr, "gbuffer_mode"), 1);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-09 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-09 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-07 START");
            byte[] rejectedCutout = Pixel(2, 2);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-07 DONE");
            if (rejectedCutout[0] > 3 || Math.Abs(rejectedCutout[1] - 128) > 2)
                throw new InvalidOperationException("A non-opaque filtered texel entered the opaque G-buffer.");
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 255, 255, 255 });
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-10 START");
            Quad(-1, 1, 0);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=draw-10 DONE");
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-08 START");
            byte[] acceptedCutout = Pixel(2, 2);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=pixel-read-08 DONE");
            if (acceptedCutout[0] < 252 || acceptedCutout[1] > 3)
                throw new InvalidOperationException("A fully opaque texel was lost from the G-buffer.");
            Console.WriteLine("[renderwindowcheck] reduced PBR depth, foreground FOV, single dynamic lighting, transparency, final gamma/HDR and exact opaque-alpha pixel ownership PASS");
        }
        catch (Exception ex)
        {
            ShaderDiagnosticPolicy.WriteException(Console.Error, "layered-pbr before resource cleanup", ex);
            throw;
        }
        finally
        {
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=cleanup START");
            GraphicsApi.UseProgram(0);
            for (int unit = 6; unit >= 0; unit--)
            {
                GraphicsApi.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + unit));
                GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            }
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.DepthMask(true);
            foreach (int fbo in new[] { sceneFbo, resolveFbo, outputFbo, depthFbo })
                GraphicsApi.DeleteFramebuffer(fbo);
            foreach (int tex in new[] { scene, resolved, output, albedo, normal,
                material, cutout, ownedDepth, forwardDepth, depthColor,
                hdrSource, hdrResolved, hdrScene })
                GraphicsApi.DeleteTexture(tex);
            GraphicsApi.DeleteProgram(world);
            GraphicsApi.DeleteProgram(post);
            GraphicsApi.DeleteProgram(pbr);
            GraphicsApi.DeleteProgram(toneMap);
            ShaderDiagnosticPolicy.Write(Console.Out, "DIAGNOSTIC renderwindowcheck layered-pbr phase=cleanup DONE");
        }
    }

    private static void ConfigurePost(int program, int scene, int depth,
        int albedo, int normal, int material, int mode)
    {
        GraphicsApi.UseProgram(program);
        int[] textures = { scene, depth, 0, 0, albedo, normal, material };
        string[] samplers = { "tex", "depth_tex", "shadow_tex", "history_tex",
            "pbr_albedo", "pbr_normal", "pbr_material" };
        for (int unit = 0; unit < textures.Length; unit++)
        {
            GraphicsApi.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + unit));
            GraphicsApi.BindTexture(TextureTarget.Texture2D, textures[unit]);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, samplers[unit]), unit);
        }
        GraphicsApi.ActiveTexture(TextureUnit.Texture0);
        bool depthAvailable = Scene.UseFinalSceneDepth(true, true,
            opaqueWorldResolved: mode == 0);
        GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, "depth_available"), depthAvailable ? 1 : 0);
        GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, "pbr_enabled"), mode);
        foreach (string name in new[] { "gamma_value", "contrast_value", "saturation_value" })
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, name), 1f);
        GraphicsApi.Uniform2(GraphicsApi.GetUniformLocation(program, "texel"), 0.25f, 0.25f);
        GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(program, "camera_position"), new Vector3(0, 0, 2));
        GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(program, "pbr_light1_dir"), new Vector3(0, 0, -1));
        GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(program, "pbr_light1_color"), Vector3.One);
        GraphicsApi.Uniform3(GraphicsApi.GetUniformLocation(program, "pbr_light2_dir"), new Vector3(0, 0, -1));
        // Keep a light active through both stages deliberately. Depth ownership
        // alone must prevent generic final lighting over the resolved material
        // and over the foreground's separate projection.
        GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, "dynamic_light_count"), 1);
        GraphicsApi.Uniform4(GraphicsApi.GetUniformLocation(program, "dynamic_light_pos[0]"), 0f, 0f, 1f, 8f);
        GraphicsApi.Uniform4(GraphicsApi.GetUniformLocation(program, "dynamic_light_color[0]"), 0.8f, 0.2f, 0.1f, 0.6f);
        Matrix4 identity = Matrix4.Identity;
        foreach (string name in new[] { "inv_projection", "inv_view", "view_matrix", "projection" })
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(program, name), false, ref identity);
    }

    private static void ConfigureWorld(int program)
    {
        GraphicsApi.UseProgram(program);
        GraphicsApi.Disable(EnableCap.Texture2D);
        GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, "show_colors"), 1);
        GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(program, "mat_alpha"), 1f);
        Matrix4 identity = Matrix4.Identity;
        foreach (string name in new[] { "proj_mtx", "view_mtx", "view_inv_mtx", "tex_mtx" })
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(program, name), false, ref identity);
        float[] stack = new float[32 * 16];
        for (int i = 0; i < 32; i++)
            stack[i * 16] = stack[i * 16 + 5] = stack[i * 16 + 10] = stack[i * 16 + 15] = 1;
        GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(program, "mtx_stack"), 32, false, stack);
        GraphicsApi.Normal3(0, 0, 1);
    }

    private static int ColorTexture(int size, byte[] pixel)
    {
        int texture = GraphicsApi.GenTexture();
        GraphicsApi.ActiveTexture(TextureUnit.Texture0);
        GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
        byte[] pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4) pixel.CopyTo(pixels, i);
        GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        return texture;
    }

    private static int DepthTexture(int size)
    {
        int texture = GraphicsApi.GenTexture();
        GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
        GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Depth24Stencil8,
            size, size, 0, PixelFormat.DepthStencil, PixelType.UnsignedInt248, IntPtr.Zero);
        return texture;
    }

    private static int FloatColorTexture(int size, float[] pixel)
    {
        int texture = GraphicsApi.GenTexture();
        GraphicsApi.ActiveTexture(TextureUnit.Texture0);
        GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
        float[] pixels = new float[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4) pixel.CopyTo(pixels, i);
        GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f,
            size, size, 0, PixelFormat.Rgba, PixelType.Float, pixels);
        GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GraphicsApi.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        return texture;
    }

    private static int Framebuffer(int color, int depth = 0)
    {
        int fbo = GraphicsApi.GenFramebuffer();
        GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
        if (depth != 0) GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment, TextureTarget.Texture2D, depth, 0);
        if (GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException("PBR pixel fixture framebuffer is incomplete.");
        return fbo;
    }

    private static void ReportTransparentMismatch(byte[] expected, byte[] actual, int channel)
    {
        try
        {
            if (!ShaderDiagnosticPolicy.Enabled(
                Environment.GetEnvironmentVariable(ShaderDiagnosticPolicy.EnvironmentVariable),
                Environment.GetEnvironmentVariable("PRIME_WGPU_VALIDATION"),
                Environment.GetEnvironmentVariable("PRIME_WGPU_GPU_VALIDATION")))
                return;
            ShaderDiagnosticPolicy.Write(Console.Error,
                $"DIAGNOSTIC renderwindowcheck layered-pbr transparent-comparison expectedRead=2 actualRead=4 channel={channel} absoluteDelta={Math.Abs(actual[channel] - expected[channel])} expectedRgba={expected[0]},{expected[1]},{expected[2]},{expected[3]} actualRgba={actual[0]},{actual[1]},{actual[2]},{actual[3]}");
        }
        catch { } // Observation cannot replace the unchanged strict pixel failure.
    }

    private static byte[] Pixel(int x, int y)
    {
        byte[] pixel = new byte[4];
        GraphicsApi.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
        return pixel;
    }

    private static void Quad(float left, float right, float z)
    {
        GraphicsApi.Begin(PrimitiveType.Quads);
        GraphicsApi.TexCoord2(0, 0); GraphicsApi.Vertex3(left, -1, z);
        GraphicsApi.TexCoord2(1, 0); GraphicsApi.Vertex3(right, -1, z);
        GraphicsApi.TexCoord2(1, 1); GraphicsApi.Vertex3(right, 1, z);
        GraphicsApi.TexCoord2(0, 1); GraphicsApi.Vertex3(left, 1, z);
        GraphicsApi.End();
    }

    private static int Link(string vertexSource, string fragmentSource)
    {
        int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
        int fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
        GraphicsApi.ShaderSource(vertex, vertexSource); GraphicsApi.CompileShader(vertex);
        GraphicsApi.ShaderSource(fragment, fragmentSource); GraphicsApi.CompileShader(fragment);
        int program = GraphicsApi.CreateProgram();
        GraphicsApi.AttachShader(program, vertex); GraphicsApi.AttachShader(program, fragment);
        GraphicsApi.LinkProgram(program);
        GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        GraphicsApi.DetachShader(program, vertex); GraphicsApi.DetachShader(program, fragment);
        GraphicsApi.DeleteShader(vertex); GraphicsApi.DeleteShader(fragment);
        if (linked == 0) throw new InvalidOperationException("PBR pixel fixture shader failed to link.");
        return program;
    }
}
#endif
