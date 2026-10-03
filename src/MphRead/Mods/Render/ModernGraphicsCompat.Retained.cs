#if !MPHREAD_SERVER
using System;
using MphRead;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        private sealed class RetainedWorldUniformOffsets
        {
            internal readonly int UseLight;
            internal readonly int UseTexture;
            internal readonly int Light1Vector;
            internal readonly int Light1Color;
            internal readonly int Light2Vector;
            internal readonly int Light2Color;
            internal readonly int Diffuse;
            internal readonly int Ambient;
            internal readonly int Specular;
            internal readonly int Emission;
            internal readonly int ViewInverse;
            internal readonly int TextureMatrix;
            internal readonly int TexgenMode;
            internal readonly int MatrixStack;
            internal readonly int AdvancedMaterials;
            internal readonly int UseNormalMap;
            internal readonly int UseSpecularMap;
            internal readonly int UseEmissiveMap;
            internal readonly int UseOverride;
            internal readonly int TexturedPlayerSkin;
            internal readonly int PlayerOutlineMask;
            internal readonly int UsePaletteOverride;
            internal readonly int MaterialAlpha;
            internal readonly int MaterialMode;
            internal readonly int UseFlat;
            internal readonly int CosmeticSkin;
            internal readonly int CosmeticPreservePalette;
            internal readonly int CosmeticEffect;
            internal readonly int PrimeImmColor;
            internal readonly int PrimeImmNormal;
            internal readonly int PrimeAlphaFunction;
            internal readonly int PrimeAlphaReference;

            internal RetainedWorldUniformOffsets(ModernShaderLayout layout)
            {
                UseLight = Word(layout, "use_light");
                UseTexture = Word(layout, "use_texture");
                Light1Vector = Word(layout, "light1vec");
                Light1Color = Word(layout, "light1col");
                Light2Vector = Word(layout, "light2vec");
                Light2Color = Word(layout, "light2col");
                Diffuse = Word(layout, "diffuse");
                Ambient = Word(layout, "ambient");
                Specular = Word(layout, "specular");
                Emission = Word(layout, "emission");
                ViewInverse = Word(layout, "view_inv_mtx");
                TextureMatrix = Word(layout, "tex_mtx");
                TexgenMode = Word(layout, "texgen_mode");
                MatrixStack = Word(layout, "mtx_stack");
                AdvancedMaterials = Word(layout, "advanced_materials");
                UseNormalMap = Word(layout, "use_normal_map");
                UseSpecularMap = Word(layout, "use_specular_map");
                UseEmissiveMap = Word(layout, "use_emissive_map");
                UseOverride = Word(layout, "use_override");
                TexturedPlayerSkin = Word(layout, "textured_player_skin");
                PlayerOutlineMask = Word(layout, "player_outline_mask");
                UsePaletteOverride = Word(layout, "use_pal_override");
                MaterialAlpha = Word(layout, "mat_alpha");
                MaterialMode = Word(layout, "mat_mode");
                UseFlat = Word(layout, "use_flat");
                CosmeticSkin = Word(layout, "cosmetic_skin");
                CosmeticPreservePalette = Word(layout, "cosmetic_preserve_palette");
                CosmeticEffect = Word(layout, "cosmetic_effect");
                PrimeImmColor = Word(layout, "prime_imm_color");
                PrimeImmNormal = Word(layout, "prime_imm_normal");
                PrimeAlphaFunction = Word(layout, "prime_alpha_func");
                PrimeAlphaReference = Word(layout, "prime_alpha_ref");
            }

            private static int Word(ModernShaderLayout layout, string name)
            {
                for (int i = 0; i < layout.Uniforms.Length; i++)
                {
                    if (layout.Uniforms[i].Name == name)
                        return layout.Uniforms[i].Offset / 4;
                }
                throw new InvalidOperationException(
                    $"Generated World layout is missing retained uniform '{name}'.");
            }
        }

        private uint[]? _retainedWorldFrameTemplate;
        private RetainedWorldUniformOffsets? _retainedWorldOffsets;
        private int _retainedWorldFrameFramebuffer = -1;
        private int _retainedWorldFrameWidth;
        private int _retainedWorldFrameHeight;
        private bool _retainedWorldFrameReady;
        private long _retainedWorldUniformTemplateBuilds;
        private long _retainedWorldUniformPatches;

        internal static long RetainedWorldUniformTemplateBuilds =>
            _current?._retainedWorldUniformTemplateBuilds ?? 0;
        internal static long RetainedWorldUniformPatches =>
            _current?._retainedWorldUniformPatches ?? 0;

        internal static void BeginRetainedWorldFrame()
        {
            if (_current != null) Current.BeginRetainedWorldFrameCore();
        }

        private void BeginRetainedWorldFrameCore()
        {
            _retainedWorldFrameReady = false;
            if (CurrentProgramKind() != ModernProgramKind.World)
                return;
            if (_resources.DrawFramebuffer == 0 && !AcquireSurfaceTexture())
                return;

            CoreTarget target = ResolveDrawTarget();
            GeneratedProgram generated = GeneratedShader(ModernProgramKind.World);
            ModernGraphicsCompatState.ProgramRecord program =
                _programs.Program(_programs.CurrentProgram);
            PopulateGeneratedUniformWords(
                ModernProgramKind.World, target, generated, program);

            if (_retainedWorldFrameTemplate == null
                || _retainedWorldFrameTemplate.Length != generated.Words.Length)
            {
                _retainedWorldFrameTemplate = new uint[generated.Words.Length];
            }
            Array.Copy(generated.Words, _retainedWorldFrameTemplate,
                generated.Words.Length);
            _retainedWorldOffsets ??=
                new RetainedWorldUniformOffsets(generated.Layout);
            _retainedWorldFrameFramebuffer = _resources.DrawFramebuffer;
            _retainedWorldFrameWidth = target.Width;
            _retainedWorldFrameHeight = target.Height;
            _retainedWorldFrameReady = true;
            _retainedWorldUniformTemplateBuilds++;
        }

        internal static bool RetainedWorldPacketEligible(RenderItem item) =>
            item.Type == RenderItemType.Mesh
            && item.RenderMode == RenderMode.Normal
            && item.Alpha >= 0.999f
            && !item.ViewModel
            && item.BillboardMode == BillboardMode.None
            && !item.Wireframe
            && item.MatrixStackCount == 0
            && item.Cosmetics == default
            && item.CosmeticMaterial == default
            && item.OverrideColor == null
            && item.PaletteOverride == null
            && !item.TexturedPlayerSkin
            && item.PlayerOutlineColor == null;

        internal static bool TryDrawRetainedWorld(RenderItem item,
            RetainedMeshDescriptor mesh, TextureSamplerDescriptor sampling,
            bool showTextures, bool useLighting, bool faceCulling)
        {
            if (_current == null || !RetainedWorldPacketEligible(item)) return false;
            return Current.TryDrawRetainedWorldCore(item, mesh, sampling,
                showTextures, useLighting, faceCulling);
        }

        private bool TryDrawRetainedWorldCore(RenderItem item,
            RetainedMeshDescriptor mesh, TextureSamplerDescriptor sampling,
            bool showTextures, bool useLighting, bool faceCulling)
        {
            if (CurrentProgramKind() != ModernProgramKind.World
                || _wireframe
                || RenderOptions.AdvancedMaterials
                || RenderOptions.CelShading)
            {
                return false;
            }

            if (!_lists.TryGetValue(mesh.ListId, out GeometryList? geometry)
                || geometry.Vertices.Length == 0
                || geometry.Triangles.Length == 0
                || geometry.Lines.Length != 0)
            {
                return false;
            }

            NativeGeometry? native = geometry.TriangleGeometry;
            if (native == null || native.Vertex == null || native.Index == null)
            {
                bool drawingList = _drawingList;
                _drawingList = true;
                try
                {
                    native = PrepareGeometry(geometry.Vertices, geometry.Triangles);
                    geometry.TriangleGeometry = native;
                }
                finally
                {
                    _drawingList = drawingList;
                }
            }

            // Keep the tiny pieces of compatibility shadow state that are
            // observable by a later fallback draw. Per-packet shader uniforms
            // are written directly from the retained template below.
            // Keep compatibility shadow state synchronized even though this path
            // does not replay the public GL-style calls.
            _currentColor = new Vector4(item.Diffuse, 1f);
            if (faceCulling && item.CullingMode != CullingMode.Neither)
            {
                _enabled.Add(EnableCap.CullFace);
                _cullFace = item.CullingMode == CullingMode.Back
                    ? TriangleFace.Back : TriangleFace.Front;
            }
            else
            {
                _enabled.Remove(EnableCap.CullFace);
            }

            int baseTexture =
                BindRetainedWorldTextures(item, sampling, showTextures);

            if (_resources.DrawFramebuffer == 0 && !AcquireSurfaceTexture())
                return true;
            _device.ThrowIfFailed();
            CoreTarget target = ResolveDrawTarget();

            if (!_retainedWorldFrameReady
                || _retainedWorldFrameTemplate == null
                || _retainedWorldOffsets == null
                || _retainedWorldFrameFramebuffer != _resources.DrawFramebuffer
                || _retainedWorldFrameWidth != target.Width
                || _retainedWorldFrameHeight != target.Height)
            {
                return false;
            }

            GeneratedProgram generated = GeneratedShader(ModernProgramKind.World);
            Array.Copy(_retainedWorldFrameTemplate, generated.Words,
                generated.Words.Length);
            PatchRetainedWorldUniformWords(
                generated, target, item, baseTexture, showTextures, useLighting);
            UploadGeneratedUniformWords(generated);

            CorePipelineRecord pipeline = CorePipeline(
                ModernProgramKind.World, PrimitiveTopology.TriangleList, target);
            BindGroup* bindGroup = GeneratedBindGroup(
                ModernProgramKind.World, pipeline.Layout);

            ulong vertexBytes = checked((ulong)geometry.Vertices.Length * sizeof(float));
            ulong indexBytes = checked((ulong)geometry.Triangles.Length * sizeof(int));
            RenderPassEncoder* pass = CoreRenderPass(target, 1, pipeline.Pipeline);
            _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
            _api.RenderPassEncoderSetVertexBuffer(pass, 0, native.Vertex,
                native.VertexOffset, vertexBytes);
            _api.RenderPassEncoderSetIndexBuffer(pass, native.Index,
                IndexFormat.Uint32, native.IndexOffset, indexBytes);
            _api.RenderPassEncoderSetViewport(pass, 0, 0,
                target.Width, target.Height, 0, 1);
            ApplyScissor(pass, target.Width, target.Height);
            if (_enabled.Contains(EnableCap.StencilTest) && target.HasDepth)
                _api.RenderPassEncoderSetStencilReference(pass, (uint)_stencilReference);
            _api.RenderPassEncoderDrawIndexed(pass,
                (uint)geometry.Triangles.Length, 1, 0, 0, 0);
            if (_measurePerformance) _coreDraws++;
            RecordCommandOperation();

            if (geometry.EndNormal is Vector3 normal) _currentNormal = normal;
            if (geometry.EndColor is Vector4 color) _currentColor = color;
            return true;
        }

        private int BindRetainedWorldTextures(RenderItem item,
            TextureSamplerDescriptor sampling, bool showTextures)
        {
            int baseTexture = item.HasTexture && showTextures
                ? item.TextureBindingId : 0;

            _resources.ActiveTexture(TextureUnit.Texture0);
            _resources.BindTexture(TextureTarget.Texture2D, baseTexture);
            if (baseTexture != 0)
                ApplyRetainedTextureSampling(baseTexture, sampling,
                    item.XRepeat, item.YRepeat);

            _resources.ActiveTexture(TextureUnit.Texture1);
            _resources.BindTexture(TextureTarget.Texture2D, 0);
            _resources.ActiveTexture(TextureUnit.Texture2);
            _resources.BindTexture(TextureTarget.Texture2D, 0);
            _resources.ActiveTexture(TextureUnit.Texture3);
            _resources.BindTexture(TextureTarget.Texture2D, 0);
            _resources.ActiveTexture(TextureUnit.Texture0);
            return baseTexture;
        }

        private void ApplyRetainedTextureSampling(int texture,
            TextureSamplerDescriptor sampling, RepeatMode xRepeat, RepeatMode yRepeat)
        {
            ModernGraphicsResourceState.TextureRecord record = _resources.Texture(texture);
            int minFilter = sampling.Mipmaps
                ? (int)(sampling.LinearMinification
                    ? TextureMinFilter.LinearMipmapLinear
                    : TextureMinFilter.NearestMipmapNearest)
                : (int)(sampling.LinearMinification
                    ? TextureMinFilter.Linear : TextureMinFilter.Nearest);
            int magFilter = (int)(sampling.LinearMagnification
                ? TextureMagFilter.Linear : TextureMagFilter.Nearest);
            int wrapS = (int)RetainedTextureWrap(xRepeat);
            int wrapT = (int)RetainedTextureWrap(yRepeat);
            int anisotropy = Math.Clamp(sampling.Anisotropy, 1, 16);

            bool samplerChanged = record.MinFilter != minFilter
                || record.MagFilter != magFilter
                || record.WrapS != wrapS
                || record.WrapT != wrapT
                || record.Anisotropy != anisotropy;
            record.MinFilter = minFilter;
            record.MagFilter = magFilter;
            record.WrapS = wrapS;
            record.WrapT = wrapT;
            record.Anisotropy = anisotropy;

            if (sampling.Mipmaps
                && record.CompressionFormat == GpuTextureCompressionFormat.None
                && !record.HasMipmaps)
            {
                record.HasMipmaps = true;
                record.MipmapsDirty = true;
                samplerChanged = true;
            }
            if (samplerChanged) record.SamplerDirty = true;
            _ = EnsureTexture(texture);
        }

        private static TextureWrapMode RetainedTextureWrap(RepeatMode mode) => mode switch
        {
            RepeatMode.Repeat => TextureWrapMode.Repeat,
            RepeatMode.Mirror => TextureWrapMode.MirroredRepeat,
            _ => TextureWrapMode.ClampToEdge
        };

        private void PatchRetainedWorldUniformWords(
            GeneratedProgram generated, CoreTarget target, RenderItem item,
            int baseTexture, bool showTextures, bool useLighting)
        {
            uint[] words = generated.Words;
            RetainedWorldUniformOffsets o = _retainedWorldOffsets!;

            RetainedInt(words, o.UseLight,
                useLighting && item.Lighting ? 1 : 0);
            RetainedInt(words, o.UseTexture,
                item.HasTexture && showTextures && baseTexture != 0 ? 1 : 0);
            RetainedVec3(words, o.Light1Vector, item.LightInfo.Light1Vector);
            RetainedVec3(words, o.Light1Color, item.LightInfo.Light1Color);
            RetainedVec3(words, o.Light2Vector, item.LightInfo.Light2Vector);
            RetainedVec3(words, o.Light2Color, item.LightInfo.Light2Color);
            RetainedVec3(words, o.Diffuse, item.Diffuse);
            RetainedVec3(words, o.Ambient, item.Ambient);
            RetainedVec3(words, o.Specular, item.Specular);
            RetainedVec3(words, o.Emission, item.Emission);
            RetainedMatrix(words, o.ViewInverse, Matrix4.Identity);
            RetainedMatrix(words, o.TextureMatrix, item.TexcoordMatrix);
            RetainedInt(words, o.TexgenMode, (int)item.TexgenMode);
            RetainedMatrix(words, o.MatrixStack, item.Transform);
            RetainedFloat(words, o.MaterialAlpha, item.Alpha);
            RetainedInt(words, o.MaterialMode, (int)item.PolygonMode);

            // Direct eligibility excludes these features. Force their shader
            // gates off so stale compatibility state cannot leak into a packet.
            RetainedInt(words, o.AdvancedMaterials, 0);
            RetainedInt(words, o.UseNormalMap, 0);
            RetainedInt(words, o.UseSpecularMap, 0);
            RetainedInt(words, o.UseEmissiveMap, 0);
            RetainedInt(words, o.UseOverride, 0);
            RetainedInt(words, o.TexturedPlayerSkin, 0);
            RetainedInt(words, o.PlayerOutlineMask, 0);
            RetainedInt(words, o.UsePaletteOverride, 0);
            RetainedInt(words, o.UseFlat, 0);
            RetainedInt(words, o.CosmeticSkin, 0);
            RetainedInt(words, o.CosmeticPreservePalette, 0);
            RetainedInt(words, o.CosmeticEffect, 0);

            RetainedVec4(words, o.PrimeImmColor,
                new Vector4(item.Diffuse, 1f));
            RetainedVec3(words, o.PrimeImmNormal, _currentNormal);
            RetainedInt(words, o.PrimeAlphaFunction,
                (int)(_enabled.Contains(EnableCap.AlphaTest)
                    ? _alphaFunction : AlphaFunction.Always));
            RetainedFloat(words, o.PrimeAlphaReference, _alphaReference);

            // Direct World uses unit zero only. Companion maps stay disabled.
            for (int i = 0; i < generated.Textures.Length; i++)
            {
                generated.Textures[i] = 0;
                int flip = generated.Layout.FlipOffset / 4 + i * 4;
                generated.Words[flip] = 0;
                generated.Words[flip + 1] = 0;
                generated.Words[flip + 2] = 0;
                generated.Words[flip + 3] = 0;
            }
            if (generated.Textures.Length > 0 && baseTexture != 0)
            {
                generated.Textures[0] = ValidateRenderPassResources(
                    0, required: true, target);
            }

            _retainedWorldUniformPatches++;
        }

        private static void RetainedInt(uint[] words, int at, int value) =>
            words[at] = unchecked((uint)value);

        private static void RetainedFloat(uint[] words, int at, float value) =>
            words[at] = BitConverter.SingleToUInt32Bits(value);

        private static void RetainedVec3(uint[] words, int at, Vector3 value)
        {
            RetainedFloat(words, at, value.X);
            RetainedFloat(words, at + 1, value.Y);
            RetainedFloat(words, at + 2, value.Z);
        }

        private static void RetainedVec4(uint[] words, int at, Vector4 value)
        {
            RetainedFloat(words, at, value.X);
            RetainedFloat(words, at + 1, value.Y);
            RetainedFloat(words, at + 2, value.Z);
            RetainedFloat(words, at + 3, value.W);
        }

        private static void RetainedMatrix(uint[] words, int at, Matrix4 value)
        {
            RetainedFloat(words, at, value.M11);
            RetainedFloat(words, at + 1, value.M12);
            RetainedFloat(words, at + 2, value.M13);
            RetainedFloat(words, at + 3, value.M14);
            RetainedFloat(words, at + 4, value.M21);
            RetainedFloat(words, at + 5, value.M22);
            RetainedFloat(words, at + 6, value.M23);
            RetainedFloat(words, at + 7, value.M24);
            RetainedFloat(words, at + 8, value.M31);
            RetainedFloat(words, at + 9, value.M32);
            RetainedFloat(words, at + 10, value.M33);
            RetainedFloat(words, at + 11, value.M34);
            RetainedFloat(words, at + 12, value.M41);
            RetainedFloat(words, at + 13, value.M42);
            RetainedFloat(words, at + 14, value.M43);
            RetainedFloat(words, at + 15, value.M44);
        }

    }
}
#endif
