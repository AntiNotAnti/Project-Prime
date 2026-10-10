#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Render;

/// <summary>
/// Real software/desktop OpenGL pixel regression, no proprietary game assets.
/// This is a depth/stencil/alpha baseline, not a claim that full Ice Hive or
/// shadow-map rendering has been verified on end-user GPUs.
/// </summary>
internal static class OpenGlVisualCheck
{
    private const int Width = 64;
    private const int Height = 64;
    private static readonly Dictionary<string, string> _images = new();
    private static int _assertions;

    internal static int Run(string output)
    {
        try
        {
            Directory.CreateDirectory(output);
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new Vector2i(Width, Height);
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            DesktopGraphicsSession.Resize(window);
            Render(output);
            File.WriteAllText(Path.Combine(output, "evidence.json"),
                JsonSerializer.Serialize(new
                {
                    schema = 1,
                    scope = "synthetic-desktop-OpenGL-depth-stencil-alpha",
                    width = Width,
                    height = Height,
                    format = "RGBA8-bottom-up",
                    driver = GraphicsApi.GetString(StringName.Version),
                    checkedPixels = _assertions,
                    captures = _images
                }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[glvisualcheck] PASS {_assertions} pixel checks, {_images.Count} captures");
            return 0;
        }
        catch (InvalidOperationException ex)
            when (DesktopGlContext.HostedMacLacksNsgl(ex))
        {
            Console.Error.WriteLine("[glvisualcheck] SKIP hosted macOS NSGL unavailable");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[glvisualcheck] FAIL " + ex);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void PixelIs(byte[] frame, int x, int y,
        Func<byte, byte, byte, bool> predicate, string label)
    {
        int offset = (y * Width + x) * 4;
        byte red = frame[offset], green = frame[offset + 1], blue = frame[offset + 2];
        Require(predicate(red, green, blue),
            $"{label}: got ({red},{green},{blue}) at ({x},{y})");
    }

    private static void DrawQuad(float left, float bottom,
        float right, float top, float depth, float red, float green,
        float blue, float alpha = 1f)
    {
        GraphicsApi.Color4(red, green, blue, alpha);
        GraphicsApi.Begin(PrimitiveType.Quads);
        GraphicsApi.Vertex3(left, bottom, depth);
        GraphicsApi.Vertex3(right, bottom, depth);
        GraphicsApi.Vertex3(right, top, depth);
        GraphicsApi.Vertex3(left, top, depth);
        GraphicsApi.End();
    }

    private static void ResetPass()
    {
        GraphicsApi.UseProgram(0);
        GraphicsApi.MatrixMode(MatrixMode.Projection);
        GraphicsApi.LoadIdentity();
        GraphicsApi.MatrixMode(MatrixMode.Modelview);
        GraphicsApi.LoadIdentity();
        GraphicsApi.Viewport(0, 0, Width, Height);
        GraphicsApi.ColorMask(true, true, true, true);
        GraphicsApi.DepthMask(true);
        GraphicsApi.Enable(EnableCap.DepthTest);
        GraphicsApi.DepthFunc(DepthFunction.Less);
        GraphicsApi.Disable(EnableCap.AlphaTest);
        GraphicsApi.Disable(EnableCap.Texture2D);
        GraphicsApi.Disable(EnableCap.StencilTest);
        GraphicsApi.Disable(EnableCap.Blend);
        GraphicsApi.Disable(EnableCap.ScissorTest);
        GraphicsApi.StencilMask(0xFF);
        GraphicsApi.ClearStencil(0);
        GraphicsApi.ClearColor(0, 0, 0, 1);
        GraphicsApi.Clear(ClearBufferMask.ColorBufferBit
            | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
    }

    private static byte[] Snapshot(string output, string name)
    {
        byte[] pixels = new byte[Width * Height * 4];
        GraphicsApi.ReadPixels(0, 0, Width, Height,
            PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        Require(GraphicsApi.GetError() == ErrorCode.NoError,
            name + " generated a GL error");
        File.WriteAllBytes(Path.Combine(output, name + ".rgba"), pixels);
        _images[name + ".rgba"] = Convert.ToHexString(SHA256.HashData(pixels));
        return pixels;
    }

    private static void CompileColorQuad(int id, float left, float right,
        float red, float green)
    {
        GraphicsApi.RegisterRoomGeometryList(id);
        GraphicsApi.NewList(id, ListMode.Compile);
        try
        {
            GraphicsApi.Color3(red, green, 0f);
            GraphicsApi.TexCoord3(0f, 0f, 0f);
            DrawQuad(left, -.6f, right, .6f, 0f, red, green, 0f);
        }
        finally { GraphicsApi.EndList(); }
    }

    private static void Render(string output)
    {
        int framebuffer = GraphicsApi.GenFramebuffer();
        int color = GraphicsApi.GenTexture();
        int depthStencil = GraphicsApi.GenRenderbuffer();
        try
        {
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                PixelInternalFormat.Rgba8, Width, Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
            GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthStencil);
            GraphicsApi.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                RenderbufferStorage.Depth24Stencil8, Width, Height);
            GraphicsApi.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, depthStencil);
            Require(GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                == FramebufferErrorCode.FramebufferComplete,
                "depth/stencil framebuffer incomplete");

            // A red wall in front of a green pickup. Only portions of the
            // green geometry peeking around wall edges may reach the image.
            ResetPass();
            DrawQuad(-.25f, -1f, .25f, 1f, -.6f, 1, 0, 0);
            DrawQuad(-.5f, -.4f, .5f, .4f, .4f, 0, 1, 0);
            byte[] wall = Snapshot(output, "opaque-wall");
            PixelIs(wall, 32, 32, (r,g,b) => r > 245 && g < 10 && b < 10,
                "pickup must not bleed through opaque wall");
            PixelIs(wall, 20, 32, (r,g,b) => g > 245 && r < 10 && b < 10,
                "pickup must remain visible beside edge");

            // Check the same draw twice, catching retained stale depth/color
            // state and frame-to-frame ghosting in the synthetic path.
            ResetPass();
            DrawQuad(-.25f, -1f, .25f, 1f, -.6f, 1, 0, 0);
            DrawQuad(-.5f, -.4f, .5f, .4f, .4f, 0, 1, 0);
            byte[] repeat = Snapshot(output, "opaque-wall-repeat");
            Require(wall.SequenceEqual(repeat),
                "repeated world frame changed without any scene changes");

            // Glass is not an opaque occluder: the behind-surface pickup
            // must contribute to the blended output at the same pixel.
            ResetPass();
            DrawQuad(-.5f, -.5f, .5f, .5f, .4f, 0, 1, 0);
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha,
                BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.DepthMask(false);
            DrawQuad(-.25f, -.7f, .25f, .7f, -.6f, 0, 0, 1, .5f);
            byte[] glass = Snapshot(output, "translucent-window");
            PixelIs(glass, 32, 32,
                (r,g,b) => r < 15 && g > 90 && b > 90,
                "see-through glass lost the behind-surface pickup");

            // Stencil policy: block the center of a front item while
            // allowing identical depth elsewhere on the same render surface.
            ResetPass();
            DrawQuad(-1, -1, 1, 1, .7f, 1, 0, 0);
            GraphicsApi.Enable(EnableCap.StencilTest);
            GraphicsApi.ColorMask(false, false, false, false);
            GraphicsApi.DepthMask(false);
            GraphicsApi.StencilFunc(StencilFunction.Always, 1, 0xFF);
            GraphicsApi.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            DrawQuad(-.25f, -.25f, .25f, .25f, -.7f, 1, 1, 1);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.DepthMask(true);
            GraphicsApi.StencilMask(0);
            GraphicsApi.StencilFunc(StencilFunction.Equal, 0, 0xFF);
            GraphicsApi.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            DrawQuad(-.5f, -.5f, .5f, .5f, .25f, 0, 1, 0);
            byte[] stencil = Snapshot(output, "stencil-mask");
            PixelIs(stencil, 32, 32, (r,g,b) => r > 245 && g < 10,
                "stencil-masked pickup leaked through center");
            PixelIs(stencil, 20, 32, (r,g,b) => g > 245 && r < 10,
                "stencil geometry outside mask must stay visible");

            // The same geometry is drawn through native display lists in
            // -gllegacylist mode, and a single merged indexed draw by
            // default. The A/B fixture comparator is pixel-exact.
            ResetPass();
            int firstList = GraphicsApi.GenLists(2);
            try
            {
                CompileColorQuad(firstList, -.8f, 0f, 1f, 0f);
                CompileColorQuad(firstList + 1, 0f, .8f, 0f, 1f);
                if (DesktopRetainedGeometry.BatchEnabled)
                {
                    GraphicsApi.BeginScopedWorldStateElision();
                    try
                    {
                        Require(GraphicsApi.TryDrawRetainedBatch(
                            new[] { firstList, firstList + 1 }, 2),
                            "static opaque geometry failed indexed batch promotion");
                    }
                    finally { GraphicsApi.EndScopedWorldStateElision(); }
                }
                else
                {
                    GraphicsApi.CallList(firstList);
                    GraphicsApi.CallList(firstList + 1);
                }
                byte[] combined = Snapshot(output, "combined-opaque");
                PixelIs(combined, 16, 32, (r,g,b) => r > 240 && g < 10,
                    "left merged draw lost vertex color");
                PixelIs(combined, 48, 32, (r,g,b) => g > 240 && r < 10,
                    "right merged draw lost vertex color");
            }
            finally
            {
                GraphicsApi.DeleteLists(firstList, 2);
            }
        }
        finally
        {
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteRenderbuffer(depthStencil);
            GraphicsApi.DeleteTexture(color);
            GraphicsApi.DeleteFramebuffer(framebuffer);
        }
    }
}
#endif
