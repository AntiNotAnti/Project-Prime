using System;
using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

internal static class SceneTargetChecks
{
    internal static int Run()
    {
        var scene = new Scene(new(32, 32), SyntheticInput.CreateKeyboard(),
            SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
        MethodInfo replace = Method("ReplaceSceneTargets"), depth = Method("UpdateDepthAttachment");
        FieldInfo callback = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.OpenGlFailureInjection")!
            .GetField("Callback", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool originalCel = RenderOptions.CelShading;
        float originalEdge = RenderOptions.CelEdge;
        ShadowQuality originalShadows = RenderOptions.Shadows;
        int injectedFramebuffer = 0, injectedColor = 0;
        try
        {
            RenderOptions.CelShading = false; RenderOptions.Shadows = ShadowQuality.Off;
            Check((bool)replace.Invoke(scene, new object[] { new Vector2i(32, 32) })!, "initial bundle drawable");
            int initialFramebuffer = Value("_frameBuffer"), initialColor = Value("_screenTexture");
            Check(Value("_celTexture") == 0 && Value("_renderBuffer") != 0, "cel off has one color and one depth owner");
            callback.SetValue(null, (Action<string>)(stage =>
            {
                if (stage != "scene-target-before-publish") return;
                injectedFramebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
                GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    FramebufferParameterName.FramebufferAttachmentObjectName, out injectedColor);
                throw new InvalidOperationException("synthetic replacement failure");
            }));
            Check(!(bool)replace.Invoke(scene, new object[] { new Vector2i(40, 24) })!, "injected failure refuses replacement");
            callback.SetValue(null, null);
            Check(Value("_frameBuffer") == initialFramebuffer && Value("_screenTexture") == initialColor
                && GL.IsTexture(initialColor), "failure retains original owned bundle");
            Check(!GL.IsFramebuffer(injectedFramebuffer) && !GL.IsTexture(injectedColor), "failure retires temporary bundle");
            Check(!(bool)replace.Invoke(scene, new object[] { new Vector2i(40, 24) })!, "same refused request is bounded");
            RenderOptions.CelShading = true; RenderOptions.CelEdge = 1;
            Check((bool)replace.Invoke(scene, new object[] { new Vector2i(48, 24) })!, "size/settings transition retries deliberately");
            Check(!GL.IsFramebuffer(initialFramebuffer) && !GL.IsTexture(initialColor), "successful replacement retires old names");
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Value("_frameBuffer"));
            depth.Invoke(scene, new object[] { new Vector2i(48, 24) });
            Check(Value("_depthTexture") != 0 && Value("_renderBuffer") == 0, "readable depth has no redundant renderbuffer");
            int cel = Value("_celTexture");
            Check(cel != 0, "cel target allocated on demand");
            RenderOptions.CelShading = false;
            Method("UpdateCelColorTarget").Invoke(scene, null);
            int readableDepth = Value("_depthTexture");
            callback.SetValue(null, (Action<string>)(stage =>
            {
                if (stage == "renderbuffer-depth-before-publish")
                    throw new InvalidOperationException("synthetic depth replacement failure");
            }));
            depth.Invoke(scene, new object[] { new Vector2i(48, 24) });
            callback.SetValue(null, null);
            Check(Value("_depthTexture") == readableDepth && GL.IsTexture(readableDepth)
                && Value("_renderBuffer") == 0, "failed renderbuffer transition preserves readable depth");
            Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete,
                "failed depth replacement leaves scene drawable");
            RenderOptions.CelShading = true;
            depth.Invoke(scene, new object[] { new Vector2i(48, 24) });
            RenderOptions.CelShading = false;
            depth.Invoke(scene, new object[] { new Vector2i(48, 24) });
            Check(Value("_celTexture") == 0 && !GL.IsTexture(cel), "cel off releases optional storage");
            Check(Value("_depthTexture") == 0 && Value("_renderBuffer") != 0, "opaque depth fallback restored");
            Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete,
                "target remains complete after depth transitions");

            // Model a supersampled scene with a smaller processed output. Read
            // right/top edges to detect reading with the scene's old dimensions.
            int processed = GL.GenFramebuffer(), color = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, color);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                17, 11, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, processed);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, color, 0);
            GL.Disable(EnableCap.ScissorTest); GL.ColorMask(true, true, true, true);
            GL.ClearColor(1, 0, 0, 1); GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.Enable(EnableCap.ScissorTest); GL.Scissor(16, 10, 1, 1);
            GL.ClearColor(0, 1, 0, 1); GL.Clear(ClearBufferMask.ColorBufferBit); GL.Disable(EnableCap.ScissorTest);
            Field("_graphicsOutputFramebuffer").SetValue(scene, processed);
            Field("_graphicsOutputTexture").SetValue(scene, color);
            Field("_graphicsOutputSize").SetValue(scene, new Vector2i(17, 11));
            Field("_graphicsOutputReady").SetValue(scene, true);
            byte[] rgb = scene.ReadSceneTarget(out int width, out int height)!;
            Check(width == 17 && height == 11 && rgb.Length == 17 * 11 * 3,
                "processed capture descriptor carries actual dimensions");
            Check(rgb[^3] == 0 && rgb[^2] == 255 && rgb[0] == 255,
                "processed capture includes exact edge pixels");
            Check(GL.GetError() == ErrorCode.NoError, "target and capture transitions have no GL errors");
            Console.WriteLine("SCENETARGETS PASS Windows native replacement/failure/cel/depth/capture");
            return 0;
        }
        finally
        {
            callback.SetValue(null, null); scene.UnloadGl();
            RenderOptions.CelShading = originalCel; RenderOptions.CelEdge = originalEdge;
            RenderOptions.Shadows = originalShadows;
        }
        int Value(string name) => (int)Field(name).GetValue(scene)!;
    }

    private static MethodInfo Method(string name) => typeof(Scene).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static FieldInfo Field(string name) => typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
