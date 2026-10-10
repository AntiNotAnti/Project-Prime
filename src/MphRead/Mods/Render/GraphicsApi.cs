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
            _capabilityCache?.ResetContext();
            _bindingCache?.ResetContext();
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
        {
#if !ANDROID
            DesktopGL.LoadBindings(context);
            _legacyUniforms.ResetContext();
            DesktopRetainedGeometry.ResetContext();
#endif
            ResetStateElisionForContext();
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
            DesktopRetainedGeometry.End();
#endif
        }
        public static void Vertex2(float x, float y)
        {
#if !ANDROID
            DesktopRetainedGeometry.Vertex(new Vector3(x, y, 0));
#endif
            DesktopGL.Vertex2(x, y);
        }
        public static void Vertex3(float x, float y, float z)
        {
#if !ANDROID
            DesktopRetainedGeometry.Vertex(new Vector3(x, y, z));
#endif
            DesktopGL.Vertex3(x, y, z);
        }
        public static void Vertex3(Vector3 value)
        {
#if !ANDROID
            DesktopRetainedGeometry.Vertex(value);
#endif
            DesktopGL.Vertex3(value);
        }
        public static void Color3(float r, float g, float b)
        {
#if !ANDROID
            DesktopRetainedGeometry.Color(new Vector4(r, g, b, 1));
#endif
            DesktopGL.Color3(r, g, b);
        }
        public static void Color3(Vector3 value)
        {
#if !ANDROID
            DesktopRetainedGeometry.Color(new Vector4(value, 1));
#endif
            DesktopGL.Color3(value);
        }
        public static void Color4(float r, float g, float b, float a)
        {
#if !ANDROID
            DesktopRetainedGeometry.Color(new Vector4(r, g, b, a));
#endif
            DesktopGL.Color4(r, g, b, a);
        }
        public static void Normal3(float x, float y, float z)
        {
#if !ANDROID
            DesktopRetainedGeometry.Normal(new Vector3(x, y, z));
#endif
            DesktopGL.Normal3(x, y, z);
        }
        public static void TexCoord2(float s, float t)
        {
#if !ANDROID
            DesktopRetainedGeometry.Texcoord(new Vector3(s, t, 0));
#endif
            DesktopGL.TexCoord2(s, t);
        }
        public static void TexCoord3(float s, float t, float r)
        {
#if !ANDROID
            DesktopRetainedGeometry.Texcoord(new Vector3(s, t, r));
#endif
            DesktopGL.TexCoord3(s, t, r);
        }
        public static void TexCoord3(Vector3 value)
        {
#if !ANDROID
            DesktopRetainedGeometry.Texcoord(value);
#endif
            DesktopGL.TexCoord3(value);
        }
        public static void MultiTexCoord2(TextureUnit unit, float s, float t)
        {
#if !ANDROID
            if (unit == TextureUnit.Texture0)
                DesktopRetainedGeometry.Texcoord(new Vector3(s, t, 0));
            else
                DesktopRetainedGeometry.RejectCurrentList();
#endif
            DesktopGL.MultiTexCoord2(unit, s, t);
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
            _bindingCache?.Invalidate();
            CapabilityCache.BeginList(list, mode);
#if !ANDROID
            DesktopRetainedGeometry.NewList(list, mode);
#endif
            DesktopGL.NewList(list, mode);
        }
        public static void EndList()
        {
            DesktopGL.EndList();
            _bindingCache?.Invalidate();
#if !ANDROID
            DesktopRetainedGeometry.EndList();
#endif
            CapabilityCache.EndList();
        }
        public static void CallList(int list)
        {
            if (_bindingCache != null && _bindingCache.Active
                && (_capabilityCache == null
                    || !_capabilityCache.IsGeometryOnlyList(list)))
                _bindingCache.Invalidate();
            _capabilityCache?.CallList(list);
#if !ANDROID
            if (DesktopRetainedGeometry.TryDraw(list))
            {
                OpenGlSubmissionProfiler.NoteVboDraw();
                return;
            }
#endif
            OpenGlSubmissionProfiler.NoteDisplayList();
            DesktopGL.CallList(list);
        }
        public static void DeleteLists(int list, int range)
        {
            _bindingCache?.Invalidate();
            DesktopGL.DeleteLists(list, range);
#if !ANDROID
            DesktopRetainedGeometry.DeleteLists(list, range);
#endif
            _capabilityCache?.DeleteLists(list, range);
        }

        public static int GenTexture() => DesktopGL.GenTexture();
        public static void DeleteTexture(int texture)
        {
            _bindingCache?.DeleteTexture(texture);
            DesktopGL.DeleteTexture(texture);
        }
        public static bool IsTexture(int texture) => DesktopGL.IsTexture(texture);
        public static void BindTexture(TextureTarget target, int texture)
        {
            OpenGlSubmissionProfiler.NoteTextureBind();
            if (_bindingCache != null && !_bindingCache.ShouldBindTexture(target, texture))
            {
                OpenGlSubmissionProfiler.NoteTextureBindElided();
                return;
            }
            DesktopGL.BindTexture(target, texture);
        }
        public static void ActiveTexture(TextureUnit unit)
        {
            if (_bindingCache != null && !_bindingCache.ShouldActivateTexture(unit))
            {
                OpenGlSubmissionProfiler.NoteTextureUnitElided();
                return;
            }
            DesktopGL.ActiveTexture(unit);
        }
        public static void TexParameter(TextureTarget target, TextureParameterName name, int value)
        { DesktopGL.TexParameter(target, name, value); }
        public static void TexEnv(TextureEnvTarget target, TextureEnvParameter name, int value)
        { DesktopGL.TexEnv(target, name, value); }
        public static void GenerateMipmap(GenerateMipmapTarget target)
        { DesktopGL.GenerateMipmap(target); }
        public static void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels)
        {
            DesktopGL.TexImage2D(target, level, internalFormat, width, height, border, format, type, pixels);
        }
        public static void TexImage2D<T>(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            DesktopGL.TexImage2D(target, level, internalFormat, width, height, border, format, type, pixels);
        }
        public static void TexSubImage2D<T>(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            DesktopGL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels);
        }
        public static void TexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, IntPtr pixels)
        {
            DesktopGL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels);
        }
        public static void CopyTexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int x, int y, int width, int height)
        {
            DesktopGL.CopyTexSubImage2D(target, level, xoffset, yoffset, x, y, width, height);
        }
        public static void GetTexLevelParameter(TextureTarget target, int level, GetTextureParameter name,
            out int value)
        {
            DesktopGL.GetTexLevelParameter(target, level, name, out value);
        }

        public static int CreateShader(ShaderType type) => DesktopGL.CreateShader(type);
        public static void ShaderSource(int shader, string source) { DesktopGL.ShaderSource(shader, source); }
        public static void CompileShader(int shader) { DesktopGL.CompileShader(shader); }
        public static void GetShader(int shader, ShaderParameter name, out int value)
        { DesktopGL.GetShader(shader, name, out value); }
        public static string GetShaderInfoLog(int shader) => DesktopGL.GetShaderInfoLog(shader);
        public static void DeleteShader(int shader) { DesktopGL.DeleteShader(shader); }
        public static int CreateProgram() => DesktopGL.CreateProgram();
        public static void DeleteProgram(int program)
        {
            {
                DesktopGL.DeleteProgram(program);
#if !ANDROID
                _legacyUniforms.InvalidateAll();
#endif
            }
        }
        public static void AttachShader(int program, int shader) { DesktopGL.AttachShader(program, shader); }
        public static void DetachShader(int program, int shader) { DesktopGL.DetachShader(program, shader); }
        public static void LinkProgram(int program)
        {
            {
                DesktopGL.LinkProgram(program);
#if !ANDROID
                // Relinking can replace every uniform value/location contract.
                _legacyUniforms.InvalidateAll();
#endif
            }
        }
        public static void UseProgram(int program)
        {
            {
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
        {
            {
#if !ANDROID
                if (!_legacyUniforms.Submit(location, value)) return;
#endif
                DesktopGL.Uniform1(location, value);
            }
        }
        public static void Uniform1(int location, float value)
        {
            {
#if !ANDROID
                if (!_legacyUniforms.Submit(location, value)) return;
#endif
                DesktopGL.Uniform1(location, value);
            }
        }
        public static void Uniform1(int location, int count, float[] value)
        {
            {
#if !ANDROID
                _legacyUniforms.NoteUncachedWrite();
#endif
                DesktopGL.Uniform1(location, count, value);
            }
        }
        public static void Uniform2(int location, float x, float y)
        {
            {
#if !ANDROID
                if (!_legacyUniforms.Submit2(location, x, y)) return;
#endif
                DesktopGL.Uniform2(location, x, y);
            }
        }
        public static void Uniform3(int location, Vector3 value)
        {
            {
#if !ANDROID
                if (!_legacyUniforms.Submit3(location, value)) return;
#endif
                DesktopGL.Uniform3(location, value);
            }
        }
        public static void Uniform3(int location, int count, float[] value)
        {
            {
#if !ANDROID
                _legacyUniforms.NoteUncachedWrite();
#endif
                DesktopGL.Uniform3(location, count, value);
            }
        }
        public static void Uniform4(int location, Vector4 value)
        {
            {
#if !ANDROID
                if (!_legacyUniforms.Submit4(location, value)) return;
#endif
                DesktopGL.Uniform4(location, value);
            }
        }
        public static void Uniform4(int location, ref Vector4 value)
        {
            {
#if !ANDROID
                if (!_legacyUniforms.Submit4(location, value)) return;
#endif
                DesktopGL.Uniform4(location, ref value);
            }
        }
        public static void Uniform4(int location, float x, float y, float z, float w)
        {
            {
#if !ANDROID
                var value = new Vector4(x, y, z, w);
                if (!_legacyUniforms.Submit4(location, value)) return;
#endif
                DesktopGL.Uniform4(location, x, y, z, w);
            }
        }
        public static void Uniform4(int location, int x, int y, int z, int w)
        {
            {
#if !ANDROID
                if (!_legacyUniforms.Submit4(location, x, y, z, w)) return;
#endif
                DesktopGL.Uniform4(location, x, y, z, w);
            }
        }
        public static void UniformMatrix4(int location, bool transpose, ref Matrix4 value)
        {
            {
#if !ANDROID
                if (!_legacyUniforms.SubmitMatrix4(location, transpose, in value)) return;
#endif
                DesktopGL.UniformMatrix4(location, transpose, ref value);
            }
        }
        public static void UniformMatrix4(int location, int count, bool transpose, float[] value)
        {
            {
#if !ANDROID
                _legacyUniforms.NoteUncachedWrite();
#endif
                DesktopGL.UniformMatrix4(location, count, transpose, value);
            }
        }

        public static int GenFramebuffer() => DesktopGL.GenFramebuffer();
        public static void DeleteFramebuffer(int framebuffer) { DesktopGL.DeleteFramebuffer(framebuffer); }
        public static void BindFramebuffer(FramebufferTarget target, int framebuffer)
        {
            OpenGlSubmissionProfiler.NoteFramebufferBind();
            DesktopGL.BindFramebuffer(target, framebuffer);
        }
        public static bool IsFramebuffer(int framebuffer) => DesktopGL.IsFramebuffer(framebuffer);
        public static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment,
            TextureTarget textureTarget, int texture, int level)
        { DesktopGL.FramebufferTexture2D(target, attachment, textureTarget, texture, level); }
        public static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target)
            => DesktopGL.CheckFramebufferStatus(target);
        public static int GenRenderbuffer() => DesktopGL.GenRenderbuffer();
        public static void DeleteRenderbuffer(int renderbuffer) { DesktopGL.DeleteRenderbuffer(renderbuffer); }
        public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer) { DesktopGL.BindRenderbuffer(target, renderbuffer); }
        public static void RenderbufferStorage(RenderbufferTarget target, RenderbufferStorage format,
            int width, int height) { DesktopGL.RenderbufferStorage(target, format, width, height); }
        public static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderbufferTarget, int renderbuffer)
        { DesktopGL.FramebufferRenderbuffer(target, attachment, renderbufferTarget, renderbuffer); }
        public static void GetFramebufferAttachmentParameter(FramebufferTarget target,
            FramebufferAttachment attachment, FramebufferParameterName name, out int value)
        { DesktopGL.GetFramebufferAttachmentParameter(target, attachment, name, out value); }
        public static void BlitFramebuffer(int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            ClearBufferMask mask, BlitFramebufferFilter filter)
        {
            DesktopGL.BlitFramebuffer(sourceX0, sourceY0, sourceX1, sourceY1,
                destinationX0, destinationY0, destinationX1, destinationY1, mask, filter);
        }

        public static void Enable(EnableCap cap)
        {
            OpenGlSubmissionProfiler.NoteStateRequest();
            if (_capabilityCache != null && !_capabilityCache.ShouldSubmit(cap, true))
            {
                OpenGlSubmissionProfiler.NoteStateRequestSkipped();
                return;
            }
            DesktopGL.Enable(cap);
        }
        public static void Disable(EnableCap cap)
        {
            OpenGlSubmissionProfiler.NoteStateRequest();
            if (_capabilityCache != null && !_capabilityCache.ShouldSubmit(cap, false))
            {
                OpenGlSubmissionProfiler.NoteStateRequestSkipped();
                return;
            }
            DesktopGL.Disable(cap);
        }
        public static bool IsEnabled(EnableCap cap) => DesktopGL.IsEnabled(cap);
        public static void AlphaFunc(AlphaFunction function, float reference) { DesktopGL.AlphaFunc(function, reference); }
        public static void PolygonMode(TriangleFace face, OpenTK.Graphics.OpenGL.PolygonMode mode)
        {
            if (_bindingCache != null && !_bindingCache.ShouldPolygonMode(face, mode))
            {
                OpenGlSubmissionProfiler.NoteRasterStateElided();
                return;
            }
            DesktopGL.PolygonMode(face, mode);
        }
        public static void LineWidth(float width)
        {
            if (_bindingCache != null && !_bindingCache.ShouldLineWidth(width))
            {
                OpenGlSubmissionProfiler.NoteRasterStateElided();
                return;
            }
            DesktopGL.LineWidth(width);
        }
        public static void Clear(ClearBufferMask mask) { DesktopGL.Clear(mask); }
        public static void ClearColor(Color4 color) { DesktopGL.ClearColor(color); }
        public static void ClearColor(float red, float green, float blue, float alpha) { DesktopGL.ClearColor(red, green, blue, alpha); }
        public static void ClearStencil(int value) { DesktopGL.ClearStencil(value); }
        public static void ColorMask(bool red, bool green, bool blue, bool alpha) { DesktopGL.ColorMask(red, green, blue, alpha); }
        public static void DepthMask(bool enabled) { DesktopGL.DepthMask(enabled); }
        public static void DepthFunc(DepthFunction function) { DesktopGL.DepthFunc(function); }
        public static void CullFace(TriangleFace face)
        {
            if (_bindingCache != null && !_bindingCache.ShouldCullFace(face))
            {
                OpenGlSubmissionProfiler.NoteRasterStateElided();
                return;
            }
            DesktopGL.CullFace(face);
        }
        public static void BlendFunc(BlendingFactor source, BlendingFactor destination) { DesktopGL.BlendFunc(source, destination); }
        public static void BlendEquation(BlendEquationMode mode) { DesktopGL.BlendEquation(mode); }
        public static void StencilFunc(StencilFunction function, int reference, int mask)
        {
            OpenGlSubmissionProfiler.NoteStencilFunc();
            DesktopGL.StencilFunc(function, reference, mask);
        }
        public static void StencilOp(StencilOp fail, StencilOp zfail, StencilOp zpass) { DesktopGL.StencilOp(fail, zfail, zpass); }
        public static void StencilMask(int mask) { DesktopGL.StencilMask(mask); }
        public static void PolygonOffset(float factor, float units) { DesktopGL.PolygonOffset(factor, units); }
        public static void Viewport(int x, int y, int width, int height) { DesktopGL.Viewport(x, y, width, height); }
        public static void Scissor(int x, int y, int width, int height) { DesktopGL.Scissor(x, y, width, height); }
        public static void PixelStore(PixelStoreParameter name, int value) { DesktopGL.PixelStore(name, value); }
        public static void ReadBuffer(ReadBufferMode mode) { DesktopGL.ReadBuffer(mode); }
        public static void DrawBuffer(DrawBufferMode mode) { DesktopGL.DrawBuffer(mode); }
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

        public static void MatrixMode(MatrixMode mode) { DesktopGL.MatrixMode(mode); }
        public static void PushMatrix() { DesktopGL.PushMatrix(); }
        public static void PopMatrix() { DesktopGL.PopMatrix(); }
        public static void LoadIdentity() { DesktopGL.LoadIdentity(); }
        public static void PushAttrib(AttribMask mask)
        {
            _capabilityCache?.Invalidate();
            _bindingCache?.Invalidate();
            DesktopGL.PushAttrib(mask);
        }
        public static void PopAttrib()
        {
            DesktopGL.PopAttrib();
            _capabilityCache?.Invalidate();
            _bindingCache?.Invalidate();
        }
    }
}
#endif
