using System;
using MphRead.Mods.Render;

internal static class MipResidencyChecks
{
    internal static void Run()
    {
        NativeKtxMipChecks.Run();
        var requested = new TextureSamplerDescriptor(true, true, true, 4, 0);
        foreach (GpuTextureCompressionFormat format in new[] {
            GpuTextureCompressionFormat.Bc7Rgba, GpuTextureCompressionFormat.Etc2Rgba8, GpuTextureCompressionFormat.Astc4x4Rgba })
        {
            Check(!Asset(format, (8, 4)).EffectiveSampling(requested).Mipmaps,
                "base-only compressed image must not select a mip filter");
            Check(!Asset(format, (8, 4), (4, 2)).EffectiveSampling(requested).Mipmaps,
                "partial NPOT chain must not select a mip filter");
            var complete = Asset(format, (7, 3), (3, 1), (1, 1));
            Check(complete.EffectiveSampling(requested).Mipmaps && !complete.CanGenerateMipmaps
                && complete.ResidentMipLevels == 3, "complete NPOT chain is resident and never generated");
            Check(!complete.EffectiveSampling(requested with { Mipmaps = false }).Mipmaps,
                "mip toggle remains authoritative with a complete resident chain");
            Check(Asset(format, (1, 1)).EffectiveSampling(requested).Mipmaps,
                "one-by-one compressed image is a complete chain");
            try { Asset(format, (2, 2), (1, 1), (1, 1)); throw new Exception("excess mip accepted"); }
            catch (ArgumentException) { }
        }
        var authored = new RgbaMipTextureAsset("authored", TextureAssetClass.World, TextureAssetChannel.Albedo,
            new[] { new CompressedTextureMip(2, 2, new byte[16]), new CompressedTextureMip(1, 1, new byte[4]) });
        Check(authored.MipmapsAvailable && !authored.CanGenerateMipmaps
            && authored.EstimateGpuBytes(false) == 20, "authored RGBA levels retain exact byte residency");
        Console.WriteLine("PASS compressed mip residency regression (BC7/ETC2/ASTC, base/partial/complete/1x1/NPOT/toggle/malformed)");
    }

    internal static void RunGpu()
    {
        int texture = OpenTK.Graphics.OpenGL.GL.GenTexture();
        try
        {
            OpenTK.Graphics.OpenGL.GL.BindTexture(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, texture);
            byte[] red = { 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255 };
            byte[] green = { 0, 255, 0, 255 };
            var authored = new RgbaMipTextureAsset("authored-distinct-levels", TextureAssetClass.World,
                TextureAssetChannel.Albedo, new[] { new CompressedTextureMip(2, 2, red), new CompressedTextureMip(1, 1, green) });
            TextureAssetManager.UploadPreparedBound(authored, false, new(true, true, true, 1, 0));
            byte[] actual = new byte[4];
            OpenTK.Graphics.OpenGL.GL.GetTexImage(OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, 1,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, actual);
            Check(actual[0] == 0 && actual[1] == 255 && actual[2] == 0,
                "authored green lower mip survives upload instead of red GPU regeneration");
            Check(OpenTK.Graphics.OpenGL.GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError,
                "authored mip upload has no GL errors");
            Console.WriteLine("PASS Windows native authored RGBA mip content");
        }
        finally { OpenTK.Graphics.OpenGL.GL.DeleteTexture(texture); }
    }

    private static CompressedTextureAsset Asset(GpuTextureCompressionFormat format,
        params (int Width, int Height)[] sizes)
    {
        var mips = new CompressedTextureMip[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
            mips[i] = new(sizes[i].Width, sizes[i].Height,
                new byte[GpuCompressedTextureMath.LevelBytes(sizes[i].Width, sizes[i].Height)]);
        return new("synthetic", TextureAssetClass.World, TextureAssetChannel.Albedo, format, mips);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
