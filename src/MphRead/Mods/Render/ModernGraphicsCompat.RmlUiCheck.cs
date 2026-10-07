#if !MPHREAD_SERVER
using System;
#if MPHREAD_RMLUI_POC
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Render;
using OpenTK.Graphics.OpenGL;
#endif

namespace MphRead.Mods.Render;

// Native rendering regression follows the existing renderer-check convention.
// It draws into the engine's ordinary surface and reads its final composite.
public static class RmlUiCompositorCheck
{
#if !ANDROID
    public static void RunDesktop(string backend, bool recovery = false)
    {
#if MPHREAD_RMLUI_POC
        GraphicsBackendPolicy.Configure(backend);
        var settings = DesktopGlContext.Settings(background: true);
        settings.ClientSize = new(96, 64);
        using var window = new OpenTK.Windowing.Desktop.NativeWindow(settings);
        using var session = new DesktopGraphicsSession(window);
        Run(window.FramebufferSize.X, window.FramebufferSize.Y, recovery);
#else
        throw new InvalidOperationException("Build with -p:MphReadRmlUi=true to exercise the RmlUi compositor.");
#endif
    }
#endif
    public static void Run(int width, int height, bool recovery = false)
    {
#if MPHREAD_RMLUI_POC
        RunCore(width, height, recovery);
#else
        throw new InvalidOperationException("Build with -p:MphReadRmlUi=true to exercise the RmlUi compositor.");
#endif
    }

#if MPHREAD_RMLUI_POC
    private static unsafe void RunCore(int width, int height, bool recovery)
    {
        if (!ModernGraphicsCompat.Active) throw new InvalidOperationException("The compositor check requires a modern graphics backend.");
        Check(Marshal.SizeOf<RmlUiDrawCommand>() == 120 && Marshal.SizeOf<RmlUiVertex>() == 20, "native draw-list ABI packing");
        Check(ModernGraphicsCompat.RmlScissor(-4, -6, 14, 16, width, height) == (0u, 0u, 10u, 10u), "top-left scissor clips negative coordinates");
        Check(ModernGraphicsCompat.RmlScissor(int.MaxValue - 2, 0, 100, 1, width, height).Width == 0, "scissor endpoints cannot overflow");
        try
        {
            _ = new RmlUiDrawGeometry(new RmlUiVertex[1], new[] { 0, 1, 0 });
            throw new InvalidOperationException("invalid index was accepted");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("invalid vertex index", StringComparison.Ordinal))
        { Console.WriteLine("RMLGPU PASS invalid native indices rejected before GPU upload"); }

        var fill = Quad(0, 0, width, height, 0xff00ff00);
        var mask = Quad(0, 0, width / 2f, height, 0xffffffff);
        var topMask = Quad(0, 0, width, height / 2f, 0xffffffff);
        var geometry = new Dictionary<ulong, RmlUiDrawGeometry> { [1] = fill, [2] = mask, [3] = topMask };
        var textures = new Dictionary<ulong, RmlUiDrawTexture>();
        ulong generation = 1;

        void Draw(params RmlUiDrawCommand[] commands)
        {
            // Complete the previous public frame so repeated draws exercise
            // arena and bind-group reuse across actual engine presentation.
            ModernGraphicsCompat.Present();
            GraphicsApi.Disable(EnableCap.ScissorTest);
            GraphicsApi.ClearColor(1, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            ModernGraphicsCompat.DrawRmlUi(new RmlUiDrawListFrame(generation, commands, geometry, textures), width, height);
            ModernGraphicsCompat.ThrowIfDeviceFailedForCheck();
        }
        RmlUiDrawCommand draw = Command(RmlUiDrawCommandKind.Geometry, 1);
        Draw(draw);
        byte[] pixels = FinalCompositeCapture.Read(width, height);
        Check(Is(pixels, width, height, width / 2, height / 2, 0, 255, 0), "compiled geometry overlays the final scene");
        Draw(draw);
        Check(Is(FinalCompositeCapture.Read(width, height), width, height, 1, 1, 0, 255, 0), "retained geometry reused on the next frame");

        var scissorOn = Command(RmlUiDrawCommandKind.EnableScissor); scissorOn.Enabled = 1;
        var scissor = Command(RmlUiDrawCommandKind.Scissor);
        scissor.X = width / 2; scissor.Y = 0; scissor.Width = width / 2; scissor.Height = height / 2;
        Draw(scissorOn, scissor, draw);
        pixels = FinalCompositeCapture.Read(width, height);
        Check(Is(pixels, width, height, width * 3 / 4, height / 4, 0, 255, 0)
            && Is(pixels, width, height, width / 4, height / 4, 255, 0, 0)
            && Is(pixels, width, height, width * 3 / 4, height * 3 / 4, 255, 0, 0), "scissor uses framebuffer coordinates with top-left origin");

        var clipOn = Command(RmlUiDrawCommandKind.EnableClipMask); clipOn.Enabled = 1;
        var set = Command(RmlUiDrawCommandKind.ClipMask, 2);
        Draw(clipOn, set, draw);
        pixels = FinalCompositeCapture.Read(width, height);
        Check(Is(pixels, width, height, width / 4, height / 2, 0, 255, 0)
            && Is(pixels, width, height, width * 3 / 4, height / 2, 255, 0, 0), "Set clip mask uses its compiled geometry");
        set.Operation = RmlUiClipOperation.SetInverse;
        Draw(clipOn, set, draw);
        pixels = FinalCompositeCapture.Read(width, height);
        Check(Is(pixels, width, height, width / 4, height / 2, 255, 0, 0)
            && Is(pixels, width, height, width * 3 / 4, height / 2, 0, 255, 0), "SetInverse clips inside the geometry");
        set.Operation = RmlUiClipOperation.Set;
        var intersection = Command(RmlUiDrawCommandKind.ClipMask, 3); intersection.Operation = RmlUiClipOperation.Intersect;
        Draw(clipOn, set, intersection, draw);
        pixels = FinalCompositeCapture.Read(width, height);
        Check(Is(pixels, width, height, width / 4, height / 4, 0, 255, 0)
            && Is(pixels, width, height, width / 4, height * 3 / 4, 255, 0, 0)
            && Is(pixels, width, height, width * 3 / 4, height / 4, 255, 0, 0), "Intersect preserves only the overlap");
        set.Operation = RmlUiClipOperation.SetInverse;
        Draw(clipOn, set, intersection, draw);
        pixels = FinalCompositeCapture.Read(width, height);
        Check(Is(pixels, width, height, width * 3 / 4, height / 4, 0, 255, 0)
            && Is(pixels, width, height, width / 4, height / 4, 255, 0, 0)
            && Is(pixels, width, height, width * 3 / 4, height * 3 / 4, 255, 0, 0), "Intersect after SetInverse preserves only the outside overlap");

        geometry[1] = Quad(0, 0, width / 4f, height / 4f, 0xff00ff00);
        var transform = Command(RmlUiDrawCommandKind.Transform); transform.Enabled = 1;
        transform.Transform[0] = transform.Transform[5] = transform.Transform[10] = transform.Transform[15] = 1;
        transform.Transform[12] = width / 2f; transform.Transform[13] = height / 2f;
        draw.TranslationX = width / 8f;
        Draw(transform, draw);
        pixels = FinalCompositeCapture.Read(width, height);
        Check(Is(pixels, width, height, width * 3 / 4, height * 5 / 8, 0, 255, 0)
            && Is(pixels, width, height, width / 8, height / 8, 255, 0, 0), "column-major transform follows local geometry translation");

        draw.TranslationX = 0;
        geometry[1] = Quad(0, 0, width, height, 0xffffffff);
        textures[7] = new RmlUiDrawTexture(1, 1, new byte[] { 0, 0, 255, 255 });
        draw.Texture = 7;
        Draw(draw);
        Check(Is(FinalCompositeCapture.Read(width, height), width, height, width / 2, height / 2, 0, 0, 255), "generated atlas samples premultiplied RGBA");
        textures[7] = new RmlUiDrawTexture(1, 1, new byte[] { 0, 128, 0, 128 });
        Draw(draw);
        Check(Is(FinalCompositeCapture.Read(width, height), width, height, width / 2, height / 2, 127, 128, 0), "premultiplied alpha composites once over scene color");
        textures.Clear(); draw.Texture = 0;
        generation++;
        Draw(draw);
        Check(Is(FinalCompositeCapture.Read(width, height), width, height, 1, 1, 255, 255, 255), "native generation change releases old geometry and atlas identities");
        ModernGraphicsCompat.Present();
        int resizedWidth = width / 2, resizedHeight = height / 2;
        ModernGraphicsCompat.Resize(resizedWidth, resizedHeight);
        GraphicsApi.ClearColor(1, 0, 0, 1); GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
        ModernGraphicsCompat.DrawRmlUi(new RmlUiDrawListFrame(generation, new[] { draw }, geometry, textures), resizedWidth, resizedHeight);
        Check(Is(FinalCompositeCapture.Read(resizedWidth, resizedHeight), resizedWidth, resizedHeight,
            resizedWidth / 2, resizedHeight / 2, 255, 255, 255), "surface resize recreates the UI stencil without stale dimensions");
        ModernGraphicsCompat.Present();
        ModernGraphicsCompat.Resize(0, 0);
        ModernGraphicsCompat.DrawRmlUi(new RmlUiDrawListFrame(generation, new[] { draw }, geometry, textures), 0, 0);
        ModernGraphicsCompat.Resize(width, height);
        Draw(draw);
        Check(Is(FinalCompositeCapture.Read(width, height), width, height, 1, 1, 255, 255, 255), "zero-size suspension resumes on the engine surface");
        if (recovery)
        {
            int previous = ModernGraphicsCompat.DeviceGeneration;
            ModernGraphicsCompat.DestroyDeviceForCheck();
            Draw(draw);
            Check(ModernGraphicsCompat.DeviceGeneration > previous
                && Is(FinalCompositeCapture.Read(width, height), width, height, 1, 1, 255, 255, 255), "device reconstruction recreates compiled geometry and shader resources");
        }
        ModernGraphicsCompat.ThrowIfDeviceFailedForCheck();
        ModernGraphicsCompat.Present();
        ModernGraphicsCompat.ReleaseRmlUiFrames();
        Draw(draw);
        Check(Is(FinalCompositeCapture.Read(width, height), width, height, 1, 1, 255, 255, 255), "UI shutdown and reentry cannot reuse released native geometry or atlas handles");
        ModernGraphicsCompat.Present();
        Console.WriteLine("RMLGPU PASS renderer-owned surface, resource release, and validation checks");
    }

    private static RmlUiDrawGeometry Quad(float x, float y, float width, float height, uint color) => new(
        new[]
        {
            new RmlUiVertex { X = x, Y = y, U = 0, V = 0, Color = color },
            new RmlUiVertex { X = x + width, Y = y, U = 1, V = 0, Color = color },
            new RmlUiVertex { X = x + width, Y = y + height, U = 1, V = 1, Color = color },
            new RmlUiVertex { X = x, Y = y + height, U = 0, V = 1, Color = color }
        }, new[] { 0, 1, 2, 0, 2, 3 });
    private static RmlUiDrawCommand Command(RmlUiDrawCommandKind kind, ulong geometry = 0)
    {
        var command = RmlUiDrawCommand.Create(kind); command.Geometry = geometry; return command;
    }
    private static bool Is(byte[] pixels, int width, int height, int x, int y, int red, int green, int blue)
    {
        int at = ((height - 1 - y) * width + x) * 3;
        return Math.Abs(pixels[at] - red) <= 3 && Math.Abs(pixels[at + 1] - green) <= 3 && Math.Abs(pixels[at + 2] - blue) <= 3;
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("RMLGPU FAIL " + message);
        Console.WriteLine("RMLGPU PASS " + message);
    }
#endif
}
#endif
