using System;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Mods.Render;
using SkiaSharp;

internal static class ColorTexturePolicyCheck
{
    internal static void Run()
    {
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Color texture policy check failed: " + message);
        }

        TextureAssetSemantics srgb = TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
            transferOverride: TextureTransferFunction.Srgb, wrap: TextureWrapPolicy.Clamp);
        TextureAssetSemantics linear = srgb.WithTransfer(TextureTransferFunction.Linear);
        byte[] blackWhite =
        {
            0,0,0,255, 0,0,0,255,
            255,255,255,255, 255,255,255,255
        };
        byte[] srgbAverage = ColorTexturePolicy.Filter(blackWhite, 2, 2, 1, 1, srgb);
        byte[] linearAverage = ColorTexturePolicy.Filter(blackWhite, 2, 2, 1, 1, linear);
        Check(srgbAverage[0] is >= 187 and <= 189 && linearAverage[0] is >= 127 and <= 128,
            "sRGB filtering averages in linear light and encodes the result");

        TextureAssetSemantics normal = TextureAssetSemantics.ForChannel(TextureAssetChannel.Normal,
            wrap: TextureWrapPolicy.Clamp);
        byte[] opposingNormals = { 255,128,128,255, 0,127,127,255 };
        byte[] normalMip = ColorTexturePolicy.Filter(opposingNormals, 2, 1, 1, 1, normal);
        Check(normalMip[0] is >= 127 and <= 128 && normalMip[1] is >= 127 and <= 128
            && normalMip[2] >= 254, "normal mip vectors are renormalized after averaging");

        TextureAssetSemantics material = TextureAssetSemantics.ForChannel(TextureAssetChannel.Material,
            wrap: TextureWrapPolicy.Clamp);
        byte[] roughnessMip = ColorTexturePolicy.Filter(
            new byte[] { 0,0,0,255, 0,255,0,255 }, 2, 1, 1, 1, material);
        Check(roughnessMip[1] is >= 179 and <= 181,
            "material roughness uses RMS filtering while other data remains linear");

        byte[] edge =
        {
            0,0,0,255, 0,0,0,255,
            0,0,0,255, 255,255,255,255
        };
        byte[] repeated = ColorTexturePolicy.Filter(edge, 4, 1, 2, 1,
            linear.WithWrap(TextureWrapPolicy.Repeat));
        byte[] clamped = ColorTexturePolicy.Filter(edge, 4, 1, 2, 1,
            linear.WithWrap(TextureWrapPolicy.Clamp));
        byte[] mirrored = ColorTexturePolicy.Filter(edge, 4, 1, 2, 1,
            linear.WithWrap(new TextureWrapPolicy(RepeatMode.Mirror, RepeatMode.Mirror)));
        Check(repeated.SequenceEqual(clamped) && repeated.SequenceEqual(mirrored),
            "exact-area mip footprints do not depend on the draw-time wrap mode");
        byte[] unevenArea = ColorTexturePolicy.Filter(new byte[]
        {
            0,0,0,255, 0,0,0,255, 255,0,0,255
        }, 3, 1, 2, 1, linear);
        Check(unevenArea[0] == 0 && unevenArea[4] is >= 169 and <= 171,
            "non-integer resize uses exact fractional source-pixel areas");

        TextureAlphaPolicy blendPolicy = new(TextureAlphaMode.Blend);
        TextureAssetSemantics blend = TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
            alphaPolicy: blendPolicy, wrap: TextureWrapPolicy.Clamp);
        ModernTextureAsset transparentEdge = ModernTextureAsset.FromRgba("transparent-edge",
            TextureAssetClass.World, TextureAssetChannel.Albedo, 2, 1,
            new byte[] { 0,0,0,0, 255,0,0,255 }, blend);
        var blendedMips = (RgbaMipTextureAsset)ColorTexturePolicy.PrepareForLinearFiltering(transparentEdge);
        Check(blendedMips.Mips[0].Data[0] >= 254 && blendedMips.Mips[0].Data[3] == 0,
            "transparent blend texels inherit nearby color without changing alpha");

        byte[] edgeColorS =
        {
            0,0,0,0, 0,0,0,0, 0,0,0,255, 0,0,0,255, 255,0,0,255
        };
        RgbaMipTextureAsset Dilated(int width, int height, byte[] pixels, TextureWrapPolicy wrap) => (RgbaMipTextureAsset)
            ColorTexturePolicy.PrepareForLinearFiltering(ModernTextureAsset.FromRgba(
                "transparent-edge-wrap", TextureAssetClass.World, TextureAssetChannel.Albedo,
                width, height, pixels, TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
                    alphaPolicy: blendPolicy, wrap: wrap)));
        byte[] repeatedEdge = Dilated(5, 1, edgeColorS, TextureWrapPolicy.Repeat).Mips[0].Data;
        byte[] clampedEdge = Dilated(5, 1, edgeColorS, TextureWrapPolicy.Clamp).Mips[0].Data;
        byte[] mirroredEdge = Dilated(5, 1, edgeColorS,
            new TextureWrapPolicy(RepeatMode.Mirror, RepeatMode.Clamp)).Mips[0].Data;
        byte[] edgeColorT =
        {
            0,0,0,0, 0,0,0,0, 0,0,0,255, 0,0,0,255, 0,0,255,255
        };
        byte[] repeatedVertical = Dilated(1, 5, edgeColorT,
            new TextureWrapPolicy(RepeatMode.Clamp, RepeatMode.Repeat)).Mips[0].Data;
        byte[] clampedVertical = Dilated(1, 5, edgeColorT,
            new TextureWrapPolicy(RepeatMode.Clamp, RepeatMode.Clamp)).Mips[0].Data;
        Check(repeatedEdge[0] > 200 && clampedEdge[0] == 0 && mirroredEdge[0] == 0
            && repeatedVertical[2] > 200 && clampedVertical[2] == 0
            && repeatedEdge[3] == 0 && clampedEdge[3] == 0 && mirroredEdge[3] == 0,
            "explicit S/T wrap modes control transparent-edge RGB dilation without changing alpha");
        RgbaMipTextureAsset ambiguous = (RgbaMipTextureAsset)ColorTexturePolicy.PrepareForLinearFiltering(
            ModernTextureAsset.FromRgba("transparent-edge-unknown", TextureAssetClass.World,
                TextureAssetChannel.Albedo, 2, 1, new byte[] { 0,0,0,0, 255,0,0,255 },
                TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo, alphaPolicy: blendPolicy)));
        Check(ambiguous.Semantics.Wrap == null && ambiguous.Mips[0].Data[0] == 0
            && ambiguous.Mips[0].Data[3] == 0,
            "unknown wrap stays unknown and skips guessed transparent-edge dilation");

        CheckCappedMaskCoverage();

        byte[] authoredBase = Enumerable.Range(0, 16).Select(value => (byte)(value * 13)).ToArray();
        byte[] authoredLast = { 7, 17, 27, 37 };
        var authored = new RgbaMipTextureAsset("authored-test", TextureAssetClass.World,
            TextureAssetChannel.Albedo, new[]
            {
                new CompressedTextureMip(2, 2, authoredBase),
                new CompressedTextureMip(1, 1, authoredLast)
            });
        var retained = (RgbaMipTextureAsset)ColorTexturePolicy.PrepareForLinearFiltering(authored,
            new TextureAlphaPolicy(TextureAlphaMode.Mask, 0.4f), TextureWrapPolicy.Clamp);
        Check(retained.Mips[0].Data.SequenceEqual(authoredBase)
            && retained.Mips[1].Data.SequenceEqual(authoredLast),
            "authored mip levels stay byte-identical when modern filtering is enabled");
    }

    private static void CheckCappedMaskCoverage()
    {
        const int sourceWidth = 16, sourceHeight = 16, targetWidth = 8, targetHeight = 8;
        var policy = new TextureAlphaPolicy(TextureAlphaMode.Mask, 0.5f);
        TextureAssetSemantics semantics = TextureAssetSemantics.ForChannel(TextureAssetChannel.Albedo,
            alphaPolicy: policy, wrap: TextureWrapPolicy.Clamp);
        byte[]? candidate = null;
        byte[]? expected = null;
        int sourceCoverage = 0, rawCoverage = 0, adjustedCoverage = 0;
        for (int attempt = 0; attempt < 128; attempt++)
        {
            var random = new Random(0x523 + attempt);
            byte[] source = new byte[sourceWidth * sourceHeight * 4];
            for (int pixel = 0; pixel < sourceWidth * sourceHeight; pixel++)
            {
                int offset = pixel * 4;
                source[offset] = 170; source[offset + 1] = 90; source[offset + 2] = 35;
                source[offset + 3] = (byte)random.Next(256);
            }
            int initialCoverage = ColorTexturePolicy.Coverage(source, policy.Cutoff, 1f);
            byte[] filtered = ColorTexturePolicy.Filter(source, sourceWidth, sourceHeight,
                targetWidth, targetHeight, semantics);
            int unadjustedCoverage = ColorTexturePolicy.Coverage(filtered, policy.Cutoff, 1f);
            byte[] adjusted = (byte[])filtered.Clone();
            ColorTexturePolicy.PreserveAlphaCoverage(adjusted, initialCoverage,
                sourceWidth * sourceHeight, policy.Cutoff, 1f);
            int preservedCoverage = ColorTexturePolicy.Coverage(adjusted, policy.Cutoff, 1f);
            double desired = (double)initialCoverage / (sourceWidth * sourceHeight);
            if (unadjustedCoverage != preservedCoverage
                && Math.Abs((double)preservedCoverage / (targetWidth * targetHeight) - desired)
                    < Math.Abs((double)unadjustedCoverage / (targetWidth * targetHeight) - desired))
            {
                candidate = source; expected = adjusted; sourceCoverage = initialCoverage;
                rawCoverage = unadjustedCoverage; adjustedCoverage = preservedCoverage;
                break;
            }
        }
        if (candidate == null || expected == null)
            throw new InvalidOperationException("Could not create a deterministic capped-mask coverage fixture.");

        string path = Path.Combine(Path.GetTempPath(), "prime-cap-mask-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            using (var bitmap = new SKBitmap(sourceWidth, sourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul))
            {
                for (int y = 0; y < sourceHeight; y++)
                    for (int x = 0; x < sourceWidth; x++)
                    {
                        int offset = (y * sourceWidth + x) * 4;
                        bitmap.SetPixel(x, y, new SKColor(candidate[offset], candidate[offset + 1],
                            candidate[offset + 2], candidate[offset + 3]));
                    }
                using var image = SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                using var output = File.Create(path);
                png.SaveTo(output);
            }
            using var input = File.OpenRead(path);
            ModernTextureAsset capped = ModernTextureAsset.Decode(input, "capped-mask-check",
                TextureAssetClass.World, TextureAssetChannel.Albedo, targetWidth, semantics,
                linearFiltering: true);
            int actualCoverage = ColorTexturePolicy.Coverage(capped.Pixels, policy.Cutoff, 1f);
            double target = (double)sourceCoverage / (sourceWidth * sourceHeight);
            bool alphaMatches = true;
            for (int i = 3; i < capped.Pixels.Length; i += 4)
                if (capped.Pixels[i] != expected[i]) { alphaMatches = false; break; }
            if (actualCoverage != adjustedCoverage || !alphaMatches
                || Math.Abs((double)actualCoverage / (targetWidth * targetHeight) - target)
                    >= Math.Abs((double)rawCoverage / (targetWidth * targetHeight) - target))
                throw new InvalidOperationException("Capped mask filtering did not preserve the source alpha coverage.");
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
