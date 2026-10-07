#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Presentation-only Menu Stage atmosphere around the real Hunter preview.
    ///
    /// The room photograph is already on screen. This pass adds cues that need
    /// to exist physically below or above the isolated preview model: halo,
    /// low fog, floor bounce, foot contact, dust and a very light foreground
    /// haze that optically ties the Hunter back into the same air as the room.
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
            MenuStageProfile stage = LauncherMenuStage.Current;
            LauncherBackdropStyle style = LauncherMenuVisuals.Style;
            LauncherActivityAmbience activity = LauncherMenuVisuals.Activity;
            LauncherHunterTheme theme = LauncherMenuVisuals.Hunter(LauncherHunter.Hunter);

            float left = LauncherHunter.Left;
            float right = LauncherHunter.Right;
            float top = LauncherHunter.Top;
            float bottom = LauncherHunter.Bottom;
            float spanX = Math.Max(0.10f, right - left);
            float spanY = Math.Max(0.10f, bottom - top);
            float centerX = (left + right) * 0.5f + 0.008f;
            float centerY = (top + bottom) * 0.5f - 0.03f;

            BeginScreenPass();

            // Broad cool halo. It stays behind the model, so the silhouette
            // remains crisp while the room immediately around it gains enough
            // luminance separation to read as an authored hero shot.
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            float atmosphere = Math.Clamp(
                style.Fog * activity.FogBias + stage.Atmosphere * 0.20f,
                0f, 0.42f);
            DrawRadial(centerX, centerY,
                spanX * 1.34f * theme.HaloScale, spanY * 0.98f,
                theme.Halo.R, theme.Halo.G, theme.Halo.B,
                (0.055f + atmosphere * 0.24f) * theme.AccentStrength);
            DrawRadial(centerX + spanX * 0.025f, centerY - spanY * 0.01f,
                spanX * 0.84f, spanY * 0.66f,
                theme.Rim.R, theme.Rim.G, theme.Rim.B,
                (0.025f + atmosphere * 0.11f) * theme.AccentStrength);

            // Slow low fog stays behind the model. It is made from broad alpha
            // fields rather than gameplay particles, so it cannot affect room
            // simulation or turn into combat smoke.
            double time = LauncherPrefs.ReduceMotion ? 0
                : Environment.TickCount64 / 1000.0;
            float fogShift = (float)Math.Sin(time * 0.11) * 0.035f;
            MenuRgb fogColor = MenuRgb.Lerp(activity.Accent,
                new MenuRgb(0.15f, 0.23f, 0.31f), 0.62f);
            DrawRadial(0.50f + fogShift, 0.79f, 0.92f, 0.23f,
                fogColor.R, fogColor.G, fogColor.B, atmosphere * 0.19f);
            DrawRadial(0.68f - fogShift * 0.7f, 0.68f, 0.62f, 0.17f,
                fogColor.R, fogColor.G, fogColor.B, atmosphere * 0.10f);

            // Deterministic dust gives the stage a little life while keeping
            // screenshot comparison stable. Reduce Motion freezes it.
            float dust = Math.Clamp(
                style.Particles * activity.ParticleBias * theme.ParticleScale
                + stage.Dust * 0.12f, 0f, 0.34f);
            for (int i = 0; i < 14; i++)
            {
                float seedX = Hash01(i * 17 + 3);
                float seedY = Hash01(i * 29 + 11);
                float phase = Hash01(i * 43 + 7) * MathF.PI * 2f;
                float dx = LauncherPrefs.ReduceMotion ? 0
                    : MathF.Sin((float)time * (0.07f + Hash01(i + 91) * 0.05f) + phase) * 0.018f;
                float dy = LauncherPrefs.ReduceMotion ? 0
                    : MathF.Cos((float)time * (0.05f + Hash01(i + 53) * 0.04f) + phase) * 0.012f;
                float moteX = 0.30f + seedX * 0.55f + dx;
                float moteY = 0.18f + seedY * 0.58f + dy;
                float size = 0.004f + Hash01(i * 61 + 5) * 0.009f;
                MenuRgb mote = MenuRgb.Lerp(activity.Secondary,
                    theme.Particle, 0.46f);
                DrawRadial(moteX, moteY, size, size * 1.15f,
                    mote.R, mote.G, mote.B,
                    dust * (0.16f + Hash01(i * 13 + 2) * 0.32f));
            }

            // Grounding is shaped around feet rather than one broad ellipse.
            // The screenshot that prompted Slice C showed the old shadow well
            // below Trace's boots, which made the model float despite all the
            // surrounding atmosphere.
            float floorY = Math.Clamp(bottom - GroundInset(LauncherHunter.Hunter),
                0.15f, 0.94f);

            // Reflected floor light first.
            DrawRadial(centerX, floorY - 0.006f, spanX * 0.76f, 0.075f,
                theme.Floor.R, theme.Floor.G, theme.Floor.B,
                0.055f + style.FloorGlow * theme.AccentStrength * 0.075f);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Wide, faint penumbra ties both feet into one floor plane.
            DrawRadial(centerX, floorY + 0.003f, spanX * 0.54f, 0.046f,
                0.00f, 0.00f, 0.00f, 0.24f);

            // Two darker contact cores sit under the actual stance. They are
            // deliberately tiny so they read as weight, not painted circles.
            float spread = spanX * FootSpread(LauncherHunter.Hunter);
            float coreWidth = spanX * 0.17f;
            DrawRadial(centerX - spread, floorY, coreWidth, 0.019f,
                0.00f, 0.00f, 0.00f, 0.44f);
            DrawRadial(centerX + spread, floorY, coreWidth, 0.019f,
                0.00f, 0.00f, 0.00f, 0.44f);

            EndScreenPass();
        }

        /// <summary>
        /// Put a very small amount of the stage atmosphere in front of the
        /// Hunter too. Because this is drawn after the model, it gently pulls
        /// extreme suit saturation/contrast toward the room without changing
        /// the actual skin, recolor, gameplay material, or preview shader.
        /// </summary>
        public static void DrawOverHunter(int width, int height)
        {
            if (!Enabled || !LauncherHunter.Wanted || width <= 0 || height <= 0)
                return;

            MenuStageProfile stage = LauncherMenuStage.Current;
            LauncherBackdropStyle style = LauncherMenuVisuals.Style;
            LauncherActivityAmbience activity = LauncherMenuVisuals.Activity;
            LauncherHunterTheme theme = LauncherMenuVisuals.Hunter(LauncherHunter.Hunter);
            float amount = Math.Clamp(
                stage.ForegroundHaze * 0.55f
                + style.Fog * activity.FogBias * 0.10f,
                0f, 0.10f);
            if (amount <= 0)
                return;

            EnsureTexture();

            float left = LauncherHunter.Left;
            float right = LauncherHunter.Right;
            float top = LauncherHunter.Top;
            float bottom = LauncherHunter.Bottom;
            float spanX = Math.Max(0.10f, right - left);
            float spanY = Math.Max(0.10f, bottom - top);
            float centerX = (left + right) * 0.5f;
            float centerY = (top + bottom) * 0.5f;

            MenuRgb air = MenuRgb.Lerp(activity.Accent, theme.Halo,
                LauncherBackdrop.Scene == LauncherBackdropScene.Adventure ? 0.18f : 0.28f);
            float r = air.R;
            float g = air.G;
            float b = air.B;

            BeginScreenPass();
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Full-body air layer is almost imperceptible; the lower-body veil
            // is stronger because that is where the character meets room haze.
            DrawRadial(centerX, centerY + spanY * 0.04f,
                spanX * 0.86f, spanY * 0.78f, r, g, b, amount * 0.30f);
            DrawRadial(centerX, centerY + spanY * 0.24f,
                spanX * 0.76f, spanY * 0.43f, r, g, b, amount);

            EndScreenPass();
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

        private static float GroundInset(Hunter hunter) => hunter switch
        {
            Hunter.Trace => 0.115f,
            Hunter.Spire => 0.085f,
            Hunter.Kanden => 0.100f,
            Hunter.Weavel => 0.095f,
            Hunter.Noxus => 0.100f,
            Hunter.Sylux => 0.095f,
            _ => 0.100f
        };

        private static float FootSpread(Hunter hunter) => hunter switch
        {
            Hunter.Trace => 0.135f,
            Hunter.Spire => 0.080f,
            Hunter.Kanden => 0.105f,
            Hunter.Weavel => 0.105f,
            Hunter.Noxus => 0.100f,
            Hunter.Sylux => 0.095f,
            _ => 0.095f
        };

        private static void BeginScreenPass()
        {
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
        }

        private static void EndScreenPass()
        {
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
                    rgba[offset + 3] = (byte)Math.Clamp(
                        (int)MathF.Round(alpha * 255f), 0, 255);
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

        private static float Hash01(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (x & 0x00ffffff) / 16777215f;
            }
        }

        private static void DrawRadial(float centerX, float centerY,
            float width, float height, float r, float g, float b, float a)
        {
            if (a <= 0 || width <= 0 || height <= 0)
                return;

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
