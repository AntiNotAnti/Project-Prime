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
    /// hero backdrop rendered directly in the existing OpenGL context, so every
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

        private const string VertexSource = @"#version 120
varying vec2 chamber_uv;
void main()
{
    chamber_uv = gl_MultiTexCoord0.xy;
    gl_Position = gl_Vertex;
}
";

        private const string FragmentSource = @"#version 120
varying vec2 chamber_uv;

uniform vec2 resolution;
uniform float time_value;
uniform vec3 base_top;
uniform vec3 base_mid;
uniform vec3 base_bottom;
uniform vec3 activity_accent;
uniform vec3 activity_secondary;
uniform vec3 hunter_halo;
uniform vec3 hunter_rim;
uniform float energy;
uniform float fog_amount;
uniform float particle_amount;
uniform float structure_amount;
uniform float floor_grid;
uniform float hero_light;
uniform float pulse_speed;
uniform float warmth;
uniform float halo_strength;
uniform float floor_glow;
uniform float left_darken;
uniform float right_darken;
uniform float beam_intensity;
uniform float background_softness;

float hash21(vec2 p)
{
    p = fract(p * vec2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return fract(p.x * p.y);
}

float line(float value, float width)
{
    return 1.0 - smoothstep(width, width * 2.2, abs(value));
}

float box_mask(vec2 uv, vec2 lo, vec2 hi, float feather)
{
    vec2 a = smoothstep(lo - feather, lo, uv);
    vec2 b = 1.0 - smoothstep(hi, hi + feather, uv);
    return a.x * a.y * b.x * b.y;
}

float ellipse_mask(vec2 uv, vec2 center, vec2 radius)
{
    vec2 d = (uv - center) / max(radius, vec2(0.001));
    float q = dot(d, d);
    return 1.0 - smoothstep(0.35, 1.0, q);
}

void main()
{
    vec2 uv = chamber_uv;
    float aspect = max(resolution.x / max(resolution.y, 1.0), 1.0);
    vec2 p = vec2((uv.x - 0.5) * aspect, uv.y - 0.5);

    // Deep three-stop chamber base, brighter near the horizon and central bay.
    float upper = smoothstep(0.0, 0.52, uv.y);
    vec3 col = mix(base_top, base_mid, upper);
    col = mix(col, base_bottom, smoothstep(0.58, 1.0, uv.y));
    float horizon = exp(-pow((uv.y - 0.52) * 4.8, 2.0));
    col += activity_accent * horizon * 0.035 * energy;

    // Distant central bay. It is intentionally broad and low-detail so Hunter
    // silhouettes remain the only detailed subject in the middle of the frame.
    float bay = box_mask(uv, vec2(0.305, 0.14), vec2(0.855, 0.72), 0.035);
    col += mix(activity_accent, hunter_halo, 0.28) * bay * 0.040 * structure_amount;

    // Neutral torso-level lift keeps the selected Hunter readable without
    // painting Hunter identity color through the model itself. This is the
    // brightest part of the room, but still reads as reflected chamber light.
    float torsoLift = ellipse_mask(uv, vec2(0.615, 0.49), vec2(0.255, 0.335));
    vec3 heroAmbient = mix(base_mid, vec3(0.105, 0.145, 0.185), 0.55);
    col += heroAmbient * torsoLift * (0.15 + 0.05 * hero_light);

    // Recessed inner wall panels add a second depth plane behind the hero.
    float leftRecess = box_mask(uv, vec2(0.355, 0.30), vec2(0.455, 0.69), 0.014);
    float rightRecess = box_mask(uv, vec2(0.775, 0.30), vec2(0.855, 0.69), 0.014);
    float recess = min(1.0, leftRecess + rightRecess);
    col *= 1.0 - recess * 0.10 * structure_amount;
    float innerEdges = line(uv.x - 0.355, 0.0022) + line(uv.x - 0.455, 0.0022)
        + line(uv.x - 0.775, 0.0022) + line(uv.x - 0.855, 0.0022);
    float innerGate = smoothstep(0.27, 0.34, uv.y) * (1.0 - smoothstep(0.68, 0.74, uv.y));
    col += activity_secondary * innerEdges * innerGate * 0.018 * structure_amount;

    // Vertical chamber ribs and recessed side panels.
    for (int i = 0; i < 7; i++)
    {
        float fi = float(i);
        float x = 0.16 + fi * 0.135;
        float rib = line(uv.x - x, 0.0018 + 0.001 * uv.y);
        float gate = smoothstep(0.10, 0.20, uv.y) * (1.0 - smoothstep(0.76, 0.90, uv.y));
        col += activity_accent * rib * gate * 0.045 * structure_amount;
    }

    // Angled architectural braces. The opposing slopes frame the hero instead
    // of crossing the torso.
    float leftBrace = line((uv.x - 0.17) - (0.64 - uv.y) * 0.26, 0.006);
    float rightBrace = line((0.91 - uv.x) - (0.64 - uv.y) * 0.22, 0.006);
    float braceGate = smoothstep(0.16, 0.30, uv.y) * (1.0 - smoothstep(0.66, 0.82, uv.y));
    col += activity_secondary * (leftBrace + rightBrace) * braceGate
        * 0.06 * structure_amount;

    // Technical light rails in the upper bay.
    float railBand = box_mask(uv, vec2(0.36, 0.20), vec2(0.82, 0.24), 0.008);
    float railPattern = step(0.58, fract(uv.x * 22.0));
    col += activity_secondary * railBand * railPattern
        * (0.08 + 0.08 * energy);

    // Hero halo behind the selected Hunter. Hunter theme drives the hue while
    // activity mood decides how energetic the chamber feels.
    vec2 heroCenter = vec2(0.615, 0.49);
    float hero = ellipse_mask(uv, heroCenter, vec2(0.205, 0.39));
    float pulse = 1.0;
    if (time_value > 0.0)
        pulse += sin(time_value * (0.55 + pulse_speed * 0.45)) * 0.035;
    col += hunter_halo * hero * halo_strength * hero_light
        * (0.095 + 0.035 * energy) * pulse;

    // Narrow rear rim column gives shoulders/head a clean edge without turning
    // the whole background into a glowing circle.
    float rimColumn = ellipse_mask(uv, vec2(0.615, 0.43), vec2(0.105, 0.32));
    col += hunter_rim * rimColumn * 0.040 * hero_light;

    // Soft volumetric shafts from above.
    float shaftA = max(0.0, 1.0 - abs((uv.x - 0.58) * 6.4 + (uv.y - 0.15) * 0.55));
    float shaftB = max(0.0, 1.0 - abs((uv.x - 0.70) * 7.1 - (uv.y - 0.12) * 0.45));
    float shaftGate = (1.0 - smoothstep(0.20, 0.86, uv.y)) * smoothstep(0.04, 0.18, uv.y);
    col += activity_secondary * (shaftA + shaftB) * shaftGate
        * 0.028 * beam_intensity * energy;

    // Floor plane and perspective grid.
    float floorMask = smoothstep(0.58, 0.78, uv.y);
    col = mix(col, base_bottom * 0.72, floorMask * 0.56);

    float fy = max(uv.y - 0.56, 0.001);
    float perspectiveY = 1.0 / (fy * 10.0 + 0.55);
    float horizontal = line(fract(perspectiveY * 4.2) - 0.5, 0.06);
    float centeredX = (uv.x - 0.615) / max(fy + 0.20, 0.20);
    float vertical = line(fract(centeredX * 4.5) - 0.5, 0.035);
    float grid = max(horizontal, vertical) * floorMask;
    col += activity_accent * grid * 0.038 * floor_grid;

    // Dedicated hero platform. A dark center plus colored edge is cleaner than
    // pretending the Hunter is standing on a photographed map floor.
    vec2 platformDelta = (uv - vec2(0.615, 0.805)) / vec2(0.235, 0.060);
    float platformQ = dot(platformDelta, platformDelta);
    float platformFill = 1.0 - smoothstep(0.62, 1.0, platformQ);
    float platformEdge = smoothstep(0.62, 0.77, platformQ)
        * (1.0 - smoothstep(0.88, 1.0, platformQ));
    col = mix(col, vec3(0.006, 0.012, 0.022), platformFill * 0.70);
    float platformCore = 1.0 - smoothstep(0.08, 0.70, platformQ);
    col += mix(activity_accent, hunter_halo, 0.30)
        * platformCore * floor_glow * 0.045;
    col += hunter_halo * platformEdge * floor_glow * 0.22;
    col += activity_secondary * platformEdge * floor_glow * 0.075;

    // Low chamber haze.
    float lowFog = smoothstep(0.52, 0.92, uv.y)
        * (1.0 - smoothstep(0.88, 1.0, uv.y));
    float fogWave = 1.0;
    if (time_value > 0.0)
        fogWave += sin(uv.x * 8.0 + time_value * 0.18) * 0.10;
    vec3 fogColor = mix(activity_accent, vec3(0.16, 0.24, 0.31), 0.58);
    col = mix(col, fogColor, lowFog * fog_amount * 0.10 * fogWave);

    // Sparse energy dust. Large cells make this cheap and intentionally subtle.
    vec2 cells = floor(uv * vec2(80.0, 45.0));
    vec2 cellUv = fract(uv * vec2(80.0, 45.0)) - 0.5;
    float seed = hash21(cells);
    float mote = step(0.955 - particle_amount * 0.018, seed);
    float sparkle = 1.0 - smoothstep(0.02, 0.12, length(cellUv));
    float twinkle = 0.75;
    if (time_value > 0.0)
        twinkle += 0.25 * sin(time_value * (0.7 + seed) + seed * 18.0);
    col += mix(activity_secondary, hunter_rim, 0.45)
        * mote * sparkle * twinkle * particle_amount * 0.30;

    // Adventure can warm the distance without recoloring the Hunter itself.
    col = mix(col, col * vec3(1.10, 0.97, 0.84), warmth * 0.30);

    // UI readability zones. These are part of the world art, not opaque panels.
    float leftMask = 1.0 - smoothstep(0.02, 0.37, uv.x);
    float rightMask = smoothstep(0.78, 1.0, uv.x);
    col *= 1.0 - leftMask * left_darken;
    col *= 1.0 - rightMask * right_darken;

    // Soft global depth and vignette.
    float vignette = smoothstep(0.56, 1.03,
        length(vec2((uv.x - 0.52) * 1.08, (uv.y - 0.48) * 0.92)));
    col *= 1.0 - vignette * (0.20 + background_softness * 0.18);

    // Tiny film grain prevents large gradients from banding at dark values.
    float grain = hash21(gl_FragCoord.xy + vec2(time_value * 3.0, 0.0)) - 0.5;
    col += grain * 0.006;

    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
";

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
                Mods.DebugLog.Line("rmlui",
                    "deployment chamber renderer ready: procedural GL hero scene");
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
        }
    }
}
#endif
