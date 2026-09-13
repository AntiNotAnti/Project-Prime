#if ANDROID
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead
{
    internal readonly record struct GlesDrawResources(
        int TextureBinding,
        Vector3? FlatColor,
        GlesEnhancedDrawResources? Enhanced = null);

    /// <summary>
    /// Borrowed device state plus the frame-local native bindings resolved by
    /// the frontend while it is still the sole producer. The table is sealed
    /// before rendering so the GLES executor never calls back into the scene.
    /// </summary>
    internal sealed class GlesWorldContext
    {
        private readonly Dictionary<DrawSubmission, GlesDrawResources> _draws
            = new(ReferenceEqualityComparer.Instance);
        private bool _sealed;

        public ShaderLocations ShaderLocations { get; private set; } = null!;
        public GlesEnhancedRuntime? EnhancedRuntime { get; private set; }
        private GlesEnhancedRuntime? TextureRuntime { get; set; }

        public void BeginFrame(ShaderLocations shaderLocations,
            GlesEnhancedRuntime? enhancedRuntime = null,
            GlesEnhancedRuntime? textureRuntime = null)
        {
            ShaderLocations = shaderLocations
                ?? throw new ArgumentNullException(nameof(shaderLocations));
            _draws.Clear();
            _sealed = false;
            EnhancedRuntime = enhancedRuntime;
            TextureRuntime = textureRuntime ?? enhancedRuntime;
            EnhancedRuntime?.BeginResourceFrame();
            if (!ReferenceEquals(TextureRuntime, EnhancedRuntime))
            {
                TextureRuntime?.BeginResourceFrame();
            }
        }

        public void Add(DrawSubmission submission, int textureBinding, RenderFrame frame)
        {
            if (_sealed) throw new InvalidOperationException("The GLES world context is sealed.");
            if (submission == null) throw new ArgumentNullException(nameof(submission));
            if (frame == null) throw new ArgumentNullException(nameof(frame));

            Vector3? flatColor = null;
            if (submission.Material.Textured)
            {
                TextureIdentity? requested = SdlGpuCelSurface.UsesEnhancedTextures(
                    frame.Options)
                        ? submission.Material.Enhanced?.Albedo
                            ?? submission.Material.Texture
                        : submission.Material.Texture;
                if (requested is not TextureIdentity identity)
                {
                    throw new InvalidOperationException(
                        $"Textured scene submission polygon {submission.PolygonId} has no texture identity.");
                }
                if (!frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels))
                {
                    throw new InvalidOperationException(
                        $"Sealed scene frame is missing texture pixels for {identity}.");
                }
                flatColor = pixels.AlphaWeightedFlatColor;
            }
            GlesEnhancedDrawResources? enhanced = EnhancedRuntime?.ResolveResources(
                submission, frame, textureBinding);
            int resolvedTexture = enhanced is GlesEnhancedDrawResources enhancedResources
                ? enhancedResources.AlbedoTexture
                : TextureRuntime?.ResolveAlbedo(submission, frame, textureBinding)
                    ?? textureBinding;
            _draws.Add(submission, new GlesDrawResources(
                resolvedTexture, flatColor, enhanced));
        }

        public void Seal() => _sealed = true;

        public GlesDrawResources Get(DrawSubmission submission)
        {
            if (!_sealed) throw new InvalidOperationException("The GLES world context is not sealed.");
            if (!_draws.TryGetValue(submission, out GlesDrawResources resources))
            {
                throw new InvalidOperationException(
                    $"Sealed GLES context is missing resources for polygon {submission.PolygonId}.");
            }
            return resources;
        }
    }

    /// <summary>
    /// Android's world-pass encoder. EGL acquisition, swapping and successful
    /// presentation acknowledgement remain owned by GameView.
    /// </summary>
    internal static class GlesBackend
    {
        public static void RenderWorld(RenderFrame frame, GlesWorldContext context)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!frame.IsSealed) throw new InvalidOperationException("The GLES world frame is not sealed.");

            ShaderLocations locations = context.ShaderLocations;
            context.EnhancedRuntime?.ApplyFrame(frame);

            // pass 1: opaque
            GL.ColorMask(true, true, true, true);
            GL.Enable(EnableCap.AlphaTest);
            GL.AlphaFunc(AlphaFunction.Equal, 1f);
            GL.DepthFunc(DepthFunction.Less);
            GL.DepthMask(true);
            GL.Enable(EnableCap.StencilTest);
            GL.StencilMask(0xFF);
            GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);
            GL.StencilFunc(StencilFunction.Always, 0, 0xFF);
            RenderPass(frame, context, locations, RenderPassKind.Opaque);
            GL.Disable(EnableCap.AlphaTest);

            // pass 2: decal
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(-1, -1);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            RenderPass(frame, context, locations, RenderPassKind.Decal);
            GL.PolygonOffset(0, 0);
            GL.Disable(EnableCap.PolygonOffsetFill);

            // pass 3: mark transparent faces in stencil
            GL.Enable(EnableCap.AlphaTest);
            GL.AlphaFunc(AlphaFunction.Less, 1f);
            GL.ColorMask(false, false, false, false);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            RenderPass(frame, context, locations, RenderPassKind.TransparentStencil);

            // pass 4: rebuild depth buffer
            GL.Clear(ClearBufferMask.DepthBufferBit);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            GL.StencilFunc(StencilFunction.Always, 0, 0xFF);
            GL.AlphaFunc(AlphaFunction.Equal, 1f);
            RenderPass(frame, context, locations, RenderPassKind.DepthRebuild);

            // pass 5: translucent surfaces behind the marked polygon
            GL.AlphaFunc(AlphaFunction.Less, 1f);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(false);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            RenderPass(frame, context, locations, RenderPassKind.TransparentBehind);

            // pass 6: translucent surfaces in front of the marked polygon
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            RenderPass(frame, context, locations, RenderPassKind.TransparentFront);

            GL.DepthMask(true);
            GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.StencilTest);
            GL.PolygonMode(TriangleFace.FrontAndBack,
                OpenTK.Graphics.OpenGL.PolygonMode.Fill);
        }

        private static void RenderPass(RenderFrame frame, GlesWorldContext context,
            ShaderLocations locations, RenderPassKind pass)
        {
            IReadOnlyList<DrawSubmission> submissions
                = RenderWorldPlan.GetSubmissions(frame, pass);
            for (int i = 0; i < submissions.Count; i++)
            {
                DrawSubmission submission = submissions[i];
                if (pass == RenderPassKind.TransparentStencil)
                {
                    GL.StencilFunc(StencilFunction.Greater, submission.PolygonId, 0xFF);
                }
                else if (pass == RenderPassKind.TransparentBehind)
                {
                    GL.StencilFunc(StencilFunction.Notequal, submission.PolygonId, 0xFF);
                }
                else if (pass == RenderPassKind.TransparentFront)
                {
                    GL.StencilFunc(StencilFunction.Equal, submission.PolygonId, 0xFF);
                }
                RenderSubmission(frame, context, locations, submission);
            }
        }

        private static void RenderSubmission(RenderFrame frame, GlesWorldContext context,
            ShaderLocations locations, DrawSubmission submission)
        {
            RenderMaterial material = submission.Material;
            GlesDrawResources resources = context.Get(submission);

            GL.Uniform3(locations.Light1Vector, submission.LightInfo.Light1Vector);
            GL.Uniform3(locations.Light1Color, submission.LightInfo.Light1Color);
            GL.Uniform3(locations.Light2Vector, submission.LightInfo.Light2Vector);
            GL.Uniform3(locations.Light2Color, submission.LightInfo.Light2Color);

            if (submission.MatrixStackCount > 0)
            {
                GL.UniformMatrix4(locations.MatrixStack, submission.MatrixStackCount,
                    transpose: false, submission.MatrixStack);
            }
            else
            {
                Matrix4 transform = submission.Transform;
                GL.UniformMatrix4(locations.MatrixStack, transpose: false, ref transform);
            }
            Matrix4 viewInverse = RenderWorldPlan.GetBillboardMatrix(frame, material);
            GL.UniformMatrix4(locations.ViewInvMatrix, transpose: false, ref viewInverse);

            ApplyMaterial(frame.Options, locations, material);
            ApplyTexture(frame.Options, locations, material, resources);
            if (context.EnhancedRuntime is GlesEnhancedRuntime enhancedRuntime
                && resources.Enhanced is GlesEnhancedDrawResources enhanced)
            {
                ApplyEnhancedMaterial(enhancedRuntime, enhanced, material,
                    frame.Options);
            }
            ApplyRasterState(frame.Options, material);

            CpuMesh mesh = RenderWorldPlan.ResolveMesh(frame, submission);
            RenderMeshStreams streams = RenderWorldPlan.GetMeshStreams(submission, frame.Options);
            object meshIdentity = RenderWorldPlan.GetMeshIdentity(submission);
            if (submission.Primitive == RenderPrimitive.Mesh)
            {
                GL.DrawStaticMesh(meshIdentity, mesh, streams);
            }
            else if (submission.Primitive != RenderPrimitive.Ngon)
            {
                GL.DrawDynamicMesh(mesh, streams);
            }
            else
            {
                RenderMeshStreams triangles = streams & RenderMeshStreams.Triangles;
                if (triangles != RenderMeshStreams.None)
                {
                    GL.DrawDynamicMesh(mesh, triangles);
                }
                if ((streams & RenderMeshStreams.Lines) != 0)
                {
                    GL.Uniform1(locations.UseOverride, 1);
                    GL.Uniform4(locations.OverrideColor, submission.FrozenEdgeColor);
                    GL.DrawDynamicMesh(mesh, RenderMeshStreams.Lines);
                }
            }
        }

        private static void ApplyEnhancedMaterial(GlesEnhancedRuntime runtime,
            GlesEnhancedDrawResources enhanced, RenderMaterial material,
            RenderFrameOptions options)
        {
            GL.Uniform1(runtime.SmoothnessLocation, enhanced.Smoothness);
            GL.Uniform4(runtime.EnhancedEmissionLocation,
                enhanced.EmissionTint.X, enhanced.EmissionTint.Y,
                enhanced.EmissionTint.Z, enhanced.EmissionStrength);
            GL.Uniform1(runtime.UseNormalMapLocation,
                material.Textured && enhanced.UseNormalMap ? 1 : 0);
            GL.Uniform1(runtime.UseEmissiveMapLocation,
                material.Textured && enhanced.UseEmissiveMap ? 1 : 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, enhanced.AlbedoTexture);
            GlesEnhancedRuntime.ConfigureMaterialSampler(options.Filtering,
                material.WrapX, material.WrapY);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, enhanced.NormalTexture);
            GlesEnhancedRuntime.ConfigureMaterialSampler(options.Filtering,
                material.WrapX, material.WrapY);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, enhanced.EmissiveTexture);
            GlesEnhancedRuntime.ConfigureMaterialSampler(options.Filtering,
                material.WrapX, material.WrapY);
            GL.ActiveTexture(TextureUnit.Texture0);
        }

        private static void ApplyMaterial(RenderFrameOptions options, ShaderLocations locations,
            RenderMaterial material)
        {
            GL.Uniform1(locations.UseLight, options.Lighting && material.Lighting ? 1 : 0);
            GL.Color3(material.Diffuse);
            GL.Uniform3(locations.Diffuse, material.Diffuse);
            GL.Uniform3(locations.Ambient, material.Ambient);
            GL.Uniform3(locations.Specular, material.Specular);
            GL.Uniform3(locations.Emission, material.Emission);
            GL.Uniform1(locations.MaterialAlpha, material.Alpha);
            GL.Uniform1(locations.MaterialMode, (int)material.PolygonMode);
        }

        private static void ApplyTexture(RenderFrameOptions options, ShaderLocations locations,
            RenderMaterial material, GlesDrawResources resources)
        {
            if (material.Textured)
            {
                GL.BindTexture(TextureTarget.Texture2D, resources.TextureBinding);
                bool filtering = options.Filtering
                    && SdlGpuCelSurface.Style(options) != Mods.VisualStyle.Pixelated;
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    filtering ? (int)TextureMinFilter.Linear : (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    filtering ? (int)TextureMagFilter.Linear : (int)TextureMagFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                    Wrap(material.WrapX));
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                    Wrap(material.WrapY));
                Matrix4 textureMatrix = material.TextureMatrix;
                GL.Uniform1(locations.TexgenMode, (int)material.TexgenMode);
                GL.UniformMatrix4(locations.TextureMatrix, transpose: false, ref textureMatrix);
            }

            bool useTexture = material.Textured && options.ShowTextures;
            GL.Uniform1(locations.UseTexture, useTexture ? 1 : 0);
            if (SdlGpuCelSurface.Style(options) is Mods.VisualStyle.Cel or Mods.VisualStyle.Flat
                && useTexture && resources.FlatColor is Vector3 flatColor)
            {
                GL.Uniform1(locations.UseFlat, 1);
                GL.Uniform3(locations.FlatColor, flatColor);
            }
            else
            {
                GL.Uniform1(locations.UseFlat, 0);
            }

            if (material.ColorOverride is Vector4 colorOverride)
            {
                GL.Uniform1(locations.UseOverride, 1);
                GL.Uniform4(locations.OverrideColor, colorOverride);
            }
            else
            {
                GL.Uniform1(locations.UseOverride, 0);
            }
            if (material.PaletteOverride is Vector4 paletteOverride)
            {
                GL.Uniform1(locations.UsePaletteOverride, 1);
                GL.Uniform4(locations.PaletteOverrideColor, paletteOverride);
            }
            else
            {
                GL.Uniform1(locations.UsePaletteOverride, 0);
            }
        }

        private static void ApplyRasterState(RenderFrameOptions options, RenderMaterial material)
        {
            if (!options.FaceCulling || material.CullingMode == CullingMode.Neither)
            {
                GL.Disable(EnableCap.CullFace);
            }
            else
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(material.CullingMode == CullingMode.Front
                    ? TriangleFace.Front : TriangleFace.Back);
            }
            GL.PolygonMode(TriangleFace.FrontAndBack,
                options.Wireframe || material.Wireframe
                    ? OpenTK.Graphics.OpenGL.PolygonMode.Line
                    : OpenTK.Graphics.OpenGL.PolygonMode.Fill);
        }

        private static int Wrap(RepeatMode repeat) => repeat switch
        {
            RepeatMode.Repeat => (int)TextureWrapMode.Repeat,
            RepeatMode.Mirror => (int)TextureWrapMode.MirroredRepeat,
            _ => (int)TextureWrapMode.ClampToEdge
        };
    }
}
#endif
