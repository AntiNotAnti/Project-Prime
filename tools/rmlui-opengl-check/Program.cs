using System;
using System.IO;
using MphRead.Mods.Render;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("RMLUI GL PASS " + name);
}

if (args.Length != 1 || !File.Exists(Path.Combine(args[0], "prime_home.rml")))
    throw new ArgumentException("Pass the RmlUi asset directory containing prime_home.rml.");

Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_PROFILE", "1");
GraphicsBackendPolicy.Configure("opengl");
var settings = DesktopGlContext.Settings(background: true);
settings.ClientSize = new(960, 600);
using var window = new NativeWindow(settings);
using var context = new DesktopGraphicsSession(window);
int width = window.FramebufferSize.X;
int height = window.FramebufferSize.Y;
Check(width > 0 && height > 0, "GL framebuffer created");

using var host = new RmlUiHost();
Check(host.Initialize(width, height, 1, Path.GetFullPath(args[0]), RmlUiRenderBackend.OpenGl),
    "native GL2 bridge and document initialize");

GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
GL.Viewport(0, 0, width, height);
GL.Disable(EnableCap.ScissorTest);
GL.ClearColor(0.2f, 0.3f, 0.4f, 1f);
GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

// Deliberately leave nondefault state for RmlUi to restore.
GL.Enable(EnableCap.DepthTest);
GL.DepthMask(true);
GL.Enable(EnableCap.CullFace);
GL.Enable(EnableCap.ScissorTest);
GL.Scissor(9, 11, width - 18, height - 22);
GL.Viewport(3, 5, width - 6, height - 10);
GL.Disable(EnableCap.Blend);
GL.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
GL.ActiveTexture(TextureUnit.Texture1);
int texture = GL.GenTexture();
GL.BindTexture(TextureTarget.Texture2D, texture);
int readFbo = GL.GenFramebuffer();
GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFbo);
GL.MatrixMode(MatrixMode.Projection);
GL.PushMatrix();
GL.LoadIdentity();
GL.Translate(0.25f, 0.5f, 0);
int matrix = GL.GetInteger(GetPName.MatrixMode);
int active = GL.GetInteger(GetPName.ActiveTexture);
int program = GL.GetInteger(GetPName.CurrentProgram);
int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
int read = GL.GetInteger(GetPName.ReadFramebufferBinding);
int boundTexture = GL.GetInteger(GetPName.TextureBinding2D);
var viewport = new int[4];
GL.GetInteger(GetPName.Viewport, viewport);

host.Update();
RmlUiOpenGlFrame.Draw(host, width, height);
Check(GL.GetInteger(GetPName.ActiveTexture) == active
    && GL.GetInteger(GetPName.TextureBinding2D) == boundTexture
    && GL.GetInteger(GetPName.CurrentProgram) == program,
    "program, active texture and texture binding restored");
Check(GL.GetInteger(GetPName.DrawFramebufferBinding) == draw
    && GL.GetInteger(GetPName.ReadFramebufferBinding) == read,
    "read/draw framebuffer bindings restored");
Check(GL.GetInteger(GetPName.MatrixMode) == matrix
    && GL.IsEnabled(EnableCap.DepthTest)
    && GL.IsEnabled(EnableCap.CullFace)
    && GL.IsEnabled(EnableCap.ScissorTest)
    && !GL.IsEnabled(EnableCap.Blend)
    && GL.GetInteger(GetPName.DepthWritemask) != 0,
    "fixed-function matrix mode and depth/cull/scissor/blend states restored");
int[] afterViewport = new int[4];
GL.GetInteger(GetPName.Viewport, afterViewport);
Check(viewport.AsSpan().SequenceEqual(afterViewport), "viewport restored");

// A clean read FBO is necessary for readback; the GL2 render was drawn to
// default output even though a different READ framebuffer was bound at entry.
GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
GL.Disable(EnableCap.ScissorTest);
var picture = FinalCompositeCapture.Read(width, height);
Check(picture.Length == width * height * 3, "framebuffer readback dimensions");
// At least part of the native Home must differ from the pre-rendered clear.
// Pixel formats are RGB, captured from the actual final framebuffer.
int changed = 0;
for (int i = 0; i < picture.Length; i += 3)
{
    if (Math.Abs(picture[i] - 51) > 24
        || Math.Abs(picture[i + 1] - 77) > 24
        || Math.Abs(picture[i + 2] - 102) > 24) changed++;
}
Check(changed > width * height / 300, "native RmlUi painted actual Home pixels");
Check(GL.GetError() == ErrorCode.NoError, "no OpenGL errors after native frame");
host.Shutdown();
GL.MatrixMode(MatrixMode.Projection);
GL.PopMatrix();
GL.MatrixMode(MatrixMode.Modelview);
GL.DeleteTexture(texture);
GL.DeleteFramebuffer(readFbo);
Console.WriteLine("RMLUI GL2 real-software-context acceptance PASS");
