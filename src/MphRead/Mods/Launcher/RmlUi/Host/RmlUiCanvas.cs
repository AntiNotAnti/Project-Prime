using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host;

[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiCanvasNativeLease
{
    internal uint Size, Version;
    internal ulong Generation, Document, Epoch;
    internal static RmlUiCanvasNativeLease Request() => new() { Size = 32, Version = 1 };
}
[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiCanvasNativeTexture
{
    internal uint Size, Version, Width, Height, Offset, Length, Filter, WrapS, WrapT, Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiCanvasVertex
{
    internal float X, Y;
    internal byte R, G, B, A;
    internal float U, V;
    internal RmlUiCanvasVertex(float x, float y, byte r, byte g, byte b, byte a, float u, float v)
    { X = x; Y = y; R = r; G = g; B = b; A = a; U = u; V = v; }
    internal static byte Quantize(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        return (byte)MathF.Floor(value * 255 + .5f);
    }
}
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct RmlUiCanvasBatch(uint Texture, uint First, uint Count, uint Reserved = 0);
[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiCanvasNativeFrame
{
    internal uint Size, Version;
    internal ulong Revision;
    internal float Width, Height;
    internal uint Vertices, Indices, Batches, Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiCanvasNativeState
{
    internal uint Size, Version;
    internal ulong Revision;
    internal uint TextureCount, BatchCount, Flags, Reserved;
    internal static RmlUiCanvasNativeState Request() => new() { Size = 32, Version = 1 };
}
internal sealed record RmlUiCanvasImage(int Width, int Height, ReadOnlyMemory<byte> Rgba, uint Filter, uint WrapS, uint WrapT);

/// <summary>Detached straight RGBA table. Source pixels and sampler policy are copied once before publication.</summary>
internal sealed class RmlUiCanvasTextureTable
{
    private readonly RmlUiCanvasNativeTexture[] _textures;
    private readonly byte[] _pixels;
    internal int Count => _textures.Length;
    internal int ByteLength => _pixels.Length;
    internal RmlUiCanvasTextureTable(IReadOnlyList<RmlUiCanvasImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count > 64) throw new ArgumentOutOfRangeException(nameof(images));
        var plans = new List<RmlUiCanvasImage>(); int bytes = 0;
        foreach (var image in images)
        {
            if (plans.Count >= 64 || image == null || image.Width is < 1 or > 512 || image.Height is < 1 or > 512
                || image.Filter > 1 || image.WrapS > 1 || image.WrapT > 1 || image.Rgba.Length != image.Width * image.Height * 4)
                throw new ArgumentException("Canvas image or sampler extent is invalid.", nameof(images));
            bytes = checked(bytes + image.Rgba.Length);
            if (bytes > 4 * 1024 * 1024) throw new ArgumentException("Canvas texture table exceeds its RGBA budget.", nameof(images));
            plans.Add(image);
        }
        _textures = new RmlUiCanvasNativeTexture[plans.Count]; _pixels = new byte[bytes]; int offset = 0;
        for (int i = 0; i < plans.Count; i++)
        {
            var image = plans[i]; image.Rgba.Span.CopyTo(_pixels.AsSpan(offset));
            _textures[i] = new() { Size = 40, Version = 1, Width = (uint)image.Width, Height = (uint)image.Height,
                Offset = (uint)offset, Length = (uint)image.Rgba.Length, Filter = image.Filter, WrapS = image.WrapS, WrapT = image.WrapT };
            offset += image.Rgba.Length;
        }
    }
    internal int Publish(IRmlUiNativeBridge native, ref RmlUiCanvasNativeLease lease)
        => native.CanvasTextures(ref lease, _textures, (uint)_textures.Length, _pixels, (uint)_pixels.Length);
}

/// <summary>Immutable bounded triangle packet. Texture ordinals refer only to this lease's immutable table.</summary>
internal sealed class RmlUiCanvasFrame
{
    private readonly RmlUiCanvasVertex[] _vertices;
    private readonly int[] _indices;
    private readonly RmlUiCanvasBatch[] _batches;
    private readonly float _width, _height;
    internal int VertexCount => _vertices.Length;
    internal int IndexCount => _indices.Length;
    internal int BatchCount => _batches.Length;
    internal RmlUiCanvasFrame(float width, float height, ReadOnlySpan<RmlUiCanvasVertex> vertices,
        ReadOnlySpan<int> indices, ReadOnlySpan<RmlUiCanvasBatch> batches)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width is < 1 or > 16384 || height is < 1 or > 16384
            || vertices.Length > 65536 || indices.Length > 196608 || batches.Length > 4096 || indices.Length % 3 != 0
            || ((vertices.Length == 0 || indices.Length == 0 || batches.Length == 0) && (vertices.Length != 0 || indices.Length != 0 || batches.Length != 0)))
            throw new ArgumentException("Canvas frame extent is invalid.");
        // Bound copies first, then validate owned values; no caller mutation can alter an admitted packet.
        _vertices = vertices.ToArray(); _indices = indices.ToArray(); _batches = batches.ToArray();
        foreach (var vertex in _vertices)
            if (!Coordinate(vertex.X) || !Coordinate(vertex.Y) || !Coordinate(vertex.U) || !Coordinate(vertex.V))
                throw new ArgumentException("Canvas vertex exceeds its finite coordinate domain.");
        foreach (int index in _indices)
            if ((uint)index >= (uint)_vertices.Length) throw new ArgumentException("Canvas index is outside its vertex table.");
        ulong end = 0;
        foreach (var batch in _batches)
        {
            if (batch.Reserved != 0 || batch.First != end || batch.Count == 0 || batch.Count % 3 != 0
                || (batch.Texture != uint.MaxValue && batch.Texture >= 64)) throw new ArgumentException("Canvas batch is invalid.");
            end += batch.Count;
            if (end > (ulong)_indices.Length) throw new ArgumentException("Canvas batch leaves its index extent.");
        }
        if (end != (ulong)_indices.Length) throw new ArgumentException("Canvas batches do not cover the index table.");
        _width = width; _height = height;
    }
    private static bool Coordinate(float value) => float.IsFinite(value) && MathF.Abs(value) <= 1048576;
    internal int Publish(IRmlUiNativeBridge native, ref RmlUiCanvasNativeLease lease, ulong revision, int textureCount)
    {
        foreach (var batch in _batches)
            if (batch.Texture != uint.MaxValue && batch.Texture >= textureCount) return 0;
        var header = new RmlUiCanvasNativeFrame { Size = 40, Version = 1, Revision = revision,
            Width = _width, Height = _height, Vertices = (uint)_vertices.Length, Indices = (uint)_indices.Length, Batches = (uint)_batches.Length };
        return native.CanvasFrame(ref lease, ref header, _vertices, _indices, _batches);
    }
}

/// <summary>Owner-thread lease, independently fenced from document tokens. No finalizer or worker-side native cleanup.</summary>
internal sealed class RmlUiCanvas : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly IRmlUiNativeBridge _native;
    private readonly RmlUiDocumentToken _document;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private RmlUiCanvasNativeLease _lease;
    private bool _disposed, _tableSet;
    private ulong _revision;
    private int _textures, _batches;
    internal RmlUiCanvas(RmlUiHost host, IRmlUiNativeBridge native, RmlUiDocumentToken document, RmlUiCanvasNativeLease lease)
    { _host = host; _native = native; _document = document; _lease = lease; }
    internal bool SetTextures(RmlUiCanvasTextureTable table)
    {
        ArgumentNullException.ThrowIfNull(table); VerifyOwner();
        if (_disposed || _tableSet || !_host.CanvasAllowed(_document, true)) return false;
        if (table.Publish(_native, ref _lease) == 0) return false;
        _tableSet = true; _textures = table.Count; return true;
    }
    internal bool Submit(RmlUiCanvasFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame); VerifyOwner();
        if (_disposed || !_tableSet || _revision == ulong.MaxValue || !_host.CanvasAllowed(_document, true)) return false;
        if (frame.Publish(_native, ref _lease, _revision + 1, _textures) == 0) return false;
        _revision++; _batches = frame.BatchCount; return true;
    }
    internal bool TryState(out RmlUiCanvasNativeState state)
    {
        VerifyOwner(); state = RmlUiCanvasNativeState.Request();
        return !_disposed && _host.CanvasAllowed(_document, false) && _native.CanvasState(ref _lease, ref state) != 0
            && state.Size == 32 && state.Version == 1 && state.Reserved == 0 && state.Flags <= 3
            && state.TextureCount == _textures && state.BatchCount == _batches && state.Revision == _revision
            && ((state.Flags & 2) != 0) == _tableSet;
    }
    public void Dispose()
    {
        VerifyOwner(); if (_disposed) return;
        if (_host.CanvasAllowed(_document, false)) _native.CanvasRelease(ref _lease);
        _disposed = true; _lease = default;
    }
    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Canvas belongs to its creating UI thread.");
    }
}
