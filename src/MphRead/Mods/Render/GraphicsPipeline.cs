using System;
using MphRead.Mods;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Optional OpenGL/GLES directional-shadow compositor. Native scene, world
    /// stencil, arm cannon, weapon effects and HUD still use their established
    /// rendering path. No temporal, HDR, PBR or color-grading dependencies.
    /// </summary>
    public partial class Scene
    {
        private int _graphicsProgram;
        private int _graphicsOutputTexture;
        private int _graphicsOutputFramebuffer;
        private Vector2i _graphicsOutputSize;
        private bool _graphicsOutputReady;
        private bool _graphicsPipelineRefused;

        private int _gfxSceneSampler;
        private int _gfxDepthSampler;
        private int _gfxShadowSampler;
        private int _gfxTexel;
        private int _gfxInvProjection;
        private int _gfxInvView;
        private int _gfxShadowView;
        private int _gfxShadowProjection;
        private int _gfxShadowTexel;
        private int _gfxShadowLightDir;

        /// <summary>
        /// A disabled or invalid shadow map must not allocate a fullscreen
        /// intermediate. This gate also allows graphics policy tests to run
        /// without constructing a native GL context.
        /// </summary>
        internal static bool ShouldCompositeDirectionalShadow(
            ShadowQuality quality, bool shadowReady, bool depthReady)
            => quality != ShadowQuality.Off && shadowReady && depthReady;

        private void ApplyGraphicsPostProcess()
        {
            _graphicsOutputReady = false;
            if (_graphicsPipelineRefused
                || !ShouldCompositeDirectionalShadow(
                    RenderOptions.Shadows, ShadowMapReady, _depthTexture != 0))
            {
                // The scene's original backbuffer is composited directly.
                // Delete the optional shadow color target when shadows are
                // switched off, without holding a large GPU allocation until
                // this scene is unloaded.
                if (_graphicsOutputTexture != 0
                    && (_graphicsPipelineRefused
                        || RenderOptions.Shadows == ShadowQuality.Off))
                    ReleaseGraphicsOutputTarget();
                return;
            }

            try
            {
                Vector2i target = ResolveGraphicsProcessingSize(
                    _targetSize, Size, taaActive: false);
                EnsureGraphicsPipeline(target);

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _graphicsOutputFramebuffer);
                GL.Viewport(0, 0, target.X, target.Y);
                SetScreenPassState();
                GL.Disable(EnableCap.Blend);
                GL.UseProgram(_graphicsProgram);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _screenTexture);
                GL.Uniform1(_gfxSceneSampler, 0);
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, _depthTexture);
                GL.Uniform1(_gfxDepthSampler, 1);
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, _shadowDepthTexture);
                GL.Uniform1(_gfxShadowSampler, 2);

                GL.Uniform2(_gfxTexel,
                    1f / Math.Max(1, _targetSize.X), 1f / Math.Max(1, _targetSize.Y));
                Matrix4 invProjection = _perspectiveMatrix.Inverted();
                Matrix4 invView = _viewMatrix.Inverted();
                GL.UniformMatrix4(_gfxInvProjection, false, ref invProjection);
                GL.UniformMatrix4(_gfxInvView, false, ref invView);
                GL.UniformMatrix4(_gfxShadowView, false, ref _shadowView);
                GL.UniformMatrix4(_gfxShadowProjection, false, ref _shadowProjection);
                GL.Uniform2(_gfxShadowTexel,
                    1f / Math.Max(1, _shadowTargetSize),
                    1f / Math.Max(1, _shadowTargetSize));
                Vector3 direction = _light1Vector;
                if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)
                    || !float.IsFinite(direction.Z) || direction.LengthSquared < .0001f)
                    direction = new Vector3(-.45f, -.82f, -.35f);
                GL.Uniform3(_gfxShadowLightDir, direction.Normalized());

                DrawGraphicsFullscreenQuad();
                _graphicsOutputReady = true;
                RenderPostProcessCount++;
                CheckGlError("DirectionalShadowComposite");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _graphicsPipelineRefused = true;
                _graphicsOutputReady = false;
                Console.WriteLine("[render] shadow composition unavailable: " + ex.Message);
            }
            finally
            {
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer);
                GL.Viewport(0, 0, _targetSize.X, _targetSize.Y);
            }
        }

        internal static Vector2i ResolveGraphicsProcessingSize(
            Vector2i sceneTarget, Vector2i outputSize, bool taaActive)
        {
            if (taaActive || sceneTarget.X <= 0 || sceneTarget.Y <= 0
                || outputSize.X <= 0 || outputSize.Y <= 0
                || (sceneTarget.X <= outputSize.X && sceneTarget.Y <= outputSize.Y))
                return sceneTarget;

            double scale = Math.Min((double)outputSize.X / sceneTarget.X,
                (double)outputSize.Y / sceneTarget.Y);
            return new Vector2i(
                Math.Max(1, (int)Math.Round(sceneTarget.X * scale)),
                Math.Max(1, (int)Math.Round(sceneTarget.Y * scale)));
        }

        private int GraphicsCompositeTexture()
            => _graphicsOutputReady ? _graphicsOutputTexture : _screenTexture;

        private void EnsureGraphicsPipeline(Vector2i target)
        {
            if (_graphicsProgram == 0)
            {
                int vertex = CompileGraphicsShader(ShaderType.VertexShader,
                    Mods.Render.GraphicsPipelineShader.VertexSource);
                int fragment = 0;
                try
                {
                    fragment = CompileGraphicsShader(ShaderType.FragmentShader,
                        Mods.Render.GraphicsPipelineShader.FragmentSource);
                    _graphicsProgram = GL.CreateProgram();
                    GL.AttachShader(_graphicsProgram, vertex);
                    GL.AttachShader(_graphicsProgram, fragment);
                    GL.LinkProgram(_graphicsProgram);
#if !ANDROID
                    GL.GetProgram(_graphicsProgram, GetProgramParameterName.LinkStatus,
                        out int linked);
                    if (linked == 0)
                        throw new ProgramException(GL.GetProgramInfoLog(_graphicsProgram));
#endif
                    _gfxSceneSampler = GL.GetUniformLocation(_graphicsProgram, "tex");
                    _gfxDepthSampler = GL.GetUniformLocation(_graphicsProgram, "depth_tex");
                    _gfxShadowSampler = GL.GetUniformLocation(_graphicsProgram, "shadow_tex");
                    _gfxTexel = GL.GetUniformLocation(_graphicsProgram, "texel");
                    _gfxInvProjection = GL.GetUniformLocation(_graphicsProgram, "inv_projection");
                    _gfxInvView = GL.GetUniformLocation(_graphicsProgram, "inv_view");
                    _gfxShadowView = GL.GetUniformLocation(_graphicsProgram, "shadow_view");
                    _gfxShadowProjection = GL.GetUniformLocation(_graphicsProgram, "shadow_projection");
                    _gfxShadowTexel = GL.GetUniformLocation(_graphicsProgram, "shadow_texel");
                    _gfxShadowLightDir = GL.GetUniformLocation(_graphicsProgram, "shadow_light_dir");
                }
                finally
                {
                    GL.DeleteShader(vertex);
                    if (fragment != 0) GL.DeleteShader(fragment);
                }
            }

            if (_graphicsOutputFramebuffer == 0)
                _graphicsOutputFramebuffer = GL.GenFramebuffer();

            if (_graphicsOutputTexture == 0)
            {
                _graphicsOutputTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, _graphicsOutputTexture);
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
            if (_graphicsOutputSize != target)
            {
                // Reuse the same texture/FBO across frames. Old transient
                // leasing detached and reattached this target every frame.
                GL.BindTexture(TextureTarget.Texture2D, _graphicsOutputTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                    target.X, target.Y, 0, PixelFormat.Rgba,
                    PixelType.UnsignedByte, IntPtr.Zero);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _graphicsOutputFramebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D,
                    _graphicsOutputTexture, 0);
                ValidateFramebuffer("Directional shadow output");
                _graphicsOutputSize = target;
            }
        }

        private void ReleaseGraphicsOutputTarget()
        {
            _graphicsOutputReady = false;
            if (_graphicsOutputFramebuffer != 0)
            {
                GL.DeleteFramebuffer(_graphicsOutputFramebuffer);
                _graphicsOutputFramebuffer = 0;
            }
            DeleteTexture(ref _graphicsOutputTexture);
            _graphicsOutputSize = default;
        }

        private void DisposeGraphicsPipeline()
        {
            ReleaseGraphicsOutputTarget();
            _graphicsPipelineRefused = false;
            DeleteProgram(ref _graphicsProgram);
        }

        private static int CompileGraphicsShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                throw new ProgramException(log);
            }
            return shader;
        }

        private static void DrawGraphicsFullscreenQuad()
        {
            GL.Begin(PrimitiveType.TriangleStrip);
            GL.TexCoord3(1f, 1f, 0f); GL.Vertex3(1f, 1f, 0f);
            GL.TexCoord3(0f, 1f, 0f); GL.Vertex3(-1f, 1f, 0f);
            GL.TexCoord3(1f, 0f, 0f); GL.Vertex3(1f, -1f, 0f);
            GL.TexCoord3(0f, 0f, 0f); GL.Vertex3(-1f, -1f, 0f);
            GL.End();
        }
    }
}

namespace MphRead.Mods.Render
{
    internal static class GraphicsPipelineShader
    {
#if ANDROID
        public static string VertexSource { get; } = @"#version 300 es
precision highp float;
layout(location = 0) in vec4 a_position;
layout(location = 3) in vec3 a_texcoord;
out vec2 texcoord;
void main() {
    gl_Position = vec4(a_position.xy, 0.0, 1.0);
    texcoord = a_texcoord.xy;
}
";
        public static string FragmentSource { get; } =
            "#version 300 es\nprecision highp float;\n"
            + "in vec2 texcoord;\nout vec4 frag_color;\n"
            + "#define SAMPLE texture\n#define OUTPUT frag_color\n"
            + "#define DEPTH_PRECISION highp\n" + ShadowBody;
#else
        public static string VertexSource { get; } = @"#version 120
varying vec2 texcoord;
void main() {
    gl_Position = vec4(gl_Vertex.xy, 0.0, 1.0);
    texcoord = gl_MultiTexCoord0.xy;
}
";
        public static string FragmentSource { get; } =
            "#version 120\nvarying vec2 texcoord;\n"
            + "#define SAMPLE texture2D\n#define OUTPUT gl_FragColor\n"
            + "#define DEPTH_PRECISION\n" + ShadowBody;
#endif

        // Preserve the validated depth reconstruction, normal-bias, and
        // nine-tap PCF coefficients of the original directional-shadow path.
        // Retired HDR/temporal/PBR/reflection/grade code no longer compiles.
        private const string ShadowBody = @"
uniform sampler2D tex;
uniform DEPTH_PRECISION sampler2D depth_tex;
uniform DEPTH_PRECISION sampler2D shadow_tex;
uniform vec2 texel;
uniform mat4 inv_projection;
uniform mat4 inv_view;
uniform mat4 shadow_view;
uniform mat4 shadow_projection;
uniform vec2 shadow_texel;
uniform vec3 shadow_light_dir;

float raw_depth(vec2 uv) {
    return SAMPLE(depth_tex, clamp(uv, vec2(0.0), vec2(1.0))).r;
}

vec3 view_position(vec2 uv, float d) {
    vec4 clip = vec4(uv * 2.0 - 1.0, d * 2.0 - 1.0, 1.0);
    vec4 view = inv_projection * clip;
    return view.xyz / max(abs(view.w), 0.000001);
}

vec3 world_position(vec2 uv, float d) {
    vec4 world = inv_view * vec4(view_position(uv, d), 1.0);
    return world.xyz / max(abs(world.w), 0.000001);
}

vec3 depth_normal(vec2 uv, float centerDepth) {
    if (centerDepth >= 0.999999) return vec3(0.0, 0.0, 1.0);
    vec3 p = view_position(uv, centerDepth);
    float dx = raw_depth(uv + vec2(texel.x, 0.0));
    float dy = raw_depth(uv + vec2(0.0, texel.y));
    vec3 px = view_position(uv + vec2(texel.x, 0.0), dx);
    vec3 py = view_position(uv + vec2(0.0, texel.y), dy);
    vec3 n = normalize(cross(px - p, py - p));
    if (n.z < 0.0) n = -n;
    return n;
}

float directional_shadow(vec3 worldPos, vec3 viewNormal) {
    vec4 lightClip = shadow_projection * shadow_view * vec4(worldPos, 1.0);
    if (lightClip.w <= 0.0) return 1.0;
    vec3 ndc = lightClip.xyz / lightClip.w;
    vec2 suv = ndc.xy * 0.5 + 0.5;
    float receiver = ndc.z * 0.5 + 0.5;
    if (suv.x <= 0.002 || suv.x >= 0.998 || suv.y <= 0.002 || suv.y >= 0.998
        || receiver <= 0.0 || receiver >= 1.0) return 1.0;
    vec3 worldNormal = normalize(mat3(inv_view) * viewNormal);
    float alignment = abs(dot(worldNormal, normalize(shadow_light_dir)));
    float bias = mix(0.0032, 0.0007, alignment);
    float lit = 0.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            float stored = SAMPLE(shadow_tex,
                suv + vec2(float(x), float(y)) * shadow_texel).r;
            lit += receiver - bias <= stored ? 1.0 : 0.0;
        }
    }
    lit /= 9.0;
    return mix(0.58, 1.0, lit);
}

void main() {
    vec2 uv = texcoord;
    vec3 color = SAMPLE(tex, clamp(uv, vec2(0.0), vec2(1.0))).rgb;
    float d = raw_depth(uv);
    if (d < 0.999999) {
        vec3 worldPos = world_position(uv, d);
        vec3 normal = depth_normal(uv, d);
        color *= directional_shadow(worldPos, normal);
    }
    OUTPUT = vec4(clamp(color, 0.0, 1.0), 1.0);
}
";
    }
}
