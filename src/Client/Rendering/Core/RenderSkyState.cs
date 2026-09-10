using System;
using System.Collections.Generic;
using System.Linq;

namespace MphRead
{
    internal static class EnhancedSkyRuntimePolicy
    {
        public static bool TryCreateRoomDefaultKey(string? archive,
            out EnhancedSkyAssetKey key)
            => EnhancedSkyAssetKey.TryParse($"room/{archive}/sky/default", out key);
    }

    public sealed record RenderSkyCubemap(
        TextureIdentity PositiveX,
        TextureIdentity NegativeX,
        TextureIdentity PositiveY,
        TextureIdentity NegativeY,
        TextureIdentity PositiveZ,
        TextureIdentity NegativeZ)
    {
        public IReadOnlyList<TextureIdentity> Faces { get; } = Array.AsReadOnly(new[]
        {
            PositiveX, NegativeX, PositiveY, NegativeY, PositiveZ, NegativeZ
        });
    }

    public sealed record RenderSkyLayer(
        string StableKey,
        EnhancedSkyCompositionKind Kind,
        TextureIdentity Texture,
        int Order,
        float ScrollU,
        float ScrollV,
        float RotationSpeed,
        float Opacity,
        float Intensity,
        float TwinkleRate,
        EnhancedSkyOverlayBlend Blend,
        bool SelectiveBloom);

    /// <summary>
    /// Immutable, bounded sky selection captured with one presentation frame.
    /// It contains identities and animation values only; decoded pixels remain
    /// in the frame resource table and GPU resources remain backend-owned.
    /// </summary>
    public sealed class RenderSkyState
    {
        private readonly IReadOnlyList<RenderSkyLayer> _composition;
        private readonly IReadOnlyList<TextureIdentity> _textures;

        private RenderSkyState(EnhancedSkyAssetKey key, EnhancedSkyBaseKind baseKind,
            TextureIdentity? background, RenderSkyCubemap? cubemap,
            IReadOnlyList<RenderSkyLayer> composition, float timeSeconds)
        {
            if (!key.IsValid) throw new ArgumentException("Sky key must be valid.", nameof(key));
            if (!float.IsFinite(timeSeconds) || timeSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timeSeconds));
            if (composition.Count > EnhancedSkyPackLimits.MaximumLayersPerSky + 2)
                throw new ArgumentException("Sky composition exceeds its bounded layer count.", nameof(composition));
            if ((baseKind == EnhancedSkyBaseKind.Cubemap && cubemap is null)
                || (baseKind == EnhancedSkyBaseKind.Background2D && background is null)
                || baseKind == EnhancedSkyBaseKind.None)
            {
                throw new ArgumentException("Sky base selection is incomplete.");
            }

            Key = key;
            BaseKind = baseKind;
            Background = baseKind == EnhancedSkyBaseKind.Background2D ? background : null;
            Cubemap = baseKind == EnhancedSkyBaseKind.Cubemap ? cubemap : null;
            _composition = Array.AsReadOnly(composition.ToArray());
            TimeSeconds = timeSeconds;
            var textures = new List<TextureIdentity>(6 + composition.Count);
            if (Background is TextureIdentity baseTexture) textures.Add(baseTexture);
            if (Cubemap is not null) textures.AddRange(Cubemap.Faces);
            textures.AddRange(_composition.Select(layer => layer.Texture));
            _textures = Array.AsReadOnly(textures.Distinct().ToArray());
        }

        public bool Enabled => true;
        public EnhancedSkyAssetKey Key { get; }
        public EnhancedSkyBaseKind BaseKind { get; }
        public TextureIdentity? Background { get; }
        public RenderSkyCubemap? Cubemap { get; }
        public IReadOnlyList<RenderSkyLayer> Composition => _composition;
        public IReadOnlyList<TextureIdentity> TextureIdentities => _textures;
        public float TimeSeconds { get; }
        public bool SelectiveBloom => false;

        public static RenderSkyState? FromReplacement(EnhancedSkyReplacement? replacement,
            TimeSpan presentationTime)
        {
            if (replacement is null || presentationTime < TimeSpan.Zero
                || !double.IsFinite(presentationTime.TotalSeconds)
                || presentationTime.TotalSeconds > float.MaxValue)
            {
                return null;
            }

            TextureIdentity? background = replacement.BaseBackground?.TextureIdentity;
            RenderSkyCubemap? cubemap = replacement.BaseCubemap is EnhancedSkyCubemap source
                ? new RenderSkyCubemap(source.PositiveX.TextureIdentity,
                    source.NegativeX.TextureIdentity, source.PositiveY.TextureIdentity,
                    source.NegativeY.TextureIdentity, source.PositiveZ.TextureIdentity,
                    source.NegativeZ.TextureIdentity)
                : null;
            if (replacement.BaseKind == EnhancedSkyBaseKind.None) return null;
            RenderSkyLayer[] composition = replacement.Composition.Select(entry =>
                new RenderSkyLayer(entry.StableKey, entry.Kind, entry.Texture.TextureIdentity,
                    entry.Order, entry.ScrollU, entry.ScrollV, entry.RotationSpeed,
                    entry.Opacity, entry.Intensity, entry.TwinkleRate, entry.Blend,
                    entry.SelectiveBloom)).ToArray();
            return new RenderSkyState(replacement.Key, replacement.BaseKind, background,
                cubemap, composition, (float)presentationTime.TotalSeconds);
        }
    }
}
