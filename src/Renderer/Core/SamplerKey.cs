using System;
using MphRead.Mods;

namespace MphRead
{
    public enum RenderFilterMode : byte
    {
        Nearest,
        Linear,
        LinearMipmapped,
        Anisotropic
    }

    /// <summary>Small immutable sampler-cache key; no backend enum leaks out.</summary>
    public readonly struct SamplerKey : IEquatable<SamplerKey>
    {
        public RenderFilterMode Filter { get; }
        public RepeatMode WrapX { get; }
        public RepeatMode WrapY { get; }
        public bool Mipmapped { get; }
        public AnisotropyLevel Anisotropy { get; }

        public SamplerKey(RenderFilterMode filter, RepeatMode wrapX, RepeatMode wrapY)
            : this(filter, wrapX, wrapY, mipmapped: false, AnisotropyLevel.Off)
        {
        }

        public SamplerKey(RenderFilterMode filter, RepeatMode wrapX, RepeatMode wrapY,
            bool mipmapped, AnisotropyLevel anisotropy)
        {
            Filter = filter;
            WrapX = wrapX;
            WrapY = wrapY;
            Mipmapped = mipmapped;
            Anisotropy = NormalizeAnisotropy(anisotropy);
        }

        public static SamplerKey From(RenderMaterial material, bool filtering = false)
        {
            return new SamplerKey(filtering ? RenderFilterMode.Linear : RenderFilterMode.Nearest,
                material.WrapX, material.WrapY);
        }

        public static SamplerKey From(RenderMaterial material, RenderQualitySnapshot quality)
        {
            AnisotropyLevel anisotropy = quality.TextureFilteringPreset == TextureFilteringPreset.Enhanced
                ? NormalizeAnisotropy(quality.Anisotropy) : AnisotropyLevel.Off;
            RenderFilterMode filter = quality.TextureFilteringPreset switch
            {
                TextureFilteringPreset.Smooth => RenderFilterMode.Linear,
                TextureFilteringPreset.Enhanced when anisotropy != AnisotropyLevel.Off
                    => RenderFilterMode.Anisotropic,
                TextureFilteringPreset.Enhanced => RenderFilterMode.LinearMipmapped,
                _ => RenderFilterMode.Nearest
            };
            return new SamplerKey(filter, material.WrapX, material.WrapY,
                quality.UsesMipmaps, anisotropy);
        }

        public SamplerKey WithAnisotropy(AnisotropyLevel anisotropy)
        {
            anisotropy = NormalizeAnisotropy(anisotropy);
            RenderFilterMode filter = anisotropy == AnisotropyLevel.Off
                ? (Mipmapped ? RenderFilterMode.LinearMipmapped
                    : Filter == RenderFilterMode.Anisotropic ? RenderFilterMode.Linear : Filter)
                : RenderFilterMode.Anisotropic;
            return new SamplerKey(filter, WrapX, WrapY, Mipmapped, anisotropy);
        }

        public bool Equals(SamplerKey other)
            => Filter == other.Filter && WrapX == other.WrapX && WrapY == other.WrapY
                && Mipmapped == other.Mipmapped && Anisotropy == other.Anisotropy;
        public override bool Equals(object? obj) => obj is SamplerKey other && Equals(other);
        public override int GetHashCode()
            => HashCode.Combine(Filter, WrapX, WrapY, Mipmapped, Anisotropy);
        public static bool operator ==(SamplerKey left, SamplerKey right) => left.Equals(right);
        public static bool operator !=(SamplerKey left, SamplerKey right) => !left.Equals(right);

        private static AnisotropyLevel NormalizeAnisotropy(AnisotropyLevel value)
            => value switch
            {
                AnisotropyLevel.X2 or AnisotropyLevel.X4 or AnisotropyLevel.X8
                    or AnisotropyLevel.X16 => value,
                _ => AnisotropyLevel.Off
            };
    }
}
