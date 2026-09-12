#if ANDROID
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using MphRead.Mods;
using MphRead.Mods.Render;

namespace MphRead
{
    /// <summary>
    /// Owns every object in the optional GLES Enhanced path. Nothing here is
    /// consulted by Original or Performance; a feature-local failure returns
    /// Legacy without disturbing the already initialized legacy renderer.
    /// </summary>
    internal sealed class GlesEnhancedRuntime
    {
        private readonly GlesFloatingPointFailureCache _floatingPointFailures = new();
        private readonly GlesEnhancedTargetFailureCache _targetFailures = new();
        private readonly Dictionary<TextureIdentity, CachedTexture> _textures = new();
        private readonly HashSet<TextureIdentity> _pinnedTextures = new();
        private ulong _generation;
        private GlesEnhancedCapabilities _capabilities;
        private bool _capabilitiesQueried;
        private bool _programFailure;
        private bool _loggedProgramFailure;
        private int _sceneProgram;
        private int _toneProgram;
        private int _compositionProgram;
        private int _sceneFramebuffer;
        private int _sceneTexture;
        private int _sceneDepthStencil;
        private int _compositionFramebuffer;
        private int _compositionTexture;
        private int _scratchFramebuffer;
        private int _scratchTexture;
        private int _captureFramebuffer;
        private int _captureTexture;
        private int _whiteTexture;
        private int _normalTexture;
        private int _blackTexture;
        private Vector2i _sceneSize;
        private Vector2i _drawableSize;
        private bool _floatingPoint;
        private long _textureUseOrdinal;

        private readonly record struct CachedTexture(
            GlesImmutableTextureKey Key,
            int Binding,
            long LastUseOrdinal);

        public ShaderLocations SceneLocations { get; } = new();
        public ShaderLocations CompositionLocations { get; } = new();
        public int SceneProgram => _sceneProgram;
        public int CompositionProgram => _compositionProgram;
        public int SceneCaptureFramebuffer => _captureFramebuffer;
        public Vector2i SceneCaptureSize => _drawableSize;

        public int SmoothnessLocation { get; private set; } = -1;
        public int EnhancedEmissionLocation { get; private set; } = -1;
        public int UseNormalMapLocation { get; private set; } = -1;
        public int UseEmissiveMapLocation { get; private set; } = -1;
        public int NormalTextureLocation { get; private set; } = -1;
        public int EmissiveTextureLocation { get; private set; } = -1;
        private int _cameraPosition = -1;
        private int _visualLightCount = -1;
        private int _visualLightPositionRadius = -1;
        private int _visualLightColorIntensity = -1;
        private int _toneScene = -1;
        private int _toneLut = -1;
        private int _toneExposure = -1;
        private int _toneApply = -1;
        private int _toneUseLut = -1;
        private int _toneLutStrength = -1;
        private int _toneTransfer = -1;

        public GlesEnhancedPlan Prepare(GraphicsPreset preset, Vector2i sceneSize,
            Vector2i drawableSize)
        {
            SynchronizeContext();
            if (preset != GraphicsPreset.Enhanced)
            {
                // Do not even query or probe the context for legacy presets.
                return new GlesEnhancedPlan(GlesEnhancedMode.Legacy,
                    GlesEnhancedFallbackReason.PresetDoesNotRequestEnhanced);
            }
            if (sceneSize.X <= 0 || sceneSize.Y <= 0
                || drawableSize.X <= 0 || drawableSize.Y <= 0)
            {
                return new GlesEnhancedPlan(GlesEnhancedMode.Legacy,
                    GlesEnhancedFallbackReason.EnhancedTargetAllocationFailed);
            }
            var configuration = new GlesFloatingPointConfiguration(
                _generation, sceneSize.X, sceneSize.Y);
            if (!_capabilitiesQueried)
            {
                _capabilities = GlEs.QueryEnhancedCapabilities();
                _capabilitiesQueried = true;
            }
            GlesEnhancedPlan plan = GlesEnhancedPolicy.Resolve(preset, _capabilities,
                configuration, _floatingPointFailures);
            if (!plan.UsesEnhancedLightingAndMaterials) return plan;
            if (!EnsurePrograms())
            {
                return new GlesEnhancedPlan(GlesEnhancedMode.Legacy,
                    GlesEnhancedFallbackReason.EnhancedProgramUnavailable);
            }

            var targetConfiguration = new GlesEnhancedTargetConfiguration(_generation,
                sceneSize.X, sceneSize.Y, drawableSize.X, drawableSize.Y);
            if (!_targetFailures.ShouldAttempt(targetConfiguration))
            {
                return new GlesEnhancedPlan(GlesEnhancedMode.Legacy,
                    GlesEnhancedFallbackReason.EnhancedTargetAllocationFailed);
            }

            if (TargetsMatch(sceneSize, drawableSize, plan.UsesFloatingPointSceneTarget))
                return plan;

            if (TryReplaceTargets(sceneSize, drawableSize,
                    plan.UsesFloatingPointSceneTarget))
            {
                if (plan.UsesFloatingPointSceneTarget) _floatingPointFailures.RecordSuccess();
                _targetFailures.RecordSuccess();
                return plan;
            }

            if (plan.UsesFloatingPointSceneTarget)
            {
                _floatingPointFailures.RecordFailure(configuration);
                Console.WriteLine("[gles] RGBA16F Enhanced target allocation failed; "
                    + "using Enhanced LDR until the context or scene size changes.");
                plan = new GlesEnhancedPlan(GlesEnhancedMode.EnhancedLdr,
                    GlesEnhancedFallbackReason.FloatingPointAllocationFailed);
                if (TryReplaceTargets(sceneSize, drawableSize, floatingPoint: false))
                {
                    _targetFailures.RecordSuccess();
                    return plan;
                }
            }

            _targetFailures.RecordFailure(targetConfiguration);
            Console.WriteLine("[gles] Enhanced composition target allocation failed; using legacy rendering.");
            return new GlesEnhancedPlan(GlesEnhancedMode.Legacy,
                GlesEnhancedFallbackReason.EnhancedTargetAllocationFailed);
        }

        private void SynchronizeContext()
        {
            ulong current = GlEs.ContextGeneration;
            if (current == _generation) return;
            // Old native names died with the EGL context. Do not delete them
            // through the replacement context.
            _generation = current;
            _textures.Clear();
            _pinnedTextures.Clear();
            _sceneProgram = _toneProgram = _compositionProgram = 0;
            _sceneFramebuffer = _sceneTexture = _sceneDepthStencil = 0;
            _compositionFramebuffer = _compositionTexture = 0;
            _scratchFramebuffer = _scratchTexture = 0;
            _captureFramebuffer = _captureTexture = 0;
            _whiteTexture = _normalTexture = _blackTexture = 0;
            _sceneSize = _drawableSize = Vector2i.Zero;
            _programFailure = _loggedProgramFailure = false;
            _capabilities = default;
            _capabilitiesQueried = false;
        }

        private bool EnsurePrograms()
        {
            if (_sceneProgram != 0) return true;
            if (_programFailure) return false;
            try
            {
                int scene = 0;
                int tone = 0;
                int composition = 0;
                try
                {
                    scene = CompileProgram(GlesEnhancedShaders.VertexShader,
                    GlesEnhancedShaders.FragmentShader);
                    tone = CompileProgram(GlesEnhancedShaders.FullscreenVertexShader,
                    GlesEnhancedShaders.ToneMapFragmentShader);
                    composition = CompileProgram(GlesEnhancedShaders.CompositionVertexShader,
                    GlesEnhancedShaders.CompositionFragmentShader);
                    _sceneProgram = scene; scene = 0;
                    _toneProgram = tone; tone = 0;
                    _compositionProgram = composition; composition = 0;
                }
                finally
                {
                    GL.DeleteProgram(scene);
                    GL.DeleteProgram(tone);
                    GL.DeleteProgram(composition);
                }
                PopulateSceneLocations();
                PopulateCompositionLocations();
                CreateFallbackTextures();
                return true;
            }
            catch (Exception ex)
            {
                _programFailure = true;
                if (!_loggedProgramFailure)
                {
                    Console.WriteLine($"[gles] Enhanced shader setup failed; using legacy rendering: {ex.Message}");
                    _loggedProgramFailure = true;
                }
                ReleaseFallbackTextures();
                ReleasePrograms();
                return false;
            }
        }

        private static int CompileProgram(string vertexSource, string fragmentSource)
        {
            int vertex = 0;
            int fragment = 0;
            int program = 0;
            try
            {
                vertex = CompileShader(ShaderType.VertexShader, vertexSource);
                fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
                program = GL.CreateProgram();
                GL.AttachShader(program, vertex);
                GL.AttachShader(program, fragment);
                try
                {
                    GL.LinkProgram(program);
                    GL.GetProgram(program, GetProgramParameterName.LinkStatus,
                        out int linkStatus);
                    if (linkStatus == 0)
                    {
                        throw new ProgramException(GL.GetProgramInfoLog(program));
                    }
                }
                finally
                {
                    GL.DetachShader(program, vertex);
                    GL.DetachShader(program, fragment);
                }
            }
            catch
            {
                GL.DeleteProgram(program);
                throw;
            }
            finally
            {
                if (vertex != 0) GL.DeleteShader(vertex);
                if (fragment != 0) GL.DeleteShader(fragment);
            }
            return program;
        }

        private static int CompileShader(ShaderType type, string source)
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

        private void PopulateSceneLocations()
        {
            SceneLocations.UseLight = L(_sceneProgram, "use_light");
            SceneLocations.ShowColors = L(_sceneProgram, "show_colors");
            SceneLocations.UseTexture = L(_sceneProgram, "use_texture");
            SceneLocations.Light1Color = L(_sceneProgram, "light1col");
            SceneLocations.Light1Vector = L(_sceneProgram, "light1vec");
            SceneLocations.Light2Color = L(_sceneProgram, "light2col");
            SceneLocations.Light2Vector = L(_sceneProgram, "light2vec");
            SceneLocations.Diffuse = L(_sceneProgram, "diffuse");
            SceneLocations.Ambient = L(_sceneProgram, "ambient");
            SceneLocations.Specular = L(_sceneProgram, "specular");
            SceneLocations.Emission = L(_sceneProgram, "emission");
            SceneLocations.CelBands = L(_sceneProgram, "cel_bands");
            SceneLocations.UseFlat = L(_sceneProgram, "use_flat");
            SceneLocations.FlatColor = L(_sceneProgram, "flat_color");
            SceneLocations.UseOverride = L(_sceneProgram, "use_override");
            SceneLocations.OverrideColor = L(_sceneProgram, "override_color");
            SceneLocations.UsePaletteOverride = L(_sceneProgram, "use_pal_override");
            SceneLocations.PaletteOverrideColor = L(_sceneProgram, "pal_override_color");
            SceneLocations.MaterialAlpha = L(_sceneProgram, "mat_alpha");
            SceneLocations.MaterialMode = L(_sceneProgram, "mat_mode");
            SceneLocations.ViewMatrix = L(_sceneProgram, "view_mtx");
            SceneLocations.ViewInvMatrix = L(_sceneProgram, "view_inv_mtx");
            SceneLocations.ProjectionMatrix = L(_sceneProgram, "proj_mtx");
            SceneLocations.TextureMatrix = L(_sceneProgram, "tex_mtx");
            SceneLocations.TexgenMode = L(_sceneProgram, "texgen_mode");
            SceneLocations.MatrixStack = L(_sceneProgram, "mtx_stack");
            SceneLocations.ToonTable = L(_sceneProgram, "toon_table");
            SceneLocations.UseFog = L(_sceneProgram, "fog_enable");
            SceneLocations.FogColor = L(_sceneProgram, "fog_color");
            SceneLocations.FogMinDistance = L(_sceneProgram, "fog_min");
            SceneLocations.FogMaxDistance = L(_sceneProgram, "fog_max");
            SmoothnessLocation = L(_sceneProgram, "smoothness");
            EnhancedEmissionLocation = L(_sceneProgram, "enhanced_emission");
            UseNormalMapLocation = L(_sceneProgram, "use_normal_map");
            UseEmissiveMapLocation = L(_sceneProgram, "use_emissive_map");
            NormalTextureLocation = L(_sceneProgram, "normal_tex");
            EmissiveTextureLocation = L(_sceneProgram, "emissive_tex");
            _cameraPosition = L(_sceneProgram, "camera_world_position");
            _visualLightCount = L(_sceneProgram, "visual_light_count");
            _visualLightPositionRadius = L(_sceneProgram, "visual_light_position_radius");
            _visualLightColorIntensity = L(_sceneProgram, "visual_light_color_intensity");
            GL.UseProgram(_sceneProgram);
            GL.Uniform1(L(_sceneProgram, "albedo_tex"), GlesEnhancedShaderContract.AlbedoTextureUnit);
            GL.Uniform1(NormalTextureLocation, GlesEnhancedShaderContract.NormalTextureUnit);
            GL.Uniform1(EmissiveTextureLocation, GlesEnhancedShaderContract.EmissiveTextureUnit);
            var toon = new float[Metadata.ToonTable.Count * 3];
            for (int i = 0; i < Metadata.ToonTable.Count; i++)
            {
                toon[i * 3] = Metadata.ToonTable[i].X;
                toon[i * 3 + 1] = Metadata.ToonTable[i].Y;
                toon[i * 3 + 2] = Metadata.ToonTable[i].Z;
            }
            GL.Uniform3(SceneLocations.ToonTable, Metadata.ToonTable.Count, toon);
        }

        private void PopulateCompositionLocations()
        {
            CompositionLocations.FadeColor = L(_compositionProgram, "fade_color");
            CompositionLocations.LayerAlpha = L(_compositionProgram, "alpha");
            CompositionLocations.UseMask = L(_compositionProgram, "use_mask");
            CompositionLocations.ViewWidth = L(_compositionProgram, "view_width");
            CompositionLocations.ViewHeight = L(_compositionProgram, "view_height");
            CompositionLocations.UseHudVertexColor = L(_compositionProgram, "use_hud_vertex_color");
            CompositionLocations.UseHudTexture = L(_compositionProgram, "use_hud_texture");
            GL.UseProgram(_compositionProgram);
            GL.Uniform1(L(_compositionProgram, "tex"), 0);
            GL.Uniform1(L(_compositionProgram, "mask"), 1);
            _toneScene = L(_toneProgram, "scene_tex");
            _toneLut = L(_toneProgram, "color_grade_lut");
            _toneExposure = L(_toneProgram, "exposure");
            _toneApply = L(_toneProgram, "apply_tone_map");
            _toneUseLut = L(_toneProgram, "use_color_grade_lut");
            _toneLutStrength = L(_toneProgram, "color_grade_strength");
            _toneTransfer = L(_toneProgram, "apply_output_transfer");
            GL.UseProgram(_toneProgram);
            GL.Uniform1(_toneScene, 0);
            GL.Uniform1(_toneLut, 1);
        }

        private static int L(int program, string name) => GL.GetUniformLocation(program, name);

        private bool TargetsMatch(Vector2i scene, Vector2i drawable, bool floatingPoint)
            => _sceneFramebuffer != 0 && _sceneSize == scene && _drawableSize == drawable
                && _floatingPoint == floatingPoint;

        private bool TryReplaceTargets(Vector2i scene, Vector2i drawable, bool floatingPoint)
        {
            int sceneTexture = 0, sceneFramebuffer = 0, depth = 0;
            int compositionTexture = 0, compositionFramebuffer = 0;
            int scratchTexture = 0, scratchFramebuffer = 0;
            int captureTexture = 0, captureFramebuffer = 0;
            try
            {
                if (!TryCreateColorTarget(scene, floatingPoint, withDepth: true,
                        out sceneTexture, out sceneFramebuffer, out depth)
                    || !TryCreateColorTarget(drawable, floatingPoint: false, withDepth: false,
                        out compositionTexture, out compositionFramebuffer, out _)
                    || !TryCreateColorTarget(drawable, floatingPoint: false, withDepth: false,
                        out scratchTexture, out scratchFramebuffer, out _))
                    return false;
                if (!TryCreateColorTarget(drawable, floatingPoint: false, withDepth: false,
                        out captureTexture, out captureFramebuffer, out _))
                    return false;

                ReleaseTargets();
                _sceneTexture = sceneTexture; sceneTexture = 0;
                _sceneFramebuffer = sceneFramebuffer; sceneFramebuffer = 0;
                _sceneDepthStencil = depth; depth = 0;
                _compositionTexture = compositionTexture; compositionTexture = 0;
                _compositionFramebuffer = compositionFramebuffer; compositionFramebuffer = 0;
                _scratchTexture = scratchTexture; scratchTexture = 0;
                _scratchFramebuffer = scratchFramebuffer; scratchFramebuffer = 0;
                _captureTexture = captureTexture; captureTexture = 0;
                _captureFramebuffer = captureFramebuffer; captureFramebuffer = 0;
                _sceneSize = scene;
                _drawableSize = drawable;
                _floatingPoint = floatingPoint;
                return true;
            }
            catch (Exception) { return false; }
            finally
            {
                DeleteTarget(sceneTexture, sceneFramebuffer, depth);
                DeleteTarget(compositionTexture, compositionFramebuffer, 0);
                DeleteTarget(scratchTexture, scratchFramebuffer, 0);
                DeleteTarget(captureTexture, captureFramebuffer, 0);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
        }

        private static bool TryCreateColorTarget(Vector2i size, bool floatingPoint,
            bool withDepth, out int texture, out int framebuffer, out int depth)
        {
            texture = GL.GenTexture();
            framebuffer = GL.GenFramebuffer();
            depth = 0;
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0,
                floatingPoint ? PixelInternalFormat.Rgba16f : PixelInternalFormat.Rgba,
                size.X, size.Y, 0, PixelFormat.Rgba,
                floatingPoint ? PixelType.HalfFloat : PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            if (withDepth)
            {
                depth = GL.GenRenderbuffer();
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                    RenderbufferStorage.Depth24Stencil8, size.X, size.Y);
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthStencilAttachment,
                    RenderbufferTarget.Renderbuffer, depth);
            }
            return GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                == FramebufferErrorCode.FramebufferComplete;
        }

        public void BeginScene(Vector2i sceneSize)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);
            GL.Viewport(0, 0, sceneSize.X, sceneSize.Y);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
            GL.UseProgram(_sceneProgram);
        }

        public void ApplyFrame(RenderFrame frame)
        {
            GL.Uniform3(_cameraPosition, frame.CameraWorldPosition);
            int count = Math.Min(frame.VisualLights.Count,
                GlesEnhancedShaderContract.MaximumPointLights);
            float[] positions = new float[GlesEnhancedShaderContract.MaximumPointLights * 4];
            float[] colors = new float[GlesEnhancedShaderContract.MaximumPointLights * 4];
            for (int i = 0; i < count; i++)
            {
                RenderVisualLight light = frame.VisualLights[i];
                positions[i * 4] = light.Position.X;
                positions[i * 4 + 1] = light.Position.Y;
                positions[i * 4 + 2] = light.Position.Z;
                positions[i * 4 + 3] = light.Radius;
                colors[i * 4] = light.Color.X;
                colors[i * 4 + 1] = light.Color.Y;
                colors[i * 4 + 2] = light.Color.Z;
                colors[i * 4 + 3] = light.Intensity;
            }
            GL.Uniform1(_visualLightCount, count);
            if (count > 0)
            {
                GL.Uniform4(_visualLightPositionRadius, count, positions);
                GL.Uniform4(_visualLightColorIntensity, count, colors);
            }
        }

        public void PrepareHudScene()
        {
            GL.Uniform1(UseNormalMapLocation, 0);
            GL.Uniform1(UseEmissiveMapLocation, 0);
            GL.Uniform4(EnhancedEmissionLocation, 1, 1, 1, 0);
            GL.Uniform1(_visualLightCount, 0);
        }

        public GlesEnhancedDrawResources ResolveResources(DrawSubmission submission,
            RenderFrame frame, int originalTexture)
        {
            EnhancedMaterial enhanced = submission.Material.Enhanced
                ?? EnhancedMaterial.FromOriginal(submission.Material);
            int albedo = Resolve(enhanced.Albedo, frame, originalTexture);
            int normal = Resolve(enhanced.Normal, frame, _normalTexture);
            int emissive = Resolve(enhanced.Emissive, frame, _blackTexture);
            return new GlesEnhancedDrawResources(albedo, normal, emissive,
                enhanced.Normal.HasValue, enhanced.Emissive.HasValue,
                enhanced.Smoothness, enhanced.EmissionTint, enhanced.EmissionStrength);
        }

        public void BeginResourceFrame()
        {
            _pinnedTextures.Clear();
        }

        private int Resolve(TextureIdentity? identity, RenderFrame frame, int fallback)
        {
            TryResolve(identity, frame, fallback, out int binding);
            return binding;
        }

        private bool TryResolve(TextureIdentity? identity, RenderFrame frame,
            int fallback, out int binding)
        {
            if (identity is not TextureIdentity key
                || !frame.TextureResources.TryGetValue(key, out RenderTexturePixels? pixels))
            {
                binding = fallback;
                return false;
            }
            var uploadKey = new GlesImmutableTextureKey(_generation, key, pixels.Revision);
            if (_textures.TryGetValue(key, out CachedTexture cached)
                && cached.Key == uploadKey)
            {
                _textures[key] = cached with { LastUseOrdinal = ++_textureUseOrdinal };
                _pinnedTextures.Add(key);
                binding = cached.Binding;
                return true;
            }
            if (cached.Binding != 0)
            {
                // A sealed frame may already contain this native binding.
                // Never replace it until the next BeginResourceFrame clears pins.
                if (_pinnedTextures.Contains(key))
                {
                    binding = fallback;
                    return false;
                }
                GL.DeleteTexture(cached.Binding);
                _textures.Remove(key);
            }
            if (!TryEvictOldestTextureIfFull())
            {
                binding = fallback;
                return false;
            }
            binding = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, binding);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                pixels.Width, pixels.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte,
                pixels.Rgba8.ToArray());
            ConfigureCompleteSampler(linear: true, RepeatMode.Clamp, RepeatMode.Clamp);
            _textures[key] = new CachedTexture(uploadKey, binding, ++_textureUseOrdinal);
            _pinnedTextures.Add(key);
            return true;
        }

        private bool TryEvictOldestTextureIfFull()
        {
            if (_textures.Count < GlesEnhancedShaderContract.MaximumCachedTextures)
                return true;
            int unpinnedCount = _textures.Count - _pinnedTextures.Count;
            if (!GlesEnhancedTextureCachePolicy.CanAllocate(
                    _textures.Count, unpinnedCount)) return false;
            TextureIdentity oldestIdentity = default;
            CachedTexture oldest = default;
            bool found = false;
            foreach ((TextureIdentity identity, CachedTexture candidate) in _textures)
            {
                if (_pinnedTextures.Contains(identity)) continue;
                if (!found || candidate.LastUseOrdinal < oldest.LastUseOrdinal)
                {
                    oldestIdentity = identity;
                    oldest = candidate;
                    found = true;
                }
            }
            if (found)
            {
                _textures.Remove(oldestIdentity);
                GL.DeleteTexture(oldest.Binding);
                return true;
            }
            return false;
        }

        public void RetireRoomTextures()
        {
            if (_generation == GlEs.ContextGeneration)
            {
                foreach (CachedTexture cached in _textures.Values)
                    GL.DeleteTexture(cached.Binding);
            }
            _textures.Clear();
            _pinnedTextures.Clear();
            _textureUseOrdinal = 0;
        }

        public void ResolveScene(RenderFrame frame)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositionFramebuffer);
            GL.Viewport(0, 0, _drawableSize.X, _drawableSize.Y);
            EnterFullscreen();
            GL.UseProgram(_toneProgram);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _sceneTexture);
            TextureIdentity? lutIdentity = frame.ColorGrade.LutTexture;
            bool gradeRequested = _floatingPoint && frame.ColorGrade.Enabled
                && lutIdentity.HasValue;
            int lutBinding = _whiteTexture;
            bool lutResolved = gradeRequested
                && TryResolve(lutIdentity, frame, _whiteTexture, out lutBinding);
            bool grade = GlesEnhancedColorGradePolicy.ShouldEnable(
                gradeRequested, lutResolved);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, lutBinding);
            ConfigureCompleteSampler(linear: true, RepeatMode.Clamp, RepeatMode.Clamp);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(_toneExposure, frame.Exposure);
            GL.Uniform1(_toneApply, _floatingPoint ? 1 : 0);
            GL.Uniform1(_toneUseLut, grade ? 1 : 0);
            GL.Uniform1(_toneLutStrength, grade ? frame.ColorGrade.Strength : 0);
            GL.Uniform1(_toneTransfer, 0);
            GL.DrawFullscreenQuad();
        }

        public void BindComposition()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _compositionFramebuffer);
            GL.Viewport(0, 0, _drawableSize.X, _drawableSize.Y);
            GL.UseProgram(_compositionProgram);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }

        public void CaptureScene()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _captureFramebuffer);
            GL.Viewport(0, 0, _drawableSize.X, _drawableSize.Y);
            EnterFullscreen();
            GL.UseProgram(_toneProgram);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _compositionTexture);
            GL.Uniform1(_toneApply, 0);
            GL.Uniform1(_toneUseLut, 0);
            GL.Uniform1(_toneLutStrength, 0);
            GL.Uniform1(_toneTransfer, 1);
            GL.DrawFullscreenQuad();
        }

        public void ApplyDisruption(int shiftProgram, ShaderLocations locations,
            float shiftFactor, int shiftIndex, float lerpFactor,
            float whiteoutFactor, float[] whiteoutTable)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchFramebuffer);
            EnterFullscreen();
            GL.UseProgram(shiftProgram);
            GL.Uniform1(locations.ShiftFactor, shiftFactor);
            GL.Uniform1(locations.ShiftIndex, shiftIndex);
            GL.Uniform1(locations.LerpFactor, lerpFactor);
            GL.Uniform1(locations.WhiteoutFactor, whiteoutFactor);
            if (whiteoutFactor != 0) GL.Uniform1(locations.WhiteoutTable, 192, whiteoutTable);
            GL.BindTexture(TextureTarget.Texture2D, _compositionTexture);
            GL.DrawFullscreenQuad();
            (_compositionFramebuffer, _scratchFramebuffer)
                = (_scratchFramebuffer, _compositionFramebuffer);
            (_compositionTexture, _scratchTexture) = (_scratchTexture, _compositionTexture);
        }

        public void Present(bool faceCulling)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, _drawableSize.X, _drawableSize.Y);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            EnterFullscreen();
            GL.UseProgram(_toneProgram);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _compositionTexture);
            GL.Uniform1(_toneApply, 0);
            GL.Uniform1(_toneUseLut, 0);
            GL.Uniform1(_toneLutStrength, 0);
            GL.Uniform1(_toneTransfer, 1);
            GL.DrawFullscreenQuad();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            if (faceCulling) GL.Enable(EnableCap.CullFace);
            else GL.Disable(EnableCap.CullFace);
        }

        private static void EnterFullscreen()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);
        }

        private void CreateFallbackTextures()
        {
            _whiteTexture = CreateSolidTexture(255, 255, 255, 255);
            _normalTexture = CreateSolidTexture(128, 128, 255, 255);
            _blackTexture = CreateSolidTexture(0, 0, 0, 255);
        }

        private void ReleaseFallbackTextures()
        {
            GL.DeleteTexture(_whiteTexture);
            GL.DeleteTexture(_normalTexture);
            GL.DeleteTexture(_blackTexture);
            _whiteTexture = _normalTexture = _blackTexture = 0;
        }

        private static int CreateSolidTexture(byte r, byte g, byte b, byte a)
        {
            int texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte,
                new[] { r, g, b, a });
            ConfigureCompleteSampler(linear: true, RepeatMode.Clamp, RepeatMode.Clamp);
            return texture;
        }

        public static void ConfigureMaterialSampler(bool filtering,
            RepeatMode wrapX, RepeatMode wrapY)
            => ConfigureCompleteSampler(filtering, wrapX, wrapY);

        private static void ConfigureCompleteSampler(bool linear,
            RepeatMode wrapX, RepeatMode wrapY)
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                linear ? (int)TextureMinFilter.Linear : (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                linear ? (int)TextureMagFilter.Linear : (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                Wrap(wrapX));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                Wrap(wrapY));
        }

        private static int Wrap(RepeatMode repeat) => repeat switch
        {
            RepeatMode.Repeat => (int)TextureWrapMode.Repeat,
            RepeatMode.Mirror => (int)TextureWrapMode.MirroredRepeat,
            _ => (int)TextureWrapMode.ClampToEdge
        };

        private void ReleaseTargets()
        {
            DeleteTarget(_sceneTexture, _sceneFramebuffer, _sceneDepthStencil);
            DeleteTarget(_compositionTexture, _compositionFramebuffer, 0);
            DeleteTarget(_scratchTexture, _scratchFramebuffer, 0);
            DeleteTarget(_captureTexture, _captureFramebuffer, 0);
            _sceneTexture = _sceneFramebuffer = _sceneDepthStencil = 0;
            _compositionTexture = _compositionFramebuffer = 0;
            _scratchTexture = _scratchFramebuffer = 0;
            _captureTexture = _captureFramebuffer = 0;
        }

        private static void DeleteTarget(int texture, int framebuffer, int depth)
        {
            GL.DeleteRenderbuffer(depth);
            GL.DeleteFramebuffer(framebuffer);
            GL.DeleteTexture(texture);
        }

        private void ReleasePrograms()
        {
            GL.DeleteProgram(_sceneProgram);
            GL.DeleteProgram(_toneProgram);
            GL.DeleteProgram(_compositionProgram);
            _sceneProgram = _toneProgram = _compositionProgram = 0;
        }
    }

    internal readonly record struct GlesEnhancedDrawResources(
        int AlbedoTexture,
        int NormalTexture,
        int EmissiveTexture,
        bool UseNormalMap,
        bool UseEmissiveMap,
        float Smoothness,
        Vector3 EmissionTint,
        float EmissionStrength);
}
#endif
