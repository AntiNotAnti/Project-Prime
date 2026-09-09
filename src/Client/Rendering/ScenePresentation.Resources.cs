using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MphRead.Effects;
using MphRead.Formats;
using OpenTK.Mathematics;
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
        private readonly Dictionary<int, TextureIdentity> _bindingTextureIdentities = new();
        private readonly Dictionary<int, DynamicTextureSource> _dynamicTextureSources = new();
        private long _textureRevision;

        private sealed class Binding
        {
            public Binding() { }
            public int Id;
        }
        private readonly ConditionalWeakTable<object, Binding> _meshBindings = new();
        private readonly ConditionalWeakTable<Material, Binding> _materialBindings = new();
        private readonly ConditionalWeakTable<EffectElementEntry, List<int>> _effectBindings = new();
        public int GetMeshListId(Mesh mesh) => _meshBindings.GetOrCreateValue(mesh.GeometryIdentity).Id;
        public void SetMeshListId(Mesh mesh, int id) => _meshBindings.GetOrCreateValue(mesh.GeometryIdentity).Id = id;
        public int GetTextureBindingId(Material material) => _materialBindings.GetOrCreateValue(material).Id;
        public void SetTextureBindingId(Material material, int id) => _materialBindings.GetOrCreateValue(material).Id = id;
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
            return identity;
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
