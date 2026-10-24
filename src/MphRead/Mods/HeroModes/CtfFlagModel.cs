using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ReFuel.Stb;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.HeroModes;

/// <summary>Physical pole and waving cloth, using the exact user-supplied Sanctuary banner atlas.</summary>
internal sealed class CtfFlagModel
{
    private const int PoleSides = 8, ClothColumns = 8, ClothRows = 12;
    private sealed record Atlas(int Binding);
    // Bindings belong to a GL scene/context. Do not reuse texture ids across scene disposal.
    private static readonly ConditionalWeakTable<Scene, Atlas> Atlases = new();
    private static Atlas LoadAtlas(Scene scene)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Ctf.SanctuaryBanners.png")
            ?? throw new InvalidDataException("Missing embedded Sanctuary CTF banner atlas.");
        using StbImage image = StbImage.Load(stream, StbiImageFormat.Rgba);
        if (image.Width != 1254 || image.Height != 1254 || image.ImagePointer == IntPtr.Zero)
            throw new InvalidDataException("Invalid Sanctuary banner atlas dimensions/pixels.");
        // This STB wrapper reports the original RGB component count in AsSpan,
        // even when decoding to RGBA. Copy the requested RGBA buffer explicitly,
        // as the existing ModernTextureAsset loader does.
        var bytes = new byte[checked(image.Width * image.Height * 4)];
        Marshal.Copy(image.ImagePointer, bytes, 0, bytes.Length);
        var pixels = new ColorRgba[image.Width * image.Height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new ColorRgba(bytes[i*4], bytes[i*4+1], bytes[i*4+2], bytes[i*4+3]);
        return new(scene.BindCtfBannerTexture(pixels, image.Width, image.Height));
    }
    public void Draw(Scene scene, Vector3 origin, Vector3 right, Vector3 up, Vector3 forward, float scale, int team)
    {
        Vector3 Transform(float x, float y, float z) => origin + scale * (right * x + up * y + forward * z);
        void Quad(int i, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector4 color)
        {
            // Scene takes ownership of non-mesh point arrays and returns them on the
            // next draw. Never cache or reuse an array across submissions: the world
            // flag and first-person flag may both be submitted in the same frame.
            var p = ArrayPool<Vector3>.Shared.Rent(4);
            p[0] = a; p[1] = b; p[2] = c; p[3] = d;
            scene.AddRenderItem(CullingMode.Neither, scene.GetNextPolygonId(), color, RenderItemType.Quad, p, 4, true);
        }
        for (int i = 0; i < PoleSides; i++)
        {
            float a = i * MathF.Tau / PoleSides, b = (i + 1) * MathF.Tau / PoleSides;
            float x = MathF.Cos(a) * .035f, z = MathF.Sin(a) * .035f;
            float u = MathF.Cos(b) * .035f, v = MathF.Sin(b) * .035f;
            float shade = .4f + .35f * MathF.Max(0, MathF.Cos(a - .6f));
            Quad(i, Transform(x, 0, z), Transform(u, 0, v), Transform(u, 2.0f, v), Transform(x, 2.0f, z), new Vector4(shade, shade, shade * 1.07f, 1));
        }
        int texture = Atlases.GetValue(scene, LoadAtlas).Binding;
        float time = (float)(scene.FrameCount % 36000) / 60f;
        float Wave(float x, float y) => MathF.Sin(x * 8 - time * 3 + y) * .07f * x;
        // Portrait artwork retains its 1:2 aspect. Blue occupies the left half,
        // red the right half. A half-texel inset prevents atlas-edge color bleed.
        float u0 = team == 0 ? .5f : 0, inset = .5f / 1254;
        var cloth = ArrayPool<Vector3>.Shared.Rent(ClothColumns * ClothRows * 8);
        int n = 0;
        void Vertex(float x, float y)
        {
            float worldX = .035f + x * .625f, worldY = .70f + y * 1.25f;
            cloth[n++] = new Vector3(u0 + inset + x * (.5f - 2 * inset), 1 - inset - y * (1 - 2 * inset), 0);
            cloth[n++] = Transform(worldX, worldY, Wave(worldX, worldY));
        }
        for (int x = 0; x < ClothColumns; x++) for (int y = 0; y < ClothRows; y++)
        {
            float left = x / (float)ClothColumns, r = (x + 1f) / ClothColumns;
            float bottom = y / (float)ClothRows, top = (y + 1f) / ClothRows;
            Vertex(left, bottom); Vertex(r, bottom); Vertex(r, top); Vertex(left, top);
        }
        // One textured mesh submission, double sided. This is not a billboard.
        scene.AddRenderItem(RenderItemType.TrailStack, 1f, scene.GetNextPolygonId(), Vector3.One,
            RepeatMode.Clamp, RepeatMode.Clamp, 1, 1, Matrix4.Identity, cloth, texture,
            BillboardMode.None, ClothColumns * ClothRows);
        // Finial: four deliberate tapered faces.
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 2, b = (i + 1) * MathF.PI / 2;
            var tip = Transform(0, 2.14f, 0);
            Quad(PoleSides + ClothColumns * ClothRows + i, Transform(MathF.Cos(a) * .06f, 1.98f, MathF.Sin(a) * .06f),
                Transform(MathF.Cos(b) * .06f, 1.98f, MathF.Sin(b) * .06f), tip, tip, new Vector4(.81f, .69f, .36f, 1));
        }
    }
}
