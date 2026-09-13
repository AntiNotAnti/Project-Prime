using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;
#if ANDROID
using OpenTK.Graphics.OpenGL;
#endif
namespace MphRead
{
    public partial class ScenePresentation
    {
        private sealed class DynamicTextureSource
        {
            public DynamicTextureSource(int width, int height) { Width = width; Height = height; }
            public int Width { get; }
            public int Height { get; }
        }

        private readonly Dictionary<TextureIdentity, RenderTexturePixels> _textureResources = new();
        private readonly EnhancedTextureResolver _enhancedTextureResolver
            = LoadSelectedEnhancedTextureResolver();
        private readonly EnhancedEnvironmentOverrides _enhancedEnvironmentOverrides
            = LoadDefaultEnhancedEnvironmentOverrides();
        private readonly EnhancedColorGradeResolver _enhancedColorGradeResolver
            = LoadDefaultEnhancedColorGradeResolver();
        private readonly ReflectionProbeResolver _reflectionProbeResolver
            = LoadDefaultReflectionProbeResolver();
        private readonly SoftParticleProfileCatalog _softParticleProfiles
            = LoadDefaultSoftParticleProfiles();
        private readonly EnhancedSkyResolver _enhancedSkyResolver
            = LoadDefaultEnhancedSkyResolver();
        private ReflectionCubemapAsset? _cachedReflectionCubemap;
        private RenderReflectionProbe? _cachedRenderReflectionProbe;
        private readonly HashSet<ReflectionProbeKey> _reportedGeneratedProbeGaps = new();
        private readonly Dictionary<int, TextureIdentity> _bindingTextureIdentities = new();
        private readonly Dictionary<int, DynamicTextureSource> _dynamicTextureSources = new();
#if ANDROID
        private readonly Dictionary<TextureIdentity, int> _dynamicTextureBindings = new();
#endif
        private long _textureRevision;

        private sealed class Binding
        {
            public Binding() { }
            public int Id;
        }
        private readonly ConditionalWeakTable<object, Binding> _meshBindings = new();
        private readonly ConditionalWeakTable<Material, Binding> _materialBindings = new();
        private readonly ConditionalWeakTable<EffectElementEntry, List<int>> _effectBindings = new();
        private sealed class SoftParticleBinding
        {
            public SoftParticleProfile? Profile;
        }
        private readonly ConditionalWeakTable<EffectElementEntry, SoftParticleBinding>
            _softParticleBindings = new();
        public int GetMeshListId(Mesh mesh) => _meshBindings.GetOrCreateValue(mesh.GeometryIdentity).Id;
        public void SetMeshListId(Mesh mesh, int id) => _meshBindings.GetOrCreateValue(mesh.GeometryIdentity).Id = id;
        public int GetTextureBindingId(Material material) => _materialBindings.GetOrCreateValue(material).Id;
        public void SetTextureBindingId(Material material, int id) => _materialBindings.GetOrCreateValue(material).Id = id;
        internal SoftParticleProfile? GetSoftParticleProfile(EffectElementEntry element)
            => _softParticleBindings.TryGetValue(element, out SoftParticleBinding? binding)
                ? binding.Profile : null;

        internal void SetSoftParticleProfile(EffectElementEntry element,
            int effectId, int elementIndex)
        {
            SoftParticleBinding binding = _softParticleBindings.GetOrCreateValue(element);
            binding.Profile = SoftParticlePresentationKey.Create(effectId, elementIndex)
                is SoftParticlePresentationKey key
                && _softParticleProfiles.TryResolve(key, out SoftParticleProfile profile)
                    ? profile : null;
        }

        internal void ClearSoftParticleProfile(EffectElementEntry element)
            => _softParticleBindings.GetOrCreateValue(element).Profile = null;
        public TextureIdentity? GetTextureIdentity(Model model, Material material, int recolorId, object? variant = null,
            OpenTK.Mathematics.Vector4? paletteOverride = null)
        {
            if (material.CurrentTextureId < 0 || model.Recolors.Count == 0)
            {
                return null;
            }

            // Recolors are parsed immutable source objects shared by runtime
            // model copies. They therefore remain stable across frames while
            // the explicit IDs retain animated texture/palette selection.
            if ((uint)recolorId >= (uint)model.Recolors.Count)
            {
                recolorId = 0;
            }
            TextureIdentity identity = new TextureIdentity(model.Recolors[recolorId], material.CurrentTextureId,
                material.CurrentPaletteId, recolorId, variant, paletteOverride);
            if (!_textureResources.ContainsKey(identity))
            {
                Texture texture = model.Recolors[recolorId].Textures[material.CurrentTextureId];
                IReadOnlyList<ColorRgba> pixels = model.GetPixels(material.CurrentTextureId,
                    material.CurrentPaletteId, recolorId);
                try
                {
                    PrepareTexture(identity, pixels, texture.Width, texture.Height);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"Model {model.Name} current texture "
                        + $"{material.CurrentTextureId}, palette {material.CurrentPaletteId}, recolor {recolorId} "
                        + $"decoded {pixels.Count} pixels for {texture.Width}x{texture.Height} dimensions.", ex);
                }
            }
            return identity;
        }

        public static TextureAssetKey? GetModelTextureAssetKey(Model model,
            Material material, int recolorId)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(material);
            if (material.CurrentTextureId < 0 || material.CurrentPaletteId < 0
                || recolorId < 0)
            {
                return null;
            }
            return TextureAssetKey.TryParse(
                $"model/{model.Name}/texture/{material.CurrentTextureId}/palette/"
                + $"{material.CurrentPaletteId}/recolor/{recolorId}", out TextureAssetKey key)
                    ? key : null;
        }

        public static TextureAssetKey? GetEffectTextureAssetKey(string effectName,
            Material material)
        {
            ArgumentNullException.ThrowIfNull(material);
            return TextureAssetKey.TryParse(
                $"effect/{effectName}/texture/{material.CurrentTextureId}", out TextureAssetKey key)
                    ? key : null;
        }

        public static TextureAssetKey? GetRoomTextureAssetKey(RoomMetadata room,
            Material material)
        {
            ArgumentNullException.ThrowIfNull(room);
            ArgumentNullException.ThrowIfNull(material);
            if (material.CurrentTextureId < 0 || material.CurrentPaletteId < 0)
                return null;
            // Archive is the stable content name (for example mp1). The room's
            // display/metadata Name contains spaces and is not a canonical key.
            return TextureAssetKey.TryParse(
                $"room/{room.Archive}/texture/{material.CurrentTextureId}/palette/"
                + material.CurrentPaletteId, out TextureAssetKey key) ? key : null;
        }

        internal void ResolveEnhancedMaterial(DrawSubmission submission)
        {
            if (!RenderOptions.TexturePackEnabled
                || submission.TextureAssetKey is not TextureAssetKey key
                || !key.IsValid)
            {
                return;
            }
            submission.FreezeMaterial();
            EnhancedMaterial material = _enhancedTextureResolver.Resolve(key,
                submission.Material);
            submission.EnhancedMaterial = material;
            RegisterEnhancedTexture(material.Albedo);
            RegisterEnhancedTexture(material.Normal);
            RegisterEnhancedTexture(material.Emissive);
        }

        private void RegisterEnhancedTexture(TextureIdentity? requested)
        {
            if (requested is not TextureIdentity identity
                || identity.Source is not EnhancedTextureAsset asset
                || _textureResources.ContainsKey(identity))
            {
                return;
            }
            (bool onlyOpaque, Vector3 flat) = AnalyzeRgba(asset.Rgba8.Span);
            _textureResources.Add(identity, new RenderTexturePixels(identity,
                asset.Width, asset.Height, asset.Rgba8, revision: 1,
                onlyOpaque: onlyOpaque, alphaWeightedFlatColor: flat));
        }

        private static (bool OnlyOpaque, Vector3 FlatColor) AnalyzeRgba(
            ReadOnlySpan<byte> rgba)
        {
            if (rgba.Length == 0) return (true, Vector3.One);
            double red = 0;
            double green = 0;
            double blue = 0;
            double alphaWeight = 0;
            bool onlyOpaque = true;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                double alpha = rgba[i + 3] / 255d;
                onlyOpaque &= rgba[i + 3] == 255;
                red += rgba[i] * alpha;
                green += rgba[i + 1] * alpha;
                blue += rgba[i + 2] * alpha;
                alphaWeight += alpha;
            }
            Vector3 flat = alphaWeight > 0
                ? new Vector3((float)(red / alphaWeight / 255d),
                    (float)(green / alphaWeight / 255d),
                    (float)(blue / alphaWeight / 255d))
                : Vector3.One;
            return (onlyOpaque, flat);
        }

        private static EnhancedTextureResolver LoadSelectedEnhancedTextureResolver()
        {
            if (!RenderOptions.TexturePackEnabled)
                return EnhancedTextureResolver.Empty;
            string root = TexturePackCatalog.PackRoot(LauncherPrefs.Directory,
                RenderOptions.TexturePackId);
            if (!TexturePackCatalog.IsUsablePackDirectory(root))
                return EnhancedTextureResolver.Empty;
            EnhancementPackLoadResult loaded = EnhancementPackLoader.Load(root);
            if (loaded.HasIssues)
            {
                Console.WriteLine($"[render] enhancement pack loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            }
            return loaded.Resolver;
        }

        internal (EnhancedEnvironment Environment, EnhancedColorGradeSelection ColorGrade)
            ResolveEnhancedEnvironmentSnapshot(RenderQualitySnapshot quality,
                bool enhancedEnvironmentBackend)
        {
            if (!enhancedEnvironmentBackend || quality.GraphicsPreset != GraphicsPreset.Enhanced
                || World.Room == null)
            {
                return (EnhancedEnvironment.Neutral,
                    _enhancedColorGradeResolver.Resolve((EnhancedEnvironmentAssetKey?)null));
            }

            EnhancedEnvironment environment
                = _enhancedEnvironmentOverrides.Resolve(World.Room.Meta);
            return (environment, _enhancedColorGradeResolver.Resolve(environment));
        }

        internal RenderReflectionProbe? ResolveEnhancedReflectionProbeSnapshot(
            RenderQualitySnapshot quality, bool sdlBackend,
            EnhancedEnvironment environment)
        {
            if (!sdlBackend || quality.GraphicsPreset != GraphicsPreset.Enhanced
                || World.Room == null)
            {
                return null;
            }

            string room = World.Room.Meta.Archive;
            ReflectionProbeSelection declared = _reflectionProbeResolver.Resolve(room,
                environment.ReflectionProbeKey);
            if (declared.Kind == ReflectionProbeSourceKind.GeneratedStatic
                && _reportedGeneratedProbeGaps.Add(declared.Key))
            {
                Console.WriteLine($"[render] generated-static reflection probe "
                    + $"'{declared.Key}' has no offline cube source; using authored fallback.");
            }
            ReflectionProbeSelection selected
                = _reflectionProbeResolver.ResolveUploadable(room,
                    environment.ReflectionProbeKey);
            if (selected.Cubemap is not ReflectionCubemapAsset cubemap)
                return null;
            if (!ReferenceEquals(_cachedReflectionCubemap, cubemap))
            {
                _cachedReflectionCubemap = cubemap;
                _cachedRenderReflectionProbe = new RenderReflectionProbe(cubemap);
            }
            return _cachedRenderReflectionProbe;
        }

        internal RenderSkyState? ResolveEnhancedSkySnapshot(RenderQualitySnapshot quality,
            bool sdlBackend, TimeSpan presentationTime)
        {
            if (!sdlBackend || quality.GraphicsPreset != GraphicsPreset.Enhanced
                || World.Room == null
                || !EnhancedSkyRuntimePolicy.TryCreateRoomDefaultKey(
                    World.Room.Meta.Archive, out EnhancedSkyAssetKey key)
                || !_enhancedSkyResolver.TryGetReplacement(key,
                    out EnhancedSkyReplacement? replacement))
            {
                return null;
            }
            return RenderSkyState.FromReplacement(replacement, presentationTime);
        }

        private static EnhancedSkyResolver LoadDefaultEnhancedSkyResolver()
        {
            string root = Path.Combine(LauncherPrefs.Directory,
                "enhancements", "default");
            if (!File.Exists(Path.Combine(root, EnhancedSkyPackLoader.ManifestFileName)))
                return EnhancedSkyResolver.Empty;
            EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(root);
            if (loaded.HasIssues)
            {
                Console.WriteLine($"[render] enhanced skies loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            }
            return loaded.Resolver;
        }

        private static EnhancedEnvironmentOverrides LoadDefaultEnhancedEnvironmentOverrides()
        {
            string root = Path.Combine(LauncherPrefs.Directory,
                "enhancements", "default");
            if (!File.Exists(Path.Combine(root,
                EnhancedEnvironmentOverrideLoader.ManifestFileName)))
            {
                return EnhancedEnvironmentOverrides.Empty;
            }
            EnhancedEnvironmentLoadResult loaded
                = EnhancedEnvironmentOverrideLoader.Load(root);
            if (loaded.Issues.Count != 0)
            {
                Console.WriteLine($"[render] enhanced environments loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            }
            return loaded.Overrides;
        }

        private static EnhancedColorGradeResolver LoadDefaultEnhancedColorGradeResolver()
        {
            string root = Path.Combine(LauncherPrefs.Directory,
                "enhancements", "default");
            if (!File.Exists(Path.Combine(root,
                EnhancedColorGradePackLoader.ManifestFileName)))
            {
                return EnhancedColorGradeResolver.Empty;
            }
            EnhancedColorGradePackLoadResult loaded
                = EnhancedColorGradePackLoader.Load(root);
            if (loaded.HasIssues)
            {
                Console.WriteLine($"[render] enhanced color grades loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            }
            return loaded.Resolver;
        }

        private static ReflectionProbeResolver LoadDefaultReflectionProbeResolver()
        {
            string root = Path.Combine(LauncherPrefs.Directory,
                "enhancements", "default");
            if (!File.Exists(Path.Combine(root,
                ReflectionProbePackLoader.ManifestFileName)))
            {
                return ReflectionProbeResolver.Empty;
            }
            ReflectionProbePackLoadResult loaded = ReflectionProbePackLoader.Load(root);
            if (loaded.Issues.Count != 0)
            {
                Console.WriteLine($"[render] reflection probes loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            }
            return loaded.Resolver;
        }

        private static SoftParticleProfileCatalog LoadDefaultSoftParticleProfiles()
        {
            string root = Path.Combine(LauncherPrefs.Directory,
                "enhancements", "default");
            if (!File.Exists(Path.Combine(root, SoftParticleProfileLoader.ManifestFileName)))
                return SoftParticleProfileCatalog.Empty;
            SoftParticleProfileLoadResult loaded = SoftParticleProfileLoader.Load(root);
            if (loaded.Issues.Count != 0)
            {
                Console.WriteLine($"[render] soft-particle profiles loaded with "
                    + $"{loaded.Issues.Count} ignored issue(s).");
            }
            return loaded.Catalog;
        }
        internal List<int> GetEffectTextureBindings(EffectElementEntry element) => _effectBindings.GetOrCreateValue(element);

        /// <summary>
        /// Decode one source texture exactly once per client revision. The
        /// record is backend-neutral; OpenGL and SDL upload their own device
        /// handles from the same RGBA8 bytes.
        /// </summary>
        public RenderTexturePixels PrepareTexture(TextureIdentity identity,
            IReadOnlyList<ColorRgba> pixels, int width, int height, long? revision = null)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            int expectedBytes = RenderTexturePixels.ValidateRgba8ByteCount(width, height);
            int expectedPixels = expectedBytes / 4;
            if (pixels.Count < expectedPixels)
            {
                throw new ArgumentException(
                    $"Decoded texture contains {pixels.Count} pixels, but {width}x{height} requires at least {expectedPixels}.",
                    nameof(pixels));
            }
            long currentRevision = revision ?? ++_textureRevision;
            if (_textureResources.TryGetValue(identity, out RenderTexturePixels? existing)
                && existing.Revision == currentRevision)
            {
                return existing;
            }
            byte[] rgba = new byte[expectedBytes];
            bool onlyOpaque = true;
            float red = 0, green = 0, blue = 0, weight = 0;
            float plainRed = 0, plainGreen = 0, plainBlue = 0;
            // Dynamic HUD textures retain a maximum-size backing array while
            // Width/Height describe the active, tightly packed prefix. This
            // matches the legacy TexImage2D contract, which consumed exactly
            // width*height texels and ignored spare capacity.
            for (int i = 0; i < expectedPixels; i++)
            {
                ColorRgba pixel = pixels[i];
                int offset = i * 4;
                rgba[offset] = pixel.Red;
                rgba[offset + 1] = pixel.Green;
                rgba[offset + 2] = pixel.Blue;
                rgba[offset + 3] = pixel.Alpha;
                onlyOpaque &= pixel.Alpha == 255;
                float alpha = pixel.Alpha / 255f;
                red += pixel.Red * alpha;
                green += pixel.Green * alpha;
                blue += pixel.Blue * alpha;
                weight += alpha;
                plainRed += pixel.Red;
                plainGreen += pixel.Green;
                plainBlue += pixel.Blue;
            }
            Vector3 flat = weight > .01f
                ? new Vector3(red, green, blue) / weight / 255f
                : new Vector3(plainRed, plainGreen, plainBlue) / expectedPixels / 255f;
            var record = new RenderTexturePixels(identity, width, height, rgba, currentRevision,
                onlyOpaque, flat);
            _textureResources[identity] = record;
            return record;
        }

        public bool TryGetTexture(TextureIdentity identity, out RenderTexturePixels? texture)
            => _textureResources.TryGetValue(identity, out texture);

        public TextureIdentity CreateDynamicTextureIdentity(IReadOnlyList<ColorRgba> pixels, int width, int height,
            int? bindingId = null)
        {
            DynamicTextureSource source;
            if (bindingId is int id)
            {
                if (!_dynamicTextureSources.TryGetValue(id, out source!))
                {
                    source = new DynamicTextureSource(width, height);
                    _dynamicTextureSources.Add(id, source);
                }
            }
            else
            {
                source = new DynamicTextureSource(width, height);
            }
            TextureIdentity identity = new TextureIdentity(source, variant: source);
            PrepareTexture(identity, pixels, width, height);
#if ANDROID
            var upload = new ColorRgba[pixels.Count];
            for (int i = 0; i < upload.Length; i++) upload[i] = pixels[i];
            int binding = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, binding);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, upload);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _dynamicTextureBindings.Add(identity, binding);
#endif
            return identity;
        }

        public bool ReleaseDynamicTexture(TextureIdentity identity)
        {
            bool removed = _textureResources.Remove(identity);
            var identityBindings = new List<int>();
            foreach (KeyValuePair<int, TextureIdentity> pair in _bindingTextureIdentities)
                if (pair.Value == identity) identityBindings.Add(pair.Key);
            foreach (int binding in identityBindings) _bindingTextureIdentities.Remove(binding);
            if (identity.Source is DynamicTextureSource source)
            {
                var bindings = new List<int>();
                foreach (KeyValuePair<int, DynamicTextureSource> pair in _dynamicTextureSources)
                    if (ReferenceEquals(pair.Value, source)) bindings.Add(pair.Key);
                foreach (int binding in bindings) _dynamicTextureSources.Remove(binding);
            }
#if ANDROID
            if (_dynamicTextureBindings.Remove(identity, out int texture)) GL.DeleteTexture(texture);
#endif
            return removed;
        }

        public IReadOnlyDictionary<TextureIdentity, RenderTexturePixels> TextureResources => _textureResources;

        internal void RegisterLegacyTextureIdentity(int bindingId, TextureIdentity identity)
            => _bindingTextureIdentities[bindingId] = identity;

        public bool TryGetTextureIdentityForBinding(int bindingId, out TextureIdentity identity)
            => _bindingTextureIdentities.TryGetValue(bindingId, out identity);

        internal void ReleaseModelTextureResources(Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var sources = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (Recolor recolor in model.Recolors) sources.Add(recolor);
            var textureKeys = new List<TextureIdentity>();
            foreach (TextureIdentity identity in _textureResources.Keys)
            {
                if (sources.Contains(identity.Source)) textureKeys.Add(identity);
            }
            foreach (TextureIdentity identity in textureKeys) _textureResources.Remove(identity);
            var bindingKeys = new List<int>();
            foreach (KeyValuePair<int, TextureIdentity> binding in _bindingTextureIdentities)
            {
                if (sources.Contains(binding.Value.Source)) bindingKeys.Add(binding.Key);
            }
            foreach (int binding in bindingKeys) _bindingTextureIdentities.Remove(binding);
        }
    }
}
