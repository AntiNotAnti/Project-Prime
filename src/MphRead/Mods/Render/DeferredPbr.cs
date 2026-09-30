using System;
using MphRead.Mods;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Optional compatibility-friendly deferred PBR path.
    ///
    /// OpenGL 2.1 has no dependable MRT contract on every machine Project Prime
    /// supports, so the same opaque geometry is replayed into three compact
    /// G-buffer targets (albedo, normal, material) while reusing the finished
    /// scene depth as the visibility contract. The fullscreen graphics pass then
    /// performs GGX lighting. Failure is soft and leaves the forward frame intact.
    /// </summary>
    public partial class Scene
    {
        private Mods.Cosmetics.CosmeticUniforms? _pbrCosmetics;
        private int _pbrProgram;
        private int _pbrFramebuffer;
        private int _pbrAlbedoTexture;
        private int _pbrNormalTexture;
        private int _pbrMaterialTexture;
        private int _pbrDepthTexture;
        private Vector2i _pbrSize;
        private bool _pbrReady;
        private bool _pbrRefused;

        private int _pbrMode;
        private int _pbrProjection;
        private int _pbrView;
        private int _pbrViewInv;
        private int _pbrTextureMatrix;
        private int _pbrMatrixStack;
        private int _pbrTexgen;
        private int _pbrUseTexture;
        private int _pbrBaseSampler;
        private int _pbrNormalSampler;
        private int _pbrSpecularSampler;
        private int _pbrEmissiveSampler;
        private int _pbrUseNormal;
        private int _pbrUseSpecular;
        private int _pbrUseEmissive;
        private int _pbrOverrideEnabled;
        private int _pbrOverrideColor;
        private int _pbrPaletteEnabled;
        private int _pbrPaletteColor;
        private int _pbrMaterialSpecular;
        private int _pbrMaterialEmission;

        internal bool DeferredPbrReady => _pbrReady;
        internal int DeferredPbrAlbedo => _pbrAlbedoTexture;
        internal int DeferredPbrNormal => _pbrNormalTexture;
        internal int DeferredPbrMaterial => _pbrMaterialTexture;

        private void RenderDeferredPbrGBuffer()
        {
#if !MPHREAD_SERVER
            if (Mods.Render.ModernGraphicsCompat.Active) { _pbrReady = false; return; }
#endif
            _pbrReady = false;
            if (!RenderOptions.DeferredPbr || _pbrRefused || _depthTexture == 0)
            {
                return;
            }

            try
            {
                EnsureDeferredPbrTargets();
                if (_pbrProgram == 0 || _pbrFramebuffer == 0) return;

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pbrFramebuffer);
                GL.Viewport(0, 0, _targetSize.X, _targetSize.Y);
                GL.UseProgram(_pbrProgram);
                GL.Disable(EnableCap.Blend);
                GL.Disable(EnableCap.StencilTest);
                GL.Disable(EnableCap.AlphaTest);
                GL.Enable(EnableCap.DepthTest);
                // Replayed G-buffer geometry must match the depth that the
                // forward renderer actually accepted. LEQUAL lets a nearer
                // alpha-tested/discarded triangle paint PBR data over the
                // visible surface behind it, which shows up as giant pale
                // polygons. Exact depth equality turns the forward pass into
                // the G-buffer's visibility mask.
                GL.DepthFunc(DepthFunction.Equal);
                GL.DepthMask(false);
                GL.ColorMask(true, true, true, true);
                GL.PolygonMode(TriangleFace.FrontAndBack,
                    OpenTK.Graphics.OpenGL.PolygonMode.Fill);

                Matrix4 view = _viewMatrix;
                GL.UniformMatrix4(_pbrView, false, ref view);
                GL.Uniform1(_pbrBaseSampler, 0);
                GL.Uniform1(_pbrNormalSampler, 1);
                GL.Uniform1(_pbrSpecularSampler, 2);
                GL.Uniform1(_pbrEmissiveSampler, 3);

                for (int mode = 1; mode <= 3; mode++)
                {
                    int target = mode == 1 ? _pbrAlbedoTexture
                        : mode == 2 ? _pbrNormalTexture : _pbrMaterialTexture;
                    GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D,
                        target, 0);
                    GL.ClearColor(0, 0, 0, 0);
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                    GL.Uniform1(_pbrMode, mode);

                    for (int i = 0; i < _nonDecalItems.Count; i++)
                    {
                        RenderItem item = _nonDecalItems[i];
                        if (item.Type != RenderItemType.Mesh || item.ViewModel || item.Alpha < .999f
                            || item.RenderMode == RenderMode.Translucent)
                        {
                            continue;
                        }
                        DrawDeferredPbrItem(item);
                    }
                }
                _pbrReady = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _pbrRefused = true;
                _pbrReady = false;
                Console.WriteLine($"[render] deferred PBR unavailable: {ex.Message}");
                DisposeDeferredPbr();
                _pbrRefused = true;
            }
            finally
            {
                GL.ActiveTexture(TextureUnit.Texture3);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.DepthMask(true);
                GL.DepthFunc(DepthFunction.Lequal);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer);
                GL.UseProgram(_shaderProgramId);
                GL.Viewport(0, 0, _targetSize.X, _targetSize.Y);
                GL.ClearColor(_clearColor);
            }
        }

        private void EnsureDeferredPbrTargets()
        {
            if (_pbrProgram == 0)
            {
                int vertex = CompileDeferredPbrShader(ShaderType.VertexShader,
                    DeferredPbrShader.VertexSource);
                int fragment = 0;
                try
                {
                    fragment = CompileDeferredPbrShader(ShaderType.FragmentShader,
                        DeferredPbrShader.FragmentSource);
                    _pbrProgram = GL.CreateProgram();
                    GL.AttachShader(_pbrProgram, vertex);
                    GL.AttachShader(_pbrProgram, fragment);
                    GL.LinkProgram(_pbrProgram);
#if !ANDROID
                    GL.GetProgram(_pbrProgram, GetProgramParameterName.LinkStatus,
                        out int linked);
                    if (linked == 0)
                        throw new ProgramException(GL.GetProgramInfoLog(_pbrProgram));
#endif
                    _pbrMode = GL.GetUniformLocation(_pbrProgram, "gbuffer_mode");
                    _pbrProjection = GL.GetUniformLocation(_pbrProgram, "proj_mtx");
                    _pbrView = GL.GetUniformLocation(_pbrProgram, "view_mtx");
                    _pbrViewInv = GL.GetUniformLocation(_pbrProgram, "view_inv_mtx");
                    _pbrTextureMatrix = GL.GetUniformLocation(_pbrProgram, "tex_mtx");
                    _pbrMatrixStack = GL.GetUniformLocation(_pbrProgram, "mtx_stack");
                    _pbrTexgen = GL.GetUniformLocation(_pbrProgram, "texgen_mode");
                    _pbrUseTexture = GL.GetUniformLocation(_pbrProgram, "use_texture");
                    _pbrBaseSampler = GL.GetUniformLocation(_pbrProgram, "tex");
                    _pbrNormalSampler = GL.GetUniformLocation(_pbrProgram, "normal_tex");
                    _pbrSpecularSampler = GL.GetUniformLocation(_pbrProgram, "specular_tex");
                    _pbrEmissiveSampler = GL.GetUniformLocation(_pbrProgram, "emissive_tex");
                    _pbrCosmetics = new(_pbrProgram);
                    _pbrUseNormal = GL.GetUniformLocation(_pbrProgram, "use_normal_map");
                    _pbrUseSpecular = GL.GetUniformLocation(_pbrProgram, "use_specular_map");
                    _pbrUseEmissive = GL.GetUniformLocation(_pbrProgram, "use_emissive_map");
                    _pbrOverrideEnabled = GL.GetUniformLocation(_pbrProgram, "use_override");
                    _pbrOverrideColor = GL.GetUniformLocation(_pbrProgram, "override_color");
                    _pbrPaletteEnabled = GL.GetUniformLocation(_pbrProgram, "use_pal_override");
                    _pbrPaletteColor = GL.GetUniformLocation(_pbrProgram, "pal_override_color");
                    _pbrMaterialSpecular = GL.GetUniformLocation(_pbrProgram, "material_specular");
                    _pbrMaterialEmission = GL.GetUniformLocation(_pbrProgram, "material_emission");
                }
                finally
                {
                    GL.DeleteShader(vertex);
                    if (fragment != 0) GL.DeleteShader(fragment);
                }
            }

            if (_pbrFramebuffer == 0)
            {
                _pbrFramebuffer = GL.GenFramebuffer();
                _pbrAlbedoTexture = GL.GenTexture();
                _pbrNormalTexture = GL.GenTexture();
                _pbrMaterialTexture = GL.GenTexture();
            }

            bool resized = _pbrSize != _targetSize;
            if (resized)
            {
                AllocateDeferredPbrTexture(_pbrAlbedoTexture);
                AllocateDeferredPbrTexture(_pbrNormalTexture);
                AllocateDeferredPbrTexture(_pbrMaterialTexture);
                _pbrSize = _targetSize;
            }

            if (resized || _pbrDepthTexture != _depthTexture)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _pbrFramebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D,
                    _pbrAlbedoTexture, 0);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthStencilAttachment, TextureTarget.Texture2D,
                    _depthTexture, 0);
                ValidateFramebuffer("Deferred PBR G-buffer");
                _pbrDepthTexture = _depthTexture;
            }
        }

        private void AllocateDeferredPbrTexture(int texture)
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                _targetSize.X, _targetSize.Y, 0, PixelFormat.Rgba,
                PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void DrawDeferredPbrItem(RenderItem item)
        {
            Matrix4 projection = item.ViewModel ? _viewModelPerspectiveMatrix : _perspectiveMatrix;
            GL.UniformMatrix4(_pbrProjection, false, ref projection);

            if (item.MatrixStackCount > 0)
            {
                int matrixCount = Math.Clamp(item.MatrixStackCount, 0,
                    Math.Min(32, item.MatrixStack.Length / 16));
                if (matrixCount > 0)
                    GL.UniformMatrix4(_pbrMatrixStack, matrixCount, false, item.MatrixStack);
                else
                {
                    Matrix4 transform = item.Transform;
                    GL.UniformMatrix4(_pbrMatrixStack, false, ref transform);
                }
            }
            else
            {
                Matrix4 transform = item.Transform;
                GL.UniformMatrix4(_pbrMatrixStack, false, ref transform);
            }

            Matrix4 viewInv = Matrix4.Identity;
            if (item.BillboardMode == BillboardMode.Sphere) viewInv = _viewInvRotMatrix;
            else if (item.BillboardMode == BillboardMode.Cylinder) viewInv = _viewInvRotYMatrix;
            GL.UniformMatrix4(_pbrViewInv, false, ref viewInv);

            _pbrCosmetics?.Apply(item.Cosmetics);
            GL.Color3(item.Diffuse);
            GL.Uniform3(_pbrMaterialSpecular, item.Specular);
            GL.Uniform3(_pbrMaterialEmission, item.Emission);
            GL.Uniform1(_pbrOverrideEnabled, item.OverrideColor.HasValue ? 1 : 0);
            if (item.OverrideColor is Vector4 overrideColor)
                GL.Uniform4(_pbrOverrideColor, ref overrideColor);
            GL.Uniform1(_pbrPaletteEnabled, item.PaletteOverride.HasValue ? 1 : 0);
            if (item.PaletteOverride is Vector4 palette)
                GL.Uniform4(_pbrPaletteColor, ref palette);

            GL.Uniform1(_pbrUseTexture, item.HasTexture && _showTextures ? 1 : 0);
            GL.Uniform1(_pbrTexgen, (int)item.TexgenMode);
            Matrix4 texcoord = item.TexcoordMatrix;
            GL.UniformMatrix4(_pbrTextureMatrix, false, ref texcoord);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D,
                item.HasTexture && _showTextures ? item.TextureBindingId : 0);

            MaterialMapBindings maps = default;
            bool advanced = RenderOptions.AdvancedMaterials && item.HasTexture
                && _materialMaps.TryGetValue(item.TextureBindingId, out maps);
            if (RenderOptions.AdvancedMaterials && item.CosmeticMaterial != default)
            { maps = new(item.CosmeticMaterial.NormalBinding, item.CosmeticMaterial.SpecularBinding, item.CosmeticMaterial.EmissiveBinding); advanced = maps.Any; }
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, advanced ? maps.Normal : 0);
            GL.Uniform1(_pbrUseNormal, advanced && maps.Normal != 0 ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, advanced ? maps.Specular : 0);
            GL.Uniform1(_pbrUseSpecular, advanced && maps.Specular != 0 ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, advanced ? maps.Emissive : 0);
            GL.Uniform1(_pbrUseEmissive, advanced && maps.Emissive != 0 ? 1 : 0);
            GL.ActiveTexture(TextureUnit.Texture0);

            if (_faceCulling)
            {
                if (item.CullingMode == CullingMode.Neither) GL.Disable(EnableCap.CullFace);
                else
                {
                    GL.Enable(EnableCap.CullFace);
                    GL.CullFace(item.CullingMode == CullingMode.Back
                        ? TriangleFace.Back : TriangleFace.Front);
                }
            }
            GL.CallList(item.ListId);
        }

        private static int CompileDeferredPbrShader(ShaderType type, string source)
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

        private void DisposeDeferredPbr()
        {
            _pbrReady = false;
            _pbrSize = default;
            _pbrDepthTexture = 0;
            if (_pbrFramebuffer != 0)
            {
                GL.DeleteFramebuffer(_pbrFramebuffer);
                _pbrFramebuffer = 0;
            }
            DeleteTexture(ref _pbrAlbedoTexture);
            DeleteTexture(ref _pbrNormalTexture);
            DeleteTexture(ref _pbrMaterialTexture);
            DeleteProgram(ref _pbrProgram);
        }
    }
}

namespace MphRead.Mods.Render
{
    internal static class DeferredPbrShader
    {
#if ANDROID
        public static string VertexSource { get; } = @"#version 300 es
precision highp float;
precision highp int;
layout(location = 0) in vec4 a_position;
layout(location = 1) in vec4 a_color;
layout(location = 2) in vec3 a_normal;
layout(location = 3) in vec3 a_texcoord;
layout(location = 4) in float a_color_set;
uniform vec4 imm_color;
uniform mat4 proj_mtx;
uniform mat4 view_mtx;
uniform mat4 view_inv_mtx;
uniform mat4 tex_mtx;
uniform int texgen_mode;
uniform mat4 mtx_stack[32];
out vec2 texcoord;
out vec4 vertex_color;
out vec3 surface_normal;
out vec3 surface_position;
void main() {
    vec4 source_color = a_color_set > 0.5 ? a_color : imm_color;
    mat4 stack_mtx = mtx_stack[int(clamp(a_texcoord.z, 0.0, 31.0))];
    mat4 model_mtx = stack_mtx * view_inv_mtx;
    gl_Position = proj_mtx * view_mtx * model_mtx * a_position;
    vertex_color = vec4(source_color.rgb, 1.0);
    surface_normal = normalize(mat3(model_mtx) * a_normal);
    surface_position = (model_mtx * a_position).xyz;
    texcoord = vec2(0.0);
    if (texgen_mode == 0 || texgen_mode == 1) {
        texcoord = vec2(tex_mtx * vec4(a_texcoord.xy, 0.0, 1.0));
    }
    else {
        mat4 tex_mul = tex_mtx;
        if (texgen_mode == 2) tex_mul = transpose(tex_mtx * view_mtx * mat4(mat3(stack_mtx)));
        mat2x4 tm = mat2x4(
            vec4(tex_mul[0][0], tex_mul[0][1], tex_mul[0][2], a_texcoord.x),
            vec4(tex_mul[1][0], tex_mul[1][1], tex_mul[1][2], a_texcoord.y));
        texcoord = texgen_mode == 2 ? vec4(a_normal, 1.0) * tm
            : vec4(a_position.xyz, 1.0) * tm;
    }
}
";
        public static string FragmentSource { get; } = "#version 300 es\nprecision highp float;\n"
            + "precision highp int;\nin vec2 texcoord;\nin vec4 vertex_color;\n"
            + "in vec3 surface_normal;\nin vec3 surface_position;\nout vec4 frag_color;\n"
            + "#define SAMPLE texture\n#define OUTPUT frag_color\n" + Body;
#else
        public static string VertexSource { get; } = @"#version 120
uniform mat4 proj_mtx;
uniform mat4 view_mtx;
uniform mat4 view_inv_mtx;
uniform mat4 tex_mtx;
uniform int texgen_mode;
uniform mat4 mtx_stack[32];
varying vec2 texcoord;
varying vec4 vertex_color;
varying vec3 surface_normal;
varying vec3 surface_position;
void main() {
    mat4 stack_mtx = mtx_stack[int(clamp(gl_MultiTexCoord0.z, 0.0, 31.0))];
    mat4 model_mtx = stack_mtx * view_inv_mtx;
    gl_Position = proj_mtx * view_mtx * model_mtx * gl_Vertex;
    vertex_color = vec4(gl_Color.rgb, 1.0);
    surface_normal = normalize(mat3(model_mtx) * gl_Normal);
    surface_position = (model_mtx * gl_Vertex).xyz;
    texcoord = vec2(0.0);
    if (texgen_mode == 0 || texgen_mode == 1) {
        texcoord = vec2(tex_mtx * vec4(gl_MultiTexCoord0.xy, 0.0, 1.0));
    }
    else {
        mat4 tex_mul = tex_mtx;
        if (texgen_mode == 2) tex_mul = transpose(tex_mtx * view_mtx * mat4(mat3(stack_mtx)));
        mat2x4 tm = mat2x4(
            vec4(tex_mul[0][0], tex_mul[0][1], tex_mul[0][2], gl_MultiTexCoord0.x),
            vec4(tex_mul[1][0], tex_mul[1][1], tex_mul[1][2], gl_MultiTexCoord0.y));
        texcoord = texgen_mode == 2 ? vec4(gl_Normal, 1.0) * tm
            : vec4(gl_Vertex.xyz, 1.0) * tm;
    }
}
";
        public static string FragmentSource { get; } = "#version 120\nvarying vec2 texcoord;\n"
            + "varying vec4 vertex_color;\nvarying vec3 surface_normal;\n"
            + "varying vec3 surface_position;\n#define SAMPLE texture2D\n"
            + "#define OUTPUT gl_FragColor\n" + Body;
#endif

        private const string Body = @"
uniform int gbuffer_mode;
uniform bool use_texture;
uniform sampler2D tex;
uniform sampler2D normal_tex;
uniform sampler2D specular_tex;
uniform sampler2D emissive_tex;
uniform bool use_normal_map;
uniform bool use_specular_map;
uniform bool use_emissive_map;
uniform bool use_override;
uniform vec4 override_color;
uniform bool use_pal_override;
uniform vec4 pal_override_color;
uniform vec3 material_specular;
uniform vec3 material_emission;

" + Mods.Cosmetics.CosmeticShader.Source + @"
vec3 mapped_normal() {
    vec3 n = normalize(surface_normal);
    if (!use_normal_map) return n;
    vec3 dp1 = dFdx(surface_position), dp2 = dFdy(surface_position);
    vec2 duv1 = dFdx(texcoord), duv2 = dFdy(texcoord);
    float det = duv1.x * duv2.y - duv1.y * duv2.x;
    if (abs(det) < 0.000001) return n;
    vec3 tangent = normalize((dp1 * duv2.y - dp2 * duv1.y) / det);
    vec3 bitangent = normalize((-dp1 * duv2.x + dp2 * duv1.x) / det);
    vec3 nm = SAMPLE(normal_tex, texcoord).xyz * 2.0 - 1.0;
    return normalize(tangent * nm.x + bitangent * nm.y + n * nm.z);
}

void main() {
    vec4 base = use_texture ? SAMPLE(tex, texcoord) : vec4(1.0);
    if (base.a < 0.99) discard;
    vec3 albedo = base.rgb * vertex_color.rgb;
    if (use_pal_override) albedo = pal_override_color.rgb * vertex_color.rgb;
    if (use_override) albedo = override_color.rgb;
    vec4 cosmetic = vec4(albedo, 1.0);
    apply_cosmetics(cosmetic);
    albedo = cosmetic.rgb;

    if (gbuffer_mode == 1) {
        OUTPUT = vec4(clamp(albedo, 0.0, 1.0), 1.0);
        return;
    }
    if (gbuffer_mode == 2) {
        OUTPUT = vec4(mapped_normal() * 0.5 + 0.5, 1.0);
        return;
    }

    vec4 sm = use_specular_map ? SAMPLE(specular_tex, texcoord)
        : vec4(max(max(material_specular.r, material_specular.g),
            material_specular.b), 0.62, 0.0, 1.0);
    float roughness = clamp(sm.g, 0.04, 1.0);
    float metallic = smoothstep(0.45, 0.95, clamp(sm.r, 0.0, 1.0)) * 0.75;
    float emissive = max(max(material_emission.r, material_emission.g),
        material_emission.b);
    if (use_emissive_map) {
        vec3 e = SAMPLE(emissive_tex, texcoord).rgb;
        emissive = max(emissive, dot(e, vec3(0.2126, 0.7152, 0.0722)));
    }
    OUTPUT = vec4(metallic, roughness, clamp(emissive, 0.0, 1.0), 1.0);
}
";
    }
}
