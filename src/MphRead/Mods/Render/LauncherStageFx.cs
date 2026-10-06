#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Cheap under-Hunter stage dressing for the RmlUi proof.
    ///
    /// Everything here is deliberately presentation-only: one tiny procedural
    /// radial texture, three quads, no readback, no scene mutation. The pass is
    /// inserted after the cinematic photograph and before the preview model so
    /// the Hunter receives a real floor contact cue and a light field that is
    /// physically behind its silhouette instead of a translucent UI wash drawn
    /// over it.
    /// </summary>
    public static class LauncherStageFx
    {
        public static bool Enabled { get; set; }

        private const int TextureSize = 96;
        private static int _radialTexture;

        public static void DrawUnderHunter(int width, int height)
        {
            if (!Enabled || !LauncherHunter.Wanted || width <= 0 || height <= 0)
                return;

            EnsureTexture();

            float left = LauncherHunter.Left;
            float right = LauncherHunter.Right;
            float top = LauncherHunter.Top;
            float bottom = LauncherHunter.Bottom;
            float spanX = Math.Max(0.10f, right - left);
            float spanY = Math.Max(0.10f, bottom - top);
            float centerX = (left + right) * 0.5f + 0.008f;
            float centerY = (top + bottom) * 0.5f - 0.03f;

            GL.UseProgram(0);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.StencilTest);
            GL.Enable(EnableCap.Blend);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, _radialTexture);
            GL.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode,
                (int)TextureEnvMode.Modulate);

            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();

            // Broad cool halo. Additive and intentionally weak: it reads as
            // environmental spill around the silhouette rather than a neon UI
            // circle.
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            DrawRadial(centerX, centerY, spanX * 1.42f, spanY * 1.02f,
                0.18f, 0.56f, 0.96f, 0.16f);
            DrawRadial(centerX + spanX * 0.03f, centerY - spanY * 0.02f,
                spanX * 0.92f, spanY * 0.72f,
                0.26f, 0.72f, 1.00f, 0.08f);

            // A faint reflected pool under the boots helps the character share
            // a floor with the room even though the model itself is rendered by
            // a separate preview pass.
            float floorY = Math.Clamp(bottom - 0.055f, 0.15f, 0.94f);
            DrawRadial(centerX, floorY, spanX * 0.86f, 0.095f,
                0.16f, 0.54f, 0.82f, 0.11f);

            // Contact shadow last so it sits over the floor light and directly
            // under the feet. This is deliberately soft and broad, not a fake
            // hard projected shadow.
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            DrawRadial(centerX, floorY + 0.006f, spanX * 0.66f, 0.060f,
                0.00f, 0.00f, 0.00f, 0.48f);

            GL.Color4(1f, 1f, 1f, 1f);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Disable(EnableCap.Texture2D);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection);
            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.DepthTest);
        }

        public static void Release()
        {
            if (_radialTexture != 0)
            {
                GL.DeleteTexture(_radialTexture);
                _radialTexture = 0;
            }
            Enabled = false;
        }

        internal static void ForgetRendererResources()
        {
            _radialTexture = 0;
            Enabled = false;
        }

        private static unsafe void EnsureTexture()
        {
            if (_radialTexture != 0)
                return;

            byte[] rgba = new byte[TextureSize * TextureSize * 4];
            for (int y = 0; y < TextureSize; y++)
            {
                float ny = (y + 0.5f) / TextureSize * 2f - 1f;
                for (int x = 0; x < TextureSize; x++)
                {
                    float nx = (x + 0.5f) / TextureSize * 2f - 1f;
                    float distance = MathF.Sqrt(nx * nx + ny * ny);
                    float edge = Math.Clamp(1f - distance, 0f, 1f);
                    float alpha = edge * edge * (3f - 2f * edge);
                    int offset = (y * TextureSize + x) * 4;
                    rgba[offset] = 255;
                    rgba[offset + 1] = 255;
                    rgba[offset + 2] = 255;
                    rgba[offset + 3] = (byte)Math.Clamp((int)MathF.Round(alpha * 255f), 0, 255);
                }
            }

            _radialTexture = GL.GenTexture();
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _radialTexture);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            fixed (byte* ptr = rgba)
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                    TextureSize, TextureSize, 0, PixelFormat.Rgba,
                    PixelType.UnsignedByte, (IntPtr)ptr);
            }
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private static void DrawRadial(float centerX, float centerY,
            float width, float height, float r, float g, float b, float a)
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float x0 = centerX - halfW;
            float x1 = centerX + halfW;
            float y0 = centerY - halfH;
            float y1 = centerY + halfH;

            float left = x0 * 2f - 1f;
            float right = x1 * 2f - 1f;
            float top = 1f - y0 * 2f;
            float bottom = 1f - y1 * 2f;

            GL.Color4(r, g, b, a);
            GL.Begin(PrimitiveType.TriangleStrip);
            GL.TexCoord2(1f, 0f); GL.Vertex3(right, top, 0f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(left, top, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3(right, bottom, 0f);
            GL.TexCoord2(0f, 1f); GL.Vertex3(left, bottom, 0f);
            GL.End();
        }
    }
}
#endif
