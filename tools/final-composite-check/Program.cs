using System;
using MphRead.Mods;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using G = MphRead.Mods.Render.GraphicsApi;

Environment.SetEnvironmentVariable("PROJECT_PRIME_GL_PROFILE", "1");
GraphicsBackendPolicy.Configure(args.Length == 0 ? "opengl" : args[0]);
var settings = DesktopGlContext.Settings(background: true);
settings.ClientSize = new(65, 33);
using var window = new NativeWindow(settings);
using var session = new DesktopGraphicsSession(window);
int width = window.FramebufferSize.X, height = window.FramebufferSize.Y;
G.Viewport(0, 0, width, height);
G.Disable(EnableCap.ScissorTest);
G.ClearColor(1, 0, 0, 1); G.Clear(ClearBufferMask.ColorBufferBit);
// A last overlay pass changes only the lower half, after the scene-like clear.
G.Enable(EnableCap.ScissorTest); G.Scissor(0, 0, width, height / 2);
G.ClearColor(0, 1, 0, 1); G.Clear(ClearBufferMask.ColorBufferBit);
G.Disable(EnableCap.ScissorTest);
int framebuffer = G.GenFramebuffer();
G.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
G.PixelStore(PixelStoreParameter.PackAlignment, 8);
int alignment = G.GetInteger(GetPName.PackAlignment);
// Dirty pixel-pack state must neither redirect managed writes into a PBO nor
// change the returned row layout. The caller's state must survive capture.
int packBuffer = GL.GenBuffer();
GL.BindBuffer(BufferTarget.PixelPackBuffer, packBuffer);
GL.BufferData(BufferTarget.PixelPackBuffer, 4096, IntPtr.Zero, BufferUsageHint.StreamRead);
G.PixelStore(PixelStoreParameter.PackRowLength, 17);
G.PixelStore(PixelStoreParameter.PackSkipRows, 2);
G.PixelStore(PixelStoreParameter.PackSkipPixels, 3);
var pixels = FinalCompositeCapture.Read(width, height);
Check(pixels.Length == width * height * 3, "packed RGB byte count");
int bottom = ((height / 4) * width + width / 2) * 3;
int top = ((height * 3 / 4) * width + width / 2) * 3;
Check(pixels[bottom] < 3 && pixels[bottom + 1] > 252, "last overlay included in final composite");
Check(pixels[top] > 252 && pixels[top + 1] < 3, "scene remains above overlay; bottom-up row contract");
Check(G.GetInteger(GetPName.ReadFramebufferBinding) == framebuffer, "read framebuffer restored");
Check(G.GetInteger(GetPName.PackAlignment) == alignment, "pack alignment restored");
Check(G.GetInteger(GetPName.PixelPackBufferBinding) == packBuffer
    && G.GetInteger(GetPName.PackRowLength) == 17
    && G.GetInteger(GetPName.PackSkipRows) == 2
    && G.GetInteger(GetPName.PackSkipPixels) == 3, "pack buffer and row/skip state restored");
GL.BindBuffer(BufferTarget.PixelPackBuffer, 0); GL.DeleteBuffer(packBuffer);
G.PixelStore(PixelStoreParameter.PackRowLength, 0);
G.PixelStore(PixelStoreParameter.PackSkipRows, 0);
G.PixelStore(PixelStoreParameter.PackSkipPixels, 0);
G.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0); G.DeleteFramebuffer(framebuffer);
bool encoded = false;
ScreenCapture.PngWriter = (data, w, h, _) => encoded = w == width && h == height && data[bottom + 1] > 252;
try { Check(ScreenCapture.SaveWindow(width, height, "unused.png") && encoded, "window screenshot uses shared final contract"); }
finally { ScreenCapture.PngWriter = null; }
try { FinalCompositeCapture.Read(8192, 8192); throw new Exception("readback budget ignored"); }
catch (ArgumentOutOfRangeException) { Console.WriteLine("PASS bounded readback"); }
CaptureRegionChecks.Run();
RenderTransitionChecks.Run(width, height);
Console.WriteLine($"FINALCOMPOSITE PASS actual={G.GetString(StringName.Renderer)} {G.GetString(StringName.Version)}");
return 0;
static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
