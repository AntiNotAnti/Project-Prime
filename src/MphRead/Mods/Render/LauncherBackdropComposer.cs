#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Procedural Project Prime deployment chamber used by the RmlUi home.
    ///
    /// This is deliberately not a gameplay room. It is a lightweight authored
    /// hero backdrop rendered directly in the existing graphics context, so every
    /// Hunter gets the same clean composition while activity and Hunter themes
    /// can alter the mood without loading a second Scene or shipping game-
    /// derived room imagery.
    /// </summary>
    public static class LauncherBackdropComposer
    {
        public static bool Enabled { get; set; }

        private static int _program;
        private static int _time;
        private static int _resolution;
        private static int _top;
        private static int _mid;
        private static int _bottom;
        private static int _activityAccent;
        private static int _activitySecondary;
        private static int _hunterHalo;
        private static int _hunterRim;
        private static int _energy;
        private static int _fog;
        private static int _particles;
        private static int _structure;
        private static int _floorGrid;
        private static int _heroLight;
        private static int _pulseSpeed;
        private static int _warmth;
        private static int _haloStrength;
        private static int _floorGlow;
        private static int _leftDarken;
        private static int _rightDarken;
        private static int _beamIntensity;
        private static int _backgroundSoftness;
        private static int _lobbyMode;
        private static int _lobbyOccupancyA;
        private static int _lobbyOccupancyB;
        private static readonly int[] _lobbyPadLocations =
            new int[LauncherLobbyFormation.Capacity];
        private static readonly float[] _packedLobbyPads = LauncherLobbyFormation.PackPads();

        private static string VertexSource => LauncherChamberShader.VertexSource;

        private static string FragmentSource => LauncherChamberShader.FragmentSource;

        public static void Draw(int width, int height)
        {
            if (!Enabled || width <= 0 || height <= 0 || !EnsureProgram())
                return;

            LauncherBackdropStyle style = LauncherMenuVisuals.Style;
            LauncherActivityAmbience activity = LauncherMenuVisuals.Activity;
            LauncherHunterTheme hunter = LauncherMenuVisuals.Hunter(LauncherHunter.Hunter);
            float time = LauncherPrefs.ReduceMotion
                ? 0f
                : (float)(Environment.TickCount64 / 1000.0);

            GL.UseProgram(_program);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.Blend);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Disable(EnableCap.Texture2D);

            GL.Uniform1(_time, time);
            GL.Uniform2(_resolution, (float)width, (float)height);
            Color(_top, style.Top);
            Color(_mid, style.Mid);
            Color(_bottom, style.Bottom);
            Color(_activityAccent, activity.Accent);
            Color(_activitySecondary, activity.Secondary);
            Color(_hunterHalo, hunter.Halo);
            Color(_hunterRim, hunter.Rim);
            GL.Uniform1(_energy, activity.Energy);
            GL.Uniform1(_fog, style.Fog * activity.FogBias);
            GL.Uniform1(_particles,
                style.Particles * activity.ParticleBias * hunter.ParticleScale);
            GL.Uniform1(_structure, style.StructureOpacity * activity.StructureBias);
            GL.Uniform1(_floorGrid, activity.FloorGrid);
            GL.Uniform1(_heroLight, activity.HeroLightBias);
            GL.Uniform1(_pulseSpeed, activity.PulseSpeed);
            GL.Uniform1(_warmth, activity.Warmth);
            GL.Uniform1(_haloStrength,
                style.HeroHalo * hunter.AccentStrength * hunter.HaloScale);
            GL.Uniform1(_floorGlow, style.FloorGlow * hunter.AccentStrength);
            GL.Uniform1(_leftDarken, style.LeftUiDarken);
            GL.Uniform1(_rightDarken, style.RightUiDarken);
            GL.Uniform1(_beamIntensity, style.BeamIntensity);
            GL.Uniform1(_backgroundSoftness, style.BackgroundSoftness);
            bool lobby = LauncherLobbyVisuals.Active;
            byte occupied = LauncherLobbyVisuals.OccupiedMask;
            GL.Uniform1(_lobbyMode, lobby ? 1f : 0f);
            // This compatibility GL facade exposes typed vec4 uniforms, not
            // the pointer/count overload. Cache locations once at link time
            // and update each authored pad without unmanaged buffers.
            for (int slot = 0; slot < LauncherLobbyFormation.Capacity; slot++)
            {
                int offset = slot * 4;
                GL.Uniform4(_lobbyPadLocations[slot], new Vector4(
                    _packedLobbyPads[offset], _packedLobbyPads[offset + 1],
                    _packedLobbyPads[offset + 2], _packedLobbyPads[offset + 3]));
            }
            GL.Uniform4(_lobbyOccupancyA, new Vector4(
                (occupied & 0x01) != 0 ? 1f : 0f,
                (occupied & 0x02) != 0 ? 1f : 0f,
                (occupied & 0x04) != 0 ? 1f : 0f,
                (occupied & 0x08) != 0 ? 1f : 0f));
            GL.Uniform4(_lobbyOccupancyB, new Vector4(
                (occupied & 0x10) != 0 ? 1f : 0f,
                (occupied & 0x20) != 0 ? 1f : 0f,
                (occupied & 0x40) != 0 ? 1f : 0f,
                (occupied & 0x80) != 0 ? 1f : 0f));

            GL.Begin(PrimitiveType.TriangleStrip);
            GL.TexCoord2(0f, 0f); GL.Vertex2(-1f, 1f);
            GL.TexCoord2(1f, 0f); GL.Vertex2(1f, 1f);
            GL.TexCoord2(0f, 1f); GL.Vertex2(-1f, -1f);
            GL.TexCoord2(1f, 1f); GL.Vertex2(1f, -1f);
            GL.End();

            GL.UseProgram(0);
            GL.Color4(1f, 1f, 1f, 1f);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.DepthTest);
        }

        public static void Release()
        {
            if (_program != 0)
            {
                GL.DeleteProgram(_program);
                _program = 0;
            }
            ResetLocations();
            Enabled = false;
        }

        internal static void ForgetRendererResources()
        {
            _program = 0;
            ResetLocations();
            Enabled = false;
        }

        private static void Color(int location, MenuRgb color)
        {
            if (location >= 0)
                GL.Uniform3(location, new Vector3(color.R, color.G, color.B));
        }

        private static bool EnsureProgram()
        {
            if (_program != 0)
                return true;

            int vertex = 0, fragment = 0, program = 0;
            try
            {
                vertex = Compile(ShaderType.VertexShader, VertexSource);
                fragment = Compile(ShaderType.FragmentShader, FragmentSource);
                if (vertex == 0 || fragment == 0)
                    return false;

                program = GL.CreateProgram();
                GL.AttachShader(program, vertex);
                GL.AttachShader(program, fragment);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0)
                {
                    Mods.DebugLog.Line("rmlui",
                        "deployment chamber link failed: " + GL.GetProgramInfoLog(program));
                    GL.DeleteProgram(program);
                    return false;
                }

                _program = program;
                _time = GL.GetUniformLocation(program, "time_value");
                _resolution = GL.GetUniformLocation(program, "resolution");
                _top = GL.GetUniformLocation(program, "base_top");
                _mid = GL.GetUniformLocation(program, "base_mid");
                _bottom = GL.GetUniformLocation(program, "base_bottom");
                _activityAccent = GL.GetUniformLocation(program, "activity_accent");
                _activitySecondary = GL.GetUniformLocation(program, "activity_secondary");
                _hunterHalo = GL.GetUniformLocation(program, "hunter_halo");
                _hunterRim = GL.GetUniformLocation(program, "hunter_rim");
                _energy = GL.GetUniformLocation(program, "energy");
                _fog = GL.GetUniformLocation(program, "fog_amount");
                _particles = GL.GetUniformLocation(program, "particle_amount");
                _structure = GL.GetUniformLocation(program, "structure_amount");
                _floorGrid = GL.GetUniformLocation(program, "floor_grid");
                _heroLight = GL.GetUniformLocation(program, "hero_light");
                _pulseSpeed = GL.GetUniformLocation(program, "pulse_speed");
                _warmth = GL.GetUniformLocation(program, "warmth");
                _haloStrength = GL.GetUniformLocation(program, "halo_strength");
                _floorGlow = GL.GetUniformLocation(program, "floor_glow");
                _leftDarken = GL.GetUniformLocation(program, "left_darken");
                _rightDarken = GL.GetUniformLocation(program, "right_darken");
                _beamIntensity = GL.GetUniformLocation(program, "beam_intensity");
                _backgroundSoftness = GL.GetUniformLocation(program, "background_softness");
                _lobbyMode = GL.GetUniformLocation(program, "lobby_mode");
                _lobbyOccupancyA = GL.GetUniformLocation(program, "lobby_occupancy_a");
                _lobbyOccupancyB = GL.GetUniformLocation(program, "lobby_occupancy_b");
                for (int slot = 0; slot < LauncherLobbyFormation.Capacity; slot++)
                    _lobbyPadLocations[slot] = GL.GetUniformLocation(
                        program, $"lobby_pad_geometry[{slot}]");
                Mods.DebugLog.Line("rmlui",
                    "deployment chamber renderer ready: procedural shared hero scene");
                return true;
            }
            catch (Exception ex)
            {
                Mods.DebugLog.Line("rmlui",
                    "deployment chamber renderer failed: " + ex.Message);
                if (program != 0) GL.DeleteProgram(program);
                return false;
            }
            finally
            {
                if (vertex != 0) GL.DeleteShader(vertex);
                if (fragment != 0) GL.DeleteShader(fragment);
            }
        }

        private static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            if (compiled != 0)
                return shader;
            Mods.DebugLog.Line("rmlui",
                $"deployment chamber {type} compile failed: {GL.GetShaderInfoLog(shader)}");
            GL.DeleteShader(shader);
            return 0;
        }

        private static void ResetLocations()
        {
            _time = _resolution = _top = _mid = _bottom = -1;
            _activityAccent = _activitySecondary = _hunterHalo = _hunterRim = -1;
            _energy = _fog = _particles = _structure = _floorGrid = -1;
            _heroLight = _pulseSpeed = _warmth = _haloStrength = -1;
            _floorGlow = _leftDarken = _rightDarken = -1;
            _beamIntensity = _backgroundSoftness = -1;
            _lobbyMode = _lobbyOccupancyA = _lobbyOccupancyB = -1;
            Array.Fill(_lobbyPadLocations, -1);
        }
    }
}
#endif
