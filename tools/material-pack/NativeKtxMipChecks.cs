using System;
using System.IO;
using Ktx;
using MphRead.Mods.Render;

internal static unsafe class NativeKtxMipChecks
{
    internal static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "prime-authored-mips-" + Guid.NewGuid().ToString("N") + ".ktx2");
        Ktx2.Texture* texture = null;
        try
        {
            var info = new Ktx2.TextureCreateInfo {
                VkFormat = Ktx2.VkFormat.R8G8B8A8Unorm,
                BaseWidth = 4, BaseHeight = 4, BaseDepth = 1,
                NumDimensions = 2, NumLevels = 3, NumLayers = 1, NumFaces = 1
            };
            Success(Ktx2.Create(in info, Ktx2.TextureCreateStorage.AllocStorage, out texture));
            for (uint level = 0; level < 3; level++)
            {
                int side = 4 >> (int)level;
                byte[] rgba = new byte[side * side * 4];
                for (int pixel = 0; pixel < rgba.Length; pixel += 4)
                {
                    rgba[pixel + level] = 255;
                    rgba[pixel + 3] = 255;
                }
                Success(Ktx2.SetImageFromMemory(texture, level, 0, 0, in rgba[0], (nuint)rgba.Length));
            }
            Success(Ktx2.WriteToNamedFile(texture, path));
            using var stream = File.OpenRead(path);
            var decoded = Ktx2TextureAsset.Decode(stream, "native-authored-mips", TextureAssetClass.World,
                TextureAssetChannel.Albedo, maximumDimension: 2) as RgbaMipTextureAsset
                ?? throw new InvalidOperationException("Authored KTX2 chain was expanded/resampled instead of retained.");
            if (decoded.Width != 2 || decoded.Height != 2 || decoded.Mips.Length != 2
                || decoded.Mips[0].Data[0] != 0 || decoded.Mips[0].Data[1] != 255
                || decoded.Mips[1].Data[2] != 255 || !decoded.MipmapsAvailable
                || decoded.EstimateGpuBytes(false) != 20)
                throw new InvalidOperationException("KTX2 lower authored levels lost their exact colors/dimensions/bytes.");
            Console.WriteLine("PASS Windows native KTX2 container: authored lower levels selected before allocation, exact green/blue pixels retained");
        }
        finally
        {
            if (texture != null) Ktx2.Destroy(texture);
            File.Delete(path);
        }
    }

    private static void Success(Ktx2.ErrorCode result)
    {
        if (result != Ktx2.ErrorCode.Success) throw new InvalidOperationException("Synthetic KTX2 fixture: " + result);
    }
}
