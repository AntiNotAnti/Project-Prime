using System;
using System.Reflection;
using MphRead;
using MphRead.Mods.Input;
using MphRead.Mods;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;

// Exercise the real cel target allocator and scene teardown in one persistent
// GL context. Disable runtime initialization to avoid music/game data while
// preserving the constructor-owned registries that real scene teardown uses.
int failures = 0;
Type contextPolicy = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.DesktopGlContext")!;
MethodInfo? monitorGuard = contextPolicy.GetMethod("RequirePrimaryDisplay", BindingFlags.Static | BindingFlags.NonPublic);
bool missingDisplayRejected = false;
try { monitorGuard?.Invoke(null, new object[] { IntPtr.Zero }); }
catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException inner)
{ missingDisplayRejected = inner.Message.Contains("Wake or connect a display"); }
Check(missingDisplayRejected, "missing primary monitor fails in managed code before native video-mode query");
var settings = (NativeWindowSettings)contextPolicy.GetMethod("Settings")!.Invoke(null, new object[] { true })!;
settings.ClientSize = new Vector2i(32, 32); settings.StartVisible = false; settings.StartFocused = false;
using var window = new NativeWindow(settings);
window.Context.MakeCurrent();
GL.LoadBindings(new GLFWBindingsContext());
for (int cycle = 0; cycle < 3; cycle++)
{
    var scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(),
        SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
    int texture = GL.GenTexture();
    GL.BindTexture(TextureTarget.Texture2D, texture);
    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb,
        32, 32, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
    Field("_screenTexture").SetValue(scene, texture);
    MethodInfo allocate = typeof(Scene).GetMethod("CelFrameBuffer", BindingFlags.Instance | BindingFlags.NonPublic)!;
    int framebuffer = (int)allocate.Invoke(scene, null)!;
    Check(GL.IsFramebuffer(framebuffer), "cel framebuffer allocated");
    Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete,
        "synthetic cel attachment complete");
    Check((int)allocate.Invoke(scene, null)! == framebuffer, "cel target reused within a scene");
    // Gameplay ends a frame on the default framebuffer. The leaked, unbound
    // cel framebuffer can keep its deleted color texture's storage alive.
    GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    GL.BindTexture(TextureTarget.Texture2D, 0);
    scene.UnloadGl();
    Check(!GL.IsFramebuffer(framebuffer), "scene unload deletes cel framebuffer");
    Check((int)Field("_celFrameBuffer").GetValue(scene)! == 0
        && (int)Field("_celFrameBufferColor").GetValue(scene)! == 0, "cel handles cleared");
    Check(!GL.IsTexture(texture), "scene color texture deleted");
    scene.UnloadGl();
    Check(GL.GetError() == ErrorCode.NoError, "repeated unload is harmless");
    // Clean up the negative-control leak too, so a failing run is bounded.
    if (GL.IsFramebuffer(framebuffer)) GL.DeleteFramebuffer(framebuffer);
    foreach ((ShadowQuality quality, int size) in new[] {
        (ShadowQuality.Low, 1024), (ShadowQuality.High, 2048), (ShadowQuality.Ultra, 4096) })
    {
        RenderOptions.Shadows = quality;
        typeof(Scene).GetMethod("EnsureShadowTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scene, null);
        int shadowFbo = (int)Field("_shadowFramebuffer").GetValue(scene)!;
        int depth = (int)Field("_shadowDepthTexture").GetValue(scene)!;
        Check(GL.IsFramebuffer(shadowFbo) && GL.IsTexture(depth), $"{quality} shadow targets allocated");
        Check((int)Field("_shadowTargetSize").GetValue(scene)! == size
            && GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete,
            $"{quality} shadow dimensions and framebuffer complete");
        RenderOptions.Shadows = ShadowQuality.Off;
        typeof(Scene).GetMethod("RenderShadowMap", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scene, null);
        Check(!GL.IsFramebuffer(shadowFbo) && !GL.IsTexture(depth)
            && (int)Field("_shadowTargetSize").GetValue(scene)! == 0, $"{quality} shadow off releases attachments");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Check(GL.GetError() == ErrorCode.NoError, $"{quality} shadow lifecycle has no GL errors");
    }
}
Console.WriteLine($"RENDERRESOURCES failures={failures}");
return failures == 0 ? 0 : 1;

static FieldInfo Field(string name) => typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

void Check(bool success, string name)
{
    Console.WriteLine($"RENDERRESOURCES {(success ? "PASS" : "FAIL")} {name}");
    if (!success) failures++;
}
