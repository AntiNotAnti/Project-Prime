#if !MPHREAD_SERVER && !ANDROID
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using DesktopGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render;

/// <summary>
/// Desktop GL2.1 retained vertex/index buffers for static room lists.
/// The original compiled display list remains available for every mesh and is
/// used when a list has inherited/mixed client state or any unsupported opcode.
/// Legacy lists are never deleted until their normal owner releases them.
/// </summary>
internal static class DesktopRetainedGeometry
{
    private const int Stride = LegacyGeometryBatch.FloatsPerVertex * sizeof(float);
    // Desktop defaults are enabled; environment zero opts out of an individual
    // optimization, an explicit enable flag overrides zero, and legacy wins.
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_VBO") != "0"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-glvbo", StringComparison.OrdinalIgnoreCase));
    private static readonly bool _batchRequested =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_BATCH") != "0"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-glbatch", StringComparison.OrdinalIgnoreCase));
    private static readonly bool _legacy =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FORCE_LISTS") == "1"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-gllegacylist", StringComparison.OrdinalIgnoreCase));

    private sealed class Compiled
    {
        internal int Vao, Vbo, Ibo, TriangleCount, LineCount;
        internal OpenGlVertexArrayApi VaoApi;
        internal long Bytes;
        internal bool Standalone;
        internal bool Colors, Normals, Texcoords;
        internal bool SetColor, SetNormal, SetTexcoord;
        internal Vector4 LastColor;
        internal Vector3 LastNormal, LastTexcoord;
        internal float[]? Vertices;
        internal int[]? Indices;
    }

    private sealed class Combined
    {
        internal int[] Ids = Array.Empty<int>();
        internal Compiled Geometry = null!;
        internal long Bytes, LastUsed, Scope;
    }

    private sealed class DrawToken
    {
        internal object Owner = null!;
        internal long Generation;
        internal ulong Membership;
        internal Combined Group = null!;
    }
    [ThreadStatic] private static object? _tokenOwner;
    [ThreadStatic] private static long _tokenGeneration;
    private static readonly bool _roomGroupsEnabled = OpenGlOptimizationPolicy.Enabled("ROOM_GROUPS");
    private static bool TokenMatches(DrawToken token, object owner, long generation, ulong membership)
        => ReferenceEquals(token.Owner, owner) && token.Generation == generation && token.Membership == membership;

    internal static bool TryDrawBatch(RetainedRoomBatchPlan plan)
    {
        if (!plan.Mergeable || !BatchEnabled || !_worldScope || _lists == null) return false;
        if (!_roomGroupsEnabled) return TryDrawBatch(plan.Ids, plan.Count, plan.Hash);
        _tokenOwner ??= new object();
        if (plan.DrawToken is DrawToken token && TokenMatches(token, _tokenOwner, _tokenGeneration, plan.MembershipRevision))
        {
            DrawGroup(token.Group);
            OpenGlPassProfiler.Count(OpenGlFrameCounter.RoomDrawTokenReuses);
            return true;
        }
        if (!TryDrawBatchCore(plan.Ids, plan.Count, plan.Hash, out Combined? group)) return false;
        if (plan.DrawToken is not DrawToken saved || !ReferenceEquals(saved.Group, group))
            plan.DrawToken = saved = new DrawToken { Group = group! };
        saved.Owner = _tokenOwner; saved.Generation = _tokenGeneration; saved.Membership = plan.MembershipRevision;
        return true;
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
    [ThreadStatic] private static Dictionary<ulong, Combined>? _combinedBatches;
    // Immutable geometry failures only; budget/driver failures always retry.
    private sealed class NegativeBatchCache
    {
        internal const int MaxEntries = 128, MaxIds = 1024;
        private readonly Dictionary<ulong, int[]> _entries = new();
        internal int Count => _entries.Count;
        internal long Bytes { get; private set; }
        internal long Generation { get; private set; }
        internal bool Matches(ulong hash, int[] ids, int count)
        {
            if (!_entries.TryGetValue(hash, out var saved) || saved.Length != count) return false;
            for (int i = 0; i < count; i++) if (saved[i] != ids[i]) return false;
            return true;
        }
        internal void Record(ulong hash, int[] ids, int count)
        {
            if (count < 2 || count > MaxIds || count > ids.Length || Matches(hash, ids, count)
                || !_entries.ContainsKey(hash) && _entries.Count >= MaxEntries) return;
            if (_entries.TryGetValue(hash, out var previous)) Bytes -= previous.Length * sizeof(int);
            var saved = new int[count]; Array.Copy(ids, saved, count); _entries[hash] = saved; Bytes += count * sizeof(int);
        }
        internal void Invalidate() { _entries.Clear(); Bytes = 0; unchecked { Generation++; } }
    }
    [ThreadStatic] private static NegativeBatchCache? _negativeBatches;

    [ThreadStatic] private static long _combinedBytes;
    [ThreadStatic] private static long _useStamp, _scopeStamp, _evictionCount;
    internal static long EvictionCount => _evictionCount;
    private const long MaxCombinedBytes = 64L * 1024 * 1024;
    [ThreadStatic] private static HashSet<int>? _roomCandidates;
    [ThreadStatic] private static Capture? _capture;
    [ThreadStatic] private static bool _worldScope;
    internal static bool WorldScopeActive => _worldScope;
    [ThreadStatic] private static bool _bindingsCaptured;
    [ThreadStatic] private static int _arrayBinding, _elementBinding;
    [ThreadStatic] private static long _uploadedBytes;
    [ThreadStatic] private static long _residentBytes, _highWaterBytes;
    [ThreadStatic] private static int _vaoBinding;
    [ThreadStatic] private static bool _vaoCaptured;
    [ThreadStatic] private static int _clientTexture;
    [ThreadStatic] private static OpenGlVertexArrayApi _scopeVaoApi;
    [ThreadStatic] private static bool _vaoFailureReported;

    internal static bool Enabled => _enabled && !_legacy;
    internal static bool BatchEnabled => Enabled && _batchRequested;
    internal static int RetainedListCount => _lists?.Count ?? 0;
    internal static long UploadedBytes => _uploadedBytes;
    internal static long ResidentBytes => _residentBytes;
    internal static long HighWaterBytes => _highWaterBytes;
    [ThreadStatic] private static long _standaloneBytes, _standaloneHighWater, _cpuArrayBytes, _cpuArrayHighWater;
    internal static long StandaloneBytes => _standaloneBytes;
    internal static long StandaloneHighWaterBytes => _standaloneHighWater;
    internal static long CpuArrayBytes => _cpuArrayBytes;
    internal static long CpuArrayHighWaterBytes => _cpuArrayHighWater;
    internal static long CombinedBytes => _combinedBytes;
    internal static long NegativeBatchIdBytes => _negativeBatches?.Bytes ?? 0;


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
        if (Enabled && id > 0)
            (_roomCandidates ??= new HashSet<int>()).Add(id);
    }

    internal static void NewList(int id, ListMode mode)
    {
        _capture = null;
        // Recompiled IDs must never keep an old promoted mesh or atlas alive,
        // including when the replacement contains unsupported list operations.
        unchecked { _tokenGeneration++; }
        _negativeBatches?.Invalidate();
        InvalidateCombined(id, 1);
        if (_lists != null && _lists.Remove(id, out var previous)) Release(previous);
        if (!Enabled || _roomCandidates == null || !_roomCandidates.Remove(id)
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
        _negativeBatches?.Invalidate();
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
        Compiled? pending = null;
        int oldArray = DesktopGL.GetInteger(GetPName.ArrayBufferBinding);
        int oldElement = DesktopGL.GetInteger(GetPName.ElementArrayBufferBinding);
        try
        {
            vbo = DesktopGL.GenBuffer();
            if (vbo != 0) OpenGlFailureInjection.Hit("retained-promotion-after-vbo");
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
            pending = new Compiled
            {
                Vbo = vbo, Ibo = ibo, TriangleCount = c.Batch.TriIndices.Count,
                LineCount = c.Batch.LineIndices.Count,
                Colors = c.ColorVertices > 0, Normals = c.NormalVertices > 0,
                Texcoords = c.TexcoordVertices > 0,
                SetColor = c.SetColor, SetNormal = c.SetNormal,
                SetTexcoord = c.SetTexcoord, LastColor = c.Color,
                LastNormal = c.Normal, LastTexcoord = c.Texcoord,
                Vertices = BatchEnabled ? vertices : null,
                Indices = BatchEnabled ? indices : null,
                Bytes = (long)vertices.Length * sizeof(float) + (long)indices.Length * sizeof(int)
            };
            TryCreateVao(pending);
            _lists ??= new Dictionary<int, Compiled>();
            _lists.Add(c.Id, pending);
            NoteUpload(pending, standalone: true);
        }
        catch
        {
            // The original display list is still complete. Any failed
            // promotion simply falls back instead of failing room loading.
            if (pending?.Vao > 0) DesktopVertexArrays.Delete(pending.VaoApi, pending.Vao);
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
        ++_scopeStamp;
        _vaoCaptured = false;
        _bindingsCaptured = false;
    }

    internal static void EndWorldScope()
    {
        if (_vaoCaptured) DesktopVertexArrays.Bind(_scopeVaoApi, _vaoBinding);
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
        DrawIndexed(list);
        ApplyFinalAttributes(list);
        return true;
    }

    internal static bool TryDrawBatch(int[] ids, int count, ulong? preparedHash = null)
        => TryDrawBatchCore(ids, count, preparedHash, out _);

    private static bool TryDrawBatchCore(int[] ids, int count, ulong? preparedHash, out Combined? selected)
    {
        selected = null;
        if (!BatchEnabled || !_worldScope || _lists == null
            || count < 2 || count > ids.Length)
            return false;
        ulong key = preparedHash ?? RetainedRoomBatchPlan.HashIds(ids, count);
        _combinedBatches ??= new Dictionary<ulong, Combined>();
        if (!_combinedBatches.TryGetValue(key, out Combined? group))
        {
            OpenGlPassProfiler.Count(OpenGlFrameCounter.CombinedGeometryMisses);
            _negativeBatches ??= new NegativeBatchCache();
            if (_negativeBatches.Matches(key, ids, count))
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.BatchPreflightFailureReuses);
                return false;
            }
            group = BuildCombined(ids, count, out bool unsupported);
            if (group == null)
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.CombinedGeometryFallbacks);
                if (unsupported) _negativeBatches.Record(key, ids, count);
                return false;
            }
            _combinedBatches.Add(key, group);
        }
        else
        {
            // A hash collision must never silently draw unrelated geometry.
            if (group.Ids.Length != count) return false;
            for (int i = 0; i < count; i++)
                if (group.Ids[i] != ids[i]) return false;
            OpenGlPassProfiler.Count(OpenGlFrameCounter.CombinedGeometryHits);
        }
        selected = group;
        DrawGroup(group);
        return true;
    }

    private static void DrawGroup(Combined group)
    {
        group.LastUsed = ++_useStamp;
        group.Scope = _scopeStamp;
        DrawIndexed(group.Geometry);
        ApplyFinalAttributes(group.Geometry);
    }

    private static unsafe Combined? BuildCombined(int[] ids, int count, out bool unsupported)
    {
        using var buildTiming = OpenGlPassProfiler.Measure(OpenGlCpuPass.CombinedGeometryBuild);
        unsupported = true;
        if (_lists == null || !_lists.TryGetValue(ids[0], out Compiled? first)
            || first.Vertices == null || first.Indices == null
            || first.LineCount != 0)
            return null;
        long floats = 0, indices = 0;
        for (int i = 0; i < count; i++)
        {
            if (!_lists.TryGetValue(ids[i], out Compiled? part)
                || part.Vertices == null || part.Indices == null
                || part.TriangleCount == 0 || part.LineCount != 0
                || part.Colors != first.Colors
                || part.Normals != first.Normals
                || part.Texcoords != first.Texcoords
                || !CanMergeAttributeEffects(i == count - 1, part.Colors, part.SetColor,
                    part.Normals, part.SetNormal, part.Texcoords, part.SetTexcoord))
                return null;
            floats += part.Vertices.Length;
            indices += part.TriangleCount;
            if (floats > int.MaxValue || indices > int.MaxValue)
                return null;
        }
        unsupported = false;
        long bytes = floats * sizeof(float) + indices * sizeof(int);
        if (!ReserveCombined(bytes)) return null;
        OpenGlPassProfiler.Count(OpenGlFrameCounter.CombinedGeometryBuildBytes, bytes);
        float[] mergedVertices = new float[(int)floats];
        int[] mergedIndices = new int[(int)indices];
        int vertexFloatOffset = 0, indexOffset = 0;
        for (int i = 0; i < count; i++)
        {
            Compiled part = _lists[ids[i]];
            float[] vertices = part.Vertices!;
            int[] indexes = part.Indices!;
            Array.Copy(vertices, 0, mergedVertices, vertexFloatOffset,
                vertices.Length);
            int baseVertex = vertexFloatOffset / LegacyGeometryBatch.FloatsPerVertex;
            for (int j = 0; j < part.TriangleCount; j++)
                mergedIndices[indexOffset + j] = checked(indexes[j] + baseVertex);
            vertexFloatOffset += vertices.Length;
            indexOffset += part.TriangleCount;
        }

        int vbo = 0, ibo = 0;
        Compiled? geometry = null;
        int oldVbo = DesktopGL.GetInteger(GetPName.ArrayBufferBinding);
        int oldIbo = DesktopGL.GetInteger(GetPName.ElementArrayBufferBinding);
        try
        {
            vbo = DesktopGL.GenBuffer();
            if (vbo != 0) OpenGlFailureInjection.Hit("retained-combined-after-vbo");
            ibo = DesktopGL.GenBuffer();
            if (vbo == 0 || ibo == 0) throw new InvalidOperationException("retained batch allocation");
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            using var uploadTiming = OpenGlPassProfiler.Measure(OpenGlCpuPass.CombinedGeometryUpload);
            fixed (float* v = mergedVertices)
                DesktopGL.BufferData(BufferTarget.ArrayBuffer,
                    (IntPtr)(mergedVertices.Length * sizeof(float)), (IntPtr)v,
                    BufferUsageHint.StaticDraw);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, ibo);
            fixed (int* ix = mergedIndices)
                DesktopGL.BufferData(BufferTarget.ElementArrayBuffer,
                    (IntPtr)(mergedIndices.Length * sizeof(int)), (IntPtr)ix,
                    BufferUsageHint.StaticDraw);
            Compiled last = _lists[ids[count - 1]];
            geometry = new Compiled
            {
                Vbo = vbo, Ibo = ibo, TriangleCount = mergedIndices.Length,
                Colors = first.Colors, Normals = first.Normals,
                Texcoords = first.Texcoords, LastColor = last.LastColor,
                LastNormal = last.LastNormal, LastTexcoord = last.LastTexcoord,
                SetColor = last.SetColor, SetNormal = last.SetNormal,
                SetTexcoord = last.SetTexcoord
            };
            geometry.Bytes = bytes;
            TryCreateVao(geometry);
            NoteUpload(geometry);
            _combinedBytes += bytes;
            return new Combined
            {
                Ids = ids.AsSpan(0, count).ToArray(),
                Geometry = geometry, Bytes = bytes
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
            and not StackOverflowException)
        {
            if (geometry?.Vao > 0) DesktopVertexArrays.Delete(geometry.VaoApi, geometry.Vao);
            if (vbo != 0) DesktopGL.DeleteBuffer(vbo);
            if (ibo != 0) DesktopGL.DeleteBuffer(ibo);
            return null; // Individual display lists remain the fallback.
        }
        finally
        {
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, oldVbo);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, oldIbo);
        }
    }

    private static void CaptureBindings()
    {
        if (_bindingsCaptured) return;
        _arrayBinding = DesktopGL.GetInteger(GetPName.ArrayBufferBinding);
        _elementBinding = DesktopGL.GetInteger(GetPName.ElementArrayBufferBinding);
        _clientTexture = DesktopGL.GetInteger(GetPName.ClientActiveTexture);
        _bindingsCaptured = true;
    }

    private static void DrawIndexed(Compiled list)
    {
        if (list.Vao != 0)
        {
            if (!_vaoCaptured)
            {
                _vaoBinding = DesktopGL.GetInteger(GetPName.VertexArrayBinding);
                _scopeVaoApi = list.VaoApi;
                _vaoCaptured = true;
            }
            bool apple = list.VaoApi == OpenGlVertexArrayApi.Apple;
            if (apple) CaptureBindings();
            DesktopVertexArrays.Bind(list.VaoApi, list.Vao);
            try
            {
                // APPLE predates ARB VBOs: do not assume the core VAO's
                // element-buffer capture contract for the extension path.
                if (apple) DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, list.Ibo);
                SubmitIndices(list);
                OpenGlPassProfiler.Count(OpenGlFrameCounter.VaoDraws);
                OpenGlPassProfiler.Count(OpenGlFrameCounter.AvoidedPointerCalls,
                    1 + (list.Colors ? 1 : 0) + (list.Normals ? 1 : 0) + (list.Texcoords ? 1 : 0));
            }
            finally
            {
                DesktopVertexArrays.Bind(list.VaoApi, _vaoBinding);
                if (apple)
                {
                    DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, _arrayBinding);
                    DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBinding);
                    DesktopGL.ClientActiveTexture((TextureUnit)_clientTexture);
                }
            }
            return;
        }
        CaptureBindings();
        DesktopGL.PushClientAttrib(ClientAttribMask.ClientVertexArrayBit);
        try
        {
            DesktopGL.ClientActiveTexture(TextureUnit.Texture0);
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, list.Vbo);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, list.Ibo);
            SetupPointers(list);
            SubmitIndices(list);
        }
        finally
        {
            DesktopGL.PopClientAttrib();
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, _arrayBinding);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBinding);
        }
    }

    internal static bool TryDrawProcedural(OpenGlVertexArrayApi api, int vao,
        PrimitiveType mode, int first, int count)
    {
        if (!_worldScope || _capture != null) return false;
        if (!_vaoCaptured)
        {
            _vaoBinding = DesktopGL.GetInteger(GetPName.VertexArrayBinding);
            _scopeVaoApi = api;
            _vaoCaptured = true;
        }
        bool apple = api == OpenGlVertexArrayApi.Apple;
        if (apple) CaptureBindings();
        DesktopVertexArrays.Bind(api, vao);
        try { DesktopGL.DrawArrays(mode, first, count); }
        finally
        {
            DesktopVertexArrays.Bind(api, _vaoBinding);
            if (apple)
            {
                DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, _arrayBinding);
                DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBinding);
                DesktopGL.ClientActiveTexture((TextureUnit)_clientTexture);
            }
        }
        return true;
    }

    private static void NoteUpload(Compiled list, bool standalone = false)
    {
        list.Standalone = standalone;
        if (standalone)
        {
            _standaloneBytes += list.Bytes;
            _standaloneHighWater = Math.Max(_standaloneHighWater, _standaloneBytes);
            _cpuArrayBytes += CpuBytes(list);
            _cpuArrayHighWater = Math.Max(_cpuArrayHighWater, _cpuArrayBytes);
        }
        _uploadedBytes += list.Bytes; _residentBytes += list.Bytes;
        _highWaterBytes = Math.Max(_highWaterBytes, _residentBytes);
        OpenGlPassProfiler.Count(OpenGlFrameCounter.BufferUploads, 2);
        OpenGlPassProfiler.Count(OpenGlFrameCounter.UploadBytes, list.Bytes);
    }

    private static void TryCreateVao(Compiled list)
    {
        list.VaoApi = OpenGlContextPolicy.VaoApi;
        if (list.VaoApi == OpenGlVertexArrayApi.None) return;
        int previous = DesktopGL.GetInteger(GetPName.VertexArrayBinding);
        int clientTexture = DesktopGL.GetInteger(GetPName.ClientActiveTexture);
        try
        {
            list.Vao = DesktopVertexArrays.Create(list.VaoApi);
            if (list.Vao == 0) return;
            OpenGlFailureInjection.Hit("retained-vao-after-create");
            DesktopVertexArrays.Bind(list.VaoApi, list.Vao);
            DesktopGL.ClientActiveTexture(TextureUnit.Texture0);
            DesktopGL.BindBuffer(BufferTarget.ArrayBuffer, list.Vbo);
            DesktopGL.BindBuffer(BufferTarget.ElementArrayBuffer, list.Ibo);
            SetupPointers(list);
            // Verify retained buffer associations after an actual unbind/rebind.
            // This happens only at geometry creation, never per presentation.
            DesktopVertexArrays.Bind(list.VaoApi, previous);
            DesktopVertexArrays.Bind(list.VaoApi, list.Vao);
            if (!ValidateVaoPointers(list))
                throw new InvalidOperationException("retained vertex-array state did not survive rebinding");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (list.Vao != 0) DesktopVertexArrays.Delete(list.VaoApi, list.Vao);
            list.Vao = 0;
            if (!_vaoFailureReported)
            {
                Console.WriteLine("[glvao] layout validation failed; using retained client arrays: " + ex.Message);
                _vaoFailureReported = true;
            }
        }
        finally
        {
            DesktopVertexArrays.Bind(list.VaoApi, previous);
            DesktopGL.ClientActiveTexture((TextureUnit)clientTexture);
        }
    }

    private static unsafe bool ValidateVaoPointers(Compiled list)
    {
        bool Array(bool enabled, GetPName enable, GetPName buffer, GetPName stride,
            GetPName type, GetPointervPName pointer, int offset, GetPName size, int components)
        {
            if ((DesktopGL.GetInteger(enable) != 0) != enabled) return false;
            if (!enabled) return true;
            IntPtr value = IntPtr.Zero;
            DesktopGL.GetPointer(pointer, (IntPtr)(&value));
            return DesktopGL.GetInteger(buffer) == list.Vbo
                && DesktopGL.GetInteger(stride) == Stride
                && DesktopGL.GetInteger(type) == (int)VertexPointerType.Float
                && value == (IntPtr)offset
                && (components == 0 || DesktopGL.GetInteger(size) == components);
        }
        return (list.VaoApi != OpenGlVertexArrayApi.Apple || DesktopGL.Apple.IsVertexArray(list.Vao))
            && Array(true, GetPName.VertexArray, GetPName.VertexArrayBufferBinding, GetPName.VertexArrayStride,
                GetPName.VertexArrayType, GetPointervPName.VertexArrayPointer, 0, GetPName.VertexArraySize, 3)
            && Array(list.Colors, GetPName.ColorArray, GetPName.ColorArrayBufferBinding, GetPName.ColorArrayStride,
                GetPName.ColorArrayType, GetPointervPName.ColorArrayPointer, 3 * sizeof(float), GetPName.ColorArraySize, 4)
            && Array(list.Normals, GetPName.NormalArray, GetPName.NormalArrayBufferBinding, GetPName.NormalArrayStride,
                GetPName.NormalArrayType, GetPointervPName.NormalArrayPointer, 7 * sizeof(float), default, 0)
            && Array(list.Texcoords, GetPName.TextureCoordArray, GetPName.TextureCoordArrayBufferBinding, GetPName.TextureCoordArrayStride,
                GetPName.TextureCoordArrayType, GetPointervPName.TextureCoordArrayPointer, 10 * sizeof(float), GetPName.TextureCoordArraySize, 3);
    }

    private static void SetupPointers(Compiled list)
    {
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
    }

    private static void SubmitIndices(Compiled list)
    {
        OpenGlPassProfiler.Count(OpenGlFrameCounter.IndexedDraws,
            (list.TriangleCount > 0 ? 1 : 0) + (list.LineCount > 0 ? 1 : 0));
        if (list.TriangleCount > 0)
            DesktopGL.DrawElements(PrimitiveType.Triangles, list.TriangleCount,
                DrawElementsType.UnsignedInt, IntPtr.Zero);
        if (list.LineCount > 0)
            DesktopGL.DrawElements(PrimitiveType.Lines, list.LineCount,
                DrawElementsType.UnsignedInt,
                (IntPtr)(list.TriangleCount * sizeof(int)));
    }

    private static void ApplyFinalAttributes(Compiled list)
    {
        // A sequence of GL display lists leaves the last list's current
        // attributes active, even after the client array draw is finished.
        if (list.SetColor)
            DesktopGL.Color4(list.LastColor.X, list.LastColor.Y,
                list.LastColor.Z, list.LastColor.W);
        if (list.SetNormal)
            DesktopGL.Normal3(list.LastNormal.X, list.LastNormal.Y, list.LastNormal.Z);
        if (list.SetTexcoord)
            DesktopGL.TexCoord3(list.LastTexcoord.X, list.LastTexcoord.Y,
                list.LastTexcoord.Z);
    }

    // Only combined copies are evictable; original promoted lists retain ownership.
    // GL deletion is asynchronous and needs no fence or blocking completion wait.
    private static bool ReserveCombined(long bytes)
    {
        if (bytes > MaxCombinedBytes || bytes < 0) return false;
        if (bytes <= MaxCombinedBytes - _combinedBytes) return true;
        if (!OpenGlOptimizationPolicy.Enabled("ATLAS_EVICTION") || _combinedBatches == null) return false;
        long evictable = 0;
        foreach (Combined batch in _combinedBatches.Values)
            if (batch.Scope != _scopeStamp) evictable += batch.Bytes;
        // Avoid discarding useful old groups when the current scope pins too much.
        if (!CanReserveCombined(bytes, _combinedBytes, evictable, MaxCombinedBytes)) return false;
        while (bytes > MaxCombinedBytes - _combinedBytes)
        {
            ulong key = 0;
            Combined? oldest = null;
            foreach (var pair in _combinedBatches)
            {
                Combined batch = pair.Value;
                if (OlderEvictable(batch.LastUsed, batch.Scope, _scopeStamp, oldest?.LastUsed))
                { key = pair.Key; oldest = batch; }
            }
            if (oldest == null) return false;
            _combinedBytes -= oldest.Bytes;
            Release(oldest.Geometry);
            _combinedBatches.Remove(key);
            ++_evictionCount;
        }
        return true;
    }

    internal static bool CanReserveCombined(long requested, long resident, long evictable, long budget)
        => requested >= 0 && requested <= budget && resident >= 0 && resident <= budget
        && evictable >= 0 && evictable <= resident && requested <= budget - resident + evictable;
    internal static bool OlderEvictable(long used, long scope, long currentScope, long? oldest)
        => scope != currentScope && (!oldest.HasValue || used < oldest.Value);

    // A disabled array inherits one current value for the entire merged draw.
    // Earlier lists must not change it after their vertices: ordered lists can
    // feed that write into a later list, and the final list may leave it intact.
    internal static bool CanMergeAttributeEffects(bool final, bool colors, bool setsColor,
        bool normals, bool setsNormal, bool texcoords, bool setsTexcoord)
        => final || (colors || !setsColor) && (normals || !setsNormal) && (texcoords || !setsTexcoord);

    internal static int CheckTrailingAttributePolicy()
    {
        int failures = 0;
        void Check(bool value, string name)
        { Console.WriteLine($"[glbatchattributecheck] {(value ? "PASS" : "FAIL")}: {name}"); if (!value) failures++; }
        Check(CanMergeAttributeEffects(false, false, false, false, false, false, false),
            "pure inherited lists can share unchanged current attributes");
        Check(!CanMergeAttributeEffects(false, false, true, false, false, false, false),
            "nonfinal trailing color cannot disappear from merged final state");
        Check(!CanMergeAttributeEffects(false, false, false, false, true, false, false),
            "nonfinal trailing normal cannot feed later inherited vertices in merged arrays");
        Check(!CanMergeAttributeEffects(false, false, false, false, false, false, true),
            "nonfinal trailing UV cannot feed later inherited coverage or palette index");
        Check(CanMergeAttributeEffects(true, false, true, false, true, false, true),
            "final trailing attributes remain reproducible by final attribute application");
        Check(CanMergeAttributeEffects(false, true, true, true, true, true, true),
            "fully authored arrays retain per-vertex attributes and final list state");
        return failures;
    }

    internal static int CheckDrawTokenPolicy()
    {
        int failures = 0;
        void Check(bool value, string name)
        { Console.WriteLine($"[glbatchtokencheck] {(value ? "PASS" : "FAIL")}: {name}"); if (!value) failures++; }
        object owner = new(); var token = new DrawToken { Owner = owner, Generation = 3, Membership = 9, Group = new Combined() };
        Check(TokenMatches(token, owner, 3, 9), "same owning context and exact membership generation reuse token");
        Check(!TokenMatches(token, new object(), 3, 9), "different or recreated context cannot inherit handles");
        Check(!TokenMatches(token, owner, 4, 9), "compile/delete/eviction generation rejects released geometry");
        Check(!TokenMatches(token, owner, 3, 10), "ordered membership change rejects prior group");
        var group = token.Group; token.Generation = 4;
        Check(TokenMatches(token, owner, 4, 9) && ReferenceEquals(token.Group, group), "successful revalidation can renew token without allocation");
        return failures;
    }

    internal static int CheckNegativeBatchPolicy()
    {
        int failures = 0;
        void Check(bool condition, string name)
        {
            Console.WriteLine($"[glbatchpreflightcheck] {(condition ? "PASS" : "FAIL")}: {name}");
            if (!condition) failures++;
        }
        var cache = new NegativeBatchCache();
        int[] ids = { 1, 2, 3 };
        Check(!cache.Matches(7, ids, 3), "unknown failure retries geometry preflight");
        cache.Record(7, ids, 3);
        Check(cache.Matches(7, new[] { 1, 2, 3 }, 3), "exact failure reuses proof across passes and frames");
        Check(!cache.Matches(7, new[] { 1, 3, 2 }, 3) && !cache.Matches(7, ids, 2), "hash collision and sequence length never authorize fallback proof");
        ids[0] = 4;
        Check(!cache.Matches(7, ids, 3) && cache.Matches(7, new[] { 1, 2, 3 }, 3), "stored IDs are isolated from mutable scratch");
        long revision = cache.Generation; cache.Invalidate();
        Check(cache.Generation != revision && cache.Count == 0 && cache.Bytes == 0 && !cache.Matches(7, new[] { 1, 2, 3 }, 3), "compile/delete generation invalidates every negative proof");
        Check(!cache.Matches(8, ids, 3), "unrecorded budget or driver failure retries next attempt");
        for (int i = 0; i < NegativeBatchCache.MaxEntries + 1; i++) cache.Record((ulong)i, ids, 3);
        Check(cache.Count == NegativeBatchCache.MaxEntries && cache.Bytes == NegativeBatchCache.MaxEntries * 3L * sizeof(int) && !cache.Matches((ulong)NegativeBatchCache.MaxEntries, ids, 3), "bounded entry budget preserves ordinary fallback");
        cache.Invalidate();
        var oversized = new int[NegativeBatchCache.MaxIds + 1]; cache.Record(1, oversized, oversized.Length);
        Check(cache.Count == 0, "oversized ID sequence cannot retain unbounded storage");
        cache.Record(1, new int[NegativeBatchCache.MaxIds], NegativeBatchCache.MaxIds);
        Check(cache.Count == 1 && cache.Matches(1, new int[NegativeBatchCache.MaxIds], NegativeBatchCache.MaxIds), "maximum bounded proof remains reusable");
        Check(!new NegativeBatchCache().Matches(1, new int[NegativeBatchCache.MaxIds], NegativeBatchCache.MaxIds), "new context cache cannot inherit another context proof");
        return failures;
    }

    internal static int CheckAtlasEvictionPolicy()
    {
        bool valid = CanReserveCombined(20, 80, 0, 100)
            && CanReserveCombined(40, 80, 20, 100)
            && !CanReserveCombined(41, 80, 20, 100)
            && !CanReserveCombined(101, 80, 80, 100)
            && !CanReserveCombined(-1, 80, 20, 100)
            && OlderEvictable(10, 1, 2, null)
            && OlderEvictable(10, 1, 2, 11)
            && !OlderEvictable(12, 1, 2, 11)
            && !OlderEvictable(1, 2, 2, 11);
        Console.WriteLine($"[glatlasevictioncheck] {(valid ? "PASS" : "FAIL")}: bounded LRU, oversized fallback and scope pinning");
        return valid ? 0 : 1;
    }

    private static void InvalidateCombined(int first, int count)
    {
        if (_combinedBatches == null || count <= 0) return;
        List<ulong>? remove = null;
        foreach (var pair in _combinedBatches)
        {
            foreach (int id in pair.Value.Ids)
            {
                if (id < first || (long)id >= (long)first + count) continue;
                (remove ??= new()).Add(pair.Key);
                break;
            }
        }
        if (remove == null) return;
        foreach (ulong key in remove)
        {
            Combined batch = _combinedBatches[key];
            _combinedBytes -= batch.Bytes;
            Release(batch.Geometry);
            _combinedBatches.Remove(key);
        }
    }

    private static void ClearCombined()
    {
        if (_combinedBatches != null)
        {
            foreach (Combined batch in _combinedBatches.Values)
                Release(batch.Geometry);
            _combinedBatches.Clear();
        }
        _combinedBytes = 0;
    }

    private static long CpuBytes(Compiled c) => (long)(c.Vertices?.Length ?? 0) * sizeof(float)
        + (long)(c.Indices?.Length ?? 0) * sizeof(int);

    private static void Release(Compiled c)
    {
        unchecked { _tokenGeneration++; }
        if (c.Standalone) { _standaloneBytes -= c.Bytes; _cpuArrayBytes -= CpuBytes(c); }
        if (c.Vao != 0) DesktopVertexArrays.Delete(c.VaoApi, c.Vao);
        _residentBytes -= c.Bytes;
        DesktopGL.DeleteBuffer(c.Vbo);
        DesktopGL.DeleteBuffer(c.Ibo);
    }

    internal static void DeleteLists(int first, int count)
    {
        // A promoted list can appear in several combined immutable buffers.
        // Discard them before the list ID is recycled or the owning map unloads.
        unchecked { _tokenGeneration++; }
        _negativeBatches?.Invalidate();
        InvalidateCombined(first, count);
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
        _tokenOwner = null; _tokenGeneration = 0;
        _lists = null;
        _combinedBatches = null;
        _negativeBatches = null;
        _combinedBytes = 0;
        _useStamp = _scopeStamp = _evictionCount = 0;
        _roomCandidates = null;
        _capture = null;
        _worldScope = false;
        _bindingsCaptured = false;
        _uploadedBytes = 0;
        _residentBytes = _highWaterBytes = 0;
        _standaloneBytes = _standaloneHighWater = _cpuArrayBytes = _cpuArrayHighWater = 0;
        _vaoCaptured = false;
        _scopeVaoApi = OpenGlVertexArrayApi.None;
        _vaoFailureReported = false;
    }
}
#endif
