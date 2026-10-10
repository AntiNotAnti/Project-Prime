using System;
using MphRead.Mods;
using MphRead.Entities;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Modern presentation-only processing for the finished 3D scene.
    ///
    /// The cartridge renderer still produces the authoritative world picture.
    /// This pass reads that offscreen picture (and depth when available), writes
    /// a second scene texture, then the existing composite/HUD path presents it.
    /// Nothing here advances simulation, moves an entity or changes hit logic.
    /// </summary>
    public partial class Scene
    {
        private int _graphicsProgram;
        private int _graphicsToneMapProgram;
        private int _graphicsToneMapSource;
        private int _graphicsOutputTexture;
        private int _graphicsOutputFramebuffer;
        private int _graphicsHdrTexture;
        private int _graphicsHdrFramebuffer;
        private int _graphicsHistoryTexture;
        private Vector2i _graphicsHistorySize;
        private PixelInternalFormat _graphicsHistoryFormat;
        private bool _graphicsHistoryValid;
        private Matrix4 _graphicsPreviousViewProjection = Matrix4.Identity;
        private Vector2i _graphicsOutputSize;
        private bool _graphicsOutputReady;
        private bool _graphicsPipelineRefused;
        private bool _graphicsOutputHdr;
        private bool _graphicsHdrRefused;

        private int _gfxSceneSampler, _gfxDepthSampler, _gfxTexel;
        private int _gfxNear, _gfxFar, _gfxDepthAvailable;
        private int _gfxAa, _gfxSharpen, _gfxBloom, _gfxBloomIntensity;
        private int _gfxGrade, _gfxGamma, _gfxContrast, _gfxSaturation;
        private int _gfxLighting, _gfxAo, _gfxContactShadows;
        private int _gfxEnhancedFog, _gfxVolumetricFog, _gfxHdr;
        private int _gfxReflections, _gfxDynamicGlow, _gfxFogColor, _gfxTime;
        private int _gfxInvProjection, _gfxInvView, _gfxView, _gfxProjection, _gfxCameraPosition;
        private int _gfxDynamicLightCount;
        private int _gfxShadowSampler, _gfxShadowEnabled, _gfxShadowView, _gfxShadowProjection;
        private int _gfxShadowTexel, _gfxShadowLightDir;
        private int _gfxHistorySampler, _gfxHistoryValid, _gfxPreviousViewProjection;
        private int _gfxPbrEnabled, _gfxPbrAlbedo, _gfxPbrNormal, _gfxPbrMaterial;
        private int _gfxPbrLight1Direction, _gfxPbrLight1Color;
        private int _gfxPbrLight2Direction, _gfxPbrLight2Color;
        private readonly int[] _gfxDynamicLightPos = new int[8];
        private readonly int[] _gfxDynamicLightColor = new int[8];
        private readonly DynamicLightCandidate[] _dynamicLightScratch = new DynamicLightCandidate[8];

        private readonly record struct DynamicLightCandidate(float Distance, Vector3 Position,
            Vector3 Color, float Radius, float Intensity);

        private void ApplyGraphicsPostProcess()
        {
            // Only directional shadow composition remains supported by the
            // experimental presentation pipeline. Never let a legacy setting
            // or diagnostic activate HDR/TAA/PBR passes after migration.
            RenderOptions.RetireExperimentalPostEffects();
            _graphicsOutputReady = false;
            if (!RenderOptions.PostProcessingEnabled || _graphicsPipelineRefused)
            {
                _graphicsHistoryValid = false;
                return;
            }
            try
            {
                bool pbrAvailable = RenderOptions.DeferredPbr && DeferredPbrReady;
                AntiAliasingMode effectiveAa = ResolvePostProcessAntiAliasing(
                    RenderOptions.AntiAliasing, RenderOptions.InternalHdr, pbrAvailable);
                bool taa = effectiveAa == AntiAliasingMode.Taa;
                Vector2i target = ResolveGraphicsProcessingSize(_targetSize, Size, taa);

                EnsureGraphicsPipeline(target);
                if (_graphicsProgram == 0 || _graphicsOutputFramebuffer == 0)
                {
                    return;
                }

                bool hdrActive = RenderOptions.InternalHdr && _graphicsOutputHdr
                    && !_graphicsHdrRefused && _graphicsHdrFramebuffer != 0;
                GL.BindFramebuffer(FramebufferTarget.Framebuffer,
                    hdrActive ? _graphicsHdrFramebuffer : _graphicsOutputFramebuffer);
                GL.Viewport(0, 0, target.X, target.Y);
                SetScreenPassState();
                GL.Disable(EnableCap.Blend);
                GL.UseProgram(_graphicsProgram);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _screenTexture);
                GL.Uniform1(_gfxSceneSampler, 0);

                bool depthAvailable = _depthTexture != 0 && RenderOptions.NeedsReadableDepth;
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.BindTexture(TextureTarget.Texture2D, depthAvailable ? _depthTexture : 0);
                GL.Uniform1(_gfxDepthSampler, 1);
                GL.Uniform1(_gfxDepthAvailable, depthAvailable ? 1 : 0);

                bool shadowAvailable = depthAvailable && ShadowMapReady
                    && RenderOptions.Shadows != ShadowQuality.Off;
                GL.ActiveTexture(TextureUnit.Texture2);
                GL.BindTexture(TextureTarget.Texture2D, shadowAvailable ? _shadowDepthTexture : 0);
                GL.Uniform1(_gfxShadowSampler, 2);
                GL.Uniform1(_gfxShadowEnabled, shadowAvailable ? 1 : 0);
                if (shadowAvailable)
                {
                    GL.UniformMatrix4(_gfxShadowView, false, ref _shadowView);
                    GL.UniformMatrix4(_gfxShadowProjection, false, ref _shadowProjection);
                    GL.Uniform2(_gfxShadowTexel, 1f / Math.Max(1, _shadowTargetSize),
                        1f / Math.Max(1, _shadowTargetSize));
                    Vector3 shadowDirection = _light1Vector;
                    if (shadowDirection.LengthSquared < .0001f)
                        shadowDirection = new Vector3(-.45f, -.82f, -.35f);
                    GL.Uniform3(_gfxShadowLightDir, shadowDirection.Normalized());
                }

                GL.Uniform1(_gfxHistoryValid, 0);
                GL.Uniform1(_gfxPbrEnabled, 0);
                GL.ActiveTexture(TextureUnit.Texture0);

                // Sampling offsets describe the full-resolution world/depth
                // sources, even when the expensive post-process pass is resolved
                // directly at the final presentation size.
                GL.Uniform2(_gfxTexel, 1f / Math.Max(1, _targetSize.X), 1f / Math.Max(1, _targetSize.Y));
                GL.Uniform1(_gfxNear, _nearClip);
                GL.Uniform1(_gfxFar, _useClip ? Math.Max(_farClip, _nearClip + 1f) : 10000f);
                GL.Uniform1(_gfxAa, (int)effectiveAa);
                GL.Uniform1(_gfxSharpen, RenderOptions.SharpenStrength / 100f);
                GL.Uniform1(_gfxBloom, RenderOptions.Bloom && RenderOptions.BloomIntensity > 0 ? 1 : 0);
                GL.Uniform1(_gfxBloomIntensity, RenderOptions.BloomIntensity / 100f);
                GL.Uniform1(_gfxGrade, (int)RenderOptions.ColorGrade);
                GL.Uniform1(_gfxGamma, RenderOptions.Gamma / 100f);
                GL.Uniform1(_gfxContrast, RenderOptions.Contrast / 100f);
                GL.Uniform1(_gfxSaturation, RenderOptions.Saturation / 100f);
                GL.Uniform1(_gfxLighting, RenderOptions.EnhancedLighting ? 1 : 0);
                GL.Uniform1(_gfxAo, (int)RenderOptions.AmbientOcclusion);
                GL.Uniform1(_gfxContactShadows, RenderOptions.ContactShadows ? 1 : 0);
                GL.Uniform1(_gfxEnhancedFog, RenderOptions.EnhancedFog ? 1 : 0);
                GL.Uniform1(_gfxVolumetricFog, RenderOptions.VolumetricFog ? 1 : 0);
                GL.Uniform1(_gfxHdr, hdrActive ? 1 : 0);
                GL.Uniform1(_gfxReflections, RenderOptions.Reflections ? 1 : 0);
                GL.Uniform1(_gfxDynamicGlow, RenderOptions.DynamicGlow ? 1 : 0);
                GL.Uniform4(_gfxFogColor, _fogColor);
                GL.Uniform1(_gfxTime, _globalElapsedTime);
                Matrix4 invProjection = _perspectiveMatrix.Inverted();
                Matrix4 invView = _viewMatrix.Inverted();
                GL.UniformMatrix4(_gfxInvProjection, false, ref invProjection);
                GL.UniformMatrix4(_gfxInvView, false, ref invView);
                Matrix4 viewMatrix = _viewMatrix;
                GL.UniformMatrix4(_gfxView, false, ref viewMatrix);
                Matrix4 projection = _perspectiveMatrix;
                GL.UniformMatrix4(_gfxProjection, false, ref projection);
                GL.Uniform3(_gfxCameraPosition, _cameraPosition);
                UploadDynamicLights();

                DrawGraphicsFullscreenQuad();
                if (hdrActive)
                {
                    ResolveGraphicsHdr(target);
                    ReleaseFrameTransientFramebufferTexture(
                        ref _graphicsHdrTexture, _graphicsHdrFramebuffer,
                        _graphicsOutputFramebuffer);
                }
                _graphicsHistoryValid = false;
                _graphicsOutputReady = true;
                RenderPostProcessCount++;
                CheckGlError("GraphicsPostProcess");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                _graphicsPipelineRefused = true;
                _graphicsOutputReady = false;
                Console.WriteLine($"[render] enhanced graphics unavailable: {ex.Message}");
            }
            finally
            {
                GL.ActiveTexture(TextureUnit.Texture6);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture5);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture4);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.ActiveTexture(TextureUnit.Texture3);
                GL.BindTexture(TextureTarget.Texture2D, 0);
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

        internal static AntiAliasingMode ResolvePostProcessAntiAliasing(
            AntiAliasingMode requested, bool hdrRequested, bool pbrAvailable)
        {
            // The temporal shader cannot safely consume HDR or the deferred
            // material buffer yet. Do not maintain a history texture that the
            // shader will reject; use the existing morphological resolve instead.
            return requested == AntiAliasingMode.Taa && (hdrRequested || pbrAvailable)
                ? AntiAliasingMode.Smaa : requested;
        }

        internal static Vector2i ResolveGraphicsProcessingSize(
            Vector2i sceneTarget, Vector2i outputSize, bool taaActive)
        {
            if (taaActive || sceneTarget.X <= 0 || sceneTarget.Y <= 0
                || outputSize.X <= 0 || outputSize.Y <= 0
                || (sceneTarget.X <= outputSize.X && sceneTarget.Y <= outputSize.Y))
            {
                return sceneTarget;
            }

            // Supersampling is valuable for world geometry and source texture
            // detail, but AO/SSR/bloom/HDR need not shade every supersampled
            // pixel only to be downsampled by the composite pass. Resolve those
            // effects once at the presentation size, preserving aspect ratio.
            double scale = Math.Min((double)outputSize.X / sceneTarget.X,
                (double)outputSize.Y / sceneTarget.Y);
            return new Vector2i(
                Math.Max(1, (int)Math.Round(sceneTarget.X * scale)),
                Math.Max(1, (int)Math.Round(sceneTarget.Y * scale)));
        }

        private int GraphicsCompositeTexture()
            => _graphicsOutputReady ? _graphicsOutputTexture : _screenTexture;

        private int GraphicsReadFramebuffer()
            => _graphicsOutputReady ? _graphicsOutputFramebuffer : _frameBuffer;

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
                    GL.GetProgram(_graphicsProgram, GetProgramParameterName.LinkStatus, out int linked);
                    if (linked == 0)
                    {
                        throw new ProgramException(GL.GetProgramInfoLog(_graphicsProgram));
                    }
#endif
                    _gfxSceneSampler = GL.GetUniformLocation(_graphicsProgram, "tex");
                    _gfxDepthSampler = GL.GetUniformLocation(_graphicsProgram, "depth_tex");
                    _gfxTexel = GL.GetUniformLocation(_graphicsProgram, "texel");
                    _gfxNear = GL.GetUniformLocation(_graphicsProgram, "near_plane");
                    _gfxFar = GL.GetUniformLocation(_graphicsProgram, "far_plane");
                    _gfxDepthAvailable = GL.GetUniformLocation(_graphicsProgram, "depth_available");
                    _gfxAa = GL.GetUniformLocation(_graphicsProgram, "aa_mode");
                    _gfxSharpen = GL.GetUniformLocation(_graphicsProgram, "sharpen_strength");
                    _gfxBloom = GL.GetUniformLocation(_graphicsProgram, "bloom_enable");
                    _gfxBloomIntensity = GL.GetUniformLocation(_graphicsProgram, "bloom_intensity");
                    _gfxGrade = GL.GetUniformLocation(_graphicsProgram, "grade_mode");
                    _gfxGamma = GL.GetUniformLocation(_graphicsProgram, "gamma_value");
                    _gfxContrast = GL.GetUniformLocation(_graphicsProgram, "contrast_value");
                    _gfxSaturation = GL.GetUniformLocation(_graphicsProgram, "saturation_value");
                    _gfxLighting = GL.GetUniformLocation(_graphicsProgram, "enhanced_lighting");
                    _gfxAo = GL.GetUniformLocation(_graphicsProgram, "ao_quality");
                    _gfxContactShadows = GL.GetUniformLocation(_graphicsProgram, "contact_shadows");
                    _gfxEnhancedFog = GL.GetUniformLocation(_graphicsProgram, "enhanced_fog");
                    _gfxVolumetricFog = GL.GetUniformLocation(_graphicsProgram, "volumetric_fog");
                    _gfxHdr = GL.GetUniformLocation(_graphicsProgram, "hdr_mode");
                    _gfxReflections = GL.GetUniformLocation(_graphicsProgram, "reflections");
                    _gfxDynamicGlow = GL.GetUniformLocation(_graphicsProgram, "dynamic_glow");
                    _gfxFogColor = GL.GetUniformLocation(_graphicsProgram, "fog_color");
                    _gfxTime = GL.GetUniformLocation(_graphicsProgram, "time_value");
                    _gfxInvProjection = GL.GetUniformLocation(_graphicsProgram, "inv_projection");
                    _gfxInvView = GL.GetUniformLocation(_graphicsProgram, "inv_view");
                    _gfxView = GL.GetUniformLocation(_graphicsProgram, "view_matrix");
                    _gfxProjection = GL.GetUniformLocation(_graphicsProgram, "projection");
                    _gfxCameraPosition = GL.GetUniformLocation(_graphicsProgram, "camera_position");
                    _gfxShadowSampler = GL.GetUniformLocation(_graphicsProgram, "shadow_tex");
                    _gfxShadowEnabled = GL.GetUniformLocation(_graphicsProgram, "shadow_enabled");
                    _gfxShadowView = GL.GetUniformLocation(_graphicsProgram, "shadow_view");
                    _gfxShadowProjection = GL.GetUniformLocation(_graphicsProgram, "shadow_projection");
                    _gfxShadowTexel = GL.GetUniformLocation(_graphicsProgram, "shadow_texel");
                    _gfxShadowLightDir = GL.GetUniformLocation(_graphicsProgram, "shadow_light_dir");
                    _gfxHistorySampler = GL.GetUniformLocation(_graphicsProgram, "history_tex");
                    _gfxHistoryValid = GL.GetUniformLocation(_graphicsProgram, "history_valid");
                    _gfxPreviousViewProjection = GL.GetUniformLocation(_graphicsProgram,
                        "previous_view_projection");
                    _gfxPbrEnabled = GL.GetUniformLocation(_graphicsProgram, "pbr_enabled");
                    _gfxPbrAlbedo = GL.GetUniformLocation(_graphicsProgram, "pbr_albedo");
                    _gfxPbrNormal = GL.GetUniformLocation(_graphicsProgram, "pbr_normal");
                    _gfxPbrMaterial = GL.GetUniformLocation(_graphicsProgram, "pbr_material");
                    _gfxPbrLight1Direction = GL.GetUniformLocation(_graphicsProgram, "pbr_light1_dir");
                    _gfxPbrLight1Color = GL.GetUniformLocation(_graphicsProgram, "pbr_light1_color");
                    _gfxPbrLight2Direction = GL.GetUniformLocation(_graphicsProgram, "pbr_light2_dir");
                    _gfxPbrLight2Color = GL.GetUniformLocation(_graphicsProgram, "pbr_light2_color");
                    _gfxDynamicLightCount = GL.GetUniformLocation(_graphicsProgram, "dynamic_light_count");
                    for (int i = 0; i < 8; i++)
                    {
                        _gfxDynamicLightPos[i] = GL.GetUniformLocation(_graphicsProgram,
                            $"dynamic_light_pos[{i}]");
                        _gfxDynamicLightColor[i] = GL.GetUniformLocation(_graphicsProgram,
                            $"dynamic_light_color[{i}]");
                    }
                }
                finally
                {
                    GL.DeleteShader(vertex);
                    if (fragment != 0) GL.DeleteShader(fragment);
                }
            }

            if (_graphicsToneMapProgram == 0 && RenderOptions.InternalHdr)
            {
                int vertex = CompileGraphicsShader(ShaderType.VertexShader,
                    Mods.Render.GraphicsToneMapShader.VertexSource);
                int fragment = 0;
                try
                {
                    fragment = CompileGraphicsShader(ShaderType.FragmentShader,
                        Mods.Render.GraphicsToneMapShader.FragmentSource);
                    _graphicsToneMapProgram = GL.CreateProgram();
                    GL.AttachShader(_graphicsToneMapProgram, vertex);
                    GL.AttachShader(_graphicsToneMapProgram, fragment);
                    GL.LinkProgram(_graphicsToneMapProgram);
#if !ANDROID
                    GL.GetProgram(_graphicsToneMapProgram, GetProgramParameterName.LinkStatus,
                        out int linked);
                    if (linked == 0)
                    {
                        throw new ProgramException(GL.GetProgramInfoLog(_graphicsToneMapProgram));
                    }
#endif
                    _graphicsToneMapSource = GL.GetUniformLocation(
                        _graphicsToneMapProgram, "hdr_tex");
                }
                finally
                {
                    GL.DeleteShader(vertex);
                    if (fragment != 0) GL.DeleteShader(fragment);
                }
            }

            if (_graphicsOutputFramebuffer == 0)
                _graphicsOutputFramebuffer = GL.GenFramebuffer();

            bool sizeChanged = _graphicsOutputSize != target;
            if (_graphicsOutputTexture == 0 || sizeChanged)
            {
                ReleaseFrameTransientFramebufferTexture(
                    ref _graphicsOutputTexture, _graphicsOutputFramebuffer,
                    _frameBuffer);
                _graphicsOutputTexture = AcquireFrameTransientTexture(
                    target, PixelInternalFormat.Rgba8,
                    TextureMinFilter.Linear, TextureMagFilter.Linear);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer,
                    _graphicsOutputFramebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D,
                    _graphicsOutputTexture, 0);
                ValidateFramebuffer("Enhanced graphics output");
            }

            bool wantHdr = RenderOptions.InternalHdr && !_graphicsHdrRefused;
            if (wantHdr)
            {
                if (_graphicsHdrFramebuffer == 0)
                    _graphicsHdrFramebuffer = GL.GenFramebuffer();

                if (_graphicsHdrTexture == 0 || sizeChanged || !_graphicsOutputHdr)
                {
                    ReleaseFrameTransientFramebufferTexture(
                        ref _graphicsHdrTexture, _graphicsHdrFramebuffer,
                        _frameBuffer);
                    _graphicsHdrTexture = AcquireFrameTransientTexture(
                        target, PixelInternalFormat.Rgba16f,
                        TextureMinFilter.Linear, TextureMagFilter.Linear);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer,
                        _graphicsHdrFramebuffer);
                    GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D,
                        _graphicsHdrTexture, 0);
                    FramebufferErrorCode hdrStatus = GL.CheckFramebufferStatus(
                        FramebufferTarget.Framebuffer);
                    if (hdrStatus != FramebufferErrorCode.FramebufferComplete)
                    {
                        _graphicsHdrRefused = true;
                        _graphicsOutputHdr = false;
                        ReleaseFrameTransientFramebufferTexture(
                            ref _graphicsHdrTexture, _graphicsHdrFramebuffer,
                            _frameBuffer);
                        Console.WriteLine("[render] half-float HDR target unavailable; using RGBA8.");
                    }
                    else
                    {
                        _graphicsOutputHdr = true;
                    }
                }
            }
            else
            {
                ReleaseFrameTransientFramebufferTexture(
                    ref _graphicsHdrTexture, _graphicsHdrFramebuffer,
                    _frameBuffer);
                _graphicsOutputHdr = false;
            }

            _graphicsOutputSize = target;
        }

        private void ResolveGraphicsHdr(Vector2i target)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _graphicsOutputFramebuffer);
            GL.Viewport(0, 0, target.X, target.Y);
            SetScreenPassState();
            GL.Disable(EnableCap.Blend);
            GL.UseProgram(_graphicsToneMapProgram);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _graphicsHdrTexture);
            GL.Uniform1(_graphicsToneMapSource, 0);
            DrawGraphicsFullscreenQuad();
        }

        private void EnsureGraphicsHistory(Vector2i target, bool active)
        {
            if (!active)
            {
                _graphicsHistoryValid = false;
                return;
            }
            if (_graphicsHistoryTexture == 0)
            {
                _graphicsHistoryTexture = GL.GenTexture();
            }
            PixelInternalFormat historyFormat = PixelInternalFormat.Rgba8;
            if (_graphicsHistorySize == target && _graphicsHistoryFormat == historyFormat) return;
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, _graphicsHistoryTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, historyFormat,
                target.X, target.Y, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            GL.ActiveTexture(TextureUnit.Texture0);
            _graphicsHistorySize = target;
            _graphicsHistoryFormat = historyFormat;
            _graphicsHistoryValid = false;
        }

        private void UpdateGraphicsHistory(Vector2i target, bool active)
        {
            if (!active || _graphicsHistoryTexture == 0)
            {
                _graphicsHistoryValid = false;
                return;
            }

            // History stores the raw scene, not the already processed frame.
            // That keeps color grading, bloom and HDR from being accumulated
            // repeatedly and makes the temporal stage independent of tone mapping.
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _frameBuffer);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, _graphicsHistoryTexture);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0,
                target.X, target.Y);
            GL.ActiveTexture(TextureUnit.Texture0);

            _graphicsPreviousViewProjection = _perspectiveMatrix * _viewMatrix;
            _graphicsHistoryValid = true;
        }

        private void UploadDynamicLights()
        {
            if (!RenderOptions.DynamicGlow && (!RenderOptions.ShowCustomCosmetics
                || RenderOptions.CosmeticQuality < Mods.Cosmetics.CosmeticEffectQuality.Medium))
            {
                GL.Uniform1(_gfxDynamicLightCount, 0);
                return;
            }

            int count = 0;
            foreach (EntityBase entity in Entities)
            {
                if (!RenderOptions.DynamicGlow || entity is not BeamProjectileEntity beam || beam.Lifespan <= 0
                    || beam.Flags.TestFlag(BeamFlags.Collided))
                {
                    continue;
                }

                Vector3 color = beam.Color;
                if (color.LengthSquared < 0.01f)
                {
                    color = BeamLightColor(beam.Beam);
                }
                color = new Vector3(Math.Clamp(color.X, 0f, 1f),
                    Math.Clamp(color.Y, 0f, 1f), Math.Clamp(color.Z, 0f, 1f));
                float intensity = beam.Flags.TestFlag(BeamFlags.Charged) ? 1.25f : 0.85f;
                if (beam.Flags.TestFlag(BeamFlags.Continuous)) intensity *= 0.75f;

                var candidate = new DynamicLightCandidate(
                    (beam.Position - _cameraPosition).LengthSquared,
                    beam.Position, color, BeamLightRadius(beam.Beam), intensity);

                int insert = count;
                while (insert > 0
                    && _dynamicLightScratch[insert - 1].Distance > candidate.Distance)
                {
                    if (insert < _dynamicLightScratch.Length)
                    {
                        _dynamicLightScratch[insert] = _dynamicLightScratch[insert - 1];
                    }
                    insert--;
                }
                if (insert < _dynamicLightScratch.Length)
                {
                    _dynamicLightScratch[insert] = candidate;
                    if (count < _dynamicLightScratch.Length) count++;
                }
            }

            CollectCosmeticLights(ref count);
            GL.Uniform1(_gfxDynamicLightCount, count);
            for (int i = 0; i < count; i++)
            {
                DynamicLightCandidate light = _dynamicLightScratch[i];
                GL.Uniform4(_gfxDynamicLightPos[i],
                    light.Position.X, light.Position.Y, light.Position.Z, light.Radius);
                GL.Uniform4(_gfxDynamicLightColor[i],
                    light.Color.X, light.Color.Y, light.Color.Z, light.Intensity);
            }
        }

        private static Vector3 BeamLightColor(BeamType beam) => beam switch
        {
            BeamType.PowerBeam => new Vector3(0.35f, 0.65f, 1f),
            BeamType.VoltDriver => new Vector3(0.25f, 0.75f, 1f),
            BeamType.Missile => new Vector3(1f, 0.55f, 0.20f),
            BeamType.Battlehammer => new Vector3(0.45f, 1f, 0.25f),
            BeamType.Imperialist => new Vector3(1f, 0.18f, 0.14f),
            BeamType.Judicator => new Vector3(0.35f, 0.80f, 1f),
            BeamType.Magmaul => new Vector3(1f, 0.30f, 0.06f),
            BeamType.ShockCoil => new Vector3(0.60f, 0.35f, 1f),
            BeamType.OmegaCannon => new Vector3(1f, 0.85f, 0.30f),
            _ => new Vector3(0.65f, 0.80f, 1f)
        };

        private static float BeamLightRadius(BeamType beam) => beam switch
        {
            BeamType.Imperialist => 2.5f,
            BeamType.PowerBeam => 3.0f,
            BeamType.VoltDriver => 4.5f,
            BeamType.Missile => 5.0f,
            BeamType.Battlehammer => 5.5f,
            BeamType.Judicator => 4.5f,
            BeamType.Magmaul => 6.0f,
            BeamType.ShockCoil => 4.0f,
            BeamType.OmegaCannon => 7.0f,
            _ => 3.5f
        };

        private void DisposeGraphicsPipeline()
        {
            _graphicsOutputReady = false;
            _graphicsPipelineRefused = false;
            _graphicsOutputSize = default;
            _graphicsOutputHdr = false;
            _graphicsHdrRefused = false;
            if (_graphicsOutputFramebuffer != 0)
            {
                GL.DeleteFramebuffer(_graphicsOutputFramebuffer);
                _graphicsOutputFramebuffer = 0;
            }
            if (_graphicsHdrFramebuffer != 0)
            {
                GL.DeleteFramebuffer(_graphicsHdrFramebuffer);
                _graphicsHdrFramebuffer = 0;
            }
            ReleaseFrameTransientTexture(ref _graphicsOutputTexture);
            ReleaseFrameTransientTexture(ref _graphicsHdrTexture);
            DeleteTexture(ref _graphicsHistoryTexture);
            _graphicsHistorySize = default;
            _graphicsHistoryValid = false;
            _graphicsPreviousViewProjection = Matrix4.Identity;
            DeleteProgram(ref _graphicsProgram);
            DeleteProgram(ref _graphicsToneMapProgram);
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
        public static string FragmentSource { get; } = "#version 300 es\nprecision highp float;\nprecision highp int;\n"
            + "in vec2 texcoord;\nout vec4 frag_color;\nuniform highp sampler2D depth_tex;\n"
            + "uniform highp sampler2D shadow_tex;\nuniform highp sampler2D history_tex;\n"
            + "uniform highp sampler2D pbr_albedo;\nuniform highp sampler2D pbr_normal;\n"
            + "uniform highp sampler2D pbr_material;\n"
            + "#define SAMPLE texture\n#define OUTPUT frag_color\n" + Body;
#else
        public static string VertexSource { get; } = @"#version 120
varying vec2 texcoord;
void main() {
    gl_Position = vec4(gl_Vertex.xy, 0.0, 1.0);
    texcoord = gl_MultiTexCoord0.xy;
}
";
        public static string FragmentSource { get; } = "#version 120\nvarying vec2 texcoord;\n"
            + "uniform sampler2D depth_tex;\nuniform sampler2D shadow_tex;\nuniform sampler2D history_tex;\n"
            + "uniform sampler2D pbr_albedo;\nuniform sampler2D pbr_normal;\n"
            + "uniform sampler2D pbr_material;\n"
            + "#define SAMPLE texture2D\n#define OUTPUT gl_FragColor\n" + Body;
#endif

        private const string Body = @"
uniform sampler2D tex;
uniform vec2 texel;
uniform float near_plane;
uniform float far_plane;
uniform int depth_available;
uniform int aa_mode;
uniform float sharpen_strength;
uniform int bloom_enable;
uniform float bloom_intensity;
uniform int grade_mode;
uniform float gamma_value;
uniform float contrast_value;
uniform float saturation_value;
uniform int enhanced_lighting;
uniform int ao_quality;
uniform int contact_shadows;
uniform int enhanced_fog;
uniform int volumetric_fog;
uniform int hdr_mode;
uniform int reflections;
uniform int dynamic_glow;
uniform vec4 fog_color;
uniform float time_value;
uniform mat4 inv_projection;
uniform mat4 inv_view;
uniform mat4 view_matrix;
uniform mat4 previous_view_projection;
uniform int history_valid;
uniform mat4 projection;
uniform vec3 camera_position;
uniform int shadow_enabled;
uniform mat4 shadow_view;
uniform mat4 shadow_projection;
uniform vec2 shadow_texel;
uniform vec3 shadow_light_dir;
uniform int pbr_enabled;
uniform vec3 pbr_light1_dir;
uniform vec3 pbr_light1_color;
uniform vec3 pbr_light2_dir;
uniform vec3 pbr_light2_color;
uniform int dynamic_light_count;
uniform vec4 dynamic_light_pos[8];
uniform vec4 dynamic_light_color[8];

vec2 uv_clamp(vec2 uv) {
    return clamp(uv, vec2(0.0), vec2(1.0));
}

vec3 scene(vec2 uv) {
    return SAMPLE(tex, uv_clamp(uv)).rgb;
}

float luma(vec3 c) {
    return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

float raw_depth(vec2 uv) {
    if (depth_available == 0) return 1.0;
    return SAMPLE(depth_tex, uv_clamp(uv)).r;
}

float view_depth(float d) {
    float n = max(near_plane, 0.0001);
    float f = max(far_plane, n + 0.01);
    float z = d * 2.0 - 1.0;
    return (2.0 * n * f) / max(0.0001, f + n - z * (f - n));
}

vec3 smaa_resolve(vec2 uv, vec3 center) {
    float m = luma(center);
    vec3 cn = scene(uv + vec2(0.0, texel.y));
    vec3 cs = scene(uv - vec2(0.0, texel.y));
    vec3 ce = scene(uv + vec2(texel.x, 0.0));
    vec3 cw = scene(uv - vec2(texel.x, 0.0));
    float n = luma(cn), s = luma(cs), e = luma(ce), w = luma(cw);
    float edgeX = abs(w - e);
    float edgeY = abs(n - s);
    float localMin = min(m, min(min(n, s), min(e, w)));
    float localMax = max(m, max(max(n, s), max(e, w)));
    float contrast = localMax - localMin;
    if (contrast < max(0.025, localMax * 0.055)) return center;

    vec2 axis = edgeX > edgeY ? vec2(texel.x, 0.0) : vec2(0.0, texel.y);
    vec3 a = scene(uv - axis * 0.5);
    vec3 b = scene(uv + axis * 0.5);
    vec3 c = scene(uv - axis * 1.5);
    vec3 d = scene(uv + axis * 1.5);
    float subpixel = clamp(abs(m - (n + s + e + w) * 0.25)
        / max(contrast, 0.0001), 0.0, 1.0);
    float blend = 0.42 + subpixel * 0.28;
    vec3 morphological = (a + b) * 0.375 + (c + d) * 0.125;
    return mix(center, morphological, blend);
}

vec3 fxaa(vec2 uv, vec3 center) {
    if (aa_mode == 0) return center;
    if (aa_mode == 3) return smaa_resolve(uv, center);
    float m = luma(center);
    float n = luma(scene(uv + vec2(0.0, texel.y)));
    float s = luma(scene(uv - vec2(0.0, texel.y)));
    float e = luma(scene(uv + vec2(texel.x, 0.0)));
    float w = luma(scene(uv - vec2(texel.x, 0.0)));
    float lo = min(m, min(min(n, s), min(e, w)));
    float hi = max(m, max(max(n, s), max(e, w)));
    if (hi - lo < max(0.035, hi * 0.08)) return center;

    float edgeX = abs(w - e);
    float edgeY = abs(n - s);
    vec2 dir = edgeX > edgeY ? vec2(0.0, texel.y) : vec2(texel.x, 0.0);
    vec3 a = scene(uv - dir * 0.5);
    vec3 b = scene(uv + dir * 0.5);
    vec3 resolved = (a + b) * 0.5;
    if (aa_mode > 1) {
        vec3 c = scene(uv - dir * 1.5);
        vec3 d = scene(uv + dir * 1.5);
        resolved = resolved * 0.75 + (c + d) * 0.125;
    }
    return resolved;
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

vec2 project_view(vec3 p, out float clipW) {
    vec4 clip = projection * vec4(p, 1.0);
    clipW = clip.w;
    return clip.xy / max(abs(clip.w), 0.000001) * 0.5 + 0.5;
}

vec3 projectile_lighting(vec3 worldPos) {
    if (dynamic_light_count <= 0) return vec3(0.0);
    vec3 result = vec3(0.0);
    for (int i = 0; i < 8; i++) {
        if (i >= dynamic_light_count) break;
        vec3 delta = dynamic_light_pos[i].xyz - worldPos;
        float radius = max(dynamic_light_pos[i].w, 0.01);
        float distanceToLight = length(delta);
        float falloff = 1.0 - smoothstep(radius * 0.12, radius, distanceToLight);
        falloff *= falloff;
        result += dynamic_light_color[i].rgb
            * dynamic_light_color[i].a * falloff * 0.34;
    }
    return result;
}

vec3 temporal_resolve(vec2 uv, vec3 current, float d) {
    if (aa_mode != 4 || hdr_mode != 0 || pbr_enabled != 0
        || history_valid == 0 || depth_available == 0
        || d >= 0.999999) return current;

    vec3 world = world_position(uv, d);
    vec4 previousClip = previous_view_projection * vec4(world, 1.0);
    if (previousClip.w <= 0.000001) return current;
    vec2 previousUv = previousClip.xy / previousClip.w * 0.5 + 0.5;
    if (previousUv.x <= 0.0 || previousUv.x >= 1.0
        || previousUv.y <= 0.0 || previousUv.y >= 1.0) return current;

    vec3 lo = current, hi = current;
    vec3 a = scene(uv + vec2(texel.x, 0.0));
    vec3 b = scene(uv - vec2(texel.x, 0.0));
    vec3 c = scene(uv + vec2(0.0, texel.y));
    vec3 e = scene(uv - vec2(0.0, texel.y));
    lo = min(lo, min(min(a, b), min(c, e)));
    hi = max(hi, max(max(a, b), max(c, e)));

    vec3 history = clamp(SAMPLE(history_tex, previousUv).rgb,
        lo - vec3(0.025), hi + vec3(0.025));
    float motionPixels = length((previousUv - uv) / texel);
    float historyWeight = 0.88
        * (1.0 - smoothstep(0.35, 2.5, motionPixels));
    float luminanceDelta = abs(luma(history) - luma(current));
    historyWeight *= 1.0 - smoothstep(0.08, 0.35, luminanceDelta);
    return mix(current, history, clamp(historyWeight, 0.0, 0.88));
}

float pbr_distribution_ggx(vec3 n, vec3 h, float roughness) {
    float a = roughness * roughness;
    float a2 = a * a;
    float nh = max(dot(n, h), 0.0);
    float d = nh * nh * (a2 - 1.0) + 1.0;
    return a2 / max(3.14159265 * d * d, 0.0001);
}
float pbr_geometry_schlick(float nv, float roughness) {
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    return nv / max(nv * (1.0 - k) + k, 0.0001);
}
vec3 pbr_fresnel(float cosTheta, vec3 f0) {
    return f0 + (1.0 - f0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}
vec3 pbr_direct(vec3 albedo, vec3 n, vec3 v, vec3 l, vec3 radiance,
    float metallic, float roughness) {
    vec3 h = normalize(v + l);
    float nv = max(dot(n, v), 0.0);
    float nl = max(dot(n, l), 0.0);
    if (nl <= 0.0 || nv <= 0.0) return vec3(0.0);
    vec3 f0 = mix(vec3(0.04), albedo, metallic);
    vec3 f = pbr_fresnel(max(dot(h, v), 0.0), f0);
    float d = pbr_distribution_ggx(n, h, roughness);
    float g = pbr_geometry_schlick(nv, roughness) * pbr_geometry_schlick(nl, roughness);
    vec3 spec = d * g * f / max(4.0 * nv * nl, 0.001);
    vec3 kd = (vec3(1.0) - f) * (1.0 - metallic);
    return (kd * albedo / 3.14159265 + spec) * radiance * nl;
}
vec3 deferred_pbr(vec2 uv, vec3 worldPos) {
    vec4 a = SAMPLE(pbr_albedo, uv);
    if (pbr_enabled == 0 || a.a < 0.5) return vec3(-1.0);
    vec3 n = normalize(SAMPLE(pbr_normal, uv).xyz * 2.0 - 1.0);
    vec4 m = SAMPLE(pbr_material, uv);
    float metallic = clamp(m.r, 0.0, 1.0);
    float roughness = clamp(m.g, 0.04, 1.0);
    vec3 v = normalize(camera_position - worldPos);
    vec3 result = a.rgb * (0.10 + 0.08 * (1.0 - metallic));
    result += pbr_direct(a.rgb, n, v, normalize(-pbr_light1_dir), pbr_light1_color, metallic, roughness);
    result += pbr_direct(a.rgb, n, v, normalize(-pbr_light2_dir), pbr_light2_color, metallic, roughness);
    for (int i = 0; i < 8; i++) {
        if (i >= dynamic_light_count) break;
        vec3 delta = dynamic_light_pos[i].xyz - worldPos;
        float dist = length(delta);
        float radius = max(dynamic_light_pos[i].w, 0.01);
        if (dist < radius) {
            float attenuation = 1.0 - smoothstep(radius * 0.1, radius, dist);
            attenuation *= attenuation;
            result += pbr_direct(a.rgb, n, v, normalize(delta),
                dynamic_light_color[i].rgb * dynamic_light_color[i].a * attenuation,
                metallic, roughness);
        }
    }
    result += a.rgb * m.b * 1.4;
    return result;
}

vec3 depth_normal(vec2 uv, float centerDepth) {
    if (depth_available == 0 || centerDepth >= 0.999999) return vec3(0.0, 0.0, 1.0);
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
    if (shadow_enabled == 0) return 1.0;
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
            float stored = SAMPLE(shadow_tex, suv + vec2(float(x), float(y)) * shadow_texel).r;
            lit += receiver - bias <= stored ? 1.0 : 0.0;
        }
    }
    lit /= 9.0;
    return mix(0.58, 1.0, lit);
}

float reflection_depth_continuity(vec2 uv, float d) {
    if (d >= 0.999999) return 0.0;
    float c = view_depth(d);
    float tolerance = max(0.10, abs(c) * 0.012);
    float d0 = raw_depth(uv + vec2(texel.x, 0.0));
    float d1 = raw_depth(uv - vec2(texel.x, 0.0));
    float d2 = raw_depth(uv + vec2(0.0, texel.y));
    float d3 = raw_depth(uv - vec2(0.0, texel.y));
    if (d0 >= 0.999999 || d1 >= 0.999999 || d2 >= 0.999999 || d3 >= 0.999999) {
        return 0.0;
    }
    float worst = max(
        max(abs(view_depth(d0) - c), abs(view_depth(d1) - c)),
        max(abs(view_depth(d2) - c), abs(view_depth(d3) - c)));
    return 1.0 - smoothstep(tolerance, tolerance * 3.0, worst);
}

vec4 screen_reflection(vec2 uv, float d, vec3 n) {
    if (reflections == 0 || depth_available == 0) return vec4(0.0);
    float sourceContinuity = reflection_depth_continuity(uv, d);
    if (sourceContinuity < 0.20) return vec4(0.0);

    vec3 origin = view_position(uv, d);
    vec3 incident = normalize(origin);
    vec3 rayDir = normalize(reflect(incident, n));
    // Rays pointing back toward or nearly parallel with the camera plane are
    // unstable in screen space and were the source of long diagonal wedges.
    if (rayDir.z >= -0.08) return vec4(0.0);

    float stepLength = max(0.12, abs(origin.z) * 0.012);
    vec3 ray = origin;
    for (int i = 0; i < 14; i++) {
        float fi = float(i);
        ray += rayDir * stepLength * (1.0 + fi * 0.10);
        float clipW = 1.0;
        vec2 hitUv = project_view(ray, clipW);
        if (clipW <= 0.0 || hitUv.x <= 0.015 || hitUv.x >= 0.985
            || hitUv.y <= 0.015 || hitUv.y >= 0.985) break;

        float sd = raw_depth(hitUv);
        if (sd >= 0.999999) continue;
        float hitContinuity = reflection_depth_continuity(hitUv, sd);
        if (hitContinuity < 0.25) continue;

        vec3 surface = view_position(hitUv, sd);
        // Keep the hit slab deliberately thin. A thick slab accepts an
        // unrelated wall behind the reflected ray and paints huge polygons.
        float thickness = max(0.045, abs(surface.z) * 0.0045);
        float separation = abs(ray.z - surface.z);
        if (separation <= thickness) {
            float edge = min(min(hitUv.x, 1.0 - hitUv.x),
                min(hitUv.y, 1.0 - hitUv.y));
            float edgeFade = smoothstep(0.015, 0.12, edge);
            float travel = length(ray - origin);
            float maxTravel = max(3.0, abs(origin.z) * 0.70);
            float travelFade = 1.0 - smoothstep(maxTravel * 0.45, maxTravel, travel);
            float slabConfidence = 1.0 - smoothstep(0.0, thickness, separation);
            float confidence = sourceContinuity * hitContinuity
                * edgeFade * travelFade * slabConfidence;
            return vec4(scene(hitUv), clamp(confidence, 0.0, 1.0));
        }
    }
    return vec4(0.0);
}

vec3 volumetric_scattering(vec3 worldPos) {
    if (volumetric_fog == 0 || dynamic_light_count <= 0) return vec3(0.0);
    vec3 cameraRay = worldPos - camera_position;
    float rayLength = length(cameraRay);
    if (rayLength <= 0.001) return vec3(0.0);
    vec3 rayDir = cameraRay / rayLength;
    vec3 scatter = vec3(0.0);
    for (int i = 0; i < 8; i++) {
        if (i >= dynamic_light_count) break;
        vec3 toLight = dynamic_light_pos[i].xyz - camera_position;
        float alongRay = clamp(dot(toLight, rayDir), 0.0, rayLength);
        vec3 closest = camera_position + rayDir * alongRay;
        float radius = max(dynamic_light_pos[i].w * 1.4, 0.5);
        float distanceToRay = length(dynamic_light_pos[i].xyz - closest);
        float beam = 1.0 - smoothstep(radius * 0.08, radius, distanceToRay);
        float distanceFade = 1.0 - smoothstep(8.0, 85.0, length(toLight));
        scatter += dynamic_light_color[i].rgb * dynamic_light_color[i].a
            * beam * beam * distanceFade * 0.10;
    }
    return scatter;
}

float ambient_occlusion(vec2 uv, float centerDepth) {
    if (depth_available == 0 || ao_quality == 0 || centerDepth >= 0.999999) return 1.0;
    float c = view_depth(centerDepth);
    float radius = ao_quality == 1 ? 1.5 : ao_quality == 2 ? 2.5 : 4.0;
    float bias = max(0.015, c * 0.0020);
    float range = max(0.12, c * 0.035);
    float occ = 0.0;
    vec2 dx = vec2(texel.x * radius, 0.0);
    vec2 dy = vec2(0.0, texel.y * radius);
    vec2 dg = vec2(texel.x * radius * 0.7071, texel.y * radius * 0.7071);
    float z0 = view_depth(raw_depth(uv + dx));
    float z1 = view_depth(raw_depth(uv - dx));
    float z2 = view_depth(raw_depth(uv + dy));
    float z3 = view_depth(raw_depth(uv - dy));
    float z4 = view_depth(raw_depth(uv + dg));
    float z5 = view_depth(raw_depth(uv - dg));
    float z6 = view_depth(raw_depth(uv + vec2(dg.x, -dg.y)));
    float z7 = view_depth(raw_depth(uv + vec2(-dg.x, dg.y)));
    occ += smoothstep(bias, range, c - z0);
    occ += smoothstep(bias, range, c - z1);
    occ += smoothstep(bias, range, c - z2);
    occ += smoothstep(bias, range, c - z3);
    occ += smoothstep(bias, range, c - z4);
    occ += smoothstep(bias, range, c - z5);
    occ += smoothstep(bias, range, c - z6);
    occ += smoothstep(bias, range, c - z7);
    float strength = ao_quality == 1 ? 0.10 : ao_quality == 2 ? 0.17 : 0.24;
    return clamp(1.0 - occ * (strength / 8.0), 0.68, 1.0);
}

float contact_shadow(vec2 uv, float centerDepth) {
    if (depth_available == 0 || contact_shadows == 0 || centerDepth >= 0.999999) return 1.0;
    float c = view_depth(centerDepth);
    vec2 dir = normalize(vec2(-0.65, 0.75));
    float shadow = 0.0;
    float bias = max(0.01, c * 0.0015);
    for (int i = 1; i <= 4; i++) {
        float fi = float(i);
        float z = view_depth(raw_depth(uv + dir * texel * (fi * 2.0)));
        float delta = c - z;
        shadow += smoothstep(bias, max(bias * 8.0, c * 0.025), delta) * (1.0 / fi);
    }
    return 1.0 - clamp(shadow * 0.055, 0.0, 0.18);
}

vec3 bloom_value(vec2 uv) {
    if (bloom_enable == 0 && dynamic_glow == 0) return vec3(0.0);
    float threshold = dynamic_glow != 0 ? 0.48 : 0.62;
    vec3 sum = vec3(0.0);
    float weight = 0.0;
    vec2 x2 = vec2(texel.x * 2.0, 0.0);
    vec2 y2 = vec2(0.0, texel.y * 2.0);
    vec2 x4 = vec2(texel.x * 4.0, 0.0);
    vec2 y4 = vec2(0.0, texel.y * 4.0);
    vec3 taps[8];
    taps[0] = scene(uv + x2); taps[1] = scene(uv - x2);
    taps[2] = scene(uv + y2); taps[3] = scene(uv - y2);
    taps[4] = scene(uv + x4); taps[5] = scene(uv - x4);
    taps[6] = scene(uv + y4); taps[7] = scene(uv - y4);
    for (int i = 0; i < 8; i++) {
        float b = max(0.0, luma(taps[i]) - threshold);
        b = b / max(0.001, 1.0 - threshold);
        sum += taps[i] * b;
        weight += b;
    }
    if (weight <= 0.0001) return vec3(0.0);
    float intensity = bloom_enable != 0 ? bloom_intensity : 0.25;
    if (dynamic_glow != 0) intensity += 0.18;
    return (sum / max(weight, 1.0)) * clamp(weight / 5.0, 0.0, 1.0) * intensity;
}

vec3 grade(vec3 c) {
    if (grade_mode == 1) {
        c = pow(max(c, vec3(0.0)), vec3(0.96));
        c = c * vec3(1.015, 1.01, 1.02);
    }
    else if (grade_mode == 2) {
        float y = luma(c);
        c = mix(vec3(y), c, 1.16);
        c *= vec3(1.025, 1.01, 1.02);
    }
    else if (grade_mode == 3) {
        float y = luma(c);
        vec3 shadows = c * vec3(0.94, 0.98, 1.06);
        vec3 highlights = c * vec3(1.06, 1.015, 0.96);
        c = mix(shadows, highlights, smoothstep(0.20, 0.82, y));
        c = mix(vec3(luma(c)), c, 1.10);
    }
    return c;
}

vec3 aces(vec3 x) {
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}

void main() {
    vec2 uv = texcoord;
    vec3 center = scene(uv);
    float d = raw_depth(uv);
    bool needWorld = pbr_enabled != 0 || shadow_enabled != 0 || dynamic_light_count > 0;
    vec3 worldP = needWorld && d < 0.999999 ? world_position(uv, d) : vec3(0.0);
    vec3 deferred = pbr_enabled != 0 && d < 0.999999 ? deferred_pbr(uv, worldP) : vec3(-1.0);
    vec3 color;
    if (deferred.r >= 0.0) {
        // The forward frame contains MPH's authored vertex/material lighting,
        // which varies per surface. The deferred pass only has a small global
        // light set, so replacing the forward frame outright turns whole rooms
        // black or chalk-white depending on which light was submitted last.
        // Luminance-match the PBR response, then blend it as material detail
        // over the authored picture instead of replacing that picture.
        float authoredY = max(luma(center), 0.025);
        float pbrY = max(luma(deferred), 0.025);
        float exposure = clamp(authoredY / pbrY, 0.65, 1.55);
        vec3 balancedPbr = deferred * exposure;
        color = mix(center, balancedPbr, 0.48);
    }
    else {
        color = aa_mode == 4 ? temporal_resolve(uv, center, d)
            : fxaa(uv, center);
    }

    if (sharpen_strength > 0.0001 && deferred.r < 0.0) {
        vec3 blur = (scene(uv + vec2(texel.x, 0.0))
            + scene(uv - vec2(texel.x, 0.0))
            + scene(uv + vec2(0.0, texel.y))
            + scene(uv - vec2(0.0, texel.y))) * 0.25;
        vec3 detail = color - blur;
        float guard = 1.0 - smoothstep(0.16, 0.45, length(detail));
        color += detail * sharpen_strength * 0.75 * guard;
    }

    if (depth_available != 0 && d < 0.999999) {
        vec3 n = vec3(0.0, 0.0, 1.0);
        // AO and basic fog do not use a surface normal. Avoid four extra
        // depth samples and reconstruction when the normal-based effects are off.
        if (enhanced_lighting != 0 || shadow_enabled != 0 || reflections != 0 || volumetric_fog != 0) {
            bool pbrPixel = pbr_enabled != 0 && SAMPLE(pbr_normal, uv).a > 0.5;
            n = pbrPixel
                ? normalize(mat3(view_matrix) * (SAMPLE(pbr_normal, uv).xyz * 2.0 - 1.0))
                : depth_normal(uv, d);
        }
        if (enhanced_lighting != 0 && deferred.r < 0.0) {
            vec3 ld = normalize(vec3(-0.45, 0.58, 0.68));
            float diffuse = dot(n, ld) * 0.5 + 0.5;
            float relief = mix(0.93, 1.09, diffuse);
            float spec = pow(max(0.0, dot(reflect(-ld, n), vec3(0.0, 0.0, 1.0))), 18.0);
            color = color * relief + vec3(spec * 0.035);
        }
        vec3 worldPos = worldP;
        color *= ambient_occlusion(uv, d);
        color *= contact_shadow(uv, d);
        color *= directional_shadow(worldPos, n);
        if (deferred.r < 0.0) color += projectile_lighting(worldPos);
        if (reflections != 0) {
            float fresnel = pow(clamp(1.0 - n.z, 0.0, 1.0), 3.0);
            vec4 reflected = screen_reflection(uv, d, n);
            float reflectionWeight = fresnel * 0.18 * reflected.a;
            color = mix(color, reflected.rgb, reflectionWeight);
        }

        if (enhanced_fog != 0 || volumetric_fog != 0) {
            float dist = view_depth(d);
            float f = max(far_plane, near_plane + 1.0);
            float normalizedDistance = clamp(dist / f, 0.0, 1.0);
            float fog = 1.0 - exp(-normalizedDistance * 2.35);
            if (volumetric_fog != 0) {
                float wave = sin(uv.x * 37.0 + time_value * 0.27)
                    * sin(uv.y * 29.0 - time_value * 0.19);
                fog *= 0.88 + 0.12 * wave;
                fog += (1.0 - n.z) * 0.025;
            }
            float amount = enhanced_fog != 0 ? 0.18 : 0.08;
            if (volumetric_fog != 0) amount += 0.10;
            color = mix(color, fog_color.rgb, clamp(fog * amount, 0.0, 0.32));
            color += volumetric_scattering(worldPos);
        }
    }

    color += bloom_value(uv);
    color = grade(color);

    float y = luma(color);
    color = mix(vec3(y), color, max(0.0, saturation_value));
    color = (color - vec3(0.5)) * contrast_value + vec3(0.5);
    color = pow(max(color, vec3(0.0)), vec3(1.0 / max(0.25, gamma_value)));

    if (hdr_mode != 0) {
        OUTPUT = vec4(max(color, vec3(0.0)), 1.0);
    }
    else {
        OUTPUT = vec4(clamp(color, 0.0, 1.0), 1.0);
    }
}
";
    }

    internal static class GraphicsToneMapShader
    {
        public static string VertexSource => GraphicsPipelineShader.VertexSource;
#if ANDROID
        public static string FragmentSource { get; } = "#version 300 es\nprecision highp float;\n"
            + "in vec2 texcoord;\nout vec4 frag_color;\n#define SAMPLE texture\n#define OUTPUT frag_color\n" + Body;
#else
        public static string FragmentSource { get; } = "#version 120\nvarying vec2 texcoord;\n"
            + "#define SAMPLE texture2D\n#define OUTPUT gl_FragColor\n" + Body;
#endif
        private const string Body = @"
uniform sampler2D hdr_tex;
vec3 aces(vec3 x) {
    return clamp((x * (2.51 * x + 0.03))
        / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
}
void main() {
    vec3 hdr = max(SAMPLE(hdr_tex, texcoord).rgb, vec3(0.0));
    OUTPUT = vec4(aces(hdr), 1.0);
}
";
    }
}
