using System;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Backend-independent texture sampling recipe. OpenGL applies this today;
    /// the modern backends consume the same policy as their world renderer is
    /// brought across so texture quality does not silently diverge by API.
    /// </summary>
    internal readonly record struct TextureSamplerDescriptor(
        bool LinearMagnification,
        bool LinearMinification,
        bool Mipmaps,
        int Anisotropy,
        float LodBias)
    {
        public string CacheKey =>
            (LinearMagnification ? "lmag" : "nmag") + "-"
            + (LinearMinification ? "lmin" : "nmin") + "-"
            + (Mipmaps ? "mip" : "nomip") + "-"
            + Anisotropy + "x-" + LodBias.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolves authored/HD texture sampling independently from source
    /// resolution. Auto intentionally stabilizes detailed 3D materials at
    /// distance; Legacy preserves nearest-neighbour cartridge presentation;
    /// Custom follows the existing filtering/mipmap/anisotropy controls.
    /// </summary>
    internal static class TextureSamplingPolicy
    {
        public static string RuntimeKey =>
            RenderOptions.TextureSampling + "|surface="
            + ResolveModern(TextureAssetClass.World, TextureAssetChannel.Albedo).CacheKey
            + "|effect=" + ResolveModern(TextureAssetClass.Effect, TextureAssetChannel.Effect).CacheKey;

        public static TextureSamplerDescriptor ResolveNativeWorld()
        {
            bool filtering = RenderOptions.TextureFiltering;
            bool mipmaps = filtering && RenderOptions.TextureMipmaps;
            return new TextureSamplerDescriptor(
                LinearMagnification: filtering,
                LinearMinification: filtering,
                Mipmaps: mipmaps,
                Anisotropy: filtering ? Math.Clamp(RenderOptions.TextureAnisotropy, 1, 16) : 1,
                LodBias: 0);
        }

        public static TextureSamplerDescriptor ResolveModern(TextureAssetClass assetClass,
            TextureAssetChannel channel)
        {
            _ = channel;

            // UI stays outside the authored 3D-material policy. Effect/model
            // replacements are still 3D materials and use Auto; intentionally
            // pixel-styled sprites should remain on their dedicated native/UI
            // paths rather than being classified as modern material assets.
            if (assetClass == TextureAssetClass.Ui)
                return ResolveNativeWorld();

            return RenderOptions.TextureSampling switch
            {
                TextureSamplingMode.Legacy => new TextureSamplerDescriptor(
                    LinearMagnification: false,
                    LinearMinification: false,
                    Mipmaps: false,
                    Anisotropy: 1,
                    LodBias: 0),
                TextureSamplingMode.Custom => ResolveNativeWorld(),
                _ => new TextureSamplerDescriptor(
                    LinearMagnification: true,
                    LinearMinification: true,
                    Mipmaps: true,
                    // Mobile gets the stable path without forcing the most
                    // expensive footprint while Android frame pacing is tight.
                    Anisotropy: OperatingSystem.IsAndroid() ? 4 : 8,
                    LodBias: 0)
            };
        }

        public static string Describe(TextureSamplerDescriptor descriptor)
        {
            string min = descriptor.Mipmaps ? "trilinear"
                : descriptor.LinearMinification ? "linear" : "nearest";
            string mag = descriptor.LinearMagnification ? "linear" : "nearest";
            return $"min={min}, mag={mag}, aniso={descriptor.Anisotropy}x, lod={descriptor.LodBias:0.###}";
        }
    }
}
