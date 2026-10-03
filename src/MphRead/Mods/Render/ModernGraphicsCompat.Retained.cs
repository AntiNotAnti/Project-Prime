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
            internal readonly int Projection;
            internal readonly int ViewInverse;
            internal readonly int TextureMatrix;
            internal readonly int TexgenMode;
            internal readonly int MatrixStack;
            internal readonly int AdvancedMaterials;
            internal readonly int UseNormalMap;
            internal readonly int UseSpecularMap;
            internal readonly int UseEmissiveMap;
            internal readonly int UseOverride;
            internal readonly int OverrideColor;
            internal readonly int TexturedPlayerSkin;
            internal readonly int PlayerOutlineMask;
            internal readonly int UsePaletteOverride;
            internal readonly int PaletteOverrideColor;
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
                Projection = Word(layout, "proj_mtx");
                ViewInverse = Word(layout, "view_inv_mtx");
                TextureMatrix = Word(layout, "tex_mtx");
                TexgenMode = Word(layout, "texgen_mode");
                MatrixStack = Word(layout, "mtx_stack");
                AdvancedMaterials = Word(layout, "advanced_materials");
                UseNormalMap = Word(layout, "use_normal_map");
                UseSpecularMap = Word(layout, "use_specular_map");
                UseEmissiveMap = Word(layout, "use_emissive_map");
                UseOverride = Word(layout, "use_override");
                OverrideColor = Word(layout, "override_color");
                TexturedPlayerSkin = Word(layout, "textured_player_skin");
                PlayerOutlineMask = Word(layout, "player_outline_mask");
                UsePaletteOverride = Word(layout, "use_pal_override");
                PaletteOverrideColor = Word(layout, "pal_override_color");
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
        private readonly System.Collections.Generic.List<GeneratedBindGroupCacheEntry>
            _retainedWorldBindGroups = new();
        private long _retainedWorldBindGroupHits;
        private long _retainedWorldBindGroupMisses;

        internal static long RetainedWorldUniformTemplateBuilds =>
            _current?._retainedWorldUniformTemplateBuilds ?? 0;
        internal static long RetainedWorldUniformPatches =>
            _current?._retainedWorldUniformPatches ?? 0;
        internal static long RetainedWorldBindGroupHits =>
            _current?._retainedWorldBindGroupHits ?? 0;
        internal static long RetainedWorldBindGroupMisses =>
            _current?._retainedWorldBindGroupMisses ?? 0;
        internal static int RetainedWorldUniformSlotHighWater =>
            _current?._retainedUniformSlotHighWater ?? 0;

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

        internal static bool ValidateRetainedWorldUniformLayout(
            out string error)
        {
            try
            {
                ModernShaderLayout layout =
                    GeneratedShaderLayouts.Get(ModernProgramKind.World);
                _ = new RetainedWorldUniformOffsets(layout);
                string[] expectedSamplers =
                    { "tex", "normal_tex", "specular_tex", "emissive_tex" };
                if (layout.Samplers.Length != expectedSamplers.Length)
                {
                    error = $"generated World sampler count is {layout.Samplers.Length}, expected {expectedSamplers.Length}";
                    return false;
                }
                for (int i = 0; i < expectedSamplers.Length; i++)
                {
                    if (layout.Samplers[i] != expectedSamplers[i])
                    {
                        error = $"generated World sampler {i} is '{layout.Samplers[i]}', expected '{expectedSamplers[i]}'";
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

        private static bool RetainedWorldPacketBaseEligible(RenderItem item) =>
            item.Type == RenderItemType.Mesh
            && (uint)item.BillboardMode <= (uint)BillboardMode.Cylinder
            && !item.Wireframe
            && item.MatrixStackCount >= 0
            && item.MatrixStackCount <= Math.Min(
                32, item.MatrixStack.Length / 16)
            && item.Cosmetics == default
            && item.CosmeticMaterial == default;

        internal static bool RetainedWorldPacketEligible(RenderItem item) =>
            RetainedWorldPacketEligibleForPass(
                item, WorldRenderPassKind.Opaque);

        internal static bool RetainedWorldPacketEligibleForPass(
            RenderItem item, WorldRenderPassKind kind)
        {
            if (!RetainedWorldPacketBaseEligible(item))
                return false;
            return kind switch
            {
                WorldRenderPassKind.Opaque
                    or WorldRenderPassKind.RebuildDepth =>
                    item.RenderMode == RenderMode.Normal
                        && item.Alpha >= 0.999f,
                WorldRenderPassKind.Decal =>
                    item.RenderMode == RenderMode.Decal,
                WorldRenderPassKind.MarkTranslucent
                    or WorldRenderPassKind.TranslucentBehind
                    or WorldRenderPassKind.TranslucentFront =>
                    item.RenderMode == RenderMode.Translucent
                        || item.Alpha < 0.999f,
                _ => false
            };
        }

        internal static bool TryDrawRetainedWorld(RenderItem item,
            RetainedMeshDescriptor mesh, RetainedWorldTextureSet textures,
            bool showTextures, bool useLighting, bool faceCulling,
            Matrix4? projectionOverride, Matrix4 viewInverse,
            WorldRenderPassKind passKind)
        {
            if (_current == null
                || !RetainedWorldPacketEligibleForPass(item, passKind))
            {
                return false;
            }
            return Current.TryDrawRetainedWorldCore(item, mesh, textures,
                showTextures, useLighting, faceCulling,
                projectionOverride, viewInverse);
        }

        private bool TryDrawRetainedWorldCore(RenderItem item,
            RetainedMeshDescriptor mesh, RetainedWorldTextureSet textures,
            bool showTextures, bool useLighting, bool faceCulling,
            Matrix4? projectionOverride, Matrix4 viewInverse)
        {
            if (CurrentProgramKind() != ModernProgramKind.World
                || _wireframe
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

            BindRetainedWorldTextures(
                textures, item.XRepeat, item.YRepeat);

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
                generated, target, item, textures, showTextures, useLighting,
                projectionOverride, viewInverse);
            int retainedSlot = UploadRetainedWorldUniformWords(generated);

            CorePipelineRecord pipeline = CorePipeline(
                ModernProgramKind.World, PrimitiveTopology.TriangleList, target);
            BindGroup* bindGroup = RetainedWorldBindGroup(
                generated, pipeline.Layout, retainedSlot);

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

        private void BindRetainedWorldTextures(
            RetainedWorldTextureSet textures,
            RepeatMode xRepeat, RepeatMode yRepeat)
        {
            for (int unit = 0; unit < 4; unit++)
            {
                RetainedTextureBinding binding = textures.At(unit);
                _resources.ActiveTexture(
                    (TextureUnit)((int)TextureUnit.Texture0 + unit));
                _resources.BindTexture(TextureTarget.Texture2D, binding.Id);
                if (binding.IsBound && binding.ApplySampling)
                {
                    ApplyRetainedTextureSampling(
                        binding.Id, binding.Sampling, xRepeat, yRepeat);
                }
            }
            _resources.ActiveTexture(TextureUnit.Texture0);
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
            RetainedWorldTextureSet textures,
            bool showTextures, bool useLighting,
            Matrix4? projectionOverride, Matrix4 viewInverse)
        {
            uint[] words = generated.Words;
            RetainedWorldUniformOffsets o = _retainedWorldOffsets!;

            RetainedInt(words, o.UseLight,
                useLighting && item.Lighting ? 1 : 0);
            RetainedInt(words, o.UseTexture,
                item.HasTexture && showTextures && textures.Albedo.IsBound ? 1 : 0);
            RetainedVec3(words, o.Light1Vector, item.LightInfo.Light1Vector);
            RetainedVec3(words, o.Light1Color, item.LightInfo.Light1Color);
            RetainedVec3(words, o.Light2Vector, item.LightInfo.Light2Vector);
            RetainedVec3(words, o.Light2Color, item.LightInfo.Light2Color);
            RetainedVec3(words, o.Diffuse, item.Diffuse);
            RetainedVec3(words, o.Ambient, item.Ambient);
            RetainedVec3(words, o.Specular, item.Specular);
            RetainedVec3(words, o.Emission, item.Emission);
            if (projectionOverride.HasValue)
                RetainedMatrix(words, o.Projection, projectionOverride.Value);
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
            RetainedFloat(words, o.MaterialAlpha, item.Alpha);
            RetainedInt(words, o.MaterialMode, (int)item.PolygonMode);

            RetainedInt(words, o.AdvancedMaterials, textures.Advanced ? 1 : 0);
            RetainedInt(words, o.UseNormalMap, textures.Normal.IsBound ? 1 : 0);
            RetainedInt(words, o.UseSpecularMap, textures.Specular.IsBound ? 1 : 0);
            RetainedInt(words, o.UseEmissiveMap, textures.Emissive.IsBound ? 1 : 0);

            // Direct eligibility still excludes the remaining special material
            // features. Force their gates off so stale compatibility state
            // cannot leak into a packet.
            if (item.OverrideColor.HasValue)
            {
                RetainedInt(words, o.UseOverride, 1);
                RetainedVec4(words, o.OverrideColor, item.OverrideColor.Value);
            }
            else
            {
                RetainedInt(words, o.UseOverride, 0);
            }
            RetainedInt(words, o.TexturedPlayerSkin,
                item.TexturedPlayerSkin
                    ? RenderOptions.BrightSkinStyle
                        == PlayerSkinStyle.HighContrastTextured ? 2 : 1
                    : 0);
            RetainedInt(words, o.PlayerOutlineMask, 0);
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

            for (int i = 0; i < generated.Textures.Length; i++)
            {
                RetainedTextureBinding binding = textures.At(i);
                generated.Textures[i] = binding.IsBound
                    ? ValidateRenderPassResources(i, required: true, target)
                    : 0;
                int flip = generated.Layout.FlipOffset / 4 + i * 4;
                float flipY = generated.Textures[i] != 0
                    && _resources.IsFramebufferTexture(generated.Textures[i])
                    ? 1f : 0f;
                generated.Words[flip] =
                    unchecked((uint)BitConverter.SingleToInt32Bits(flipY));
                generated.Words[flip + 1] = 0;
                generated.Words[flip + 2] = 0;
                generated.Words[flip + 3] = 0;
            }

            _retainedWorldUniformPatches++;
        }

        private int UploadRetainedWorldUniformWords(GeneratedProgram generated)
        {
            RetainedUniformAllocation allocation =
                RentRetainedUniformSlot((ulong)generated.Layout.Size);
            generated.UniformBuffer = (WgpuBuffer*)allocation.Buffer;
            generated.UniformOffset = allocation.Offset;
            fixed (uint* words = generated.Words)
            {
                WriteRetainedUniformBuffer(
                    allocation, words, (nuint)generated.Layout.Size);
            }
            return allocation.Slot;
        }

        private BindGroup* RetainedWorldBindGroup(
            GeneratedProgram generated, BindGroupLayout* layout, int slot)
        {
            var entries =
                stackalloc BindGroupEntry[1 + generated.Textures.Length * 2];
            Span<nint> resources =
                stackalloc nint[3 + generated.Textures.Length * 2];
            resources[0] = (nint)layout;
            resources[1] = (nint)generated.UniformBuffer;
            resources[2] = (nint)generated.UniformOffset;

            entries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = generated.UniformBuffer,
                Offset = generated.UniformOffset,
                Size = (ulong)generated.Layout.Size
            };
            uint count = 1;
            for (int i = 0; i < generated.Textures.Length; i++)
            {
                NativeTexture? texture = generated.Textures[i] == 0
                    ? null : EnsureTexture(generated.Textures[i]);
                TextureView* view =
                    texture != null ? texture.SampleView : _whiteView;
                Silk.NET.WebGPU.Sampler* sampler =
                    texture != null ? texture.Sampler : _whiteSampler;
                entries[count++] = new BindGroupEntry
                {
                    Binding = (uint)(1 + i * 2),
                    TextureView = view
                };
                entries[count++] = new BindGroupEntry
                {
                    Binding = (uint)(2 + i * 2),
                    Sampler = sampler
                };

                int fingerprint = 3 + i * 2;
                resources[fingerprint] = (nint)view;
                resources[fingerprint + 1] = (nint)sampler;
            }

            while (_retainedWorldBindGroups.Count <= slot)
                _retainedWorldBindGroups.Add(new GeneratedBindGroupCacheEntry());
            GeneratedBindGroupCacheEntry cached =
                _retainedWorldBindGroups[slot];
            if (cached.Group != null
                && cached.Resources.AsSpan().SequenceEqual(resources))
            {
                _retainedWorldBindGroupHits++;
                return cached.Group;
            }

            if (cached.Group != null)
                ReleaseTrackedBindGroup(cached.Group);
            cached.Group = CreateTrackedBindGroup(new BindGroupDescriptor
            {
                Layout = layout,
                Entries = entries,
                EntryCount = count
            });
            cached.Resources = resources.ToArray();
            _retainedWorldBindGroupMisses++;
            return cached.Group;
        }

        private void DisposeRetainedWorldBindGroups()
        {
            foreach (GeneratedBindGroupCacheEntry cached
                in _retainedWorldBindGroups)
            {
                if (cached.Group != null)
                    ReleaseTrackedBindGroup(cached.Group);
            }
            _retainedWorldBindGroups.Clear();
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

        private static void RetainedMatrices(
            uint[] words, int at, float[] values, int matrixCount)
        {
            int count = Math.Min(
                matrixCount * 16,
                Math.Min(values.Length, words.Length - at));
            for (int i = 0; i < count; i++)
                RetainedFloat(words, at + i, values[i]);
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
