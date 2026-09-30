#if ANDROID
using System;
using OpenTK.Graphics.OpenGL;
using ES = OpenTK.Graphics.ES30;
namespace MphRead.Mods.Render
{
    internal static partial class GlEs
    {
        public static void Vertex2(float x, float y) => Vertex3(x, y, 0);
        public static void TexCoord2(float s, float t) => TexCoord3(s, t, 0);
        public static void MultiTexCoord2(TextureUnit unit, float s, float t)
        {
            if (unit != TextureUnit.Texture0) throw new NotSupportedException("Only immediate texture coordinate unit zero is supported.");
            TexCoord2(s, t);
        }
        public static bool IsTexture(int texture) => ES.GL.IsTexture(texture);
        public static bool IsFramebuffer(int framebuffer) => ES.GL.IsFramebuffer(framebuffer);
        public static void GetInteger(GetPName name, out int value) => value = GetInteger(name);
        public static void Finish() => ES.GL.Finish();
        public static void GetProgram(int program, GetProgramParameterName name, out int value) =>
            ES.GL.GetProgram(program, (ES.GetProgramParameterName)(int)name, out value);
        public static string GetProgramInfoLog(int program) => ES.GL.GetProgramInfoLog(program);
        public static void TexSubImage2D(TextureTarget target, int level, int x, int y, int width,
            int height, PixelFormat format, PixelType type, IntPtr pixels) =>
            ES.GL.TexSubImage2D((ES.TextureTarget2d)(int)target, level, x, y, width, height,
                (ES.PixelFormat)(int)format, (ES.PixelType)(int)type, pixels);
        public static void DrawBuffer(DrawBufferMode mode) =>
            ES.GL.DrawBuffers(1, new[] { (ES.DrawBufferMode)(int)mode });
        public static void GetTexLevelParameter(TextureTarget target, int level, GetTextureParameter name, out int value)
        {
            // ES 3.0 has no glGetTexLevelParameter; no production Android caller
            // uses this desktop diagnostic query.
            throw new NotSupportedException("Texture-level queries require OpenGL ES 3.1.");
        }
        public static void TexEnv(TextureEnvTarget target, TextureEnvParameter name, int value) { }
        public static void MatrixMode(MatrixMode mode) => throw new NotSupportedException("Use shader matrices on GLES.");
        public static void PushMatrix() => throw new NotSupportedException("Use shader matrices on GLES.");
        public static void PopMatrix() => throw new NotSupportedException("Use shader matrices on GLES.");
        public static void LoadIdentity() => throw new NotSupportedException("Use shader matrices on GLES.");
        public static void PushAttrib(AttribMask mask) => throw new NotSupportedException("Attribute stacks are unavailable on GLES.");
        public static void PopAttrib() => throw new NotSupportedException("Attribute stacks are unavailable on GLES.");
    }
}
#endif
