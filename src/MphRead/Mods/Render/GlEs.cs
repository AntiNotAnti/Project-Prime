#if ANDROID
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ES = OpenTK.Graphics.ES30;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// The engine's <c>GL</c>, on OpenGL ES 3.0.
    ///
    /// The Android head compiles the shared sources with a global using alias
    /// pointing <c>GL</c> at this class, so every <c>GL.Something</c> in the
    /// renderer, the movie decoder and the capture code lands here instead of
    /// in OpenTK's desktop bindings, with no edit to any of them. The arguments
    /// stay the desktop enum types -- they are the same GL constants, and
    /// keeping them means the call sites do not change.
    ///
    /// Four things the desktop renderer uses do not exist on ES, and this class
    /// is where each of them is answered:
    ///
    /// - **Immediate mode.** <c>Begin</c>/<c>Vertex3</c>/<c>End</c> accumulate
    ///   into a vertex buffer instead of a command stream. Quads, quad strips,
    ///   triangle strips and fans are turned into indexed triangles, since ES
    ///   draws neither quads nor anything else the DS geometry engine emitted.
    /// - **Display lists.** <c>NewList</c>/<c>EndList</c> bake that buffer into
    ///   a VBO, an index buffer and a VAO; <c>CallList</c> is one
    ///   <c>glDrawElements</c>. This is what the display list was for and it is
    ///   the same trade -- build once at load, draw cheaply forever.
    /// - **The current colour.** A vertex with no colour of its own takes the
    ///   colour current at *execution* time, which for a display list is the
    ///   <c>GL.Color3</c> the engine issues per render item. Vertices carry a
    ///   flag for whether they had their own; the ones that did not read the
    ///   <c>imm_color</c> uniform. See <see cref="EsShaders"/>.
    /// - **The alpha test.** <c>glAlphaFunc</c> is fixed-function. The engine
    ///   asks for two comparisons and the fragment shader discards on them.
    ///
    /// And one thing is bookkeeping rather than emulation: the engine binds
    /// texture names it made up itself (<c>_textureCount</c>), which a
    /// compatibility profile allows and ES does not. Those names are kept as a
    /// map to real ones, created on first bind.
    ///
    /// What is lost: <c>glPolygonMode</c>, so the wireframe and collision-volume
    /// debug views draw solid. Nothing a player sees uses it.
    /// </summary>
    internal static partial class GlEs
    {
        // 0..2 position, 3..6 colour, 7..9 normal, 10..12 texcoord + matrix id, 13 "had its own colour"
        private const int FloatsPerVertex = LegacyGeometryBatch.FloatsPerVertex;
        private const int Stride = FloatsPerVertex * sizeof(float);

        private sealed class CompiledList
        {
            public int Vao;
            public int Vbo;
            public int Ibo;
            public int TriCount;
            public int LineCount;
        }

        // ---- current vertex state, the same state machine fixed-function GL keeps ----
        private static Vector4 _curColor = new Vector4(1, 1, 1, 1);
        private static Vector3 _curNormal = new Vector3(0, 0, 1);
        private static Vector3 _curTexCoord = Vector3.Zero;
        private static bool _colorSet = false;

        private static readonly LegacyGeometryBatch _batch = new LegacyGeometryBatch();
        private static bool _recording;
        private static int _recordListId;

        private static readonly Dictionary<int, CompiledList> _lists = new Dictionary<int, CompiledList>();
        private static int _nextListId = 1;

        // ---- the buffers the non-list Begin/End pairs draw through ----
        private static readonly DynamicGeometryStream _dynamicIndices = new();
        private static readonly bool _dynamicStreamEnabled =
            OpenGlOptimizationPolicy.Enabled("DYNAMIC_STREAM");
        private sealed class DynamicBuffers
        {
            internal int Vao, Vbo, Ibo, VertexBytes, IndexBytes;
        }
        // Rotation spreads reuse over three stores. Every reuse still orphans the
        // store, so an in-flight GPU read can never be overwritten or waited on.
        private static readonly DynamicBuffers[] _dynamicBuffers = { new(), new(), new() };
        private static int _dynamicSlot;
        private static int _dynamicPrimitiveFirstVertex;
        private static int _dynamicPrimitiveCount;
        private static long _dynamicBufferHighWater;
        internal static long DynamicBufferBytes
        {
            get
            {
                long bytes = 0;
                foreach (DynamicBuffers b in _dynamicBuffers) bytes += (long)b.VertexBytes + b.IndexBytes;
                return bytes;
            }
        }
        internal static long DynamicBufferHighWaterBytes => _dynamicBufferHighWater;
        private const int DynamicVertexLimit = 16384;

        // ---- state the shaders have to be told about ----
        private static bool _alphaTestEnabled;
        private static AlphaFunction _alphaFunc = AlphaFunction.Always;
        private static int _program;
        private static int _immColorLoc = -1;
        private static int _alphaTestLoc = -1;
        private static readonly Dictionary<int, (int ImmColor, int AlphaTest)> _programLocs
            = new Dictionary<int, (int, int)>();

        // ---- the engine's own texture names, mapped to real ones ----
        private static readonly Dictionary<int, int> _textures = new Dictionary<int, int>();
        private static int _textureHighWater;

        // Track VAO ownership within the GLES scene pass. Unknown external
        // consumers (Android UI or a new EGL context) explicitly invalidate it.
        // Disable via PROJECT_PRIME_ANDROID_PERF_OPT_IN=0 for legacy unbinds.
        [ThreadStatic] private static int _boundVertexArray;
        [ThreadStatic] private static bool _boundVertexArrayKnown;
        internal static void InvalidateVertexArrayBinding() => _boundVertexArrayKnown = false;
        private static void BindVertexArray(int name)
        {
            if (_boundVertexArrayKnown && _boundVertexArray == name
                && OpenGlOptimizationPolicy.AndroidPerformanceOptedIn)
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.StateSkips);
                return;
            }
            AndroidGlFastCalls.BindVertexArray(name);
            _boundVertexArray = name;
            _boundVertexArrayKnown = true;
        }
        private static readonly LegacyGlUniformCache _uniformCache = new();
        private static readonly bool _uniformElisionEnabled = OpenGlOptimizationPolicy.Enabled("GLES_UNIFORMS");
        private static bool CacheUniform(int location)
            => _uniformElisionEnabled && location != _immColorLoc && location != _alphaTestLoc;
        private static void NoteUniformSkip() => OpenGlPassProfiler.Count(OpenGlFrameCounter.StateSkips);

        /// <summary>Drop every GL object this class owns. For a lost context.</summary>
        public static void Reset()
        {
            GraphicsApi.ResetStateElisionForContext();
            _lists.Clear();
            _textures.Clear();
            _programLocs.Clear();
            _uniformCache.ResetContext();
            _nextListId = 1;
            _textureHighWater = 0;
            InvalidateVertexArrayBinding();
            for (int i = 0; i < _dynamicBuffers.Length; i++) _dynamicBuffers[i] = new();
            _dynamicSlot = _dynamicPrimitiveCount = 0;
            _dynamicBufferHighWater = 0;
            _dynamicIndices.Reset();
            _program = 0;
            _immColorLoc = -1;
            _alphaTestLoc = -1;
            _batch.Clear();
            _recording = false;
        }

        #region immediate mode

        public static void Begin(OpenTK.Graphics.OpenGL.PrimitiveType mode)
        {
            if (!_recording)
            {
                if (!_dynamicStreamEnabled) _batch.Clear();
                else if (DynamicGeometryStream.NeedsPrimitiveBarrier(mode,
                    _batch.TriIndices.Count, _batch.LineIndices.Count)) FlushPending();
                // Deferred dynamic vertices snapshot current attributes. Compiled
                // lists retain the original execution-time inheritance contract.
                _colorSet = false;
            }
            _batch.Begin(mode);
            _dynamicPrimitiveFirstVertex = _batch.VertexCount;
        }

        public static void End()
        {
            _batch.End();
            // Match the old per-End imm_color snapshot exactly, including a
            // primitive mixing inherited and explicit colors. Later primitives
            // may change current color before this batch reaches the driver.
            if (!_recording && _dynamicStreamEnabled)
            {
                _batch.BakeInheritedColor(_dynamicPrimitiveFirstVertex, _curColor);
                _dynamicPrimitiveCount++;
            }
            if (!_recording && (!_dynamicStreamEnabled || _batch.VertexCount >= DynamicVertexLimit))
                FlushDynamic();
        }

        // Called before every observable GL state/resource operation, pass boundary
        // and present. Only adjacent geometry with unchanged state can accumulate.
        internal static void FlushPending()
        {
            if (!_recording && _dynamicStreamEnabled && _batch.VertexCount != 0)
                FlushDynamic();
        }

        public static void Vertex3(float x, float y, float z)
        {
            _batch.AddVertex(new Vector3(x, y, z), _curColor, _curNormal, _curTexCoord, _colorSet);
        }

        public static void Vertex3(Vector3 vector)
        {
            Vertex3(vector.X, vector.Y, vector.Z);
        }

        public static void Color3(float r, float g, float b)
        {
            _curColor = new Vector4(r, g, b, 1f);
            _colorSet = true;
        }

        public static void Color3(Vector3 color)
        {
            Color3(color.X, color.Y, color.Z);
        }

        public static void Color4(float r, float g, float b, float a)
        {
            _curColor = new Vector4(r, g, b, a);
            _colorSet = true;
        }

        public static void Normal3(float x, float y, float z)
        {
            _curNormal = new Vector3(x, y, z);
        }

        public static void TexCoord3(float s, float t, float matrixId)
        {
            _curTexCoord = new Vector3(s, t, matrixId);
        }

        public static void TexCoord3(Vector3 texcoord)
        {
            _curTexCoord = texcoord;
        }


        #endregion

        #region display lists

        public static int GenLists(int range)
        {
            FlushPending();
            int id = _nextListId;
            _nextListId += range;
            return id;
        }

        public static void NewList(int list, ListMode mode)
        {
            FlushPending();
            _batch.Clear();
            _recording = true;
            _recordListId = list;
            // A list does not inherit the colour current when it was compiled;
            // it inherits the one current when it is called.
            _colorSet = false;
        }

        public static unsafe void EndList()
        {
            FlushPending();
            _recording = false;
            var compiled = new CompiledList
            {
                TriCount = _batch.TriIndices.Count,
                LineCount = _batch.LineIndices.Count
            };
            if (compiled.TriCount == 0 && compiled.LineCount == 0)
            {
                _lists[_recordListId] = compiled;
                _batch.Clear();
                return;
            }
            compiled.Vao = ES.GL.GenVertexArray();
            compiled.Vbo = ES.GL.GenBuffer();
            compiled.Ibo = ES.GL.GenBuffer();
            BindVertexArray(compiled.Vao);
            ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, compiled.Vbo);
            fixed (float* verts = CollectionsMarshal.AsSpan(_batch.Vertices))
            {
                ES.GL.BufferData(ES.BufferTarget.ArrayBuffer, _batch.Vertices.Count * sizeof(float),
                    (IntPtr)verts, ES.BufferUsageHint.StaticDraw);
            }
            ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, compiled.Ibo);
            int[] indices = _batch.BuildIndexArray();
            fixed (int* idx = indices)
            {
                ES.GL.BufferData(ES.BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int),
                    (IntPtr)idx, ES.BufferUsageHint.StaticDraw);
            }
            SetupAttributes();
            BindVertexArray(0);
            ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, 0);
            ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, 0);
            _lists[_recordListId] = compiled;
            _batch.Clear();
        }

        public static void CallList(int list)
        {
            FlushPending();
            if (!_lists.TryGetValue(list, out CompiledList? compiled) || compiled.Vao == 0)
            {
                return;
            }
            ApplyDrawState();
            BindVertexArray(compiled.Vao);
            if (compiled.TriCount > 0)
            {
                AndroidGlFastCalls.DrawElements(ES.PrimitiveType.Triangles, compiled.TriCount,
                    ES.DrawElementsType.UnsignedInt, IntPtr.Zero);
                OpenGlPassProfiler.Count(OpenGlFrameCounter.IndexedDraws);
            }
            if (compiled.LineCount > 0)
            {
                AndroidGlFastCalls.DrawElements(ES.PrimitiveType.Lines, compiled.LineCount,
                    ES.DrawElementsType.UnsignedInt, (IntPtr)(compiled.TriCount * sizeof(int)));
                OpenGlPassProfiler.Count(OpenGlFrameCounter.IndexedDraws);
            }
            // Keep a known VAO bound between consecutive retained draws.
            if (!OpenGlOptimizationPolicy.AndroidPerformanceOptedIn) BindVertexArray(0);
        }

        public static void DeleteLists(int list, int range)
        {
            FlushPending();
            for (int i = 0; i < range; i++)
            {
                int id = list + i;
                if (_lists.Remove(id, out CompiledList? compiled) && compiled.Vao != 0)
                {
                    if (_boundVertexArray == compiled.Vao) InvalidateVertexArrayBinding();
                    ES.GL.DeleteVertexArray(compiled.Vao);
                    ES.GL.DeleteBuffer(compiled.Vbo);
                    ES.GL.DeleteBuffer(compiled.Ibo);
                }
            }
        }

        private static unsafe void FlushDynamic()
        {
            int triCount = _batch.TriIndices.Count;
            int lineCount = _batch.LineIndices.Count;
            if (triCount == 0 && lineCount == 0)
            {
                _batch.Clear();
                _dynamicPrimitiveCount = 0;
                return;
            }
            DynamicBuffers buffers = _dynamicBuffers[_dynamicStreamEnabled ? _dynamicSlot : 0];
            if (_dynamicStreamEnabled) _dynamicSlot = (_dynamicSlot + 1) % _dynamicBuffers.Length;
            if (buffers.Vao == 0)
            {
                buffers.Vao = ES.GL.GenVertexArray();
                buffers.Vbo = ES.GL.GenBuffer();
                buffers.Ibo = ES.GL.GenBuffer();
                BindVertexArray(buffers.Vao);
                ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, buffers.Vbo);
                ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, buffers.Ibo);
                SetupAttributes();
            }
            else
            {
                BindVertexArray(buffers.Vao);
                ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, buffers.Vbo);
                ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, buffers.Ibo);
            }
            int vertexBytes = _batch.Vertices.Count * sizeof(float);
            fixed (float* verts = CollectionsMarshal.AsSpan(_batch.Vertices))
            {
                if (vertexBytes > buffers.VertexBytes)
                {
                    ES.GL.BufferData(ES.BufferTarget.ArrayBuffer, vertexBytes, (IntPtr)verts,
                        ES.BufferUsageHint.StreamDraw);
                    buffers.VertexBytes = vertexBytes;
                }
                else
                {
                    // orphan first, so the driver does not stall waiting for the
                    // frame that is still reading the old contents
                    ES.GL.BufferData(ES.BufferTarget.ArrayBuffer, buffers.VertexBytes, IntPtr.Zero,
                        ES.BufferUsageHint.StreamDraw);
                    OpenGlPassProfiler.Count(OpenGlFrameCounter.BufferOrphans, 1);
                    ES.GL.BufferSubData(ES.BufferTarget.ArrayBuffer, IntPtr.Zero, vertexBytes, (IntPtr)verts);
                }
            }
            ReadOnlySpan<int> indices = _dynamicStreamEnabled
                ? _dynamicIndices.Prepare(_batch) : _batch.BuildIndexArray();
            int indexBytes = indices.Length * sizeof(int);
            fixed (int* idx = indices)
            {
                if (indexBytes > buffers.IndexBytes)
                {
                    ES.GL.BufferData(ES.BufferTarget.ElementArrayBuffer, indexBytes, (IntPtr)idx,
                        ES.BufferUsageHint.StreamDraw);
                    buffers.IndexBytes = indexBytes;
                }
                else
                {
                    ES.GL.BufferData(ES.BufferTarget.ElementArrayBuffer, buffers.IndexBytes, IntPtr.Zero,
                        ES.BufferUsageHint.StreamDraw);
                    OpenGlPassProfiler.Count(OpenGlFrameCounter.BufferOrphans, 1);
                    ES.GL.BufferSubData(ES.BufferTarget.ElementArrayBuffer, IntPtr.Zero, indexBytes, (IntPtr)idx);
                }
            }
            _dynamicBufferHighWater = Math.Max(_dynamicBufferHighWater, DynamicBufferBytes);
            OpenGlPassProfiler.Count(OpenGlFrameCounter.DynamicBatches);
            OpenGlPassProfiler.Count(OpenGlFrameCounter.DynamicMergedPrimitives, Math.Max(0, _dynamicPrimitiveCount - 1));
            _dynamicPrimitiveCount = 0;
            OpenGlPassProfiler.Count(OpenGlFrameCounter.BufferUploads, 2);
            OpenGlPassProfiler.Count(OpenGlFrameCounter.UploadBytes, (long)vertexBytes + indexBytes);
            ApplyDrawState();
            if (triCount > 0)
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.IndexedDraws, 1);
                AndroidGlFastCalls.DrawElements(ES.PrimitiveType.Triangles, triCount,
                    ES.DrawElementsType.UnsignedInt, IntPtr.Zero);
            }
            if (lineCount > 0)
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.IndexedDraws, 1);
                AndroidGlFastCalls.DrawElements(ES.PrimitiveType.Lines, lineCount,
                    ES.DrawElementsType.UnsignedInt, (IntPtr)(triCount * sizeof(int)));
            }
            if (!OpenGlOptimizationPolicy.AndroidPerformanceOptedIn) BindVertexArray(0);
            _batch.Clear();
            // A single oversized primitive uses the ordinary upload path, but
            // must not permanently inflate either GPU or CPU streaming storage.
            if (_dynamicStreamEnabled && vertexBytes > DynamicVertexLimit * Stride)
            {
                ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, buffers.Vbo);
                ES.GL.BufferData(ES.BufferTarget.ArrayBuffer, 0, IntPtr.Zero, ES.BufferUsageHint.StreamDraw);
                ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, buffers.Ibo);
                ES.GL.BufferData(ES.BufferTarget.ElementArrayBuffer, 0, IntPtr.Zero, ES.BufferUsageHint.StreamDraw);
                ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, 0);
                buffers.VertexBytes = buffers.IndexBytes = 0;
                _batch.Vertices.Capacity = DynamicVertexLimit * FloatsPerVertex;
                _batch.TriIndices.Capacity = DynamicVertexLimit * 3;
                _batch.LineIndices.Capacity = DynamicVertexLimit * 2;
            }
        }

        private static void SetupAttributes()
        {
            for (int i = 0; i <= 4; i++)
            {
                ES.GL.EnableVertexAttribArray(i);
            }
            ES.GL.VertexAttribPointer(0, 3, ES.VertexAttribPointerType.Float, false, Stride, 0);
            ES.GL.VertexAttribPointer(1, 4, ES.VertexAttribPointerType.Float, false, Stride, 3 * sizeof(float));
            ES.GL.VertexAttribPointer(2, 3, ES.VertexAttribPointerType.Float, false, Stride, 7 * sizeof(float));
            ES.GL.VertexAttribPointer(3, 3, ES.VertexAttribPointerType.Float, false, Stride, 10 * sizeof(float));
            ES.GL.VertexAttribPointer(4, 1, ES.VertexAttribPointerType.Float, false, Stride, 13 * sizeof(float));
        }

        private static void ApplyDrawState()
        {
            if (_immColorLoc >= 0)
            {
                AndroidGlFastCalls.Uniform4(_immColorLoc, _curColor.X, _curColor.Y, _curColor.Z, _curColor.W);
            }
            if (_alphaTestLoc >= 0)
            {
                int mode = 0;
                if (_alphaTestEnabled)
                {
                    mode = _alphaFunc == AlphaFunction.Equal ? 1 : _alphaFunc == AlphaFunction.Less ? 2 : 0;
                }
                AndroidGlFastCalls.Uniform1(_alphaTestLoc, mode);
            }
        }

        #endregion

        #region textures the engine named itself

        private static int RealTexture(int name)
        {
            if (name == 0)
            {
                return 0;
            }
            if (name > _textureHighWater)
            {
                _textureHighWater = name;
            }
            if (!_textures.TryGetValue(name, out int real))
            {
                real = ES.GL.GenTexture();
                _textures[name] = real;
            }
            return real;
        }

        public static int GenTexture()
        {
            FlushPending();
            // The engine counts texture names itself and expects GenTexture to
            // hand out the next one in the same sequence, so keep one counter.
            int name = ++_textureHighWater;
            RealTexture(name);
            return name;
        }

        public static void DeleteTexture(int name)
        {
            FlushPending();
            if (_textures.Remove(name, out int real))
            {
                ES.GL.DeleteTexture(real);
            }
        }

        public static void BindTexture(TextureTarget target, int name)
        {
            FlushPending();
            ES.GL.BindTexture((ES.TextureTarget)(int)target, RealTexture(name));
        }

        #endregion

        #region shaders

        public static int CreateShader(ShaderType type)
        {
            FlushPending();
            return ES.GL.CreateShader((ES.ShaderType)(int)type);
        }

        public static void ShaderSource(int shader, string source)
        {
            FlushPending();
            EsShaders.CheckInSync();
            string translated = EsShaders.Translate(source) ?? source;
            ES.GL.ShaderSource(shader, 1, new string[] { translated }, new int[] { translated.Length });
        }

        public static void CompileShader(int shader)
        {
            FlushPending();
            ES.GL.CompileShader(shader);
            ES.GL.GetShader(shader, ES.ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                // The engine only reads the log under a debugger, and a shader
                // that will not compile is otherwise a black screen with no
                // explanation anywhere.
                Console.WriteLine($"[gles] shader {shader} failed to compile: {ES.GL.GetShaderInfoLog(shader)}");
            }
        }

        public static void GetShader(int shader, ShaderParameter pname, out int value)
        {
            FlushPending();
            ES.GL.GetShader(shader, (ES.ShaderParameter)(int)pname, out value);
        }

        public static string GetShaderInfoLog(int shader)
        {
            FlushPending();
            return ES.GL.GetShaderInfoLog(shader);
        }

        public static void DeleteShader(int shader)
        {
            FlushPending();
            ES.GL.DeleteShader(shader);
        }

        public static int CreateProgram()
        {
            FlushPending();
            return ES.GL.CreateProgram();
        }

        /// <summary>
        /// Release a linked program. Called when the renderer is torn down --
        /// which on the desktop happens when the window closes and on Android
        /// happens whenever the surface goes away, so it is the head that
        /// needs it most.
        /// </summary>
        public static void DeleteProgram(int program)
        {
            FlushPending();
            _uniformCache.InvalidateProgram(program);
            _programLocs.Remove(program);
            ES.GL.DeleteProgram(program);
        }

        public static void AttachShader(int program, int shader)
        {
            FlushPending();
            ES.GL.AttachShader(program, shader);
        }

        public static void DetachShader(int program, int shader)
        {
            FlushPending();
            ES.GL.DetachShader(program, shader);
        }

        public static void LinkProgram(int program)
        {
            FlushPending();
            _uniformCache.InvalidateProgram(program);
            _programLocs.Remove(program);
            ES.GL.LinkProgram(program);
            ES.GL.GetProgram(program, ES.GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                throw new ProgramException(
                    $"Failed to link program {program}: {ES.GL.GetProgramInfoLog(program)}");
            }
        }

        public static void UseProgram(int program)
        {
            FlushPending();
            ES.GL.UseProgram(program);
            _program = program;
            _uniformCache.UseProgram(program);
            if (!_programLocs.TryGetValue(program, out (int ImmColor, int AlphaTest) locs))
            {
                locs = (ES.GL.GetUniformLocation(program, "imm_color"),
                    ES.GL.GetUniformLocation(program, "alpha_test"));
                _programLocs[program] = locs;
            }
            _immColorLoc = locs.ImmColor;
            _alphaTestLoc = locs.AlphaTest;
        }

        internal static void RegisterMatrixArrayLayout(int program, int[] elements, int[] independent)
            => _uniformCache.RegisterMatrixArrayLayout(program, elements, independent);

        public static int GetUniformLocation(int program, string name)
        {
            FlushPending();
            return ES.GL.GetUniformLocation(program, name);
        }

        #endregion

        #region state

        public static void Enable(EnableCap cap)
        {
            FlushPending();
            if (cap == EnableCap.AlphaTest)
            {
                _alphaTestEnabled = true;
                return;
            }
            if (IgnoredCap(cap))
            {
                return;
            }
            ES.GL.Enable((ES.EnableCap)(int)cap);
        }

        public static void Disable(EnableCap cap)
        {
            FlushPending();
            if (cap == EnableCap.AlphaTest)
            {
                _alphaTestEnabled = false;
                return;
            }
            if (IgnoredCap(cap))
            {
                return;
            }
            ES.GL.Disable((ES.EnableCap)(int)cap);
        }

        private static bool IgnoredCap(EnableCap cap)
        {
            // Texture2D is fixed-function; the debug output extension is not
            // part of ES 3.0 and the capture code that asks for it is a desktop
            // path anyway.
            return cap == EnableCap.Texture2D || cap == EnableCap.DebugOutput
                || (int)cap == 0x8242 /* DebugOutputSynchronous */;
        }

        public static void AlphaFunc(AlphaFunction func, float reference)
        {
            FlushPending();
            _alphaFunc = func;
        }

        public static void PolygonMode(TriangleFace face, OpenTK.Graphics.OpenGL.PolygonMode mode)
        {
            FlushPending();
            // ES has no glPolygonMode. Only the debug views ask for Line.
        }

        public static void LineWidth(float width)
        {
            FlushPending();
            // Goes with PolygonMode above -- no wireframe to widen on ES.
        }

        public static void DebugMessageCallback(DebugProc callback, IntPtr userParam)
        {
            FlushPending();
        }

        public static void Clear(ClearBufferMask mask)
        {
            FlushPending();
            ES.GL.Clear((ES.ClearBufferMask)(int)mask);
        }

        public static void ClearColor(Color4 color)
        {
            FlushPending();
            ES.GL.ClearColor(color.R, color.G, color.B, color.A);
        }

        public static void ClearStencil(int value)
        {
            FlushPending();
            ES.GL.ClearStencil(value);
        }

        public static void ColorMask(bool red, bool green, bool blue, bool alpha)
        {
            FlushPending();
            ES.GL.ColorMask(red, green, blue, alpha);
        }

        public static void DepthMask(bool flag)
        {
            FlushPending();
            ES.GL.DepthMask(flag);
        }

        public static void DepthFunc(DepthFunction func)
        {
            FlushPending();
            ES.GL.DepthFunc((ES.DepthFunction)(int)func);
        }

        public static void CullFace(TriangleFace mode)
        {
            FlushPending();
            // The ES enum has no TriangleFace overload; the values are the same.
#pragma warning disable CS0618
            ES.GL.CullFace((ES.CullFaceMode)(int)mode);
#pragma warning restore CS0618
        }

        public static void BlendFunc(BlendingFactor src, BlendingFactor dst)
        {
            FlushPending();
            ES.GL.BlendFunc((ES.BlendingFactorSrc)(int)src, (ES.BlendingFactorDest)(int)dst);
        }

        public static void BlendEquation(BlendEquationMode mode)
        {
            FlushPending();
            ES.GL.BlendEquation((ES.BlendEquationMode)(int)mode);
        }

        public static bool IsEnabled(EnableCap cap)
        {
            FlushPending();
            return ES.GL.IsEnabled((ES.EnableCap)(int)cap);
        }

        public static void GetUniform(int program, int location, out int value)
        {
            FlushPending();
            ES.GL.GetUniform(program, location, out value);
        }

        public static void GetInteger(GetPName pname, int[] values)
        {
            FlushPending();
            ES.GL.GetInteger((ES.GetPName)(int)pname, values);
        }

        public static void StencilFunc(StencilFunction func, int reference, int mask)
        {
            FlushPending();
            ES.GL.StencilFunc((ES.StencilFunction)(int)func, reference, mask);
        }

        public static void StencilOp(OpenTK.Graphics.OpenGL.StencilOp fail,
            OpenTK.Graphics.OpenGL.StencilOp zfail, OpenTK.Graphics.OpenGL.StencilOp zpass)
        {
            FlushPending();
            ES.GL.StencilOp((ES.StencilOp)(int)fail, (ES.StencilOp)(int)zfail, (ES.StencilOp)(int)zpass);
        }

        public static void StencilMask(int mask)
        {
            FlushPending();
            ES.GL.StencilMask(mask);
        }

        public static void PolygonOffset(float factor, float units)
        {
            FlushPending();
            ES.GL.PolygonOffset(factor, units);
        }

        public static void Viewport(int x, int y, int width, int height)
        {
            FlushPending();
            ES.GL.Viewport(x, y, width, height);
        }

        /// <summary>
        /// Scissor, which ES has and the desktop overload list here did not.
        /// The results screen's hunter preview clears and draws inside one
        /// rectangle of the frame; see <c>Scene.ModDrawPreview</c>.
        /// </summary>
        public static void Scissor(int x, int y, int width, int height)
        {
            FlushPending();
            ES.GL.Scissor(x, y, width, height);
        }

        /// <summary>
        /// The four-component form. The desktop GL takes a Color4 or four
        /// floats; only the first was mirrored here, and the preview pass
        /// wants to put the clear colour back the way it found it.
        /// </summary>
        public static void ClearColor(float red, float green, float blue, float alpha)
        {
            FlushPending();
            ES.GL.ClearColor(red, green, blue, alpha);
        }

        public static void PixelStore(PixelStoreParameter pname, int param)
        {
            FlushPending();
            ES.GL.PixelStore((ES.PixelStoreParameter)(int)pname, param);
        }

        public static void ReadBuffer(ReadBufferMode mode)
        {
            FlushPending();
            ES.GL.ReadBuffer((ES.ReadBufferMode)(int)mode);
        }

        public static void ActiveTexture(TextureUnit texture)
        {
            FlushPending();
            ES.GL.ActiveTexture((ES.TextureUnit)(int)texture);
        }

        public static ErrorCode GetError()
        {
            FlushPending();
            return (ErrorCode)(int)ES.GL.GetError();
        }

        public static string GetString(StringName name)
        {
            FlushPending();
            return ES.GL.GetString((ES.StringName)(int)name);
        }

        public static int GetInteger(GetPName pname)
        {
            FlushPending();
            // The two the capture code asks for are context flags and the
            // profile mask, neither of which ES has; answering zero is what a
            // core context without them would report anyway.
            if ((int)pname == 0x821E || (int)pname == 0x9126)
            {
                return 0;
            }
            return ES.GL.GetInteger((ES.GetPName)(int)pname);
        }

        #endregion

        #region textures and framebuffers

        public static void TexParameter(TextureTarget target, TextureParameterName pname, int param)
        {
            FlushPending();
            ES.GL.TexParameter((ES.TextureTarget)(int)target, (ES.TextureParameterName)(int)pname, param);
        }

        public static void GenerateMipmap(GenerateMipmapTarget target)
        {
            FlushPending();
            ES.GL.GenerateMipmap((ES.TextureTarget)(int)target);
        }

        public static void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels)
        {
            FlushPending();
            ES.GL.TexImage2D((ES.TextureTarget2d)(int)target, level,
                (ES.TextureComponentCount)(int)internalFormat, width, height, border,
                (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        }

        public static void TexImage2D<T>(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            FlushPending();
            ES.GL.TexImage2D((ES.TextureTarget2d)(int)target, level,
                (ES.TextureComponentCount)(int)internalFormat, width, height, border,
                (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        }

        public static void TexSubImage2D<T>(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            FlushPending();
            ES.GL.TexSubImage2D((ES.TextureTarget2d)(int)target, level, xoffset, yoffset, width, height,
                (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        }

        /// <summary>
        /// The framebuffer that is bound for reading, into the texture that is
        /// bound. Cel shading's ink pass needs a copy of the scene to sample,
        /// since a pass cannot read the target it is drawing into, and this is
        /// that copy -- on the GPU, with no round trip through the CPU.
        /// </summary>
        public static void CopyTexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int x, int y, int width, int height)
        {
            FlushPending();
            ES.GL.CopyTexSubImage2D((ES.TextureTarget2d)(int)target, level, xoffset, yoffset,
                x, y, width, height);
        }

        public static void ReadPixels<T>(int x, int y, int width, int height, PixelFormat format,
            PixelType type, T[] pixels) where T : struct
        {
            FlushPending();
            ES.GL.ReadPixels(x, y, width, height, (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        }

        public static int GenFramebuffer()
        {
            FlushPending();
            return ES.GL.GenFramebuffer();
        }

        public static void BindFramebuffer(FramebufferTarget target, int framebuffer)
        {
            FlushPending();
            ES.GL.BindFramebuffer((ES.FramebufferTarget)(int)target, framebuffer);
        }

        public static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment,
            TextureTarget textarget, int texture, int level)
        {
            FlushPending();
            ES.GL.FramebufferTexture2D((ES.FramebufferTarget)(int)target,
                (ES.FramebufferAttachment)(int)attachment, (ES.TextureTarget2d)(int)textarget,
                RealTexture(texture), level);
        }

        public static int GenRenderbuffer()
        {
            FlushPending();
            return ES.GL.GenRenderbuffer();
        }

        public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer)
        {
            FlushPending();
            ES.GL.BindRenderbuffer((ES.RenderbufferTarget)(int)target, renderbuffer);
        }

        public static void RenderbufferStorage(RenderbufferTarget target,
            OpenTK.Graphics.OpenGL.RenderbufferStorage internalFormat, int width, int height)
        {
            FlushPending();
            ES.GL.RenderbufferStorage((ES.RenderbufferTarget)(int)target,
                (ES.RenderbufferInternalFormat)(int)internalFormat, width, height);
        }

        public static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderbuffertarget, int renderbuffer)
        {
            FlushPending();
            ES.GL.FramebufferRenderbuffer((ES.FramebufferTarget)(int)target,
                (ES.FramebufferAttachment)(int)attachment, (ES.RenderbufferTarget)(int)renderbuffertarget,
                renderbuffer);
        }

        /// <summary>
        /// Asked once per depth attachment, so the ink pass knows how many
        /// bits it is actually differencing. A driver may answer a request for
        /// a 24-bit depth texture with fewer, and the pass is looking for a
        /// difference far below one step of a coarse one.
        /// </summary>
        public static void GetFramebufferAttachmentParameter(FramebufferTarget target,
            FramebufferAttachment attachment, FramebufferParameterName pname, out int result)
        {
            FlushPending();
            ES.GL.GetFramebufferAttachmentParameter((ES.FramebufferTarget)(int)target,
                (ES.FramebufferAttachment)(int)attachment,
                (ES.FramebufferParameterName)(int)pname, out result);
        }

        /// <summary>
        /// Release the offscreen target and its depth attachment. The names
        /// are the driver's own here -- unlike textures, which GlEs hands out
        /// itself -- so both pass straight through.
        /// </summary>
        public static void BlitFramebuffer(int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            ClearBufferMask mask, BlitFramebufferFilter filter)
        {
            FlushPending();
            ES.GL.BlitFramebuffer(sourceX0, sourceY0, sourceX1, sourceY1,
                destinationX0, destinationY0, destinationX1, destinationY1,
                (ES.ClearBufferMask)(int)mask, (ES.BlitFramebufferFilter)(int)filter);
        }

        public static void DeleteFramebuffer(int framebuffer)
        {
            FlushPending();
            ES.GL.DeleteFramebuffer(framebuffer);
        }

        public static void DeleteRenderbuffer(int renderbuffer)
        {
            FlushPending();
            ES.GL.DeleteRenderbuffer(renderbuffer);
        }

        public static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target)
        {
            FlushPending();
            return (FramebufferErrorCode)(int)ES.GL.CheckFramebufferStatus((ES.FramebufferTarget)(int)target);
        }

        #endregion

        #region uniforms

        public static void Uniform1(int location, int value)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit(location, value)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform1(location, value);
        }

        public static void Uniform1(int location, float value)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit(location, value)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform1(location, value);
        }

        public static void Uniform1(int location, int count, float[] value)
        {
            FlushPending();
            _uniformCache.InvalidateFloatArray();
            ES.GL.Uniform1(location, count, value);
        }

        public static void Uniform2(int location, float x, float y)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit2(location, x, y)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform2(location, x, y);
        }

        public static void Uniform3(int location, Vector3 vector)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit3(location, vector)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform3(location, vector);
        }

        public static void Uniform3(int location, int count, float[] value)
        {
            FlushPending();
            _uniformCache.InvalidateVector3Array();
            ES.GL.Uniform3(location, count, value);
        }

        public static void Uniform4(int location, Vector4 vector)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit4(location, vector)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform4(location, vector);
        }

        public static void Uniform4(int location, ref Vector4 vector)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit4(location, vector)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform4(location, ref vector);
        }

        public static void Uniform4(int location, float v0, float v1, float v2, float v3)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit4(location, new Vector4(v0, v1, v2, v3))) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform4(location, v0, v1, v2, v3);
        }

        public static void Uniform4(int location, int v0, int v1, int v2, int v3)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.Submit4(location, v0, v1, v2, v3)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.Uniform4(location, v0, v1, v2, v3);
        }

        public static void UniformMatrix4(int location, bool transpose, ref Matrix4 matrix)
        {
            // Identical uniforms do not introduce a deferred-geometry barrier.
            if (CacheUniform(location) && !_uniformCache.SubmitMatrix4(location, transpose, in matrix)) { NoteUniformSkip(); return; }
            FlushPending();
            ES.GL.UniformMatrix4(location, transpose, ref matrix);
        }

        public static void UniformMatrix4(int location, int count, bool transpose, float[] value)
        {
            FlushPending();
            _uniformCache.InvalidateMatrix4Array(location, count);
            ES.GL.UniformMatrix4(location, count, transpose, value);
        }

        #endregion
    }
}
#endif
