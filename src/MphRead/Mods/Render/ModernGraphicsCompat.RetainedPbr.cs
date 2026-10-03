#if !MPHREAD_SERVER
using System;
using MphRead;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Silk.NET.WebGPU;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        private sealed class RetainedPbrUniformOffsets
        {
            internal readonly int Projection;
            internal readonly int ViewInverse;
            internal readonly int TextureMatrix;
            internal readonly int TexgenMode;
            internal readonly int MatrixStack;
            internal readonly int GBufferMode;
            internal readonly int UseTexture;
            internal readonly int UseNormalMap;
            internal readonly int UseSpecularMap;
            internal readonly int UseEmissiveMap;
            internal readonly int UseOverride;
            internal readonly int OverrideColor;
            internal readonly int UsePaletteOverride;
            internal readonly int PaletteOverrideColor;
            internal readonly int MaterialSpecular;
            internal readonly int MaterialEmission;
            internal readonly int CosmeticSkin;
            internal readonly int CosmeticPreservePalette;
            internal readonly int CosmeticEffect;
            internal readonly int PrimeImmColor;
            internal readonly int PrimeImmNormal;

            internal RetainedPbrUniformOffsets(ModernShaderLayout layout)
            {
                Projection = Word(layout, "proj_mtx");
                ViewInverse = Word(layout, "view_inv_mtx");
                TextureMatrix = Word(layout, "tex_mtx");
                TexgenMode = Word(layout, "texgen_mode");
                MatrixStack = Word(layout, "mtx_stack");
                GBufferMode = Word(layout, "gbuffer_mode");
                UseTexture = Word(layout, "use_texture");
                UseNormalMap = Word(layout, "use_normal_map");
                UseSpecularMap = Word(layout, "use_specular_map");
                UseEmissiveMap = Word(layout, "use_emissive_map");
                UseOverride = Word(layout, "use_override");
                OverrideColor = Word(layout, "override_color");
                UsePaletteOverride = Word(layout, "use_pal_override");
                PaletteOverrideColor = Word(layout, "pal_override_color");
                MaterialSpecular = Word(layout, "material_specular");
                MaterialEmission = Word(layout, "material_emission");
                CosmeticSkin = Word(layout, "cosmetic_skin");
                CosmeticPreservePalette = Word(layout, "cosmetic_preserve_palette");
                CosmeticEffect = Word(layout, "cosmetic_effect");
                PrimeImmColor = Word(layout, "prime_imm_color");
                PrimeImmNormal = Word(layout, "prime_imm_normal");
            }

            private static int Word(ModernShaderLayout layout, string name)
            {
                for (int i = 0; i < layout.Uniforms.Length; i++)
                {
                    if (layout.Uniforms[i].Name == name)
                        return layout.Uniforms[i].Offset / 4;
                }
                throw new InvalidOperationException(
                    $"Generated DeferredPbrMrt layout is missing retained uniform '{name}'.");
            }
        }

        private uint[]? _retainedPbrFrameTemplate;
        private RetainedPbrUniformOffsets? _retainedPbrOffsets;
        private int _retainedPbrFrameFramebuffer = -1;
        private int _retainedPbrFrameWidth;
        private int _retainedPbrFrameHeight;
        private bool _retainedPbrFrameReady;
        private long _retainedPbrTemplateBuilds;
        private long _retainedPbrUniformPatches;

        internal static long RetainedPbrTemplateBuilds =>
            _current?._retainedPbrTemplateBuilds ?? 0;
        internal static long RetainedPbrUniformPatches =>
            _current?._retainedPbrUniformPatches ?? 0;

        internal static bool ValidateRetainedDeferredPbrLayout(out string error)
        {
            try
            {
                ModernShaderLayout layout =
                    GeneratedShaderLayouts.Get(ModernProgramKind.DeferredPbrMrt);
                _ = new RetainedPbrUniformOffsets(layout);
                string[] expectedSamplers =
                    { "tex", "normal_tex", "specular_tex", "emissive_tex" };
                if (layout.Samplers.Length != expectedSamplers.Length)
                {
                    error = $"generated DeferredPbrMrt sampler count is {layout.Samplers.Length}, expected {expectedSamplers.Length}";
                    return false;
                }
                for (int i = 0; i < expectedSamplers.Length; i++)
                {
                    if (layout.Samplers[i] != expectedSamplers[i])
                    {
                        error = $"generated DeferredPbrMrt sampler {i} is '{layout.Samplers[i]}', expected '{expectedSamplers[i]}'";
                        return false;
                    }
                }
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool RetainedDeferredPbrPacketEligible(RenderItem item) =>
            item.Type == RenderItemType.Mesh
            && !item.ViewModel
            && item.RenderMode == RenderMode.Normal
            && item.Alpha >= 0.999f
            && (uint)item.BillboardMode <= (uint)BillboardMode.Cylinder
            && item.MatrixStackCount >= 0
            && item.MatrixStackCount <= Math.Min(
                32, item.MatrixStack.Length / 16)
            && item.Cosmetics == default
            && item.CosmeticMaterial == default;

        internal static bool BeginRetainedDeferredPbrFrame()
        {
            if (_current == null) return false;
            return Current.BeginRetainedDeferredPbrFrameCore();
        }

        private bool BeginRetainedDeferredPbrFrameCore()
        {
            _retainedPbrFrameReady = false;
            if (CurrentProgramKind() != ModernProgramKind.DeferredPbr)
                return false;

            ModernGraphicsCompatState.ProgramRecord program =
                _programs.Program(_programs.CurrentProgram);
            if (Int(program, "gbuffer_mode") != 0)
                return false;

            CoreTarget target = ResolveDrawTarget();
            if (target.ColorTargetCount < 3)
                return false;

            GeneratedProgram generated =
                GeneratedShader(ModernProgramKind.DeferredPbrMrt);
            PopulateGeneratedUniformWords(
                ModernProgramKind.DeferredPbrMrt, target, generated, program);

            if (_retainedPbrFrameTemplate == null
                || _retainedPbrFrameTemplate.Length != generated.Words.Length)
            {
                _retainedPbrFrameTemplate =
                    new uint[generated.Words.Length];
            }
            Array.Copy(generated.Words, _retainedPbrFrameTemplate,
                generated.Words.Length);
            _retainedPbrOffsets ??=
                new RetainedPbrUniformOffsets(generated.Layout);
            _retainedPbrFrameFramebuffer = _resources.DrawFramebuffer;
            _retainedPbrFrameWidth = target.Width;
            _retainedPbrFrameHeight = target.Height;
            _retainedPbrFrameReady = true;
            _retainedPbrTemplateBuilds++;
            return true;
        }

        internal static bool TryDrawRetainedDeferredPbrMrt(
            RenderItem item, RetainedMeshDescriptor mesh,
            RetainedWorldTextureSet textures, bool showTextures,
            bool faceCulling, Matrix4 projection, Matrix4 viewInverse)
        {
            if (_current == null
                || !RetainedDeferredPbrPacketEligible(item))
            {
                return false;
            }
            return Current.TryDrawRetainedDeferredPbrMrtCore(
                item, mesh, textures, showTextures, faceCulling,
                projection, viewInverse);
        }

        private bool TryDrawRetainedDeferredPbrMrtCore(
            RenderItem item, RetainedMeshDescriptor mesh,
            RetainedWorldTextureSet textures, bool showTextures,
            bool faceCulling, Matrix4 projection, Matrix4 viewInverse)
        {
            if (!_retainedPbrFrameReady
                || CurrentProgramKind() != ModernProgramKind.DeferredPbr)
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
                    native = PrepareGeometry(
                        geometry.Vertices, geometry.Triangles);
                    geometry.TriangleGeometry = native;
                }
                finally
                {
                    _drawingList = drawingList;
                }
            }

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

            BindRetainedWorldTextures(
                textures, item.XRepeat, item.YRepeat);

            CoreTarget target = ResolveDrawTarget();
            if (target.ColorTargetCount < 3
                || _retainedPbrFrameTemplate == null
                || _retainedPbrOffsets == null
                || _retainedPbrFrameFramebuffer != _resources.DrawFramebuffer
                || _retainedPbrFrameWidth != target.Width
                || _retainedPbrFrameHeight != target.Height)
            {
                return false;
            }

            GeneratedProgram generated =
                GeneratedShader(ModernProgramKind.DeferredPbrMrt);
            Array.Copy(_retainedPbrFrameTemplate, generated.Words,
                generated.Words.Length);
            PatchRetainedDeferredPbrWords(
                generated, target, item, textures, showTextures,
                projection, viewInverse);
            UploadGeneratedUniformWords(generated);

            CorePipelineRecord pipeline = CorePipeline(
                ModernProgramKind.DeferredPbrMrt,
                PrimitiveTopology.TriangleList, target);
            BindGroup* bindGroup = GeneratedBindGroup(
                ModernProgramKind.DeferredPbrMrt, pipeline.Layout);

            ulong vertexBytes = checked(
                (ulong)geometry.Vertices.Length * sizeof(float));
            ulong indexBytes = checked(
                (ulong)geometry.Triangles.Length * sizeof(int));
            RenderPassEncoder* pass =
                CoreRenderPass(target, 3, pipeline.Pipeline);
            _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
            _api.RenderPassEncoderSetBindGroup(
                pass, 0, bindGroup, 0, null);
            _api.RenderPassEncoderSetVertexBuffer(
                pass, 0, native.Vertex, native.VertexOffset, vertexBytes);
            _api.RenderPassEncoderSetIndexBuffer(
                pass, native.Index, IndexFormat.Uint32,
                native.IndexOffset, indexBytes);
            _api.RenderPassEncoderSetViewport(
                pass, 0, 0, target.Width, target.Height, 0, 1);
            ApplyScissor(pass, target.Width, target.Height);
            _api.RenderPassEncoderDrawIndexed(
                pass, (uint)geometry.Triangles.Length, 1, 0, 0, 0);
            if (_measurePerformance) _coreDraws++;
            RecordCommandOperation();

            if (geometry.EndNormal is Vector3 normal)
                _currentNormal = normal;
            if (geometry.EndColor is Vector4 color)
                _currentColor = color;
            return true;
        }

        private void PatchRetainedDeferredPbrWords(
            GeneratedProgram generated, CoreTarget target,
            RenderItem item, RetainedWorldTextureSet textures,
            bool showTextures, Matrix4 projection, Matrix4 viewInverse)
        {
            uint[] words = generated.Words;
            RetainedPbrUniformOffsets o = _retainedPbrOffsets!;

            RetainedMatrix(words, o.Projection, projection);
            RetainedMatrix(words, o.ViewInverse, viewInverse);
            RetainedMatrix(words, o.TextureMatrix, item.TexcoordMatrix);
            RetainedInt(words, o.TexgenMode, (int)item.TexgenMode);
            if (item.MatrixStackCount > 0)
            {
                RetainedMatrices(words, o.MatrixStack,
                    item.MatrixStack, item.MatrixStackCount);
            }
            else
            {
                RetainedMatrix(words, o.MatrixStack, item.Transform);
            }

            RetainedInt(words, o.GBufferMode, 0);
            RetainedInt(words, o.UseTexture,
                item.HasTexture && showTextures
                    && textures.Albedo.IsBound ? 1 : 0);
            RetainedInt(words, o.UseNormalMap,
                textures.Normal.IsBound ? 1 : 0);
            RetainedInt(words, o.UseSpecularMap,
                textures.Specular.IsBound ? 1 : 0);
            RetainedInt(words, o.UseEmissiveMap,
                textures.Emissive.IsBound ? 1 : 0);

            if (item.OverrideColor.HasValue)
            {
                RetainedInt(words, o.UseOverride, 1);
                RetainedVec4(
                    words, o.OverrideColor, item.OverrideColor.Value);
            }
            else
            {
                RetainedInt(words, o.UseOverride, 0);
            }

            if (item.PaletteOverride.HasValue)
            {
                RetainedInt(words, o.UsePaletteOverride, 1);
                RetainedVec4(words, o.PaletteOverrideColor,
                    item.PaletteOverride.Value);
            }
            else
            {
                RetainedInt(words, o.UsePaletteOverride, 0);
            }

            RetainedVec3(words, o.MaterialSpecular, item.Specular);
            RetainedVec3(words, o.MaterialEmission, item.Emission);

            // Cosmetic surfaces remain on the compatibility PBR path in this
            // first direct MRT slice. Force the generated gates off.
            RetainedInt(words, o.CosmeticSkin, 0);
            RetainedInt(words, o.CosmeticPreservePalette, 0);
            RetainedInt(words, o.CosmeticEffect, 0);

            RetainedVec4(words, o.PrimeImmColor,
                new Vector4(item.Diffuse, 1f));
            RetainedVec3(words, o.PrimeImmNormal, _currentNormal);

            for (int unit = 0; unit < generated.Textures.Length; unit++)
            {
                RetainedTextureBinding binding = textures.At(unit);
                bool required = unit == 0
                    ? item.HasTexture && showTextures && binding.IsBound
                    : binding.IsBound;
                int id = ValidateRenderPassResources(
                    unit, required, target);
                generated.Textures[unit] = id;

                int flip = generated.Layout.FlipOffset / 4 + unit * 4;
                generated.Words[flip] =
                    BitConverter.SingleToUInt32Bits(
                        _resources.IsFramebufferTexture(id) ? 1f : 0f);
                generated.Words[flip + 1] = 0;
                generated.Words[flip + 2] = 0;
                generated.Words[flip + 3] = 0;
            }

            _retainedPbrUniformPatches++;
        }
    }
}
#endif
