#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using DesktopGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Desktop compatibility front-end for the historical GL call surface.
    /// OpenGL remains the fallback; an explicitly initialized modern renderer
    /// consumes the same calls through <see cref="ModernGraphicsCompat"/>.
    /// </summary>
    internal static class GraphicsApi
    {
        private static bool Modern => ModernGraphicsCompat.Active;

        public static void LoadBindings(IBindingsContext context)
        {
            if (!Modern) DesktopGL.LoadBindings(context);
        }

        public static void Begin(PrimitiveType mode) { if (Modern) ModernGraphicsCompat.Begin(mode); else DesktopGL.Begin(mode); }
        public static void End() { if (Modern) ModernGraphicsCompat.End(); else DesktopGL.End(); }
        public static void Vertex2(float x, float y) { if (Modern) ModernGraphicsCompat.Vertex2(x, y); else DesktopGL.Vertex2(x, y); }
        public static void Vertex3(float x, float y, float z) { if (Modern) ModernGraphicsCompat.Vertex3(x, y, z); else DesktopGL.Vertex3(x, y, z); }
        public static void Vertex3(Vector3 value) { if (Modern) ModernGraphicsCompat.Vertex3(value); else DesktopGL.Vertex3(value); }
        public static void Color3(float r, float g, float b) { if (Modern) ModernGraphicsCompat.Color3(r, g, b); else DesktopGL.Color3(r, g, b); }
        public static void Color3(Vector3 value) { if (Modern) ModernGraphicsCompat.Color3(value); else DesktopGL.Color3(value); }
        public static void Color4(float r, float g, float b, float a) { if (Modern) ModernGraphicsCompat.Color4(r, g, b, a); else DesktopGL.Color4(r, g, b, a); }
        public static void Normal3(float x, float y, float z) { if (Modern) ModernGraphicsCompat.Normal3(x, y, z); else DesktopGL.Normal3(x, y, z); }
        public static void TexCoord2(float s, float t) { if (Modern) ModernGraphicsCompat.TexCoord2(s, t); else DesktopGL.TexCoord2(s, t); }
        public static void TexCoord3(float s, float t, float r) { if (Modern) ModernGraphicsCompat.TexCoord3(s, t, r); else DesktopGL.TexCoord3(s, t, r); }
        public static void TexCoord3(Vector3 value) { if (Modern) ModernGraphicsCompat.TexCoord3(value); else DesktopGL.TexCoord3(value); }
        public static void MultiTexCoord2(TextureUnit unit, float s, float t) { if (Modern) ModernGraphicsCompat.MultiTexCoord2(unit, s, t); else DesktopGL.MultiTexCoord2(unit, s, t); }

        public static int GenLists(int range) => Modern ? ModernGraphicsCompat.GenLists(range) : DesktopGL.GenLists(range);
        public static void NewList(int list, ListMode mode) { if (Modern) ModernGraphicsCompat.NewList(list, mode); else DesktopGL.NewList(list, mode); }
        public static void EndList() { if (Modern) ModernGraphicsCompat.EndList(); else DesktopGL.EndList(); }
        public static void CallList(int list) { if (Modern) ModernGraphicsCompat.CallList(list); else DesktopGL.CallList(list); }
        public static void DeleteLists(int list, int range) { if (Modern) ModernGraphicsCompat.DeleteLists(list, range); else DesktopGL.DeleteLists(list, range); }

        public static int GenTexture() => Modern ? ModernGraphicsCompat.GenTexture() : DesktopGL.GenTexture();
        public static void DeleteTexture(int texture) { if (Modern) ModernGraphicsCompat.DeleteTexture(texture); else DesktopGL.DeleteTexture(texture); }
        public static bool IsTexture(int texture) => Modern ? ModernGraphicsCompat.IsTexture(texture) : DesktopGL.IsTexture(texture);
        public static void BindTexture(TextureTarget target, int texture) { if (Modern) ModernGraphicsCompat.BindTexture(target, texture); else DesktopGL.BindTexture(target, texture); }
        public static void ActiveTexture(TextureUnit unit) { if (Modern) ModernGraphicsCompat.ActiveTexture(unit); else DesktopGL.ActiveTexture(unit); }
        public static void TexParameter(TextureTarget target, TextureParameterName name, int value)
        { if (Modern) ModernGraphicsCompat.TexParameter(target, name, value); else DesktopGL.TexParameter(target, name, value); }
        public static void TexEnv(TextureEnvTarget target, TextureEnvParameter name, int value)
        { if (Modern) ModernGraphicsCompat.TexEnv(target, name, value); else DesktopGL.TexEnv(target, name, value); }
        public static void GenerateMipmap(GenerateMipmapTarget target)
        { if (Modern) ModernGraphicsCompat.GenerateMipmap(target); else DesktopGL.GenerateMipmap(target); }
        public static void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, IntPtr pixels)
        {
            if (Modern) ModernGraphicsCompat.TexImage2D(target, internalFormat, width, height, format, type, pixels);
            else DesktopGL.TexImage2D(target, level, internalFormat, width, height, border, format, type, pixels);
        }
        public static void TexImage2D<T>(TextureTarget target, int level, PixelInternalFormat internalFormat,
            int width, int height, int border, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            if (Modern) ModernGraphicsCompat.TexImage2D(target, internalFormat, width, height, format, type, pixels);
            else DesktopGL.TexImage2D(target, level, internalFormat, width, height, border, format, type, pixels);
        }
        public static void TexSubImage2D<T>(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            if (Modern) ModernGraphicsCompat.TexSubImage2D(target, xoffset, yoffset, width, height, format, type, pixels);
            else DesktopGL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels);
        }
        public static void TexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int width, int height, PixelFormat format, PixelType type, IntPtr pixels)
        {
            if (Modern) ModernGraphicsCompat.TexSubImage2D(target, xoffset, yoffset, width, height, format, type, pixels);
            else DesktopGL.TexSubImage2D(target, level, xoffset, yoffset, width, height, format, type, pixels);
        }
        public static void CopyTexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset,
            int x, int y, int width, int height)
        {
            if (Modern) ModernGraphicsCompat.CopyTexSubImage2D(target, level, xoffset, yoffset, x, y, width, height);
            else DesktopGL.CopyTexSubImage2D(target, level, xoffset, yoffset, x, y, width, height);
        }
        public static void GetTexLevelParameter(TextureTarget target, int level, GetTextureParameter name,
            out int value)
        {
            if (Modern) ModernGraphicsCompat.GetTexLevelParameter(target, level, name, out value);
            else DesktopGL.GetTexLevelParameter(target, level, name, out value);
        }

        public static int CreateShader(ShaderType type) => Modern ? ModernGraphicsCompat.CreateShader(type) : DesktopGL.CreateShader(type);
        public static void ShaderSource(int shader, string source) { if (Modern) ModernGraphicsCompat.ShaderSource(shader, source); else DesktopGL.ShaderSource(shader, source); }
        public static void CompileShader(int shader) { if (Modern) ModernGraphicsCompat.CompileShader(shader); else DesktopGL.CompileShader(shader); }
        public static void GetShader(int shader, ShaderParameter name, out int value)
        { if (Modern) ModernGraphicsCompat.GetShader(shader, name, out value); else DesktopGL.GetShader(shader, name, out value); }
        public static string GetShaderInfoLog(int shader) => Modern ? ModernGraphicsCompat.GetShaderInfoLog(shader) : DesktopGL.GetShaderInfoLog(shader);
        public static void DeleteShader(int shader) { if (Modern) ModernGraphicsCompat.DeleteShader(shader); else DesktopGL.DeleteShader(shader); }
        public static int CreateProgram() => Modern ? ModernGraphicsCompat.CreateProgram() : DesktopGL.CreateProgram();
        public static void DeleteProgram(int program) { if (Modern) ModernGraphicsCompat.DeleteProgram(program); else DesktopGL.DeleteProgram(program); }
        public static void AttachShader(int program, int shader) { if (Modern) ModernGraphicsCompat.AttachShader(program, shader); else DesktopGL.AttachShader(program, shader); }
        public static void DetachShader(int program, int shader) { if (Modern) ModernGraphicsCompat.DetachShader(program, shader); else DesktopGL.DetachShader(program, shader); }
        public static void LinkProgram(int program) { if (Modern) ModernGraphicsCompat.LinkProgram(program); else DesktopGL.LinkProgram(program); }
        public static void UseProgram(int program) { if (Modern) ModernGraphicsCompat.UseProgram(program); else DesktopGL.UseProgram(program); }
        public static void GetProgram(int program, GetProgramParameterName name, out int value)
        { if (Modern) ModernGraphicsCompat.GetProgram(program, name, out value); else DesktopGL.GetProgram(program, name, out value); }
        public static string GetProgramInfoLog(int program) => Modern ? ModernGraphicsCompat.GetProgramInfoLog(program) : DesktopGL.GetProgramInfoLog(program);
        public static int GetUniformLocation(int program, string name) => Modern ? ModernGraphicsCompat.GetUniformLocation(program, name) : DesktopGL.GetUniformLocation(program, name);
        public static void GetUniform(int program, int location, out int value)
        { if (Modern) ModernGraphicsCompat.GetUniform(program, location, out value); else DesktopGL.GetUniform(program, location, out value); }

        public static void Uniform1(int location, int value) { if (Modern) ModernGraphicsCompat.Uniform1(location, value); else DesktopGL.Uniform1(location, value); }
        public static void Uniform1(int location, float value) { if (Modern) ModernGraphicsCompat.Uniform1(location, value); else DesktopGL.Uniform1(location, value); }
        public static void Uniform1(int location, int count, float[] value) { if (Modern) ModernGraphicsCompat.Uniform1(location, count, value); else DesktopGL.Uniform1(location, count, value); }
        public static void Uniform2(int location, float x, float y) { if (Modern) ModernGraphicsCompat.Uniform2(location, x, y); else DesktopGL.Uniform2(location, x, y); }
        public static void Uniform3(int location, Vector3 value) { if (Modern) ModernGraphicsCompat.Uniform3(location, value); else DesktopGL.Uniform3(location, value); }
        public static void Uniform3(int location, int count, float[] value) { if (Modern) ModernGraphicsCompat.Uniform3(location, count, value); else DesktopGL.Uniform3(location, count, value); }
        public static void Uniform4(int location, Vector4 value) { if (Modern) ModernGraphicsCompat.Uniform4(location, value); else DesktopGL.Uniform4(location, value); }
        public static void Uniform4(int location, ref Vector4 value) { if (Modern) ModernGraphicsCompat.Uniform4(location, ref value); else DesktopGL.Uniform4(location, ref value); }
        public static void Uniform4(int location, float x, float y, float z, float w) { if (Modern) ModernGraphicsCompat.Uniform4(location, x, y, z, w); else DesktopGL.Uniform4(location, x, y, z, w); }
        public static void Uniform4(int location, int x, int y, int z, int w) { if (Modern) ModernGraphicsCompat.Uniform4(location, x, y, z, w); else DesktopGL.Uniform4(location, x, y, z, w); }
        public static void UniformMatrix4(int location, bool transpose, ref Matrix4 value)
        { if (Modern) ModernGraphicsCompat.UniformMatrix4(location, transpose, ref value); else DesktopGL.UniformMatrix4(location, transpose, ref value); }
        public static void UniformMatrix4(int location, int count, bool transpose, float[] value)
        { if (Modern) ModernGraphicsCompat.UniformMatrix4(location, count, transpose, value); else DesktopGL.UniformMatrix4(location, count, transpose, value); }

        public static int GenFramebuffer() => Modern ? ModernGraphicsCompat.GenFramebuffer() : DesktopGL.GenFramebuffer();
        public static void DeleteFramebuffer(int framebuffer) { if (Modern) ModernGraphicsCompat.DeleteFramebuffer(framebuffer); else DesktopGL.DeleteFramebuffer(framebuffer); }
        public static void BindFramebuffer(FramebufferTarget target, int framebuffer) { if (Modern) ModernGraphicsCompat.BindFramebuffer(target, framebuffer); else DesktopGL.BindFramebuffer(target, framebuffer); }
        public static bool IsFramebuffer(int framebuffer) => Modern ? ModernGraphicsCompat.IsFramebuffer(framebuffer) : DesktopGL.IsFramebuffer(framebuffer);
        public static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment,
            TextureTarget textureTarget, int texture, int level)
        { if (Modern) ModernGraphicsCompat.FramebufferTexture2D(target, attachment, textureTarget, texture, level); else DesktopGL.FramebufferTexture2D(target, attachment, textureTarget, texture, level); }
        public static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target)
            => Modern ? ModernGraphicsCompat.CheckFramebufferStatus(target) : DesktopGL.CheckFramebufferStatus(target);
        public static int GenRenderbuffer() => Modern ? ModernGraphicsCompat.GenRenderbuffer() : DesktopGL.GenRenderbuffer();
        public static void DeleteRenderbuffer(int renderbuffer) { if (Modern) ModernGraphicsCompat.DeleteRenderbuffer(renderbuffer); else DesktopGL.DeleteRenderbuffer(renderbuffer); }
        public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer) { if (Modern) ModernGraphicsCompat.BindRenderbuffer(target, renderbuffer); else DesktopGL.BindRenderbuffer(target, renderbuffer); }
        public static void RenderbufferStorage(RenderbufferTarget target, RenderbufferStorage format,
            int width, int height) { if (Modern) ModernGraphicsCompat.RenderbufferStorage(target, format, width, height); else DesktopGL.RenderbufferStorage(target, format, width, height); }
        public static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
            RenderbufferTarget renderbufferTarget, int renderbuffer)
        { if (Modern) ModernGraphicsCompat.FramebufferRenderbuffer(target, attachment, renderbufferTarget, renderbuffer); else DesktopGL.FramebufferRenderbuffer(target, attachment, renderbufferTarget, renderbuffer); }
        public static void GetFramebufferAttachmentParameter(FramebufferTarget target,
            FramebufferAttachment attachment, FramebufferParameterName name, out int value)
        { if (Modern) ModernGraphicsCompat.GetFramebufferAttachmentParameter(target, attachment, name, out value); else DesktopGL.GetFramebufferAttachmentParameter(target, attachment, name, out value); }
        public static void BlitFramebuffer(int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            ClearBufferMask mask, BlitFramebufferFilter filter)
        {
            if (Modern) ModernGraphicsCompat.BlitFramebuffer(sourceX0, sourceY0, sourceX1, sourceY1,
                destinationX0, destinationY0, destinationX1, destinationY1, mask, filter);
            else DesktopGL.BlitFramebuffer(sourceX0, sourceY0, sourceX1, sourceY1,
                destinationX0, destinationY0, destinationX1, destinationY1, mask, filter);
        }

        public static void Enable(EnableCap cap) { if (Modern) ModernGraphicsCompat.Enable(cap); else DesktopGL.Enable(cap); }
        public static void Disable(EnableCap cap) { if (Modern) ModernGraphicsCompat.Disable(cap); else DesktopGL.Disable(cap); }
        public static bool IsEnabled(EnableCap cap) => Modern ? ModernGraphicsCompat.IsEnabled(cap) : DesktopGL.IsEnabled(cap);
        public static void AlphaFunc(AlphaFunction function, float reference) { if (Modern) ModernGraphicsCompat.AlphaFunc(function, reference); else DesktopGL.AlphaFunc(function, reference); }
        public static void PolygonMode(TriangleFace face, OpenTK.Graphics.OpenGL.PolygonMode mode)
        { if (Modern) ModernGraphicsCompat.PolygonMode(face, mode); else DesktopGL.PolygonMode((MaterialFace)(int)face, mode); }
        public static void LineWidth(float width) { if (Modern) ModernGraphicsCompat.LineWidth(width); else DesktopGL.LineWidth(width); }
        public static void Clear(ClearBufferMask mask) { if (Modern) ModernGraphicsCompat.Clear(mask); else DesktopGL.Clear(mask); }
        public static void ClearColor(Color4 color) { if (Modern) ModernGraphicsCompat.ClearColor(color); else DesktopGL.ClearColor(color); }
        public static void ClearColor(float red, float green, float blue, float alpha) { if (Modern) ModernGraphicsCompat.ClearColor(red, green, blue, alpha); else DesktopGL.ClearColor(red, green, blue, alpha); }
        public static void ClearStencil(int value) { if (Modern) ModernGraphicsCompat.ClearStencil(value); else DesktopGL.ClearStencil(value); }
        public static void ColorMask(bool red, bool green, bool blue, bool alpha) { if (Modern) ModernGraphicsCompat.ColorMask(red, green, blue, alpha); else DesktopGL.ColorMask(red, green, blue, alpha); }
        public static void DepthMask(bool enabled) { if (Modern) ModernGraphicsCompat.DepthMask(enabled); else DesktopGL.DepthMask(enabled); }
        public static void DepthFunc(DepthFunction function) { if (Modern) ModernGraphicsCompat.DepthFunc(function); else DesktopGL.DepthFunc(function); }
        public static void CullFace(TriangleFace face) { if (Modern) ModernGraphicsCompat.CullFace(face); else DesktopGL.CullFace(face); }
        public static void BlendFunc(BlendingFactor source, BlendingFactor destination) { if (Modern) ModernGraphicsCompat.BlendFunc(source, destination); else DesktopGL.BlendFunc(source, destination); }
        public static void BlendEquation(BlendEquationMode mode) { if (Modern) ModernGraphicsCompat.BlendEquation(mode); else DesktopGL.BlendEquation(mode); }
        public static void StencilFunc(StencilFunction function, int reference, int mask) { if (Modern) ModernGraphicsCompat.StencilFunc(function, reference, mask); else DesktopGL.StencilFunc(function, reference, mask); }
        public static void StencilOp(StencilOp fail, StencilOp zfail, StencilOp zpass) { if (Modern) ModernGraphicsCompat.StencilOp(fail, zfail, zpass); else DesktopGL.StencilOp(fail, zfail, zpass); }
        public static void StencilMask(int mask) { if (Modern) ModernGraphicsCompat.StencilMask(mask); else DesktopGL.StencilMask(mask); }
        public static void PolygonOffset(float factor, float units) { if (Modern) ModernGraphicsCompat.PolygonOffset(factor, units); else DesktopGL.PolygonOffset(factor, units); }
        public static void Viewport(int x, int y, int width, int height) { if (Modern) ModernGraphicsCompat.Viewport(x, y, width, height); else DesktopGL.Viewport(x, y, width, height); }
        public static void Scissor(int x, int y, int width, int height) { if (Modern) ModernGraphicsCompat.Scissor(x, y, width, height); else DesktopGL.Scissor(x, y, width, height); }
        public static void PixelStore(PixelStoreParameter name, int value) { if (Modern) ModernGraphicsCompat.PixelStore(name, value); else DesktopGL.PixelStore(name, value); }
        public static void ReadBuffer(ReadBufferMode mode) { if (Modern) ModernGraphicsCompat.ReadBuffer(mode); else DesktopGL.ReadBuffer(mode); }
        public static void DrawBuffer(DrawBufferMode mode) { if (Modern) ModernGraphicsCompat.DrawBuffer(mode); else DesktopGL.DrawBuffer(mode); }
        public static void Finish() { if (Modern) ModernGraphicsCompat.Finish(); else DesktopGL.Finish(); }
        public static ErrorCode GetError() => Modern ? ModernGraphicsCompat.GetError() : DesktopGL.GetError();
        public static string GetString(StringName name) => Modern ? ModernGraphicsCompat.GetString(name) : DesktopGL.GetString(name);
        public static int GetInteger(GetPName name) => Modern ? ModernGraphicsCompat.GetInteger(name) : DesktopGL.GetInteger(name);
        public static void GetInteger(GetPName name, out int value)
        { if (Modern) ModernGraphicsCompat.GetInteger(name, out value); else DesktopGL.GetInteger(name, out value); }
        public static void GetInteger(GetPName name, int[] values)
        { if (Modern) ModernGraphicsCompat.GetInteger(name, values); else DesktopGL.GetInteger(name, values); }
        public static void DebugMessageCallback(DebugProc callback, IntPtr userParam)
        { if (Modern) ModernGraphicsCompat.DebugMessageCallback(callback, userParam); else DesktopGL.DebugMessageCallback(callback, userParam); }

        public static void ReadPixels<T>(int x, int y, int width, int height, PixelFormat format,
            PixelType type, T[] pixels) where T : struct
        {
            if (Modern) ModernGraphicsCompat.ReadPixels(x, y, width, height, format, type, pixels);
            else DesktopGL.ReadPixels(x, y, width, height, format, type, pixels);
        }

        public static void MatrixMode(MatrixMode mode) { if (Modern) ModernGraphicsCompat.MatrixMode(mode); else DesktopGL.MatrixMode(mode); }
        public static void PushMatrix() { if (Modern) ModernGraphicsCompat.PushMatrix(); else DesktopGL.PushMatrix(); }
        public static void PopMatrix() { if (Modern) ModernGraphicsCompat.PopMatrix(); else DesktopGL.PopMatrix(); }
        public static void LoadIdentity() { if (Modern) ModernGraphicsCompat.LoadIdentity(); else DesktopGL.LoadIdentity(); }
        public static void PushAttrib(AttribMask mask) { if (Modern) ModernGraphicsCompat.PushAttrib(mask); else DesktopGL.PushAttrib(mask); }
        public static void PopAttrib() { if (Modern) ModernGraphicsCompat.PopAttrib(); else DesktopGL.PopAttrib(); }
    }
}
#endif
