#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        internal static bool TryDrawRetainedWorld(RenderItem item,
            RetainedMeshDescriptor mesh, TextureSamplerDescriptor sampling,
            bool showTextures, bool useLighting, bool faceCulling)
        {
            if (_current == null) return false;
            return Current.TryDrawRetainedWorldCore(item, mesh, sampling,
                showTextures, useLighting, faceCulling);
        }

        private bool TryDrawRetainedWorldCore(RenderItem item,
            RetainedMeshDescriptor mesh, TextureSamplerDescriptor sampling,
            bool showTextures, bool useLighting, bool faceCulling)
        {
            if (CurrentProgramKind() != ModernProgramKind.World
                || item.Type != RenderItemType.Mesh
                || item.RenderMode != RenderMode.Normal
                || item.ViewModel
                || item.BillboardMode != BillboardMode.None
                || item.Wireframe
                || _wireframe
                || item.MatrixStackCount != 0
                || item.Cosmetics != default
                || item.CosmeticMaterial != default
                || item.OverrideColor != null
                || item.PaletteOverride != null
                || item.TexturedPlayerSkin
                || item.PlayerOutlineColor != null
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

            ModernGraphicsCompatState.ProgramRecord program =
                _programs.Program(_programs.CurrentProgram);
            WriteRetainedWorldProgramState(program, item, showTextures, useLighting);

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

            BindRetainedWorldTextures(item, sampling, showTextures);

            if (_resources.DrawFramebuffer == 0 && !AcquireSurfaceTexture())
                return true;
            _device.ThrowIfFailed();
            CoreTarget target = ResolveDrawTarget();

            PrepareGeneratedResources(ModernProgramKind.World, target);
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

        private void BindRetainedWorldTextures(RenderItem item,
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

        private static void WriteRetainedWorldProgramState(
            ModernGraphicsCompatState.ProgramRecord program, RenderItem item,
            bool showTextures, bool useLighting)
        {
            DirectInt(program, "use_light", useLighting && item.Lighting ? 1 : 0);
            DirectInt(program, "use_texture",
                item.HasTexture && showTextures ? 1 : 0);
            DirectVec3(program, "light1vec", item.LightInfo.Light1Vector);
            DirectVec3(program, "light1col", item.LightInfo.Light1Color);
            DirectVec3(program, "light2vec", item.LightInfo.Light2Vector);
            DirectVec3(program, "light2col", item.LightInfo.Light2Color);
            DirectVec3(program, "diffuse", item.Diffuse);
            DirectVec3(program, "ambient", item.Ambient);
            DirectVec3(program, "specular", item.Specular);
            DirectVec3(program, "emission", item.Emission);
            DirectMatrix(program, "view_inv_mtx", Matrix4.Identity);
            DirectMatrix(program, "tex_mtx", item.TexcoordMatrix);
            DirectInt(program, "texgen_mode", (int)item.TexgenMode);
            DirectMatrix(program, "mtx_stack", item.Transform);
            DirectFloat(program, "mat_alpha", item.Alpha);
            DirectInt(program, "mat_mode", (int)item.PolygonMode);

            DirectInt(program, "advanced_materials", 0);
            DirectInt(program, "use_normal_map", 0);
            DirectInt(program, "use_specular_map", 0);
            DirectInt(program, "use_emissive_map", 0);
            DirectInt(program, "use_override", 0);
            DirectInt(program, "use_pal_override", 0);
            DirectInt(program, "textured_player_skin", 0);
            DirectInt(program, "player_outline_mask", 0);
            DirectInt(program, "use_flat", 0);
            DirectInt(program, "cosmetic_skin", 0);
            DirectInt(program, "cosmetic_preserve_palette", 0);
            DirectInt(program, "cosmetic_effect", 0);

            // Generated shader sampler uniforms are ordinary integer uniforms.
            DirectInt(program, "tex", 0);
            DirectInt(program, "normal_tex", 1);
            DirectInt(program, "specular_tex", 2);
            DirectInt(program, "emissive_tex", 3);
        }

        private static void DirectInt(ModernGraphicsCompatState.ProgramRecord program,
            string name, int value)
        {
            program.Uniforms[name] = new ModernGraphicsCompatState.UniformValue(value);
        }

        private static void DirectFloat(ModernGraphicsCompatState.ProgramRecord program,
            string name, float value)
        {
            Span<float> data = stackalloc float[1] { value };
            DirectFloats(program, name, data);
        }

        private static void DirectVec3(ModernGraphicsCompatState.ProgramRecord program,
            string name, Vector3 value)
        {
            Span<float> data = stackalloc float[3] { value.X, value.Y, value.Z };
            DirectFloats(program, name, data);
        }

        private static void DirectMatrix(ModernGraphicsCompatState.ProgramRecord program,
            string name, Matrix4 value)
        {
            Span<float> data = stackalloc float[16]
            {
                value.M11, value.M12, value.M13, value.M14,
                value.M21, value.M22, value.M23, value.M24,
                value.M31, value.M32, value.M33, value.M34,
                value.M41, value.M42, value.M43, value.M44
            };
            DirectFloats(program, name, data);
        }

        private static void DirectFloats(ModernGraphicsCompatState.ProgramRecord program,
            string name, ReadOnlySpan<float> values)
        {
            if (program.Uniforms.TryGetValue(name,
                out ModernGraphicsCompatState.UniformValue existing)
                && existing.Data?.Length == values.Length)
            {
                values.CopyTo(existing.Data);
                return;
            }
            program.Uniforms[name] =
                new ModernGraphicsCompatState.UniformValue(values.ToArray());
        }
    }
}
#endif
