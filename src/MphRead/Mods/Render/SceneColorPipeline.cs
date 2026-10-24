using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    // IEC 61966-2-1 transfer is RGB-only. Alpha and packed joint weights never
    // enter this helper. A false uniform preserves the native arithmetic.
    internal static class SceneColorShaderSource
    {
        internal const string Input = @"
uniform bool scene_linear_color;
uniform bool authored_linear_tint;
vec3 scene_srgb_decode(vec3 value) {
    vec3 c = max(value, vec3(0.0));
    return mix(c / 12.92, pow((c + vec3(0.055)) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}
vec3 scene_srgb_encode(vec3 value) {
    // Match the scene output's deliberate LDR encode, including highlight clipping.
    vec3 c = clamp(value, 0.0, 1.0);
    return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - vec3(0.055),
        step(vec3(0.0031308), c));
}
vec3 scene_native_fog_blend(vec3 linearSurface, vec3 encodedFog, float density) {
    if (density <= 0.0) return linearSurface;
    vec3 encodedSurface = scene_srgb_encode(linearSurface);
    vec3 encodedFogged = encodedSurface * (1.0 - density) + encodedFog * density;
    return scene_srgb_decode(encodedFogged);
}
vec3 scene_input_color(vec3 value) {
    return scene_linear_color ? scene_srgb_decode(value) : value;
}
vec3 scene_material_color(vec3 value) {
    return scene_linear_color && authored_linear_tint ? value : scene_input_color(value);
}
";
        internal const string Normal = @"
vec3 scene_normalize(vec3 n, vec3 fallback) {
    float length2 = dot(n,n);
    return length2 > 0.000000000001 && length2 < 1.0e30 ? n * inversesqrt(length2) : fallback;
}
vec3 scene_transform_normal(mat3 transform, vec3 value) {
    // Cofactor inverse transpose works on GLSL 1.20, including reflected and
    // blended palettes. The sign of determinant must survive normalization.
    vec3 c0 = cross(transform[1], transform[2]);
    vec3 c1 = cross(transform[2], transform[0]);
    vec3 c2 = cross(transform[0], transform[1]);
    float determinant = dot(transform[0], c0);
    vec3 fallback = scene_normalize(transform * value, vec3(0.0,0.0,1.0));
    if (!(abs(determinant) > 0.000000000001 && abs(determinant) < 1.0e30)) return fallback;
    vec3 n = mat3(c0,c1,c2) * value * (determinant < 0.0 ? -1.0 : 1.0);
    return scene_normalize(n, fallback);
}
";
        // Procedural palettes are authored colors too. Scalars used for masks,
        // frequencies and material coefficients keep their existing meaning.
        internal static readonly string Cosmetics = BuildCosmetics();
        private static string BuildCosmetics()
        {
            string source = MphRead.Mods.Cosmetics.CosmeticShader.Source;
            string[] palettes = { "0.20, 0.23, 0.28", "0.76, 0.58, 0.28", "0.08, 0.32, 0.28",
                "0.10, 0.14, 0.19", "0.88, 0.90, 0.85", "0.13, 0.18, 0.22", "0.08, 0.8, 0.65",
                "0.85, 0.40, 0.07", "0.08, 0.07, 0.09", "0.10, 0.12, 0.32", "0.48, 0.16, 0.56",
                "0.12, 0.16, 0.20" };
            foreach (string palette in palettes)
                source = source.Replace("vec3(" + palette + ")", "scene_input_color(vec3(" + palette + "))", StringComparison.Ordinal);
            return source.Replace("mix(cosmetic_primary, cosmetic_secondary, wave)",
                    "mix(scene_input_color(cosmetic_primary), scene_input_color(cosmetic_secondary), wave)", StringComparison.Ordinal)
                .Replace("col.rgb += cosmetic_primary *", "col.rgb += scene_input_color(cosmetic_primary) *", StringComparison.Ordinal);
        }
    }

    internal static class SceneColorEncodeShader
    {
#if ANDROID
        internal static string Vertex => "#version 300 es\nprecision highp float;\nlayout(location=0) in vec4 a_position;\nlayout(location=3) in vec3 a_texcoord;\nout vec2 uv;\nvoid main(){gl_Position=vec4(a_position.xy,0.0,1.0);uv=a_texcoord.xy;}";
        internal static string Fragment => "#version 300 es\nprecision highp float;\nin vec2 uv;\nout vec4 outputColor;\n#define SAMPLE texture\n#define OUTPUT outputColor\n" + Body;
#else
        internal static string Vertex => "#version 120\nvarying vec2 uv;\nvoid main(){gl_Position=vec4(gl_Vertex.xy,0.0,1.0);uv=gl_MultiTexCoord0.xy;}";
        internal static string Fragment => "#version 120\nvarying vec2 uv;\n#define SAMPLE texture2D\n#define OUTPUT gl_FragColor\n" + Body;
#endif
        internal const string Body = @"
uniform sampler2D tex;
vec3 scene_srgb_encode(vec3 value) {
    // Deliberate LDR contract: exposure1, clip highlights, no tone mapper.
    vec3 c = clamp(value,0.0,1.0);
    return mix(c * 12.92, 1.055 * pow(c,vec3(1.0/2.4)) - vec3(0.055), step(vec3(0.0031308),c));
}
void main(){vec4 c=SAMPLE(tex,uv);OUTPUT=vec4(scene_srgb_encode(c.rgb),1.0);}
";
    }
}

namespace MphRead
{
    public partial class Scene
    {
        private int _sceneEncodeProgram, _sceneEncodedTexture, _sceneEncodedFramebuffer, _sceneEncodedDepth;
        private int _sceneEncodeSampler;
        private Vector2i _sceneEncodedSize, _sceneColorRefusedSize;
        private bool _sceneColorRefused, _sceneLinearDrawing, _sceneEncodedReady;
#if !ANDROID
        private bool? _sceneFramebufferSrgbSupported;
        private bool _sceneRestoreFramebufferSrgb;
#endif
        internal bool SceneLinearColorActive => _sceneLinearDrawing;
        private bool LinearColorActive => SceneLinearColorActive;
        private Vector2i SceneOverlaySize => _sceneEncodedReady ? _sceneEncodedSize : _targetSize;
        private Vector2i SceneCaptureSize => _sceneEncodedReady ? _sceneEncodedSize
            : _graphicsOutputReady ? _graphicsOutputSize : _targetSize;
        private int SceneReadFramebuffer => _sceneEncodedReady ? _sceneEncodedFramebuffer : GraphicsReadFramebuffer();
        private string SceneCaptureEncoding => _sceneEncodedReady ? "srgb-encoded-rgba8" : "native-encoded-rgba8";
        private long SceneColorPipelineBytes => (long)_sceneEncodedSize.X * _sceneEncodedSize.Y * 8;

        private void SetSceneLinearColorUniform(bool linear)
        {
            GL.Uniform1(_shaderLocations.SceneLinearColor, linear ? 1 : 0);
            GL.Uniform1(_shaderLocations.ModernMaterialCorrections,
                linear || ModernMaterialCorrectionsEnabled ? 1 : 0);
        }

        // Scene request is frozen at construction. Complete upload variants
        // and both renderable targets must exist before any world draw is linear.
        private void BeginSceneColorFrame()
        {
            _sceneLinearDrawing = _sceneEncodedReady = false;
            // The retained character pipeline emits encoded SourceGX body color.
            // Keep that frame native without changing its shader/model contract.
            if (!SceneColorRequested || SceneColorTexturesFailed || _sourceBodyItems.Count != 0) return;
            Vector2i size = Size;
            if (_sceneColorRefused && _sceneColorRefusedSize == size) return;
            if (_sceneColorRefused) _sceneColorRefused = false;
            int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
            int read = GL.GetInteger(GetPName.ReadFramebufferBinding);
            int program = GL.GetInteger(GetPName.CurrentProgram);
            int active = GL.GetInteger(GetPName.ActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
            int binding = GL.GetInteger(GetPName.TextureBinding2D);
            int renderbuffer = GL.GetInteger(GetPName.RenderbufferBinding);
            try
            {
                EnsureSceneColorPipeline(size);
#if !ANDROID
                if (_sceneFramebufferSrgbSupported == null)
                {
                    string extensions = GL.GetString(StringName.Extensions) ?? "";
                    string version = GL.GetString(StringName.Version) ?? "";
                    _sceneFramebufferSrgbSupported = version.Length > 0 && version[0] >= '3'
                        || extensions.Contains("GL_ARB_framebuffer_sRGB", StringComparison.Ordinal)
                        || extensions.Contains("GL_EXT_framebuffer_sRGB", StringComparison.Ordinal);
                }
                if (_sceneFramebufferSrgbSupported == true)
                {
                    _sceneRestoreFramebufferSrgb = GL.IsEnabled((EnableCap)0x8DB9);
                    GL.Disable((EnableCap)0x8DB9);
                }
#endif
                _sceneLinearDrawing = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _sceneColorRefused = true; _sceneColorRefusedSize = size;
                Mods.DebugLog.Line("render", "linear scene color unavailable; native scene retained: " + ex.Message);
            }
            finally
            {
                GL.UseProgram(program);
                GL.BindTexture(TextureTarget.Texture2D, binding);
                GL.ActiveTexture((TextureUnit)active);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
                GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
                GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
            }
        }

        private void EnsureSceneColorPipeline(Vector2i size)
        {
            if (_sceneEncodeProgram != 0 && _sceneEncodedSize == size) return;
            if (size.X <= 0 || size.Y <= 0 || size.X > GL.GetInteger(GetPName.MaxTextureSize)
                || size.Y > GL.GetInteger(GetPName.MaxTextureSize)
                || !CanAllocateTargetBytes(checked((long)size.X * size.Y * 8)))
                throw new InvalidOperationException("Linear scene output exceeds the aggregate peak target budget/capability.");
            int vertex = 0, fragment = 0, program = 0, texture = 0, framebuffer = 0, depth = 0;
            try
            {
                if (_sceneEncodeProgram == 0)
                {
                    vertex = CompileSceneShader(ShaderType.VertexShader, Mods.Render.SceneColorEncodeShader.Vertex, "scene-color-vertex");
                    fragment = CompileSceneShader(ShaderType.FragmentShader, Mods.Render.SceneColorEncodeShader.Fragment, "scene-color-fragment");
                    program = LinkSceneProgram(vertex, fragment, "scene-color-program");
                }
                texture = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size.X, size.Y, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                depth = GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, size.X, size.Y);
                framebuffer = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, depth);
                ValidateFramebuffer("Encoded scene output");
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Encoded scene target allocation failed.");
                int sampler = GL.GetUniformLocation(program != 0 ? program : _sceneEncodeProgram, "tex");
                if (sampler < 0) throw new InvalidOperationException("Encoded scene program uniform contract incomplete.");
                Mods.Render.OpenGlFailureInjection.Hit("scene-color-before-publish");
                ReleaseSceneColorTargets();
                _sceneEncodedTexture = texture; texture = 0;
                _sceneEncodedFramebuffer = framebuffer; framebuffer = 0;
                _sceneEncodedDepth = depth; depth = 0;
                _sceneEncodedSize = size;
                if (program != 0) { _sceneEncodeProgram = program; program = 0; }
                _sceneEncodeSampler = sampler;
            }
            finally
            {
                DeleteSceneShader(ref vertex); DeleteSceneShader(ref fragment); DeleteProgram(ref program);
                if (framebuffer != 0) GL.DeleteFramebuffer(framebuffer);
                if (depth != 0) GL.DeleteRenderbuffer(depth);
                DeleteTexture(ref texture);
            }
        }

        private bool EncodeSceneColorBeforeOverlays()
        {
            try { return EncodeSceneColorBeforeOverlaysCore(); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _sceneLinearDrawing = false; _sceneColorRefused = true; _sceneColorRefusedSize = Size;
                Mods.DebugLog.Line("render", "linear resolve/encode refused; prepared native world redraw: " + ex.Message);
                return false;
            }
        }
        private bool EncodeSceneColorBeforeOverlaysCore()
        {
            if (!_sceneLinearDrawing) return true;
            if (SceneColorTexturesFailed)
                throw new InvalidOperationException("A late texture upload refused its linear scene variant.");
            // The shared resolve performs bounded successive2:1 reductions in
            // linear working storage. The last bilinear resolve/upscale happens
            // in linear light inside this shader before the single encode.
            int source = ResolveSupersampledCompositeTexture();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneEncodedFramebuffer);
            GL.Viewport(0, 0, _sceneEncodedSize.X, _sceneEncodedSize.Y);
            SetScreenPassState(); GL.Disable(EnableCap.Blend);
            GL.UseProgram(_sceneEncodeProgram);
            GL.BindTexture(TextureTarget.Texture2D, source);
            GL.Uniform1(_sceneEncodeSampler, 0);
            DrawGraphicsFullscreenQuad();
            // A late draw error cannot leave unencoded color under native UI.
            // Abort the frame; the next frame selects the full native fallback.
            if (GL.GetError() != ErrorCode.NoError)
            {
                _sceneLinearDrawing = false; _sceneColorRefused = true; _sceneColorRefusedSize = Size;
                Mods.DebugLog.Line("render", "Scene encode failed; redraw prepared world in the native domain.");
                return false;
            }
            _sceneLinearDrawing = false; _sceneEncodedReady = true;
            GL.UseProgram(_shaderProgramId); SetSceneLinearColorUniform(false);
            GL.Viewport(0, 0, _sceneEncodedSize.X, _sceneEncodedSize.Y);
            GL.DepthMask(true); GL.Enable(EnableCap.DepthTest);
            return true;
        }

        private void ReleaseSceneColorTargets()
        {
            _sceneEncodedReady = false;
            if (_sceneEncodedFramebuffer != 0) GL.DeleteFramebuffer(_sceneEncodedFramebuffer);
            if (_sceneEncodedDepth != 0) GL.DeleteRenderbuffer(_sceneEncodedDepth);
            _sceneEncodedFramebuffer = _sceneEncodedDepth = 0;
            DeleteTexture(ref _sceneEncodedTexture); _sceneEncodedSize = default;
        }
        private void EndSceneColorFrame()
        {
#if !ANDROID
            if (_sceneRestoreFramebufferSrgb) GL.Enable((EnableCap)0x8DB9);
            _sceneRestoreFramebufferSrgb = false;
#endif
        }
        private void DisposeSceneColorPipeline()
        {
            _sceneLinearDrawing = false;
            EndSceneColorFrame();
            ReleaseSceneColorTargets(); DeleteProgram(ref _sceneEncodeProgram);
            _sceneColorRefused = false;
        }
    }
}
