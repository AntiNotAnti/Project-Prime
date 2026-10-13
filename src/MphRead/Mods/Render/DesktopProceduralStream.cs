#if !MPHREAD_SERVER && !ANDROID
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using NativeGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render;

// Three orphaned stores; no waits, persistent mappings or cross-frame geometry.
// Native Quads/QuadStrip preserve the immediate renderer's primitive semantics.
internal sealed class DesktopProceduralStream : IDisposable
{
    private const int Stride = ProceduralFrameGeometry.FloatsPerVertex * sizeof(float);
    private readonly ProceduralFrameGeometry _geometry = new();
    private readonly int[] _buffers = new int[3], _vaos = new int[3];
    private readonly int[] _bytes = new int[3];
    private int _slot = -1, _generation;
    private bool _active, _failed;
    private OpenGlVertexArrayApi _api;
    [ThreadStatic] private static int _contextGeneration;
    [ThreadStatic] private static long _residentBytes;
    internal static long ResidentBytes => _residentBytes;
    internal static void ResetContext() { _contextGeneration++; _residentBytes = 0; }
    private void ObserveContext()
    {
        if (_generation == _contextGeneration) return;
        // Old context already owns deletion; never delete recycled names here.
        Array.Clear(_buffers); Array.Clear(_vaos); Array.Clear(_bytes);
        _generation = _contextGeneration; _slot = -1; _active = _failed = false;
        _geometry.Clear();
    }

    internal unsafe void Prepare(IReadOnlyList<RetainedDrawPacket> opaque,
        IReadOnlyList<RetainedDrawPacket> decals, IReadOnlyList<RetainedDrawPacket> translucent)
    {
        ObserveContext();
        _active = false;
        _geometry.Clear();
        _api = OpenGlContextPolicy.VaoApi;
        if (_failed || _api == OpenGlVertexArrayApi.None) return;
        using var timing = OpenGlPassProfiler.Measure(OpenGlCpuPass.ProceduralPrepare);
        void Add(IReadOnlyList<RetainedDrawPacket> list)
        {
            for (int i = 0; i < list.Count; i++) _geometry.Add(list[i].Item);
        }
        Add(opaque); Add(decals); Add(translucent);
        if (_geometry.VertexCount == 0) return;
        _slot = (_slot + 1) % _buffers.Length;
        int previousVao = NativeGL.GetInteger(GetPName.VertexArrayBinding);
        int previousBuffer = NativeGL.GetInteger(GetPName.ArrayBufferBinding);
        int previousElement = NativeGL.GetInteger(GetPName.ElementArrayBufferBinding);
        int previousClient = NativeGL.GetInteger(GetPName.ClientActiveTexture);
        try
        {
            if (_buffers[_slot] == 0)
            {
                _buffers[_slot] = NativeGL.GenBuffer();
                _vaos[_slot] = DesktopVertexArrays.Create(_api);
                if (_buffers[_slot] == 0 || _vaos[_slot] == 0)
                    throw new InvalidOperationException("Could not create procedural stream");
                DesktopVertexArrays.Bind(_api, _vaos[_slot]);
                NativeGL.BindBuffer(BufferTarget.ArrayBuffer, _buffers[_slot]);
                NativeGL.ClientActiveTexture(TextureUnit.Texture0);
                NativeGL.EnableClientState(ArrayCap.VertexArray);
                NativeGL.EnableClientState(ArrayCap.TextureCoordArray);
                NativeGL.DisableClientState(ArrayCap.ColorArray);
                NativeGL.DisableClientState(ArrayCap.NormalArray);
                NativeGL.VertexPointer(3, VertexPointerType.Float, Stride, IntPtr.Zero);
                NativeGL.TexCoordPointer(3, TexCoordPointerType.Float, Stride, (IntPtr)(3 * sizeof(float)));
                DesktopVertexArrays.Bind(_api, previousVao);
                DesktopVertexArrays.Bind(_api, _vaos[_slot]);
                if (!ValidateLayout(_buffers[_slot]))
                    throw new InvalidOperationException("Procedural VAO layout did not survive rebinding");
            }
            NativeGL.BindBuffer(BufferTarget.ArrayBuffer, _buffers[_slot]);
            int bytes = _geometry.VertexCount * Stride;
            fixed (float* vertices = _geometry.Vertices)
                NativeGL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)bytes, (IntPtr)vertices, BufferUsageHint.StreamDraw);
            _residentBytes += bytes - _bytes[_slot]; _bytes[_slot] = bytes;
            OpenGlPassProfiler.Count(OpenGlFrameCounter.BufferOrphans);
            OpenGlPassProfiler.Count(OpenGlFrameCounter.BufferUploads);
            OpenGlPassProfiler.Count(OpenGlFrameCounter.UploadBytes, bytes);
            _active = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _failed = true;
            Console.WriteLine("[glprocedural] using immediate fallback: " + ex.Message);
        }
        finally
        {
            DesktopVertexArrays.Bind(_api, previousVao);
            NativeGL.BindBuffer(BufferTarget.ArrayBuffer, previousBuffer);
            NativeGL.BindBuffer(BufferTarget.ElementArrayBuffer, previousElement);
            NativeGL.ClientActiveTexture((TextureUnit)previousClient);
        }
    }

    private static unsafe bool ValidateLayout(int buffer)
    {
        IntPtr vertex = IntPtr.Zero, uv = IntPtr.Zero;
        NativeGL.GetPointer(GetPointervPName.VertexArrayPointer, (IntPtr)(&vertex));
        NativeGL.GetPointer(GetPointervPName.TextureCoordArrayPointer, (IntPtr)(&uv));
        return NativeGL.GetInteger(GetPName.VertexArrayBufferBinding) == buffer
            && NativeGL.GetInteger(GetPName.TextureCoordArrayBufferBinding) == buffer
            && NativeGL.GetInteger(GetPName.VertexArrayStride) == Stride
            && NativeGL.GetInteger(GetPName.TextureCoordArrayStride) == Stride
            && NativeGL.GetInteger(GetPName.VertexArraySize) == 3
            && NativeGL.GetInteger(GetPName.TextureCoordArraySize) == 3
            && NativeGL.GetInteger(GetPName.VertexArray) != 0
            && NativeGL.GetInteger(GetPName.TextureCoordArray) != 0
            && NativeGL.GetInteger(GetPName.ColorArray) == 0
            && NativeGL.GetInteger(GetPName.NormalArray) == 0
            && vertex == IntPtr.Zero && uv == (IntPtr)(3 * sizeof(float));
    }

    internal bool TryDraw(RenderItem item)
    {
        if (!_active || _generation != _contextGeneration || !_geometry.TryGet(item, out var range)) return false;
        using var timing = OpenGlPassProfiler.Measure(OpenGlCpuPass.ProceduralSubmit);
        if (!DesktopRetainedGeometry.TryDrawProcedural(_api, _vaos[_slot], range.Mode, range.First, range.Count))
            return false;
        // Array draws leave current array attributes unspecified. Restore the
        // exact last UV used by immediate mode, including the palette index Z.
        GraphicsApi.TexCoord3(range.LastTexcoord);
        OpenGlPassProfiler.Count(OpenGlFrameCounter.ProceduralDraws);
        OpenGlPassProfiler.Count(OpenGlFrameCounter.ProceduralVertices, range.Count);
        return true;
    }
    internal void End() { _active = false; _geometry.Clear(); }
    public void Dispose()
    {
        ObserveContext();
        for (int i = 0; i < _buffers.Length; i++)
        {
            if (_vaos[i] != 0) DesktopVertexArrays.Delete(_api, _vaos[i]);
            if (_buffers[i] != 0) NativeGL.DeleteBuffer(_buffers[i]);
            _residentBytes -= _bytes[i];
        }
        Array.Clear(_buffers); Array.Clear(_vaos); Array.Clear(_bytes);
        End();
    }
}
#endif
