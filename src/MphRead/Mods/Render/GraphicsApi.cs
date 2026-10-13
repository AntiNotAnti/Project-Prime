#if !MPHREAD_SERVER
using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
#if ANDROID
using DesktopGL = MphRead.Mods.Render.GlEs;
#else
using DesktopGL = OpenTK.Graphics.OpenGL.GL;
#endif

namespace MphRead.Mods.Render
{
    /// <summary>
    /// OpenGL/GLES front-end for the historical GL call surface.
    /// Calls go directly to the active platform GL implementation.
    /// </summary>
    internal static class GraphicsApi
    {
#if !ANDROID
        [ThreadStatic] private static bool _compilingDisplayList;
        [ThreadStatic] private static ListMode _displayListMode;
#endif
        [ThreadStatic] private static HudSamplerCache? _hudSamplers;
        internal static HudSamplerScope BeginHudSamplerScope()
        {
            if (!HudSamplerCache.Enabled)
                return default;
            HudSamplerCache cache = _hudSamplers ??= new();
            cache.Begin();
            return new HudSamplerScope(cache);
        }
        internal readonly struct HudSamplerScope : IDisposable
        {
            private readonly HudSamplerCache? _cache;
            internal HudSamplerScope(HudSamplerCache cache) => _cache = cache;
            public void Dispose() => _cache?.End();
        }
        [ThreadStatic] private static MatrixPaletteUploadCache? _paletteUploads;
        [ThreadStatic] private static int _paletteProgram;
        private static MatrixPaletteUploadCache PaletteUploads => _paletteUploads ??= new();
        [ThreadStatic] private static OpenGlScopedCapabilityCache? _capabilityCache;
        [ThreadStatic] private static OpenGlScopedBindingCache? _bindingCache;
        private static OpenGlScopedCapabilityCache CapabilityCache =>
            _capabilityCache ??= new OpenGlScopedCapabilityCache();
        private static OpenGlScopedBindingCache BindingCache =>
            _bindingCache ??= new OpenGlScopedBindingCache();

        internal static void BeginScopedWorldStateElision()
        {
            if (!OpenGlSubmissionProfiler.ForceLegacyStateRequests)
                CapabilityCache.Begin();
            if (OpenGlScopedBindingCache.Enabled)
                BindingCache.Begin();
#if !ANDROID
            DesktopRetainedGeometry.BeginWorldScope();
#endif
        }

        internal static void EndScopedWorldStateElision()
        {
            _capabilityCache?.End();
            _bindingCache?.End();
#if !ANDROID
            DesktopRetainedGeometry.EndWorldScope();
#endif
        }

        internal static void ResetStateElisionForContext()
        {
#if !ANDROID
            _compilingDisplayList = false;
            DesktopProceduralStream.ResetContext();
#endif
            _capturingListAttributes = null;
            _listAttributeEffects?.Reset();
            _hudSamplers?.End();
#if !ANDROID
            OpenGlFrameProfiler.ResetContext();
#endif
            _paletteUploads?.ForgetLayout();
            _paletteProgram = 0;
            OpenGlContextPolicy.Reset();
            OpenGlPassProfiler.ResetContext();
            _capabilityCache?.ResetContext();
            _bindingCache?.ResetContext();
            OpenGlGpuProfiler.ResetContext();
        }

#if !ANDROID
        private static readonly LegacyGlUniformCache _legacyUniforms = new();

        internal static void ResetLegacyState()
        {
            _legacyUniforms.ResetContext();
            DesktopRetainedGeometry.ResetContext();
            ResetStateElisionForContext();
        }
        internal static void BeginLegacyUniformSample() => _legacyUniforms.BeginSample();
        internal static LegacyUniformSample EndLegacyUniformSample() => _legacyUniforms.EndSample();
#endif

        public static void LoadBindings(IBindingsContext context)
        { _capturingListAttributes?.Unknown();
#if !ANDROID
            DesktopGL.LoadBindings(context);
            _legacyUniforms.ResetContext();
            DesktopRetainedGeometry.ResetContext();
#endif
            ResetStateElisionForContext();
            OpenGlContextPolicy.Reset(context.GetProcAddress);
        }

        [ThreadStatic] private static DisplayListAttributeEffects? _listAttributeEffects;
        // Only the explicit geometry/list subset has replay metadata. Every
        // other state-changing facade operation invalidates an active capture.
        // Raw driver calls inside a facade-open list must invalidate it too.
        [ThreadStatic] private static DisplayListAttributeEffects? _capturingListAttributes;
        internal static bool TryGetDisplayListAttributeEffects(int id, out DisplayListAttributeEffect effect)
        {
            if (_listAttributeEffects != null) return _listAttributeEffects.TryGet(id, out effect);
            effect = default; return false;
        }

        public static void Begin(PrimitiveType mode)
        {
            OpenGlSubmissionProfiler.NoteImmediateBegin();
#if !ANDROID
            DesktopRetainedGeometry.Begin(mode);
#endif
            DesktopGL.Begin(mode);
        }
        public static void End()
        {
            DesktopGL.End();
#if !ANDROID
            // Count executed Begin/End submissions, not commands stored in a list.
            if (!_compilingDisplayList || _displayListMode == ListMode.CompileAndExecute)
                OpenGlPassProfiler.Count(OpenGlFrameCounter.ImmediateDraws);
            DesktopRetainedGeometry.End();
#endif
        }
        public static void Vertex2(float x, float y)
        {
            _capturingListAttributes?.Vertex();
#if !ANDROID
            DesktopRetainedGeometry.Vertex(new Vector3(x, y, 0));
#endif
            DesktopGL.Vertex2(x, y);
        }
        public static void Vertex3(float x, float y, float z)
        {
            _capturingListAttributes?.Vertex();
#if !ANDROID
            DesktopRetainedGeometry.Vertex(new Vector3(x, y, z));
#endif
            DesktopGL.Vertex3(x, y, z);
        }
        public static void Vertex3(Vector3 value)
        {
            _capturingListAttributes?.Vertex();
#if !ANDROID
            DesktopRetainedGeometry.Vertex(value);
#endif
            DesktopGL.Vertex3(value);
        }
        public static void Color3(float r, float g, float b)
        {
            _capturingListAttributes?.Color(new Vector4(r, g, b, 1));
#if !ANDROID
            DesktopRetainedGeometry.Color(new Vector4(r, g, b, 1));
#endif
            DesktopGL.Color3(r, g, b);
        }
        public static void Color3(Vector3 value)
        {
            _capturingListAttributes?.Color(new Vector4(value, 1));
#if !ANDROID
            DesktopRetainedGeometry.Color(new Vector4(value, 1));
#endif
            DesktopGL.Color3(value);
        }
        public static void Color4(float r, float g, float b, float a)
        {
            _capturingListAttributes?.Color(new Vector4(r, g, b, a));
#if !ANDROID
            DesktopRetainedGeometry.Color(new Vector4(r, g, b, a));
#endif
            DesktopGL.Color4(r, g, b, a);
        }
        public static void Normal3(float x, float y, float z)
        {
            _capturingListAttributes?.Normal(new Vector3(x, y, z));
#if !ANDROID
            DesktopRetainedGeometry.Normal(new Vector3(x, y, z));
#endif
            DesktopGL.Normal3(x, y, z);
        }
        public static void TexCoord2(float s, float t)
        {
            _capturingListAttributes?.Texcoord(new Vector3(s, t, 0));
#if !ANDROID
            DesktopRetainedGeometry.Texcoord(new Vector3(s, t, 0));
#endif
            DesktopGL.TexCoord2(s, t);
        }
        public static void TexCoord3(float s, float t, float r)
        {
            _capturingListAttributes?.Texcoord(new Vector3(s, t, r));
#if !ANDROID
            DesktopRetainedGeometry.Texcoord(new Vector3(s, t, r));
#endif
            DesktopGL.TexCoord3(s, t, r);
        }
        public static void TexCoord3(Vector3 value)
        {
            _capturingListAttributes?.Texcoord(value);
#if !ANDROID
            DesktopRetainedGeometry.Texcoord(value);
#endif
            DesktopGL.TexCoord3(value);
        }
        public static void MultiTexCoord2(TextureUnit unit, float s, float t)
        {
            if (unit == TextureUnit.Texture0) _capturingListAttributes?.Texcoord(new Vector3(s, t, 0));
            else _capturingListAttributes?.Unknown();
#if !ANDROID
            if (unit == TextureUnit.Texture0) DesktopRetainedGeometry.Texcoord(new Vector3(s, t, 0));
            else DesktopRetainedGeometry.RejectCurrentList();
#endif
            DesktopGL.MultiTexCoord2(unit, s, t);
        }

#if !ANDROID
        // Preserve native primitive semantics while avoiding per-vertex retained
        // list-capture hooks when no list is being compiled. No state is inferred.
        internal static bool TryDrawProceduralImmediate(RenderItem item)
        {
            if (_compilingDisplayList) return false;
            int count;
            PrimitiveType mode;
            switch (item.Type)
            {
                case RenderItemType.Particle: count = 4; mode = PrimitiveType.Quads; break;
                case RenderItemType.TrailSingle: count = 4; mode = PrimitiveType.QuadStrip; break;
                case RenderItemType.TrailMulti:
                    if (item.ItemCount < 8 || item.ItemCount % 2 != 0
                        || item.ItemCount / 2 > ProceduralFrameGeometry.MaxVertices) return false;
                    count = item.ItemCount / 2; mode = PrimitiveType.QuadStrip; break;
                case RenderItemType.TrailStack:
                    if (item.ItemCount <= 0 || item.ItemCount > ProceduralFrameGeometry.MaxVertices / 4) return false;
                    count = item.ItemCount * 4; mode = PrimitiveType.Quads; break;
                default: return false;
            }
            Vector3[] points = item.Points;
            if (points == null || points.Length / 2 < count) return false;
            bool particle = item.Type == RenderItemType.Particle;
            float scaleS = item.ScaleS, scaleT = item.ScaleT;
            OpenGlSubmissionProfiler.NoteImmediateBegin();
            DesktopGL.Begin(mode);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 uv = points[i * 2];
                    if (particle) DesktopGL.TexCoord3(uv.X * scaleS, uv.Y * scaleT, 0f);
                    else DesktopGL.TexCoord3(uv);
                    DesktopGL.Vertex3(points[i * 2 + 1]);
                }
            }
            finally
            {
                DesktopGL.End();
                OpenGlPassProfiler.Count(OpenGlFrameCounter.ImmediateDraws);
            }
            return true;
        }
#endif

        internal static bool RetainedGeometryBatchingEnabled
        {
            get
            {
#if !ANDROID
                return DesktopRetainedGeometry.BatchEnabled;
#else
                return false;
#endif
            }
        }

        internal static bool TryDrawRetainedBatch(int[] ids, int count, ulong? preparedHash = null)
        {
#if !ANDROID
            if (DesktopRetainedGeometry.TryDrawBatch(ids, count, preparedHash))
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.MergedDraws);
                OpenGlSubmissionProfiler.NoteRetainedBatch(count);
                return true;
            }
#endif
            return false;
        }

        internal static bool TryDrawRetainedBatch(RetainedRoomBatchPlan plan)
        {
#if !ANDROID
            if (DesktopRetainedGeometry.TryDrawBatch(plan))
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.MergedDraws);
                OpenGlSubmissionProfiler.NoteRetainedBatch(plan.Count);
                return true;
            }
            return false;
#else
            return TryDrawRetainedBatch(plan.Ids, plan.Count, plan.Hash);
#endif
        }

        public static int GenLists(int range) => DesktopGL.GenLists(range);
        public static void RegisterRoomGeometryList(int list)
        {
#if !ANDROID
            DesktopRetainedGeometry.RegisterRoomList(list);
#endif
        }
        public static void NewList(int list, ListMode mode)
        {
            _hudSamplers?.BeginList();
            _bindingCache?.Invalidate();
            CapabilityCache.BeginList(list, mode);
            (_listAttributeEffects ??= new()).Begin(list);
            _capturingListAttributes = _listAttributeEffects.IsCapturing ? _listAttributeEffects : null;
            if (mode != ListMode.Compile && mode != ListMode.CompileAndExecute) _capturingListAttributes?.Unknown();
#if !ANDROID
            DesktopRetainedGeometry.NewList(list, mode);
#endif
            DesktopGL.NewList(list, mode);
#if !ANDROID
            _compilingDisplayList = true;
            _displayListMode = mode;
#endif
        }
        public static void EndList()
        {
            _hudSamplers?.EndList();
            DesktopGL.EndList();
#if !ANDROID
            _compilingDisplayList = false;
#endif
            _listAttributeEffects?.End();
            _capturingListAttributes = null;
            _bindingCache?.Invalidate();
#if !ANDROID
            DesktopRetainedGeometry.EndList();
#endif
            CapabilityCache.EndList();
        }
        public static void CallList(int list)
        {
            _hudSamplers?.Invalidate();
            if (_bindingCache != null && _bindingCache.Active
                && (_capabilityCache == null
                    || !_capabilityCache.IsGeometryOnlyList(list)))
                _bindingCache.Invalidate();
            _capabilityCache?.CallList(list);
            _capturingListAttributes?.Call(list);
#if !ANDROID
            if (DesktopRetainedGeometry.TryDraw(list))
            {
                OpenGlSubmissionProfiler.NoteVboDraw();
                return;
            }
#endif
            OpenGlPassProfiler.Count(OpenGlFrameCounter.DisplayListCalls);
            OpenGlSubmissionProfiler.NoteDisplayList();
            DesktopGL.CallList(list);
        }
        public static void DeleteLists(int list, int range)
        {
            _bindingCache?.Invalidate();
            DesktopGL.DeleteLists(list, range);
            _listAttributeEffects?.Delete(list, range);
#if !ANDROID
            DesktopRetainedGeometry.DeleteLists(list, range);
#endif
            _capabilityCache?.DeleteLists(list, range);
        }

        public static int GenTexture() => DesktopGL.GenTexture();
        public static void DeleteTexture(int texture)
        { _capturingListAttributes?.Unknown();
            _hudSamplers?.Invalidate();
            _bindingCache?.DeleteTexture(texture);
            DesktopGL.DeleteTexture(texture);
        }
        public static bool IsTexture(int texture) => DesktopGL.IsTexture(texture);
        public static void BindTexture(TextureTarget target, int texture)
        { _capturingListAttributes?.Unknown();
            _hudSamplers?.Bind(target, texture);
            OpenGlSubmissionProfiler.NoteTextureBind();
            if (_bindingCache != null && !_bindingCache.ShouldBindTexture(target, texture))
            {
                OpenGlSubmissionProfiler.NoteTextureBindElided();
                return;
            }
            DesktopGL.BindTexture(target, texture);
        }
        public static void ActiveTexture(TextureUnit unit)
        { _capturingListAttributes?.Unknown();
            _hudSamplers?.Activate(unit);
            if (_bindingCache != null && !_bindingCache.ShouldActivateTexture(unit))
            {
                OpenGlSubmissionProfiler.NoteTextureUnitElided();
                return;
            }
            DesktopGL.ActiveTexture(unit);
        }
        public static void TexParameter(TextureTarget target, TextureParameterName name, int value)
        { _capturingListAttributes?.Unknown();
            if (_hudSamplers != null && !_hudSamplers.ShouldSet(target, name, value))
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.StateSkips);
                OpenGlPassProfiler.Count(OpenGlFrameCounter.SamplerSkips);
                return;
            }
            OpenGlPassProfiler.Count(OpenGlFrameCounter.SamplerWrites);
            DesktopGL.TexParameter(target, name, value);
        }
        public static void TexEnv(TextureEnvTarget target, TextureEnvParameter name, int value)
        { _capturingListAttributes?.Unknown(); DesktopGL.TexEnv(target, name, value); }
        public static void GenerateMipmap(GenerateMipmapTarget target)
        { _capturingListAttributes?.Unknown(); DesktopGL.GenerateMipmap(target); }
        public static void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels)
        { _capturingListAttributes?.Unknown();
            DesktopGL.TexImage2D(target, level, internalFormat, width, height, border, format, type, pixels);
        }
        public static void TexImage2D<T>(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, T[] pixels) where T : struct
        { _capturingListAttributes?.Unknown();
            DesktopGL.TexImage2D(target, level, internalFormat, width, height, border, format, type, pixels);
        }
        public static void TexSubImage2D<T>(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct
        { _capturingListAttributes?.Unknown();
            DesktopGL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels);
        }
        public static void TexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, IntPtr pixels)
        { _capturingListAttributes?.Unknown();
            DesktopGL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels);
        }
        public static void CopyTexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int x, int y, int width, int height)
        { _capturingListAttributes?.Unknown();
            DesktopGL.CopyTexSubImage2D(target, level, xoffset, yoffset, x, y, width, height);
        }
        public static void GetTexLevelParameter(TextureTarget target, int level, GetTextureParameter name,
            out int value)
        {
            DesktopGL.GetTexLevelParameter(target, level, name, out value);
        }

        public static int CreateShader(ShaderType type) => DesktopGL.CreateShader(type);
        public static void ShaderSource(int shader, string source) { _capturingListAttributes?.Unknown(); DesktopGL.ShaderSource(shader, source); }
        public static void CompileShader(int shader) { _capturingListAttributes?.Unknown(); DesktopGL.CompileShader(shader); }
        public static void GetShader(int shader, ShaderParameter name, out int value)
        { DesktopGL.GetShader(shader, name, out value); }
        public static string GetShaderInfoLog(int shader) => DesktopGL.GetShaderInfoLog(shader);
        public static void DeleteShader(int shader) { _capturingListAttributes?.Unknown(); DesktopGL.DeleteShader(shader); }
        public static int CreateProgram() => DesktopGL.CreateProgram();
        public static void DeleteProgram(int program)
        { _capturingListAttributes?.Unknown();
            _paletteUploads?.ForgetLayout(program);
            {
                DesktopGL.DeleteProgram(program);
#if !ANDROID
                _legacyUniforms.InvalidateProgram(program);
#endif
            }
        }
        public static void AttachShader(int program, int shader) { _capturingListAttributes?.Unknown(); DesktopGL.AttachShader(program, shader); }
        public static void DetachShader(int program, int shader) { _capturingListAttributes?.Unknown(); DesktopGL.DetachShader(program, shader); }
        public static void LinkProgram(int program)
        { _capturingListAttributes?.Unknown();
            _paletteUploads?.ForgetLayout(program);
            {
                DesktopGL.LinkProgram(program);
#if !ANDROID
                // Relinking can replace every uniform value/location contract.
                _legacyUniforms.InvalidateProgram(program);
#endif
            }
        }
        public static void UseProgram(int program)
        { _capturingListAttributes?.Unknown();
            if (_paletteProgram != program) _paletteUploads?.Invalidate();
            _paletteProgram = program;
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.ProgramBinds);
                OpenGlSubmissionProfiler.NoteProgramBind();
                DesktopGL.UseProgram(program);
#if !ANDROID
                _legacyUniforms.UseProgram(program);
#endif
            }
        }
        public static void GetProgram(int program, GetProgramParameterName name, out int value)
        { DesktopGL.GetProgram(program, name, out value); }
        public static string GetProgramInfoLog(int program) => DesktopGL.GetProgramInfoLog(program);
        public static int GetUniformLocation(int program, string name) => DesktopGL.GetUniformLocation(program, name);
        public static void GetUniform(int program, int location, out int value)
        { DesktopGL.GetUniform(program, location, out value); }

        public static void Uniform1(int location, int value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.Submit(location, value)) return;
#endif
                DesktopGL.Uniform1(location, value);
            }
        }
        public static void Uniform1(int location, float value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.Submit(location, value)) return;
#endif
                DesktopGL.Uniform1(location, value);
            }
        }
        public static void Uniform1(int location, int count, float[] value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                _legacyUniforms.InvalidateFloatArray();
                _legacyUniforms.NoteUncachedWrite();
#endif
                DesktopGL.Uniform1(location, count, value);
            }
        }
        public static void Uniform2(int location, float x, float y)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.Submit2(location, x, y)) return;
#endif
                DesktopGL.Uniform2(location, x, y);
            }
        }
        public static void Uniform3(int location, Vector3 value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.Submit3(location, value)) return;
#endif
                DesktopGL.Uniform3(location, value);
            }
        }
        public static void Uniform3(int location, int count, float[] value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                _legacyUniforms.InvalidateVector3Array();
                _legacyUniforms.NoteUncachedWrite();
#endif
                DesktopGL.Uniform3(location, count, value);
            }
        }
        public static void Uniform4(int location, Vector4 value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.Submit4(location, value)) return;
#endif
                DesktopGL.Uniform4(location, value);
            }
        }
        public static void Uniform4(int location, ref Vector4 value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.Submit4(location, value)) return;
#endif
                DesktopGL.Uniform4(location, ref value);
            }
        }
        public static void Uniform4(int location, float x, float y, float z, float w)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                var value = new Vector4(x, y, z, w);
                if (!_legacyUniforms.Submit4(location, value)) return;
#endif
                DesktopGL.Uniform4(location, x, y, z, w);
            }
        }
        public static void Uniform4(int location, int x, int y, int z, int w)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.Submit4(location, x, y, z, w)) return;
#endif
                DesktopGL.Uniform4(location, x, y, z, w);
            }
        }
        public static void UniformMatrix4(int location, bool transpose, ref Matrix4 value)
        { _capturingListAttributes?.Unknown();
            {
#if !ANDROID
                if (!_legacyUniforms.SubmitMatrix4(location, transpose, in value)) return;
#endif
                _paletteUploads?.MatrixWrite(_paletteProgram, location);
                DesktopGL.UniformMatrix4(location, transpose, ref value);
            }
        }
        public static void UniformMatrix4(int location, int count, bool transpose, float[] value)
        { _capturingListAttributes?.Unknown();
            _paletteUploads?.Invalidate();
            OpenGlPassProfiler.Count(OpenGlFrameCounter.UniformArrayUploads);
            OpenGlPassProfiler.Count(OpenGlFrameCounter.UniformArrayBytes, (long)count * 64);
            OpenGlUniformAttribution.NoteMatrixArray(_paletteProgram, count, submitted: true);
            {
#if !ANDROID
                _legacyUniforms.InvalidateMatrix4Array(location, count);
                _legacyUniforms.NoteUncachedWrite();
#endif
                DesktopGL.UniformMatrix4(location, count, transpose, value);
            }
        }

        internal static void RegisterMatrixPaletteLayout(int program, params int[] independentMatrices)
        {
            if (!MatrixPaletteRevision.Enabled) return;
            PaletteUploads.RegisterLayout(program, independentMatrices);
            int[] elements = new int[32];
            for (int i = 0; i < elements.Length; i++)
                elements[i] = DesktopGL.GetUniformLocation(program, "mtx_stack[" + i + "]");
#if ANDROID
            DesktopGL.RegisterMatrixArrayLayout(program, elements, independentMatrices);
#else
            _legacyUniforms.RegisterMatrixArrayLayout(program, elements, independentMatrices);
#endif
        }

        internal static void UniformMatrixPalette(int location, int count, float[] values,
            object? source, long revision, float scale)
        {
            if (MatrixPaletteRevision.Enabled && PaletteUploads.Matches(
                _paletteProgram, location, count, source, revision, scale))
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.UniformArraySkips);
                OpenGlUniformAttribution.NoteMatrixArray(_paletteProgram, count, submitted: false);
                return;
            }
            UniformMatrix4(location, count, false, values);
            if (MatrixPaletteRevision.Enabled)
                PaletteUploads.Record(_paletteProgram, location, count, source, revision, scale);
        }

        public static int GenFramebuffer() => DesktopGL.GenFramebuffer();
        public static void DeleteFramebuffer(int framebuffer) { _capturingListAttributes?.Unknown(); DesktopGL.DeleteFramebuffer(framebuffer); }
        public static void BindFramebuffer(FramebufferTarget target, int framebuffer)
        { _capturingListAttributes?.Unknown();
            OpenGlSubmissionProfiler.NoteFramebufferBind();
            DesktopGL.BindFramebuffer(target, framebuffer);
        }
        public static bool IsFramebuffer(int framebuffer) => DesktopGL.IsFramebuffer(framebuffer);
        public static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment,
            TextureTarget textureTarget, int texture, int level)
        { _capturingListAttributes?.Unknown(); DesktopGL.FramebufferTexture2D(target, attachment, textureTarget, texture, level); }
        public static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target)
            => DesktopGL.CheckFramebufferStatus(target);
        public static int GenRenderbuffer() => DesktopGL.GenRenderbuffer();
        public static void DeleteRenderbuffer(int renderbuffer) { _capturingListAttributes?.Unknown(); DesktopGL.DeleteRenderbuffer(renderbuffer); }
        public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer) { _capturingListAttributes?.Unknown(); DesktopGL.BindRenderbuffer(target, renderbuffer); }
        public static void RenderbufferStorage(RenderbufferTarget target, RenderbufferStorage format,
            int width, int height) { _capturingListAttributes?.Unknown(); DesktopGL.RenderbufferStorage(target, format, width, height); }
        public static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderbufferTarget, int renderbuffer)
        { _capturingListAttributes?.Unknown(); DesktopGL.FramebufferRenderbuffer(target, attachment, renderbufferTarget, renderbuffer); }
        public static void GetFramebufferAttachmentParameter(FramebufferTarget target,
            FramebufferAttachment attachment, FramebufferParameterName name, out int value)
        { DesktopGL.GetFramebufferAttachmentParameter(target, attachment, name, out value); }
        public static void BlitFramebuffer(int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            ClearBufferMask mask, BlitFramebufferFilter filter)
        { _capturingListAttributes?.Unknown();
            DesktopGL.BlitFramebuffer(sourceX0, sourceY0, sourceX1, sourceY1,
                destinationX0, destinationY0, destinationX1, destinationY1, mask, filter);
        }

        public static void Enable(EnableCap cap)
        { _capturingListAttributes?.Unknown();
            OpenGlSubmissionProfiler.NoteStateRequest();
            if (_capabilityCache != null && !_capabilityCache.ShouldSubmit(cap, true))
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.StateSkips);
                OpenGlSubmissionProfiler.NoteStateRequestSkipped();
                return;
            }
            DesktopGL.Enable(cap);
        }
        public static void Disable(EnableCap cap)
        { _capturingListAttributes?.Unknown();
            OpenGlSubmissionProfiler.NoteStateRequest();
            if (_capabilityCache != null && !_capabilityCache.ShouldSubmit(cap, false))
            {
                OpenGlPassProfiler.Count(OpenGlFrameCounter.StateSkips);
                OpenGlSubmissionProfiler.NoteStateRequestSkipped();
                return;
            }
            DesktopGL.Disable(cap);
        }
        public static bool IsEnabled(EnableCap cap) => DesktopGL.IsEnabled(cap);
        public static void AlphaFunc(AlphaFunction function, float reference) { _capturingListAttributes?.Unknown(); DesktopGL.AlphaFunc(function, reference); }
        public static void PolygonMode(TriangleFace face, OpenTK.Graphics.OpenGL.PolygonMode mode)
        { _capturingListAttributes?.Unknown();
            if (_bindingCache != null && !_bindingCache.ShouldPolygonMode(face, mode))
            {
                OpenGlSubmissionProfiler.NoteRasterStateElided();
                return;
            }
            DesktopGL.PolygonMode(face, mode);
        }
        public static void LineWidth(float width)
        { _capturingListAttributes?.Unknown();
            if (_bindingCache != null && !_bindingCache.ShouldLineWidth(width))
            {
                OpenGlSubmissionProfiler.NoteRasterStateElided();
                return;
            }
            DesktopGL.LineWidth(width);
        }
        public static void Clear(ClearBufferMask mask) { _capturingListAttributes?.Unknown(); DesktopGL.Clear(mask); }
        public static void ClearColor(Color4 color) { _capturingListAttributes?.Unknown(); DesktopGL.ClearColor(color); }
        public static void ClearColor(float red, float green, float blue, float alpha) { _capturingListAttributes?.Unknown(); DesktopGL.ClearColor(red, green, blue, alpha); }
        public static void ClearStencil(int value) { _capturingListAttributes?.Unknown(); DesktopGL.ClearStencil(value); }
        public static void ColorMask(bool red, bool green, bool blue, bool alpha) { _capturingListAttributes?.Unknown(); DesktopGL.ColorMask(red, green, blue, alpha); }
        public static void DepthMask(bool enabled) { _capturingListAttributes?.Unknown(); DesktopGL.DepthMask(enabled); }
        public static void DepthFunc(DepthFunction function) { _capturingListAttributes?.Unknown(); DesktopGL.DepthFunc(function); }
        public static void CullFace(TriangleFace face)
        { _capturingListAttributes?.Unknown();
            if (_bindingCache != null && !_bindingCache.ShouldCullFace(face))
            {
                OpenGlSubmissionProfiler.NoteRasterStateElided();
                return;
            }
            DesktopGL.CullFace(face);
        }
        public static void BlendFunc(BlendingFactor source, BlendingFactor destination) { _capturingListAttributes?.Unknown(); DesktopGL.BlendFunc(source, destination); }
        public static void BlendEquation(BlendEquationMode mode) { _capturingListAttributes?.Unknown(); DesktopGL.BlendEquation(mode); }
        public static void StencilFunc(StencilFunction function, int reference, int mask)
        { _capturingListAttributes?.Unknown();
            OpenGlSubmissionProfiler.NoteStencilFunc();
            DesktopGL.StencilFunc(function, reference, mask);
        }
        public static void StencilOp(StencilOp fail, StencilOp zfail, StencilOp zpass) { _capturingListAttributes?.Unknown(); DesktopGL.StencilOp(fail, zfail, zpass); }
        public static void StencilMask(int mask) { _capturingListAttributes?.Unknown(); DesktopGL.StencilMask(mask); }
        public static void PolygonOffset(float factor, float units) { _capturingListAttributes?.Unknown(); DesktopGL.PolygonOffset(factor, units); }
        public static void Viewport(int x, int y, int width, int height) { _capturingListAttributes?.Unknown(); DesktopGL.Viewport(x, y, width, height); }
        public static void Scissor(int x, int y, int width, int height) { _capturingListAttributes?.Unknown(); DesktopGL.Scissor(x, y, width, height); }
        public static void PixelStore(PixelStoreParameter name, int value) { _capturingListAttributes?.Unknown(); DesktopGL.PixelStore(name, value); }
        public static void ReadBuffer(ReadBufferMode mode) { _capturingListAttributes?.Unknown(); DesktopGL.ReadBuffer(mode); }
        public static void DrawBuffer(DrawBufferMode mode) { _capturingListAttributes?.Unknown(); DesktopGL.DrawBuffer(mode); }
        public static void Finish() { DesktopGL.Finish(); }
        public static ErrorCode GetError() => DesktopGL.GetError();
        public static string GetString(StringName name) => DesktopGL.GetString(name);
        public static int GetInteger(GetPName name) => DesktopGL.GetInteger(name);
        public static void GetInteger(GetPName name, out int value)
        { DesktopGL.GetInteger(name, out value); }
        public static void GetInteger(GetPName name, int[] values)
        { DesktopGL.GetInteger(name, values); }
        public static void DebugMessageCallback(DebugProc callback, IntPtr userParam)
        { DesktopGL.DebugMessageCallback(callback, userParam); }

        public static void ReadPixels<T>(int x, int y, int width, int height, PixelFormat format,
            PixelType type, T[] pixels) where T : struct
        {
            DesktopGL.ReadPixels(x, y, width, height, format, type, pixels);
        }

        public static void MatrixMode(MatrixMode mode) { _capturingListAttributes?.Unknown(); DesktopGL.MatrixMode(mode); }
        public static void PushMatrix() { _capturingListAttributes?.Unknown(); DesktopGL.PushMatrix(); }
        public static void PopMatrix() { _capturingListAttributes?.Unknown(); DesktopGL.PopMatrix(); }
        public static void LoadIdentity() { _capturingListAttributes?.Unknown(); DesktopGL.LoadIdentity(); }
        public static void PushAttrib(AttribMask mask)
        {
            _hudSamplers?.Invalidate();
            _capabilityCache?.Invalidate();
            _bindingCache?.Invalidate();
            _capturingListAttributes?.Unknown();
            DesktopGL.PushAttrib(mask);
        }
        public static void PopAttrib()
        {
            _hudSamplers?.Invalidate();
            _capturingListAttributes?.Unknown();
            DesktopGL.PopAttrib();
            _capabilityCache?.Invalidate();
            _bindingCache?.Invalidate();
        }
    }
}
#endif
