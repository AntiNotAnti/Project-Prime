#if ANDROID
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ES = OpenTK.Graphics.ES30;
using MphRead;

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
    /// Shared rendering code compiles geometry into portable indexed meshes
    /// before it reaches this class; GLES translates their packed attributes
    /// to ES buffers.
    ///
    /// - **Portable meshes.** <c>DrawStaticMesh</c> and
    ///   <c>DrawDynamicMesh</c> pack the backend-neutral <c>CpuMesh</c> into
    ///   the same 14-float VBO ABI and issue indexed draws directly. Static
    ///   objects are cached by geometry identity and dynamic objects reuse a
    ///   bounded streaming bucket. A dynamic scratch mesh reuses its bounded
    ///   vertex/index storage for the small HUD and post-process primitives.
    /// - **The current colour.** A vertex with no colour of its own takes the
    ///   colour current at draw time. Vertices carry a flag for whether they
    ///   had their own; the ones that did not read the <c>imm_color</c> uniform.
    ///   See <see cref="EsShaders"/>.
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
    internal static class GlEs
    {
        // 0..2 position, 3..6 colour, 7..9 normal, 10..12 texcoord + matrix id,
        // 13 "had its own colour", 14..17 tangent + handedness
        private const int FloatsPerVertex = RenderMeshPacking.FloatsPerVertex;
        private const int Stride = FloatsPerVertex * sizeof(float);
        private const int MaximumDynamicVertices = RenderMeshPacking.DefaultMaximumVertices;
        private const int MaximumDynamicIndices = RenderMeshPacking.DefaultMaximumIndices;

        private sealed class StaticGpuMesh
        {
            public StaticGpuMesh(object geometryIdentity, CpuMesh mesh)
            {
                GeometryIdentity = geometryIdentity;
                Mesh = mesh;
                Revision = mesh.Revision;
            }

            public object GeometryIdentity { get; }
            public CpuMesh Mesh { get; }
            public long Revision { get; }
            public int Vao;
            public int Vbo;
            public int Ibo;
            public int TriCount;
            public int LineCount;
        }

        // ---- current vertex state, the same state machine fixed-function GL keeps ----
        private static Vector4 _curColor = new Vector4(1, 1, 1, 1);
        private static readonly Dictionary<object, StaticGpuMesh> _staticMeshes
            = new Dictionary<object, StaticGpuMesh>(ReferenceEqualityComparer.Instance);

        // ---- buffers used by both direct dynamic mesh overloads ----
        private static int _dynVao;
        private static int _dynVbo;
        private static int _dynIbo;
        private static int _dynVboSize;
        private static int _dynIboSize;
        private static float[] _directDynamicVertexData = new float[4096 * FloatsPerVertex];
        private static int[] _directDynamicIndexData = new int[4096];
        private static readonly RenderMeshScratch _dynamicScratch = new();

        // ---- state the shaders have to be told about ----
        private static bool _alphaTestEnabled;
        private static AlphaFunction _alphaFunc = AlphaFunction.Always;
        private static int _immColorLoc = -1;
        private static int _alphaTestLoc = -1;
        private static readonly Dictionary<int, (int ImmColor, int AlphaTest)> _programLocs
            = new Dictionary<int, (int, int)>();

        // ---- the engine's own texture names, mapped to real ones ----
        private static readonly Dictionary<int, int> _textures = new Dictionary<int, int>();
        private static int _textureHighWater;
        // Generation zero is reserved as "uninitialized" by the policy
        // contract; the first current context is generation one.
        private static ulong _contextGeneration = 1;

        public static ulong ContextGeneration => _contextGeneration;

        /// <summary>Drop every GL object this class owns. For a lost context.</summary>
        public static void Reset()
        {
            // A replacement EGL context invalidates every native name. Do not
            // issue deletes here: the old context may already be gone, and
            // the owning lifecycle deliberately retires that context as a
            // whole. The next draw uploads fresh objects.
            _staticMeshes.Clear();
            _textures.Clear();
            _programLocs.Clear();
            _textureHighWater = 0;
            _dynVao = _dynVbo = _dynIbo = 0;
            _dynVboSize = _dynIboSize = 0;
            _immColorLoc = -1;
            _alphaTestLoc = -1;
            _contextGeneration++;
            if (_contextGeneration == 0) _contextGeneration = 1;
        }

        public static GlesEnhancedCapabilities QueryEnhancedCapabilities()
        {
            int major = ES.GL.GetInteger(ES.GetPName.MajorVersion);
            int minor = ES.GL.GetInteger(ES.GetPName.MinorVersion);
            int textureUnits = ES.GL.GetInteger(ES.GetPName.MaxTextureImageUnits);
            int uniformVectors = ES.GL.GetInteger(ES.GetPName.MaxFragmentUniformVectors);
            bool colorBufferFloat = HasExtension("GL_EXT_color_buffer_half_float")
                || HasExtension("GL_EXT_color_buffer_float");
            bool probe = colorBufferFloat && ProbeRgba16fColorTarget();
            return new GlesEnhancedCapabilities(major, minor, textureUnits,
                uniformVectors, colorBufferFloat, probe);
        }

        private static bool HasExtension(string requested)
        {
            int count = ES.GL.GetInteger(ES.GetPName.NumExtensions);
            for (int i = 0; i < count; i++)
            {
                if (String.Equals(ES.GL.GetString(ES.StringNameIndexed.Extensions, i),
                    requested, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ProbeRgba16fColorTarget()
        {
            int priorFramebuffer = ES.GL.GetInteger(ES.GetPName.FramebufferBinding);
            int priorTexture = ES.GL.GetInteger(ES.GetPName.TextureBinding2D);
            int texture = 0;
            int framebuffer = 0;
            try
            {
                while (ES.GL.GetError() != ES.ErrorCode.NoError) { }
                texture = ES.GL.GenTexture();
                ES.GL.BindTexture(ES.TextureTarget.Texture2D, texture);
                ES.GL.TexImage2D(ES.TextureTarget2d.Texture2D, 0,
                    ES.TextureComponentCount.Rgba16f, 1, 1, 0,
                    ES.PixelFormat.Rgba, ES.PixelType.HalfFloat, IntPtr.Zero);
                ES.GL.TexParameter(ES.TextureTarget.Texture2D,
                    ES.TextureParameterName.TextureMinFilter, (int)ES.TextureMinFilter.Nearest);
                ES.GL.TexParameter(ES.TextureTarget.Texture2D,
                    ES.TextureParameterName.TextureMagFilter, (int)ES.TextureMagFilter.Nearest);
                framebuffer = ES.GL.GenFramebuffer();
                ES.GL.BindFramebuffer(ES.FramebufferTarget.Framebuffer, framebuffer);
                ES.GL.FramebufferTexture2D(ES.FramebufferTarget.Framebuffer,
                    ES.FramebufferAttachment.ColorAttachment0, ES.TextureTarget2d.Texture2D,
                    texture, 0);
                return ES.GL.CheckFramebufferStatus(ES.FramebufferTarget.Framebuffer)
                    == ES.FramebufferErrorCode.FramebufferComplete
                    && ES.GL.GetError() == ES.ErrorCode.NoError;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                ES.GL.BindFramebuffer(ES.FramebufferTarget.Framebuffer, priorFramebuffer);
                ES.GL.BindTexture(ES.TextureTarget.Texture2D, priorTexture);
                if (framebuffer != 0) ES.GL.DeleteFramebuffer(framebuffer);
                if (texture != 0) ES.GL.DeleteTexture(texture);
            }
        }

        public static void Color3(float r, float g, float b)
        {
            _curColor = new Vector4(r, g, b, 1f);
        }

        public static void Color3(Vector3 color)
        {
            Color3(color.X, color.Y, color.Z);
        }

        #region direct mesh upload/draw

        /// <summary>
        /// Draw one model/HUD mesh directly from the portable CPU compiler.
        /// The geometry key is stable for a parsed mesh; the reference and
        /// revision checks make a replacement compile under that key upload a
        /// fresh object instead of drawing stale data.
        /// </summary>
        public static void DrawStaticMesh(object geometryIdentity, CpuMesh mesh)
            => DrawStaticMesh(geometryIdentity, mesh,
                RenderMeshStreams.Triangles | RenderMeshStreams.Lines);

        public static unsafe void DrawStaticMesh(object geometryIdentity, CpuMesh mesh,
            RenderMeshStreams streams)
        {
            if (geometryIdentity == null) throw new ArgumentNullException(nameof(geometryIdentity));
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            if (!_staticMeshes.TryGetValue(geometryIdentity, out StaticGpuMesh? cached)
                || RenderMeshPacking.NeedsStaticUpload(cached.GeometryIdentity, cached.Mesh,
                    cached.Revision, geometryIdentity, mesh))
            {
                if (cached != null)
                {
                    DeleteStaticMesh(cached);
                }
                cached = UploadStaticMesh(geometryIdentity, mesh);
                _staticMeshes[geometryIdentity] = cached;
            }
            DrawGpuMesh(cached.Vao, cached.TriCount, cached.LineCount, streams);
        }

        /// <summary>Release one model's static objects while its EGL context is current.</summary>
        public static void ReleaseStaticMesh(object geometryIdentity)
        {
            if (geometryIdentity == null) throw new ArgumentNullException(nameof(geometryIdentity));
            if (_staticMeshes.Remove(geometryIdentity, out StaticGpuMesh? mesh))
            {
                DeleteStaticMesh(mesh);
            }
        }

        private static unsafe StaticGpuMesh UploadStaticMesh(object geometryIdentity, CpuMesh mesh)
        {
            var compiled = new StaticGpuMesh(geometryIdentity, mesh)
            {
                TriCount = mesh.TriangleIndexCount,
                LineCount = mesh.LineIndexCount
            };
            if (compiled.TriCount == 0 && compiled.LineCount == 0)
            {
                return compiled;
            }

            float[] vertexData = new float[RenderMeshPacking.RequiredVertexFloats(mesh)];
            int[] indexData = new int[RenderMeshPacking.RequiredIndexCount(mesh)];
            RenderMeshPacking.PackVertices(mesh, vertexData);
            RenderMeshPacking.PackIndices(mesh, indexData);
            try
            {
                compiled.Vao = ES.GL.GenVertexArray();
                compiled.Vbo = ES.GL.GenBuffer();
                compiled.Ibo = ES.GL.GenBuffer();
                ES.GL.BindVertexArray(compiled.Vao);
                ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, compiled.Vbo);
                fixed (float* vertices = vertexData)
                {
                    ES.GL.BufferData(ES.BufferTarget.ArrayBuffer, vertexData.Length * sizeof(float),
                        (IntPtr)vertices, ES.BufferUsageHint.StaticDraw);
                }
                ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, compiled.Ibo);
                fixed (int* indices = indexData)
                {
                    ES.GL.BufferData(ES.BufferTarget.ElementArrayBuffer, indexData.Length * sizeof(int),
                        (IntPtr)indices, ES.BufferUsageHint.StaticDraw);
                }
                SetupAttributes();
                UnbindMeshObjects();
                return compiled;
            }
            catch
            {
                UnbindMeshObjects();
                DeleteStaticMesh(compiled);
                throw;
            }
        }

        private static void DeleteStaticMesh(StaticGpuMesh mesh)
        {
            if (mesh.Vao != 0) ES.GL.DeleteVertexArray(mesh.Vao);
            if (mesh.Vbo != 0) ES.GL.DeleteBuffer(mesh.Vbo);
            if (mesh.Ibo != 0) ES.GL.DeleteBuffer(mesh.Ibo);
            mesh.Vao = mesh.Vbo = mesh.Ibo = 0;
        }

        /// <summary>
        /// Draw dynamic compiler output. The packed arrays and GPU buffers grow
        /// only up to the explicit bounded limits and are reused thereafter.
        /// </summary>
        public static void DrawDynamicMesh(CpuMesh mesh)
            => DrawDynamicMesh(mesh, RenderMeshStreams.Triangles | RenderMeshStreams.Lines);

        public static unsafe void DrawDynamicMesh(CpuMesh mesh, RenderMeshStreams streams)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            RenderMeshPacking.ValidateDynamicCapacity(mesh, MaximumDynamicVertices,
                MaximumDynamicIndices);
            int vertexFloatCount = RenderMeshPacking.RequiredVertexFloats(mesh);
            int indexCount = RenderMeshPacking.RequiredIndexCount(mesh);
            if (indexCount == 0)
            {
                return;
            }
            EnsureDirectDynamicStorage(vertexFloatCount, indexCount);
            RenderMeshPacking.PackVertices(mesh,
                _directDynamicVertexData.AsSpan(0, vertexFloatCount));
            RenderMeshPacking.PackIndices(mesh,
                _directDynamicIndexData.AsSpan(0, indexCount));
            DrawPackedDynamicMesh(vertexFloatCount, mesh.TriangleIndexCount,
                mesh.LineIndexCount, streams);
        }

        /// <summary>
        /// Draw one reusable scratch mesh. This is the allocation-free path
        /// for short-lived HUD, composite, and diagnostic geometry; compiled
        /// world/model meshes continue to use the CpuMesh overload above.
        /// </summary>
        public static unsafe void DrawDynamicMesh(RenderMeshScratch mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            RenderMeshPacking.ValidateDynamicCapacity(mesh, MaximumDynamicVertices,
                MaximumDynamicIndices);
            int vertexFloatCount = RenderMeshPacking.RequiredVertexFloats(mesh);
            int indexCount = RenderMeshPacking.RequiredIndexCount(mesh);
            if (indexCount == 0)
            {
                return;
            }
            EnsureDirectDynamicStorage(vertexFloatCount, indexCount);
            RenderMeshPacking.PackVertices(mesh,
                _directDynamicVertexData.AsSpan(0, vertexFloatCount));
            RenderMeshPacking.PackIndices(mesh,
                _directDynamicIndexData.AsSpan(0, indexCount));
            DrawPackedDynamicMesh(vertexFloatCount, mesh.TriangleIndexCount, mesh.LineIndexCount,
                RenderMeshStreams.Triangles | RenderMeshStreams.Lines);
        }

        /// <summary>Draw the reusable scratch mesh after it was prepared.</summary>
        public static void DrawPreparedDynamicMesh()
            => DrawDynamicMesh(_dynamicScratch);

        public static void PrepareDynamicMesh(MeshPrimitiveTopology topology, int vertexCount)
            => _dynamicScratch.Prepare(topology, vertexCount);

        public static void SetDynamicMeshVertex(int index, Vector3 position,
            Vector2 texCoord = default, Vector4? color = null, uint matrixIndex = 0,
            bool explicitColor = false, Vector3? normal = null)
            => _dynamicScratch.SetVertex(index, position, texCoord, color, matrixIndex,
                explicitColor, normal);

        public static void DrawFullscreenQuad()
        {
            _dynamicScratch.SetFullscreenQuad();
            DrawPreparedDynamicMesh();
        }

        public static void DrawTexturedQuad(Vector3 topRight, Vector3 topLeft,
            Vector3 bottomRight, Vector3 bottomLeft)
        {
            _dynamicScratch.SetTexturedQuad(topRight, topLeft, bottomRight, bottomLeft);
            DrawPreparedDynamicMesh();
        }

        public static void DrawSolidQuad(Vector3 topRight, Vector3 topLeft,
            Vector3 bottomRight, Vector3 bottomLeft, Vector4 color,
            bool explicitColor = false)
        {
            _dynamicScratch.SetSolidQuad(topRight, topLeft, bottomRight, bottomLeft,
                color, explicitColor);
            DrawPreparedDynamicMesh();
        }

        public static void DrawTriangleFan(IReadOnlyList<Vector3> positions)
        {
            _dynamicScratch.SetTriangleFan(positions);
            DrawPreparedDynamicMesh();
        }

        public static void DrawTriangleFan(Vector3[] positions, int count)
        {
            _dynamicScratch.SetTriangleFan(positions, count);
            DrawPreparedDynamicMesh();
        }

        public static void DrawLineLoop(IReadOnlyList<Vector3> positions)
        {
            _dynamicScratch.SetLineLoop(positions);
            DrawPreparedDynamicMesh();
        }

        public static void DrawLineLoop(Vector3[] positions, int count)
        {
            _dynamicScratch.SetLineLoop(positions, count);
            DrawPreparedDynamicMesh();
        }

        public static void DrawCrosshairRing(float radius, float thickness,
            float halfWidth, float halfHeight, Vector2 center, int segments = 40)
        {
            if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments));
            float inner = radius - thickness / 2;
            float outer = radius + thickness / 2;
            _dynamicScratch.Prepare(MeshPrimitiveTopology.TriangleStrip, checked((segments + 1) * 2));
            for (int i = 0; i <= segments; i++)
            {
                float angle = MathHelper.TwoPi * i / segments;
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);
                _dynamicScratch.SetVertex(i * 2,
                    new Vector3(center.X + outer * cos / halfWidth, center.Y + outer * sin / halfHeight, 0f));
                _dynamicScratch.SetVertex(i * 2 + 1,
                    new Vector3(center.X + inner * cos / halfWidth, center.Y + inner * sin / halfHeight, 0f));
            }
            DrawPreparedDynamicMesh();
        }

        private static unsafe void DrawPackedDynamicMesh(int vertexFloatCount,
            int triangleIndexCount, int lineIndexCount, RenderMeshStreams streams)
        {
            EnsureDynamicGpuObjects();

            int vertexBytes = checked(vertexFloatCount * sizeof(float));
            fixed (float* vertices = _directDynamicVertexData)
            {
                if (vertexBytes > _dynVboSize)
                {
                    ES.GL.BufferData(ES.BufferTarget.ArrayBuffer, vertexBytes, (IntPtr)vertices,
                        ES.BufferUsageHint.StreamDraw);
                    _dynVboSize = vertexBytes;
                }
                else
                {
                    ES.GL.BufferData(ES.BufferTarget.ArrayBuffer, _dynVboSize, IntPtr.Zero,
                        ES.BufferUsageHint.StreamDraw);
                    ES.GL.BufferSubData(ES.BufferTarget.ArrayBuffer, IntPtr.Zero, vertexBytes,
                        (IntPtr)vertices);
                }
            }
            int indexCount = checked(triangleIndexCount + lineIndexCount);
            int indexBytes = checked(indexCount * sizeof(int));
            fixed (int* indices = _directDynamicIndexData)
            {
                if (indexBytes > _dynIboSize)
                {
                    ES.GL.BufferData(ES.BufferTarget.ElementArrayBuffer, indexBytes, (IntPtr)indices,
                        ES.BufferUsageHint.StreamDraw);
                    _dynIboSize = indexBytes;
                }
                else
                {
                    ES.GL.BufferData(ES.BufferTarget.ElementArrayBuffer, _dynIboSize, IntPtr.Zero,
                        ES.BufferUsageHint.StreamDraw);
                    ES.GL.BufferSubData(ES.BufferTarget.ElementArrayBuffer, IntPtr.Zero, indexBytes,
                        (IntPtr)indices);
                }
            }
            DrawBoundMesh(triangleIndexCount, lineIndexCount, streams);
            UnbindMeshObjects();
        }

        private static void EnsureDirectDynamicStorage(int vertexFloatCount, int indexCount)
        {
            if (vertexFloatCount > _directDynamicVertexData.Length)
            {
                int maximum = checked(MaximumDynamicVertices * FloatsPerVertex);
                int next = NextBoundedCapacity(_directDynamicVertexData.Length, vertexFloatCount, maximum);
                Array.Resize(ref _directDynamicVertexData, next);
            }
            if (indexCount > _directDynamicIndexData.Length)
            {
                int next = NextBoundedCapacity(_directDynamicIndexData.Length, indexCount,
                    MaximumDynamicIndices);
                Array.Resize(ref _directDynamicIndexData, next);
            }
        }

        private static int NextBoundedCapacity(int current, int required, int maximum)
        {
            if (required < 0 || required > maximum)
            {
                throw new InvalidOperationException("GLES dynamic mesh exceeds its bounded upload capacity.");
            }
            int doubled = current > maximum / 2 ? maximum : current * 2;
            int next = Math.Max(required, doubled);
            if (next > maximum) next = maximum;
            if (next < required)
            {
                throw new InvalidOperationException("GLES dynamic mesh exceeds its bounded upload capacity.");
            }
            return next;
        }

        private static void EnsureDynamicGpuObjects()
        {
            if (_dynVao == 0)
            {
                _dynVao = ES.GL.GenVertexArray();
                _dynVbo = ES.GL.GenBuffer();
                _dynIbo = ES.GL.GenBuffer();
                ES.GL.BindVertexArray(_dynVao);
                ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, _dynVbo);
                ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, _dynIbo);
                SetupAttributes();
            }
            else
            {
                ES.GL.BindVertexArray(_dynVao);
                ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, _dynVbo);
                ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, _dynIbo);
            }
        }

        private static void DrawGpuMesh(int vao, int triCount, int lineCount,
            RenderMeshStreams streams)
        {
            if (vao == 0) return;
            ES.GL.BindVertexArray(vao);
            DrawBoundMesh(triCount, lineCount, streams);
            ES.GL.BindVertexArray(0);
        }

        private static void DrawBoundMesh(int triCount, int lineCount,
            RenderMeshStreams streams)
        {
            ApplyDrawState();
            if (triCount > 0 && streams.HasFlag(RenderMeshStreams.Triangles))
            {
                ES.GL.DrawElements(ES.PrimitiveType.Triangles, triCount,
                    ES.DrawElementsType.UnsignedInt, IntPtr.Zero);
            }
            if (lineCount > 0 && streams.HasFlag(RenderMeshStreams.Lines))
            {
                ES.GL.DrawElements(ES.PrimitiveType.Lines, lineCount,
                    ES.DrawElementsType.UnsignedInt, (IntPtr)(triCount * sizeof(int)));
            }
        }

        private static void UnbindMeshObjects()
        {
            ES.GL.BindVertexArray(0);
            ES.GL.BindBuffer(ES.BufferTarget.ArrayBuffer, 0);
            ES.GL.BindBuffer(ES.BufferTarget.ElementArrayBuffer, 0);
        }

        #endregion

        private static void SetupAttributes()
        {
            for (int i = 0; i <= 5; i++)
            {
                ES.GL.EnableVertexAttribArray(i);
            }
            ES.GL.VertexAttribPointer(0, 3, ES.VertexAttribPointerType.Float, false, Stride, 0);
            ES.GL.VertexAttribPointer(1, 4, ES.VertexAttribPointerType.Float, false, Stride, 3 * sizeof(float));
            ES.GL.VertexAttribPointer(2, 3, ES.VertexAttribPointerType.Float, false, Stride, 7 * sizeof(float));
            ES.GL.VertexAttribPointer(3, 3, ES.VertexAttribPointerType.Float, false, Stride, 10 * sizeof(float));
            ES.GL.VertexAttribPointer(4, 1, ES.VertexAttribPointerType.Float, false, Stride, 13 * sizeof(float));
            ES.GL.VertexAttribPointer(5, 4, ES.VertexAttribPointerType.Float, false, Stride, 14 * sizeof(float));
        }

        private static void ApplyDrawState()
        {
            if (_immColorLoc >= 0)
            {
                ES.GL.Uniform4(_immColorLoc, _curColor.X, _curColor.Y, _curColor.Z, _curColor.W);
            }
            if (_alphaTestLoc >= 0)
            {
                int mode = 0;
                if (_alphaTestEnabled)
                {
                    mode = _alphaFunc == AlphaFunction.Equal ? 1 : _alphaFunc == AlphaFunction.Less ? 2 : 0;
                }
                ES.GL.Uniform1(_alphaTestLoc, mode);
            }
        }

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
            // The engine counts texture names itself and expects GenTexture to
            // hand out the next one in the same sequence, so keep one counter.
            int name = ++_textureHighWater;
            RealTexture(name);
            return name;
        }

        public static void DeleteTexture(int name)
        {
            if (_textures.Remove(name, out int real))
            {
                ES.GL.DeleteTexture(real);
            }
        }

        public static void BindTexture(TextureTarget target, int name)
        {
            ES.GL.BindTexture((ES.TextureTarget)(int)target, RealTexture(name));
        }

        #endregion

        #region shaders

        public static int CreateShader(ShaderType type)
        {
            return ES.GL.CreateShader((ES.ShaderType)(int)type);
        }

        public static void ShaderSource(int shader, string source)
        {
            ES.GL.ShaderSource(shader, 1, new string[] { source }, new int[] { source.Length });
        }

        public static void CompileShader(int shader)
        {
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
            ES.GL.GetShader(shader, (ES.ShaderParameter)(int)pname, out value);
        }

        public static string GetShaderInfoLog(int shader)
        {
            return ES.GL.GetShaderInfoLog(shader);
        }

        public static void DeleteShader(int shader)
        {
            ES.GL.DeleteShader(shader);
        }

        public static int CreateProgram()
        {
            return ES.GL.CreateProgram();
        }

        public static void DeleteProgram(int program)
        {
            if (program != 0) ES.GL.DeleteProgram(program);
        }

        public static void AttachShader(int program, int shader)
        {
            ES.GL.AttachShader(program, shader);
        }

        public static void DetachShader(int program, int shader)
        {
            ES.GL.DetachShader(program, shader);
        }

        public static void LinkProgram(int program)
        {
            ES.GL.LinkProgram(program);
            ES.GL.GetProgram(program, ES.GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                throw new ProgramException(
                    $"Failed to link program {program}: {ES.GL.GetProgramInfoLog(program)}");
            }
        }

        public static void GetProgram(int program, GetProgramParameterName pname,
            out int value)
        {
            ES.GL.GetProgram(program, (ES.GetProgramParameterName)(int)pname, out value);
        }

        public static string GetProgramInfoLog(int program)
        {
            return ES.GL.GetProgramInfoLog(program);
        }

        public static void UseProgram(int program)
        {
            ES.GL.UseProgram(program);
            if (!_programLocs.TryGetValue(program, out (int ImmColor, int AlphaTest) locs))
            {
                locs = (ES.GL.GetUniformLocation(program, "imm_color"),
                    ES.GL.GetUniformLocation(program, "alpha_test"));
                _programLocs[program] = locs;
            }
            _immColorLoc = locs.ImmColor;
            _alphaTestLoc = locs.AlphaTest;
        }

        public static int GetUniformLocation(int program, string name)
        {
            return ES.GL.GetUniformLocation(program, name);
        }

        #endregion

        #region state

        public static void Enable(EnableCap cap)
        {
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
            _alphaFunc = func;
        }

        public static void PolygonMode(TriangleFace face, OpenTK.Graphics.OpenGL.PolygonMode mode)
        {
            // ES has no glPolygonMode. Only the debug views ask for Line.
        }

        public static void DebugMessageCallback(DebugProc callback, IntPtr userParam)
        {
        }

        public static void Clear(ClearBufferMask mask)
        {
            ES.GL.Clear((ES.ClearBufferMask)(int)mask);
        }

        public static void ClearColor(Color4 color)
        {
            ES.GL.ClearColor(color.R, color.G, color.B, color.A);
        }

        public static void ClearStencil(int value)
        {
            ES.GL.ClearStencil(value);
        }

        public static void ColorMask(bool red, bool green, bool blue, bool alpha)
        {
            ES.GL.ColorMask(red, green, blue, alpha);
        }

        public static void DepthMask(bool flag)
        {
            ES.GL.DepthMask(flag);
        }

        public static void DepthFunc(DepthFunction func)
        {
            ES.GL.DepthFunc((ES.DepthFunction)(int)func);
        }

        public static void CullFace(TriangleFace mode)
        {
            // The ES enum has no TriangleFace overload; the values are the same.
#pragma warning disable CS0618
            ES.GL.CullFace((ES.CullFaceMode)(int)mode);
#pragma warning restore CS0618
        }

        public static void BlendFunc(BlendingFactor src, BlendingFactor dst)
        {
            ES.GL.BlendFunc((ES.BlendingFactorSrc)(int)src, (ES.BlendingFactorDest)(int)dst);
        }

        public static void StencilFunc(StencilFunction func, int reference, int mask)
        {
            ES.GL.StencilFunc((ES.StencilFunction)(int)func, reference, mask);
        }

        public static void StencilOp(OpenTK.Graphics.OpenGL.StencilOp fail,
            OpenTK.Graphics.OpenGL.StencilOp zfail, OpenTK.Graphics.OpenGL.StencilOp zpass)
        {
            ES.GL.StencilOp((ES.StencilOp)(int)fail, (ES.StencilOp)(int)zfail, (ES.StencilOp)(int)zpass);
        }

        public static void StencilMask(int mask)
        {
            ES.GL.StencilMask(mask);
        }

        public static void PolygonOffset(float factor, float units)
        {
            ES.GL.PolygonOffset(factor, units);
        }

        public static void Viewport(int x, int y, int width, int height)
        {
            ES.GL.Viewport(x, y, width, height);
        }

        public static void PixelStore(PixelStoreParameter pname, int param)
        {
            ES.GL.PixelStore((ES.PixelStoreParameter)(int)pname, param);
        }

        public static void ReadBuffer(ReadBufferMode mode)
        {
            ES.GL.ReadBuffer((ES.ReadBufferMode)(int)mode);
        }

        public static void ActiveTexture(TextureUnit texture)
        {
            ES.GL.ActiveTexture((ES.TextureUnit)(int)texture);
        }

        public static ErrorCode GetError()
        {
            return (ErrorCode)(int)ES.GL.GetError();
        }

        public static string GetString(StringName name)
        {
            return ES.GL.GetString((ES.StringName)(int)name);
        }

        public static int GetInteger(GetPName pname)
        {
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
            ES.GL.TexParameter((ES.TextureTarget)(int)target, (ES.TextureParameterName)(int)pname, param);
        }

        public static void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels)
        {
            ES.GL.TexImage2D((ES.TextureTarget2d)(int)target, level,
                (ES.TextureComponentCount)(int)internalFormat, width, height, border,
                (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        }

        public static void TexImage2D<T>(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            ES.GL.TexImage2D((ES.TextureTarget2d)(int)target, level,
                (ES.TextureComponentCount)(int)internalFormat, width, height, border,
                (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        }

        public static void TexSubImage2D<T>(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
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
            ES.GL.CopyTexSubImage2D((ES.TextureTarget2d)(int)target, level, xoffset, yoffset,
                x, y, width, height);
        }

        public static void ReadPixels<T>(int x, int y, int width, int height, PixelFormat format,
            PixelType type, T[] pixels) where T : struct
        {
            ES.GL.ReadPixels(x, y, width, height, (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        }

        public static int GenFramebuffer()
        {
            return ES.GL.GenFramebuffer();
        }

        public static void DeleteFramebuffer(int framebuffer)
        {
            if (framebuffer != 0) ES.GL.DeleteFramebuffer(framebuffer);
        }

        public static void BindFramebuffer(FramebufferTarget target, int framebuffer)
        {
            ES.GL.BindFramebuffer((ES.FramebufferTarget)(int)target, framebuffer);
        }

        public static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment,
            TextureTarget textarget, int texture, int level)
        {
            ES.GL.FramebufferTexture2D((ES.FramebufferTarget)(int)target,
                (ES.FramebufferAttachment)(int)attachment, (ES.TextureTarget2d)(int)textarget,
                RealTexture(texture), level);
        }

        public static int GenRenderbuffer()
        {
            return ES.GL.GenRenderbuffer();
        }

        public static void DeleteRenderbuffer(int renderbuffer)
        {
            if (renderbuffer != 0) ES.GL.DeleteRenderbuffer(renderbuffer);
        }

        public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer)
        {
            ES.GL.BindRenderbuffer((ES.RenderbufferTarget)(int)target, renderbuffer);
        }

        public static void RenderbufferStorage(RenderbufferTarget target,
            OpenTK.Graphics.OpenGL.RenderbufferStorage internalFormat, int width, int height)
        {
            ES.GL.RenderbufferStorage((ES.RenderbufferTarget)(int)target,
                (ES.RenderbufferInternalFormat)(int)internalFormat, width, height);
        }

        public static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderbuffertarget, int renderbuffer)
        {
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
            ES.GL.GetFramebufferAttachmentParameter((ES.FramebufferTarget)(int)target,
                (ES.FramebufferAttachment)(int)attachment,
                (ES.FramebufferParameterName)(int)pname, out result);
        }

        public static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target)
        {
            return (FramebufferErrorCode)(int)ES.GL.CheckFramebufferStatus((ES.FramebufferTarget)(int)target);
        }

        #endregion

        #region uniforms

        public static void Uniform1(int location, int value)
        {
            ES.GL.Uniform1(location, value);
        }

        public static void Uniform1(int location, float value)
        {
            ES.GL.Uniform1(location, value);
        }

        public static void Uniform1(int location, int count, float[] value)
        {
            ES.GL.Uniform1(location, count, value);
        }

        public static void Uniform3(int location, Vector3 vector)
        {
            ES.GL.Uniform3(location, vector);
        }

        public static void Uniform3(int location, int count, float[] value)
        {
            ES.GL.Uniform3(location, count, value);
        }

        public static void Uniform4(int location, Vector4 vector)
        {
            ES.GL.Uniform4(location, vector);
        }

        public static void Uniform4(int location, int count, float[] value)
        {
            ES.GL.Uniform4(location, count, value);
        }

        public static void Uniform4(int location, ref Vector4 vector)
        {
            ES.GL.Uniform4(location, ref vector);
        }

        public static void Uniform4(int location, float v0, float v1, float v2, float v3)
        {
            ES.GL.Uniform4(location, v0, v1, v2, v3);
        }

        public static void Uniform4(int location, int v0, int v1, int v2, int v3)
        {
            ES.GL.Uniform4(location, v0, v1, v2, v3);
        }

        public static void UniformMatrix4(int location, bool transpose, ref Matrix4 matrix)
        {
            ES.GL.UniformMatrix4(location, transpose, ref matrix);
        }

        public static void UniformMatrix4(int location, int count, bool transpose, float[] value)
        {
            ES.GL.UniformMatrix4(location, count, transpose, value);
        }

        #endregion
    }
}
#endif
