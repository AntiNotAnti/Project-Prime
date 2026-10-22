using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Ktx;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static unsafe class DesktopCompressionChecks
{
    [DllImport("ktx", EntryPoint = "ktxTexture2_CompressBasis", CallingConvention = CallingConvention.Cdecl)]
    private static extern Ktx2.ErrorCode CompressBasis(Ktx2.Texture* texture, uint quality);

    internal static void RunGpu()
    {
        if (!DesktopTextureCompressionPolicy.SupportsFormat("4.2.0 vendor", "")
            || !DesktopTextureCompressionPolicy.SupportsFormat("3.2 vendor", "GL_ARB_texture_compression_bptc")
            || DesktopTextureCompressionPolicy.SupportsFormat("3.2 vendor", "GL_ARB_texture_compression_bptc_fake")
            || DesktopTextureCompressionPolicy.SupportsFormat("2.1 vendor", ""))
            throw new Exception("BC7 core/extension capability contract changed.");
        DesktopTextureCompressionPolicy.ProbeCurrentContext();
        if (!DesktopTextureCompressionPolicy.Enabled)
        {
            if (DesktopTextureCompressionPolicy.SupportsBc7)
                throw new Exception("Disabled desktop compression published a supported capability.");
            Console.WriteLine("PASS desktop block compression remains disabled by default");
            return;
        }
        if (!DesktopTextureCompressionPolicy.SupportsBc7)
        {
            int count = GL.GetInteger(GetPName.NumCompressedTextureFormats);
            int[] formats = new int[Math.Clamp(count, 0, 4096)];
            if (formats.Length > 0) GL.GetInteger(GetPName.CompressedTextureFormats, formats);
            throw new Exception("Explicit BC7 GPU fixture requires an observed BC7-capable context. "
                + "GL=" + GL.GetString(StringName.Version) + " formats=" + string.Join(',', formats)
                + " bptcExtension=" + (GL.GetString(StringName.Extensions) ?? "").Contains("GL_ARB_texture_compression_bptc", StringComparison.Ordinal)
                + " error=" + GL.GetError());
        }
        string path = Path.Combine(Path.GetTempPath(), "prime-basis-" + Guid.NewGuid().ToString("N") + ".ktx2");
        Ktx2.Texture* texture = null;
        int binding = 0;
        try
        {
            const int side = 64;
            var info = new Ktx2.TextureCreateInfo {
                VkFormat = Ktx2.VkFormat.R8G8B8A8Unorm, BaseWidth = side, BaseHeight = side,
                BaseDepth = 1, NumDimensions = 2, NumLevels = 7, NumLayers = 1, NumFaces = 1
            };
            Success(Ktx2.Create(in info, Ktx2.TextureCreateStorage.AllocStorage, out texture));
            for (uint level = 0; level < 7; level++)
            {
                int size = side >> (int)level;
                byte[] rgba = new byte[size * size * 4];
                for (int pixel = 0; pixel < rgba.Length; pixel += 4)
                {
                    rgba[pixel] = 51; rgba[pixel + 1] = 102;
                    rgba[pixel + 2] = 153; rgba[pixel + 3] = 255;
                }
                Success(Ktx2.SetImageFromMemory(texture, level, 0, 0, in rgba[0], (nuint)rgba.Length));
            }
            Success(CompressBasis(texture, 255));
            Success(Ktx2.WriteToNamedFile(texture, path));
            Ktx2.Destroy(texture); texture = null;
            binding = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, binding);
            long totalBc7 = 0, totalRgba = 0;
            foreach (TextureAssetChannel channel in new[] { TextureAssetChannel.Albedo,
                TextureAssetChannel.Normal, TextureAssetChannel.Material, TextureAssetChannel.Emissive })
            {
                long begin = Stopwatch.GetTimestamp();
                using var encoded = File.OpenRead(path);
                var decoded = Ktx2TextureAsset.Decode(encoded, "basis-native", TextureAssetClass.World,
                    channel, maximumDimension: 32) as CompressedTextureAsset
                    ?? throw new Exception("Supported Basis texture did not transcode to BC7.");
                double decodeMs = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                if (decoded.Format != GpuTextureCompressionFormat.Bc7Rgba || decoded.Width != 32
                    || decoded.Mips.Length != 6 || !decoded.MipmapsAvailable || decoded.CanGenerateMipmaps)
                    throw new Exception("BC7 dimension cap/residency contract changed.");
                begin = Stopwatch.GetTimestamp();
                TextureAssetManager.UploadPreparedBound(decoded, false, new(true, true, true, 1, 0));
                GL.Finish();
                double uploadMs = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                long resident = 0;
                for (int level = 0; level < decoded.Mips.Length; level++)
                {
                    GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureInternalFormat, out int format);
                    GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureCompressed, out int compressed);
                    GL.GetTexLevelParameter(TextureTarget.Texture2D, level, GetTextureParameter.TextureCompressedImageSize, out int bytes);
                    if (format != DesktopTextureCompressionPolicy.Bc7InternalFormat || compressed != 1
                        || bytes != decoded.Mips[level].Data.Length)
                        throw new Exception("Actual driver BC7 storage differs from the prepared byte ledger.");
                    resident += bytes;
                }
                encoded.Position = 0;
                var reference = Ktx2TextureAsset.DecodeRgba(encoded, "basis-reference", TextureAssetClass.World,
                    channel, maximumDimension: 32, mipLevel: 1);
                byte[] pixels = new byte[32 * 32 * 4];
                GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                int worst = 0;
                for (int i = 0; i < pixels.Length; i++) worst = Math.Max(worst, Math.Abs(pixels[i] - reference.Pixels[i]));
                if (worst > 8 || resident != decoded.EstimateGpuBytes(true) || GL.GetError() != ErrorCode.NoError)
                    throw new Exception("BC7 fixture pixels, GL error or residency gate failed.");
                totalBc7 += resident;
                totalRgba += 5460; // 32x32 RGBA authored chain through 1x1.
                Console.WriteLine($"PASS Windows BC7 {channel}: actualBytes={resident} rgbaBytes=5460 worstChannelError={worst} decodeMs={decodeMs:0.0000} uploadAndFinishMs={uploadMs:0.0000}");
            }
            Console.WriteLine($"PASS observed BC7 fixture storage: {totalBc7} bytes versus {totalRgba} RGBA bytes; synthetic pixels only, no pack/normal quality or throughput acceptance");
        }
        finally
        {
            if (texture != null) Ktx2.Destroy(texture);
            if (binding != 0) GL.DeleteTexture(binding);
            File.Delete(path);
        }
    }

    private static void Success(Ktx2.ErrorCode result)
    {
        if (result != Ktx2.ErrorCode.Success) throw new Exception("Synthetic Basis fixture: " + result);
    }
}
