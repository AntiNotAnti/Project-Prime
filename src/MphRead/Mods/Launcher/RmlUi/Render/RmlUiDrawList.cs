#if (ANDROID && PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT) || MPHREAD_RMLUI_DRAWLIST_TEST
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Render;

[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiTextureSampling
{
    internal uint Size, Version, Filter, WrapS, WrapT, Flags;
    internal static RmlUiTextureSampling Request() => new() { Size = 24, Version = 1 };
    internal bool Valid => Size == 24 && Version == 1 && Filter <= 1 && WrapS <= 1 && WrapT <= 1 && Flags <= 1;
}

// ABI 1 follows the pinned RmlUi 6.3 RenderInterface. RmlUi vertices and
// generated RGBA textures already contain premultiplied, display-encoded color.
internal enum RmlUiDrawCommandKind : uint
{
    Geometry = 1, EnableScissor = 2, Scissor = 3, Transform = 4,
    EnableClipMask = 5, ClipMask = 6
}

internal enum RmlUiClipOperation : uint { Set, SetInverse, Intersect }

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct RmlUiVertex
{
    internal float X, Y;
    internal uint Color;
    internal float U, V;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RmlUiDrawCommand
{
    internal uint Size;
    internal RmlUiDrawCommandKind Kind;
    internal ulong Geometry, Texture;
    internal float TranslationX, TranslationY;
    internal int X, Y, Width, Height;
    internal uint Enabled;
    internal RmlUiClipOperation Operation;
    // Native RmlUi Matrix4f is column-major. Do not transpose this array.
    internal fixed float Transform[16];

    internal static RmlUiDrawCommand Create(RmlUiDrawCommandKind kind) => new()
    {
        Size = 120, Kind = kind
    };
}

internal sealed class RmlUiDrawGeometry
{
    internal RmlUiVertex[] Vertices { get; }
    internal int[] Indices { get; }
    internal RmlUiDrawGeometry(RmlUiVertex[] vertices, int[] indices)
    {
        if (vertices.Length == 0 || indices.Length == 0 || indices.Length % 3 != 0)
            throw new InvalidOperationException("RmlUi draw geometry must contain indexed triangles.");
        foreach (int index in indices)
            if ((uint)index >= (uint)vertices.Length)
                throw new InvalidOperationException("RmlUi draw geometry contains an invalid vertex index.");
        foreach (RmlUiVertex vertex in vertices)
            if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y)
                || !float.IsFinite(vertex.U) || !float.IsFinite(vertex.V))
                throw new InvalidOperationException("RmlUi draw geometry contains a nonfinite vertex.");
        Vertices = vertices;
        Indices = indices;
    }
}

internal sealed class RmlUiDrawTexture
{
    internal int Width { get; }
    internal int Height { get; }
    internal byte[] Pixels { get; }
    internal RmlUiTextureSampling? Sampling { get; }
    internal RmlUiDrawTexture(int width, int height, byte[] pixels, RmlUiTextureSampling? sampling = null)
    {
        if (width <= 0 || height <= 0 || checked((long)width * height * 4) != pixels.Length)
            throw new InvalidOperationException("RmlUi draw texture must contain tightly packed premultiplied RGBA pixels.");
        if (sampling is { } policy && (!policy.Valid || policy.Flags != 1))
            throw new InvalidOperationException("RmlUi canvas texture sampling is invalid.");
        Width = width; Height = height; Pixels = pixels; Sampling = sampling;
    }
}

internal sealed class RmlUiDrawListFrame
{
    internal ulong Generation { get; }
    internal RmlUiDrawCommand[] Commands { get; }
    internal IReadOnlyDictionary<ulong, RmlUiDrawGeometry> Geometry { get; }
    internal IReadOnlyDictionary<ulong, RmlUiDrawTexture> Textures { get; }
    internal RmlUiDrawListFrame(ulong generation, RmlUiDrawCommand[] commands,
        IReadOnlyDictionary<ulong, RmlUiDrawGeometry> geometry,
        IReadOnlyDictionary<ulong, RmlUiDrawTexture> textures)
    {
        Generation = generation; Commands = commands;
        // The reader prunes its caches on later captures and clears them on
        // teardown. A retained frame owns its resource map until its consumer
        // releases it; the already detached resource payloads can be shared.
        Geometry = new ReadOnlyDictionary<ulong, RmlUiDrawGeometry>(new Dictionary<ulong, RmlUiDrawGeometry>(geometry));
        Textures = new ReadOnlyDictionary<ulong, RmlUiDrawTexture>(new Dictionary<ulong, RmlUiDrawTexture>(textures));
    }
}
#endif
