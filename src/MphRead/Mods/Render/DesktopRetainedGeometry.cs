#if !MPHREAD_SERVER && !ANDROID
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using DesktopGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render;

/// <summary>
/// Opt-in desktop GL2.1 retained vertex/index buffers for static room lists.
/// The original compiled display list remains available for every mesh and is
/// used when a list has inherited/mixed client state or any unsupported opcode.
/// Legacy lists are never deleted until their normal owner releases them.
/// </summary>
internal static class DesktopRetainedGeometry
{
    private const int Stride = LegacyGeometryBatch.FloatsPerVertex * sizeof(float);
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_VBO") == "1"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-glvbo", StringComparison.OrdinalIgnoreCase));
    private static readonly bool _legacy =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FORCE_LISTS") == "1"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-gllegacylist", StringComparison.OrdinalIgnoreCase));

    private sealed class Compiled
    {
        internal int Vbo, Ibo, TriangleCount, LineCount;
        internal bool Colors, Normals, Texcoords;
        internal bool SetColor, SetNormal, SetTexcoord;
        internal Vector4 LastColor;
        internal Vector3 LastNormal, LastTexcoord;
    }

    private sealed class Capture
    {
        internal int Id;
        internal readonly LegacyGeometryBatch Batch = new();
        internal bool Invalid, Inside;
        internal bool OwnColor, OwnNormal, OwnTexcoord;
        internal bool SetColor, SetNormal, SetTexcoord;
        internal int ColorVertices, NormalVertices, TexcoordVertices;
        internal Vector4 Color = Vector4.One;
        internal Vector3 Normal = Vector3.UnitZ, Texcoord = Vector3.Zero;
    }

    [ThreadStatic] private static Dictionary<int, Compiled>? _lists;
    [ThreadStatic] private static HashSet<int>? _roomCandidates;
    [ThreadStatic] private static Capture? _capture;
    [ThreadStatic] private static bool _worldScope;
    [ThreadStatic] private static bool _bindingsCaptured;
    [ThreadStatic] private static int _arrayBinding, _elementBinding;
    [ThreadStatic] private static long _uploadedBytes;

    internal static bool Enabled => _enabled && !_legacy;
    internal static int RetainedListCount => _lists?.Count ?? 0;
    internal static long UploadedBytes => _uploadedBytes;

    // A mixed stream would inherit the current attribute from GL for only
    // some vertices. No fixed-function client array can represent that without
    // converting the entire shader/material contract, so keep its display list.
    internal static bool CanUseClientArrays(
        int vertices, int colored, int normaled, int textured)
        => vertices > 0
           && (colored == 0 || colored == vertices)
           && (normaled == 0 || normaled == vertices)
           && (textured == 0 || textured == vertices);

    internal static void RegisterRoomList(int id)
    {
        if (_enabled && id > 0)
            (_roomCandidates ??= new HashSet<int>()).Add(id);
    }

    internal static void NewList(int id, ListMode mode)
    {
        _capture = null;
        if (!_enabled || _roomCandidates == null || !_roomCandidates.Remove(id)
            || mode != ListMode.Compile)
            return;
        _capture = new Capture { Id = id };
    }

    internal static void Begin(PrimitiveType mode)
    {
        Capture? c = _capture;
        if (c == null) return;
        if (c.Inside) { c.Invalid = true; return; }
        c.Inside = true;
        c.Batch.Begin(mode);
    }

    internal static void End()
    {
        Capture? c = _capture;
        if (c == null || c.Invalid) return;
        if (!c.Inside) { c.Invalid = true; return; }
        c.Inside = false;
        try { c.Batch.End(); }
        catch (ProgramException) { c.Invalid = true; }
    }

    internal static void Vertex(Vector3 position)
    {
        Capture? c = _capture;
        if (c == null || c.Invalid) return;
        if (!c.Inside) { c.Invalid = true; return; }
        c.Batch.AddVertex(position, c.Color, c.Normal, c.Texcoord,
            c.OwnColor, c.OwnNormal);
        if (c.OwnColor) c.ColorVertices++;
        if (c.OwnNormal) c.NormalVertices++;
        if (c.OwnTexcoord) c.TexcoordVertices++;
    }

    internal static void Color(Vector4 value)
    {
        if (_capture is not { } c) return;
        c.Color = value;
        c.OwnColor = c.SetColor = true;
    }

    internal static void Normal(Vector3 value)
    {
        if (_capture is not { } c) return;
        c.Normal = value;
        c.OwnNormal = c.SetNormal = true;
    }

    internal static void Texcoord(Vector3 value)
    {
        if (_capture is not { } c) return;
        c.Texcoord = value;
        c.OwnTexcoord = c.SetTexcoord = true;
    }

    internal static void RejectCurrentList()
    {
        if (_capture != null) _capture.Invalid = true;
    }

    internal static unsafe void EndList()
    {
        Capture? c = _capture;
        _capture = null;
        if (c == null || c.Invalid || c.Inside ||
            !CanUseClientArrays(c.Batch.VertexCount, c.ColorVertices,
                c.NormalVertices, c.TexcoordVertices))
            return;
        // Immutable uploads happen only after glEndList, never inside another
        // display list's compile state. No dynamic per-frame CPU vertex rebuild.
        float[] vertices = c.Batch.Vertices.ToArray();
        int[] indices = c.Batch.BuildIndexArray();
        int vbo = 0, ibo = 0;
        int oldArray = DesktopGL.GetInteger(GetPName.ArrayBufferBinding);
        int oldElement = DesktopGL.GetInteger(GetPName.ElementArrayBufferBinding);
        try
        {
            vbo = DesktopGL.GenBuffer();
            ibo = DesktopGL.GenBuffer();
            if (vbo == 0 || ibo == 0)
                throw new InvalidOperationException("GL could not allocate retained room buffers");
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            fixed (float* v = vertices)
                DesktopGL.BufferData(BufferTarget.ArrayBuffer,
                    (IntPtr)(vertices.Length * sizeof(float)), (IntPtr)v,
                    BufferUsageHint.StaticDraw);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
            fixed (int* ix = indices)
                DesktopGL.BufferData(BufferTarget.ElementArrayBuffer,
                    (IntPtr)(indices.Length * sizeof(int)), (IntPtr)ix,
                    BufferUsageHint.StaticDraw);
            _lists ??= new Dictionary<int, Compiled>();
            if (_lists.Remove(c.Id, out Compiled? prior))
                Release(prior);
            _lists.Add(c.Id, new Compiled
            {
                Vbo = vbo, Ibo = ibo, TriangleCount = c.Batch.TriIndices.Count,
                LineCount = c.Batch.LineIndices.Count,
                Colors = c.ColorVertices > 0, Normals = c.NormalVertices > 0,
                Texcoords = c.TexcoordVertices > 0,
                SetColor = c.SetColor, SetNormal = c.SetNormal,
                SetTexcoord = c.SetTexcoord, LastColor = c.Color,
                LastNormal = c.Normal, LastTexcoord = c.Texcoord
            });
            _uploadedBytes += (long)vertices.Length * sizeof(float)
                + (long)indices.Length * sizeof(int);
        }
        catch
        {
            // The original display list is still complete. Any failed
            // promotion simply falls back instead of failing room loading.
            if (vbo != 0) DesktopGL.DeleteBuffer(vbo);
            if (ibo != 0) DesktopGL.DeleteBuffer(ibo);
        }
        finally
        {
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, oldArray);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, oldElement);
        }
    }

    internal static void BeginWorldScope()
    {
        _worldScope = true;
        _bindingsCaptured = false;
    }

    internal static void EndWorldScope()
    {
        if (_bindingsCaptured)
        {
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, _arrayBinding);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBinding);
        }
        _worldScope = false;
        _bindingsCaptured = false;
    }

    internal static bool TryDraw(int id)
    {
        if (!Enabled || !_worldScope || _lists == null
            || !_lists.TryGetValue(id, out Compiled? list))
            return false;
        if (!_bindingsCaptured)
        {
            _arrayBinding = DesktopGL.GetInteger(GetPName.ArrayBufferBinding);
            _elementBinding = DesktopGL.GetInteger(GetPName.ElementArrayBufferBinding);
            _bindingsCaptured = true;
        }
        // Preserve whatever client-array layout a different renderer/window
        // left behind. Only the world scope can consume the promoted buffers.
        DesktopGL.PushClientAttrib(ClientAttribMask.ClientVertexArrayBit);
        try
        {
            DesktopGL.ClientActiveTexture(TextureUnit.Texture0);
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, list.Vbo);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, list.Ibo);
            DesktopGL.EnableClientState(ArrayCap.VertexArray);
            DesktopGL.VertexPointer(3, VertexPointerType.Float, Stride, IntPtr.Zero);
            if (list.Colors)
            {
                DesktopGL.EnableClientState(ArrayCap.ColorArray);
                DesktopGL.ColorPointer(4, ColorPointerType.Float, Stride, (IntPtr)(3 * sizeof(float)));
            }
            else DesktopGL.DisableClientState(ArrayCap.ColorArray);
            if (list.Normals)
            {
                DesktopGL.EnableClientState(ArrayCap.NormalArray);
                DesktopGL.NormalPointer(NormalPointerType.Float, Stride, (IntPtr)(7 * sizeof(float)));
            }
            else DesktopGL.DisableClientState(ArrayCap.NormalArray);
            if (list.Texcoords)
            {
                DesktopGL.EnableClientState(ArrayCap.TextureCoordArray);
                DesktopGL.TexCoordPointer(3, TexCoordPointerType.Float, Stride, (IntPtr)(10 * sizeof(float)));
            }
            else DesktopGL.DisableClientState(ArrayCap.TextureCoordArray);
            if (list.TriangleCount > 0)
                DesktopGL.DrawElements(PrimitiveType.Triangles, list.TriangleCount,
                    DrawElementsType.UnsignedInt, IntPtr.Zero);
            if (list.LineCount > 0)
                DesktopGL.DrawElements(PrimitiveType.Lines, list.LineCount,
                    DrawElementsType.UnsignedInt, (IntPtr)(list.TriangleCount * sizeof(int)));
        }
        finally
        {
            DesktopGL.PopClientAttrib();
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, _arrayBinding);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBinding);
        }
        // Display lists execute attribute commands and leave the final current
        // values behind. Reproduce that side effect after the client-array draw.
        if (list.SetColor)
            DesktopGL.Color4(list.LastColor.X, list.LastColor.Y,
                list.LastColor.Z, list.LastColor.W);
        if (list.SetNormal)
            DesktopGL.Normal3(list.LastNormal.X, list.LastNormal.Y, list.LastNormal.Z);
        if (list.SetTexcoord)
            DesktopGL.TexCoord3(list.LastTexcoord.X, list.LastTexcoord.Y, list.LastTexcoord.Z);
        return true;
    }

    private static void Release(Compiled c)
    {
        DesktopGL.DeleteBuffer(c.Vbo);
        DesktopGL.DeleteBuffer(c.Ibo);
    }

    internal static void DeleteLists(int first, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int id = first + i;
            _roomCandidates?.Remove(id);
            if (_lists != null && _lists.Remove(id, out Compiled? c))
                Release(c);
        }
    }

    internal static void ResetContext()
    {
        // Context loss destroys its GPU objects; never delete an old context's
        // handles after the new context has been made current.
        _lists = null;
        _roomCandidates = null;
        _capture = null;
        _worldScope = false;
        _bindingsCaptured = false;
        _uploadedBytes = 0;
    }
}
#endif
