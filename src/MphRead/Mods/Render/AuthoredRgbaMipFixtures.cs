#if !MPHREAD_SERVER
using System;
using System.IO;
using System.Linq;
using Ktx;

namespace MphRead.Mods.Render;

// Synthetic authored levels intentionally differ from generated box-filtered
// levels. No game or replacement-pack content is needed by either check.
internal static unsafe class AuthoredRgbaMipFixtures
{
    internal static RgbaTextureMip[] Levels(int width, int height, TextureAssetChannel channel,
        int? levelCount = null)
    {
        int count = levelCount ?? (1 + (int)Math.Floor(Math.Log2(Math.Max(width, height))));
        var levels = new RgbaTextureMip[count];
        for (int level = 0; level < count; level++)
        {
            int w = Math.Max(1, width >> level), h = Math.Max(1, height >> level);
            byte[] pixels = new byte[checked(w * h * 4)];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int offset = (y * w + x) * 4;
                if (channel == TextureAssetChannel.Normal)
                {
                    // Authored, normalized normals with level-specific direction.
                    var normal = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(
                        .12f * (level + 1), .08f * (x - y), 1));
                    pixels[offset] = Quantize(normal.X * .5f + .5f);
                    pixels[offset + 1] = Quantize(normal.Y * .5f + .5f);
                    pixels[offset + 2] = Quantize(normal.Z * .5f + .5f);
                }
                else
                {
                    pixels[offset] = (byte)(17 + level * 29 + x * 3);
                    pixels[offset + 1] = (byte)(31 + level * 19 + y * 5);
                    pixels[offset + 2] = channel == TextureAssetChannel.Material
                        ? (byte)0 : (byte)(53 + level * 11 + x + y);
                }
                pixels[offset + 3] = (byte)(255 - level * 7);
            }
            levels[level] = new(w, h, pixels);
        }
        return levels;
    }

    internal static byte[] Encode(RgbaTextureMip[] levels)
    {
        var info = new Ktx2.TextureCreateInfo
        {
            VkFormat = Ktx2.VkFormat.R8G8B8A8Unorm,
            BaseWidth = (uint)levels[0].Width, BaseHeight = (uint)levels[0].Height,
            BaseDepth = 1, NumDimensions = 2, NumLevels = (uint)levels.Length,
            NumLayers = 1, NumFaces = 1
        };
        Ktx2.Texture* texture = null;
        string path = Path.Combine(Path.GetTempPath(), "prime-authored-mip-" + Guid.NewGuid().ToString("N") + ".ktx2");
        try
        {
            Require(Ktx2.Create(in info, Ktx2.TextureCreateStorage.AllocStorage, out texture)
                == Ktx2.ErrorCode.Success && texture != null, "Synthetic KTX2 creation failed.");
            for (int level = 0; level < levels.Length; level++)
            {
                byte[] bytes = levels[level].Data;
                Require(Ktx2.SetImageFromMemory(texture, (uint)level, 0, 0, in bytes[0], (nuint)bytes.Length)
                    == Ktx2.ErrorCode.Success, "Synthetic KTX2 level write failed.");
            }
            Require(Ktx2.WriteToNamedFile(texture, path) == Ktx2.ErrorCode.Success,
                "Synthetic KTX2 serialization failed.");
            return File.ReadAllBytes(path);
        }
        finally
        {
            if (texture != null) Ktx2.Destroy(texture);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    internal static RgbaMipTextureAsset Decode(RgbaTextureMip[] levels,
        TextureAssetClass assetClass, TextureAssetChannel channel, int cap)
    {
        using var stream = new MemoryStream(Encode(levels), writable: false);
        PreparedTextureAsset prepared = PreparedTextureCodec.Decode(stream, "synthetic-authored-mips", assetClass, channel, cap);
        Require(prepared is RgbaMipTextureAsset, "Authored character RGBA levels were flattened.");
        var asset = (RgbaMipTextureAsset)prepared;
        int first = Array.FindIndex(levels, mip => mip.Width <= cap && mip.Height <= cap);
        Require(first >= 0 && asset.SourceBaseLevel == first && asset.Mips.Length == levels.Length - first,
            "Texture cap did not select an existing authored suffix.");
        for (int level = 0; level < asset.Mips.Length; level++)
        {
            RgbaTextureMip expected = levels[first + level], actual = asset.Mips[level];
            Require(actual.Width == expected.Width && actual.Height == expected.Height
                && actual.Data.AsSpan().SequenceEqual(expected.Data), "Authored mip dimensions or bytes changed during decoding.");
        }
        long allBytes = asset.Mips.Sum(mip => (long)mip.Data.Length);
        Require(asset.EstimateGpuBytes(false) == asset.Mips[0].Data.Length,
            "Base-only residency accounts for unused authored levels.");
        Require(asset.EstimateGpuBytes(true) == (asset.MipmapsAvailable ? allBytes : asset.Mips[0].Data.Length),
            "Authored residency byte estimate differs from uploaded levels.");
        return asset;
    }

    internal static void VerifyDecode()
    {
        foreach (var shape in new[] { (7, 5), (1, 9), (9, 1), (4, 4), (1, 1) })
        foreach (var assetClass in new[] { TextureAssetClass.Hunter, TextureAssetClass.Weapon,
            TextureAssetClass.AlternateForm, TextureAssetClass.Turret })
        foreach (var channel in new[] { TextureAssetChannel.Albedo, TextureAssetChannel.Normal,
            TextureAssetChannel.Material, TextureAssetChannel.Emissive })
        {
            var levels = Levels(shape.Item1, shape.Item2, channel);
            foreach (int cap in new[] { 8192, 3, 1 })
            {
                var asset = Decode(levels, assetClass, channel, cap);
                Require(asset.CompleteMipChain && asset.MipmapsAvailable == (asset.Mips.Length > 1),
                    "Complete authored chain sampling availability is wrong.");
            }
        }
        var partial = Levels(7, 5, TextureAssetChannel.Normal, levelCount: 2);
        Require(!Decode(partial, TextureAssetClass.Hunter, TextureAssetChannel.Normal, 8192).MipmapsAvailable,
            "Incomplete authored chain must use base-only sampling.");
        ExpectInvalid(() => Decode(partial, TextureAssetClass.Hunter, TextureAssetChannel.Normal, 1));
        ExpectInvalid(() => new RgbaMipTextureAsset("bad", TextureAssetClass.Hunter, TextureAssetChannel.Normal,
            new[] { new RgbaTextureMip(2, 2, new byte[15]) }, 0));
        ExpectInvalid(() => new RgbaMipTextureAsset("bad", TextureAssetClass.Hunter, TextureAssetChannel.Normal,
            new[] { new RgbaTextureMip(2, 2, new byte[16]), new RgbaTextureMip(2, 1, new byte[8]) }, 0));
        ExpectInvalid(() => new RgbaMipTextureAsset("bad", TextureAssetClass.Hunter, TextureAssetChannel.Normal,
            new[] { new RgbaTextureMip(1, 1, new byte[4]), new RgbaTextureMip(1, 1, new byte[4]) }, 0));
        ExpectInvalid(() => new RgbaMipTextureAsset("bad", TextureAssetClass.Hunter, TextureAssetChannel.Normal,
            Levels(1, 1, TextureAssetChannel.Normal), -1));
    }

    private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid authored mip fixture was admitted.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
