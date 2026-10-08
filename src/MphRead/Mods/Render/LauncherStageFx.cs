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
            // The live chamber's per-slot GL pedestal pass already lays down
            // eight correctly aligned contact shadows from the formation table.
            // The single-hero stage shadow would otherwise float between pads.
            if (!Enabled || LauncherLobbyVisuals.Active
                || !LauncherHunter.Wanted || width <= 0 || height <= 0)
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

            // Side-bay telemetry, scanner light and orbiting service lamps turn
            // the large negative-space walls into a living deployment chamber
            // without adding another opaque UI layer.
            DrawAmbientSideBays(activity, theme, time, centerX, centerY, spanX, spanY);

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
            for (int i = 0; i < 22; i++)
            {
                float seedX = Hash01(i * 17 + 3);
                float seedY = Hash01(i * 29 + 11);
                float phase = Hash01(i * 43 + 7) * MathF.PI * 2f;
                float dx = LauncherPrefs.ReduceMotion ? 0
                    : MathF.Sin((float)time * (0.07f + Hash01(i + 91) * 0.05f) + phase) * 0.018f;
                float dy = LauncherPrefs.ReduceMotion ? 0
                    : MathF.Cos((float)time * (0.05f + Hash01(i + 53) * 0.04f) + phase) * 0.012f;
                float moteX = 0.12f + seedX * 0.76f + dx;
                float moteY = 0.16f + seedY * 0.62f + dy;
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

            float spread = spanX * FootSpread(LauncherHunter.Hunter);

            // Reflected floor light first. A broad pool establishes the hero
            // platform while two tighter pools make the boots feel planted.
            DrawRadial(centerX, floorY - 0.006f, spanX * 0.80f, 0.085f,
                theme.Floor.R, theme.Floor.G, theme.Floor.B,
                0.075f + style.FloorGlow * theme.AccentStrength * 0.11f);
            DrawRadial(centerX - spread, floorY - 0.004f, spanX * 0.18f, 0.032f,
                theme.Floor.R, theme.Floor.G, theme.Floor.B, 0.085f);
            DrawRadial(centerX + spread, floorY - 0.004f, spanX * 0.18f, 0.032f,
                theme.Floor.R, theme.Floor.G, theme.Floor.B, 0.085f);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Wide penumbra ties both feet into one floor plane.
            DrawRadial(centerX, floorY + 0.003f, spanX * 0.56f, 0.050f,
                0.00f, 0.00f, 0.00f, 0.31f);

            // Two darker contact cores sit under the actual stance. They are
            // deliberately tiny so they read as weight, not painted circles.
            float coreWidth = spanX * 0.17f;
            DrawRadial(centerX - spread, floorY, coreWidth, 0.020f,
                0.00f, 0.00f, 0.00f, 0.56f);
            DrawRadial(centerX + spread, floorY, coreWidth, 0.020f,
                0.00f, 0.00f, 0.00f, 0.56f);

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
                stage.ForegroundHaze * 0.32f
                + style.Fog * activity.FogBias * 0.055f,
                0f, 0.055f);
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

            // Foreground air is mostly neutral. Hunter identity stays behind
            // the model in the halo/floor instead of becoming a suit-color wash.
            MenuRgb air = MenuRgb.Lerp(activity.Accent, theme.Halo, 0.08f);
            air = MenuRgb.Lerp(air, new MenuRgb(0.28f, 0.36f, 0.42f), 0.58f);
            float r = air.R;
            float g = air.G;
            float b = air.B;

            BeginScreenPass();
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Full-body air layer is almost imperceptible; the lower-body veil
            // is stronger because that is where the character meets room haze.
            DrawRadial(centerX, centerY + spanY * 0.04f,
                spanX * 0.86f, spanY * 0.78f, r, g, b, amount * 0.16f);
            DrawRadial(centerX, centerY + spanY * 0.24f,
                spanX * 0.76f, spanY * 0.43f, r, g, b, amount * 0.55f);

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

        private static void DrawAmbientSideBays(
            LauncherActivityAmbience activity, LauncherHunterTheme theme,
            double time, float centerX, float centerY, float spanX, float spanY)
        {
            MenuRgb frame = MenuRgb.Lerp(activity.Accent,
                new MenuRgb(0.18f, 0.28f, 0.36f), 0.62f);
            MenuRgb signal = MenuRgb.Lerp(activity.Secondary, theme.Rim, 0.20f);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Recessed wall terminals. Their low opacity means selector/social
            // cards can sit over them while the uncovered portions still read
            // as useful chamber architecture instead of empty black space.
            DrawSolidRect(0.025f, 0.17f, 0.185f, 0.545f,
                0.010f, 0.028f, 0.045f, 0.25f);
            DrawFrameRect(0.025f, 0.17f, 0.185f, 0.545f,
                frame.R, frame.G, frame.B, 0.12f, 0.0020f);
            DrawSolidRect(0.815f, 0.16f, 0.975f, 0.690f,
                0.010f, 0.028f, 0.045f, 0.20f);
            DrawFrameRect(0.815f, 0.16f, 0.975f, 0.690f,
                frame.R, frame.G, frame.B, 0.10f, 0.0020f);

            // Animated telemetry bars. The phases are deterministic and freeze
            // at a stable layout when Reduce Motion is enabled.
            for (int i = 0; i < 7; i++)
            {
                float fi = i;
                float wave = 0.5f + 0.5f * MathF.Sin((float)time * 0.36f + fi * 0.91f);
                float width = 0.026f + wave * 0.074f;
                float y = 0.225f + fi * 0.038f;
                DrawSolidRect(0.050f, y, 0.050f + width, y + 0.004f,
                    signal.R, signal.G, signal.B, 0.08f + wave * 0.07f);

                float rightWave = 0.5f + 0.5f * MathF.Sin((float)time * 0.29f + fi * 1.17f + 1.1f);
                float rightWidth = 0.024f + rightWave * 0.068f;
                float ry = 0.255f + fi * 0.045f;
                DrawSolidRect(0.925f - rightWidth, ry, 0.925f, ry + 0.004f,
                    signal.R, signal.G, signal.B, 0.06f + rightWave * 0.06f);
            }

            // A pair of slow scanner sweeps travels through the side bays.
            // This is the most visible motion in the peripheral architecture,
            // but it remains far below UI contrast.
            float scan = LauncherPrefs.ReduceMotion
                ? 0.44f
                : 0.20f + (float)(time * 0.035 % 0.34);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            DrawRadial(0.105f, scan, 0.145f, 0.020f,
                signal.R, signal.G, signal.B, 0.055f);
            DrawRadial(0.895f, 0.74f - scan * 0.50f, 0.135f, 0.018f,
                signal.R, signal.G, signal.B, 0.045f);

            // Service lamps orbit the hero bay and provide gentle parallax.
            for (int i = 0; i < 8; i++)
            {
                float angle = (MathF.PI * 2f / 8f) * i
                    + (LauncherPrefs.ReduceMotion ? 0f : (float)time * 0.055f);
                float ox = centerX + MathF.Cos(angle) * spanX * 0.56f;
                float oy = centerY + MathF.Sin(angle) * spanY * 0.38f;
                float size = i % 2 == 0 ? 0.010f : 0.006f;
                DrawRadial(ox, oy, size, size * 1.18f,
                    signal.R, signal.G, signal.B, 0.055f + (i % 3) * 0.012f);
            }

            DrawHunterMotif(LauncherHunter.Hunter, theme, time,
                centerX, centerY, spanX, spanY);

            // Floor runway pips travel toward the hero platform and make the
            // lower chamber feel operational rather than painted.
            float travel = LauncherPrefs.ReduceMotion
                ? 0.5f
                : (float)((time * 0.11) % 1.0);
            for (int i = 0; i < 6; i++)
            {
                float phase = (i / 6f + travel) % 1f;
                float y = 0.83f - phase * 0.22f;
                float spread = 0.09f + phase * 0.16f;
                float a = (1f - phase) * 0.055f;
                DrawRadial(centerX - spread, y, 0.018f, 0.008f,
                    activity.Accent.R, activity.Accent.G, activity.Accent.B, a);
                DrawRadial(centerX + spread, y, 0.018f, 0.008f,
                    activity.Accent.R, activity.Accent.G, activity.Accent.B, a);
            }

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        private static void DrawHunterMotif(Hunter hunter,
            LauncherHunterTheme theme, double time,
            float centerX, float centerY, float spanX, float spanY)
        {
            float phase = LauncherPrefs.ReduceMotion ? 0f : (float)time;
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);

            switch (hunter)
            {
                case Hunter.Spire:
                    // Slow ember orbit and a warm floor shimmer echo Spire's
                    // volcanic silhouette without turning the whole bay orange.
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = i * (MathF.PI * 2f / 12f) + phase * 0.11f;
                        float radiusX = spanX * (0.42f + (i % 3) * 0.035f);
                        float radiusY = spanY * (0.30f + (i % 2) * 0.025f);
                        float x = centerX + MathF.Cos(angle) * radiusX;
                        float y = centerY + MathF.Sin(angle) * radiusY;
                        float size = 0.004f + (i % 4) * 0.0015f;
                        DrawRadial(x, y, size, size,
                            theme.Particle.R, theme.Particle.G, theme.Particle.B,
                            0.045f + (i % 3) * 0.014f);
                    }
                    DrawRadial(centerX, centerY + spanY * 0.44f,
                        spanX * 0.54f, 0.030f,
                        theme.Floor.R, theme.Floor.G, theme.Floor.B, 0.055f);
                    break;

                case Hunter.Trace:
                    // A few red stealth scan slivers blink in alternating
                    // columns, deliberately behind rather than over the model.
                    for (int i = 0; i < 5; i++)
                    {
                        float pulse = 0.5f + 0.5f *
                            MathF.Sin(phase * 0.68f + i * 1.47f);
                        float x = centerX - spanX * 0.43f + i * spanX * 0.215f;
                        float h = 0.030f + pulse * 0.065f;
                        DrawSolidRect(x - 0.0015f, centerY - h * 0.5f,
                            x + 0.0015f, centerY + h * 0.5f,
                            theme.Rim.R, theme.Rim.G, theme.Rim.B,
                            0.018f + pulse * 0.040f);
                    }
                    break;

                case Hunter.Sylux:
                    // Cold power nodes breathe around the rear rim.
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = i * (MathF.PI * 2f / 6f) - phase * 0.075f;
                        float x = centerX + MathF.Cos(angle) * spanX * 0.46f;
                        float y = centerY + MathF.Sin(angle) * spanY * 0.31f;
                        float pulse = 0.5f + 0.5f * MathF.Sin(phase * 0.9f + i);
                        DrawRadial(x, y, 0.009f + pulse * 0.005f,
                            0.009f + pulse * 0.005f,
                            theme.Rim.R, theme.Rim.G, theme.Rim.B,
                            0.035f + pulse * 0.045f);
                    }
                    break;

                case Hunter.Kanden:
                    // Organic energy pulses, offset rather than perfectly
                    // circular, keep Kanden's bay feeling unstable.
                    for (int i = 0; i < 5; i++)
                    {
                        float pulse = 0.5f + 0.5f *
                            MathF.Sin(phase * 0.52f + i * 1.31f);
                        float x = centerX + (i - 2) * spanX * 0.12f;
                        float y = centerY - spanY * 0.18f + (i % 2) * spanY * 0.24f;
                        DrawRadial(x, y,
                            0.010f + pulse * 0.012f, 0.010f + pulse * 0.008f,
                            theme.Particle.R, theme.Particle.G, theme.Particle.B,
                            0.020f + pulse * 0.040f);
                    }
                    break;

                case Hunter.Noxus:
                    // Sparse crystalline points stay colder and steadier than
                    // the other hunter motifs.
                    for (int i = 0; i < 6; i++)
                    {
                        float x = centerX + ((i % 3) - 1) * spanX * 0.28f;
                        float y = centerY - spanY * 0.22f + (i / 3) * spanY * 0.42f;
                        DrawRadial(x, y, 0.006f, 0.013f,
                            theme.Rim.R, theme.Rim.G, theme.Rim.B, 0.052f);
                    }
                    break;

                case Hunter.Weavel:
                    // Split mechanical rails mirror Weavel's asymmetry.
                    float split = 0.5f + 0.5f * MathF.Sin(phase * 0.42f);
                    DrawSolidRect(centerX - spanX * 0.47f, centerY - 0.002f,
                        centerX - spanX * (0.17f + split * 0.05f), centerY + 0.002f,
                        theme.Rim.R, theme.Rim.G, theme.Rim.B, 0.050f);
                    DrawSolidRect(centerX + spanX * (0.15f + split * 0.04f), centerY - 0.002f,
                        centerX + spanX * 0.48f, centerY + 0.002f,
                        theme.Rim.R, theme.Rim.G, theme.Rim.B, 0.050f);
                    break;

                case Hunter.Samus:
                    // Heroic scanner bands climb gently behind the armor.
                    for (int i = 0; i < 3; i++)
                    {
                        float travel = LauncherPrefs.ReduceMotion
                            ? 0.5f
                            : (float)((time * 0.045 + i / 3.0) % 1.0);
                        float y = centerY + spanY * (0.32f - travel * 0.64f);
                        DrawSolidRect(centerX - spanX * 0.32f, y - 0.0012f,
                            centerX + spanX * 0.32f, y + 0.0012f,
                            theme.Rim.R, theme.Rim.G, theme.Rim.B, 0.028f);
                    }
                    break;
            }

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        private static void DrawSolidRect(float x0, float y0, float x1, float y1,
            float r, float g, float b, float a)
        {
            if (a <= 0 || x1 <= x0 || y1 <= y0) return;
            GL.Disable(EnableCap.Texture2D);
            GL.Color4(r, g, b, a);
            float left = x0 * 2f - 1f;
            float right = x1 * 2f - 1f;
            float top = 1f - y0 * 2f;
            float bottom = 1f - y1 * 2f;
            GL.Begin(PrimitiveType.TriangleStrip);
            GL.Vertex3(right, top, 0f);
            GL.Vertex3(left, top, 0f);
            GL.Vertex3(right, bottom, 0f);
            GL.Vertex3(left, bottom, 0f);
            GL.End();
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, _radialTexture);
        }

        private static void DrawFrameRect(float x0, float y0, float x1, float y1,
            float r, float g, float b, float a, float thickness)
        {
            DrawSolidRect(x0, y0, x1, y0 + thickness, r, g, b, a);
            DrawSolidRect(x0, y1 - thickness, x1, y1, r, g, b, a);
            DrawSolidRect(x0, y0, x0 + thickness, y1, r, g, b, a);
            DrawSolidRect(x1 - thickness, y0, x1, y1, r, g, b, a);
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
