#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

internal static class AuthoredRgbaMipGpuCheck
{
    internal static void Verify()
    {
        var channels = new[] { TextureAssetChannel.Albedo, TextureAssetChannel.Normal,
            TextureAssetChannel.Material, TextureAssetChannel.Emissive };
        var classes = new[] { TextureAssetClass.Hunter, TextureAssetClass.Weapon,
            TextureAssetClass.AlternateForm, TextureAssetClass.Turret };
        int checkedLevels = 0;
        foreach (var shape in new[] { (7, 5), (1, 9), (9, 1) })
        for (int channel = 0; channel < channels.Length; channel++)
        foreach (int cap in new[] { 8192, 3 })
        foreach (bool mipmaps in new[] { false, true })
        {
            var levels = AuthoredRgbaMipFixtures.Levels(shape.Item1, shape.Item2, channels[channel]);
            var asset = AuthoredRgbaMipFixtures.Decode(levels, classes[channel], channels[channel], cap);
            checkedLevels += VerifyUpload(asset, mipmaps);
        }
        var partial = AuthoredRgbaMipFixtures.Decode(
            AuthoredRgbaMipFixtures.Levels(7, 5, TextureAssetChannel.Normal, levelCount: 2),
            TextureAssetClass.Hunter, TextureAssetChannel.Normal, 8192);
        checkedLevels += VerifyUpload(partial, mipmaps: true);
        VerifyResidentCache();
        Console.WriteLine($"[authoredmipcheck] PASS {GraphicsBackendPolicy.Resolved}: {checkedLevels} exact uploaded mip levels, capped/partial/base-only sampling, cache/residency/delete accounting");
    }

    private static int VerifyUpload(RgbaMipTextureAsset asset, bool mipmaps)
    {
        bool modern = ModernGraphicsCompat.Active;
        long before = modern ? ModernGraphicsCompat.TrackedStorageCapacity.TextureBytes : 0;
        int texture = GraphicsApi.GenTexture();
        try
        {
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            var sampling = new TextureSamplerDescriptor(true, true, mipmaps, 1, 0);
            if (modern) ModernGraphicsCompat.BeginPerformanceSample();
            TextureAssetManager.UploadPreparedBound(asset, repeat: false, sampling);
            if (GraphicsApi.GetError() != ErrorCode.NoError)
                throw new InvalidOperationException("Authored mip promotion returned a GL error.");
            long expected = asset.EstimateGpuBytes(mipmaps);
            if (modern)
            {
                var upload = ModernGraphicsCompat.EndPerformanceSample();
                if (upload.TextureUploadBytes != expected)
                    throw new InvalidOperationException("Authored mip upload counters omit a level.");
                if (ModernGraphicsCompat.TrackedStorageCapacity.TextureBytes - before != expected)
                    throw new InvalidOperationException("Authored mip native capacity differs from admitted bytes.");
            }
            return VerifyTexture(texture, asset, mipmaps);
        }
        finally
        {
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteTexture(texture);
            if (modern && ModernGraphicsCompat.TrackedStorageCapacity.TextureBytes != before)
                throw new InvalidOperationException("Authored mip deletion did not return native capacity.");
        }
    }

    internal static int VerifyTexture(int texture, RgbaMipTextureAsset asset, bool mipmaps)
    {
        if (ModernGraphicsCompat.Active)
            return ModernGraphicsCompat.VerifyRgbaMipTextureForCheck(texture, asset, mipmaps);
        GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
        int count = mipmaps && asset.MipmapsAvailable ? asset.Mips.Length : 1;
        OpenTK.Graphics.OpenGL.GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
        for (int level = 0; level < count; level++)
        {
            RgbaTextureMip mip = asset.Mips[level];
            GraphicsApi.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureWidth, out int width);
            GraphicsApi.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureHeight, out int height);
            byte[] pixels = new byte[mip.Data.Length];
            OpenTK.Graphics.OpenGL.GL.GetTexImage(TextureTarget.Texture2D, level, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            if (width != mip.Width || height != mip.Height || !pixels.AsSpan().SequenceEqual(mip.Data))
                throw new InvalidOperationException($"OpenGL authored mip {level} metadata or bytes changed.");
        }
        OpenTK.Graphics.OpenGL.GL.GetTexParameter(TextureTarget.Texture2D, GetTextureParameter.TextureMinFilter, out int filter);
        int expectedFilter = mipmaps && asset.MipmapsAvailable
            ? (int)TextureMinFilter.LinearMipmapLinear : (int)TextureMinFilter.Linear;
        if (filter != expectedFilter)
            throw new InvalidOperationException("OpenGL partial/base-only authored texture has the wrong sampling filter.");
        if (count < asset.Mips.Length)
        {
            GraphicsApi.GetTexLevelParameter(TextureTarget.Texture2D, count, GetTextureParameter.TextureWidth, out int width);
            if (width != 0) throw new InvalidOperationException("OpenGL uploaded unused or regenerated authored levels.");
        }
        return count;
    }

    private static void VerifyResidentCache()
    {
        foreach (var assetClass in new[] { TextureAssetClass.Hunter, TextureAssetClass.Weapon,
            TextureAssetClass.AlternateForm, TextureAssetClass.Turret })
        {
            var channel = TextureAssetChannel.Normal;
            var levels = AuthoredRgbaMipFixtures.Levels(7, 5, channel);
            byte[] encoded = AuthoredRgbaMipFixtures.Encode(levels);
            using var manager = new TextureAssetManager(GraphicsApi.GenTexture, GraphicsApi.DeleteTexture);
            int texture = manager.Upload("authored-cache", () => new System.IO.MemoryStream(encoded, writable: false),
                assetClass, channel, repeat: false, out int width, out int height);
            bool mipmaps = TextureSamplingPolicy.ResolveModern(assetClass, channel).Mipmaps;
            long expected = mipmaps ? levels.Sum(mip => (long)mip.Data.Length) : levels[0].Data.Length;
            if (texture == 0 || width != 7 || height != 5 || manager.ResidentCount != 1 || manager.ResidentBytes != expected)
                throw new InvalidOperationException("Authored texture admission/cache residency failed.");
            int cached = manager.Upload("authored-cache", () => throw new InvalidOperationException("Cache reopened source"),
                assetClass, channel, repeat: false, out _, out _);
            if (cached != texture || manager.ResidentCount != 1 || manager.ResidentBytes != expected)
                throw new InvalidOperationException("Authored texture cache duplicated a resident.");
            if (!manager.ReleaseBinding(texture) || manager.ResidentCount != 0 || manager.ResidentBytes != 0)
                throw new InvalidOperationException("Authored texture release retained residency.");
        }
    }
}
#endif
