using System;
using System.Collections.Generic;
using MphRead;

namespace MphRead.Mods.Render;

/// <summary>How texture color bytes are interpreted by linear-light sampling.</summary>
public enum TextureTransferFunction
{
    Srgb,
    Linear
}

/// <summary>Alpha behavior carried with an authored material texture.</summary>
public enum TextureAlphaMode
{
    // Keep zero-valued/default instances compatible with older assets.
    Legacy,
    Opaque,
    Mask,
    Blend
}

/// <summary>Alpha mode and alpha-test cutoff. Mask coverage defaults to one half.</summary>
public readonly record struct TextureAlphaPolicy
{
    public TextureAlphaMode Mode { get; }
    public float Cutoff { get; }

    public static TextureAlphaPolicy Legacy { get; } = new(TextureAlphaMode.Legacy);

    public TextureAlphaPolicy(TextureAlphaMode mode, float cutoff = 0.5f)
    {
        Mode = mode;
        Cutoff = cutoff;
        Validate();
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Mode)) throw new ArgumentOutOfRangeException(nameof(Mode));
        if (!float.IsFinite(Cutoff) || Cutoff < 0f || Cutoff > 1f)
            throw new ArgumentOutOfRangeException(nameof(Cutoff), "Alpha cutoff must be finite and between zero and one.");
    }
}

/// <summary>Per-axis sampler wrap behavior used by mip construction and upload.</summary>
internal readonly record struct TextureWrapPolicy(RepeatMode S, RepeatMode T)
{
    internal static TextureWrapPolicy Repeat { get; } = new(RepeatMode.Repeat, RepeatMode.Repeat);
    internal static TextureWrapPolicy Clamp { get; } = new(RepeatMode.Clamp, RepeatMode.Clamp);

    internal TextureWrapPolicy Validate()
    {
        if (!Enum.IsDefined(S) || !Enum.IsDefined(T))
            throw new ArgumentOutOfRangeException(nameof(TextureWrapPolicy));
        return this;
    }

    internal string CacheKey => $"{(byte)S},{(byte)T}";
}

/// <summary>Immutable semantic identity kept alongside prepared texture bytes.</summary>
internal sealed record TextureAssetSemantics
{
    internal TextureAssetChannel Channel { get; }
    internal TextureTransferFunction Transfer { get; }
    internal TextureAlphaPolicy Alpha { get; }
    internal TextureWrapPolicy? Wrap { get; }
    internal float AlphaFactor { get; }

    internal TextureAssetSemantics(TextureAssetChannel channel, TextureTransferFunction transfer,
        TextureAlphaPolicy alpha, TextureWrapPolicy? wrap, float alphaFactor = 1f)
    {
        if (!Enum.IsDefined(channel)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (!Enum.IsDefined(transfer)) throw new ArgumentOutOfRangeException(nameof(transfer));
        alpha.Validate();
        if (!float.IsFinite(alphaFactor) || alphaFactor < 0 || alphaFactor > 1)
            throw new ArgumentOutOfRangeException(nameof(alphaFactor), "Alpha factor must be finite and between zero and one.");
        Wrap = wrap?.Validate();
        Channel = channel;
        Transfer = transfer;
        Alpha = alpha;
        AlphaFactor = alphaFactor;
    }

    internal static TextureAssetSemantics ForChannel(TextureAssetChannel channel,
        TextureAlphaPolicy? alphaPolicy = null, TextureWrapPolicy? wrap = null,
        TextureTransferFunction? transferOverride = null, float alphaFactor = 1f)
    {
        if (transferOverride == TextureTransferFunction.Srgb
            && (channel is TextureAssetChannel.Normal or TextureAssetChannel.Material or TextureAssetChannel.Mask))
            throw new ArgumentException("Numeric texture channels must use linear transfer.", nameof(transferOverride));
        return new(channel, transferOverride ?? InferTransfer(channel),
            alphaPolicy ?? TextureAlphaPolicy.Legacy, wrap, alphaFactor);
    }

    internal TextureAssetSemantics WithTransfer(TextureTransferFunction transfer)
        => new(Channel, transfer, Alpha, Wrap, AlphaFactor);

    internal TextureAssetSemantics WithAlpha(TextureAlphaPolicy alpha)
        => new(Channel, Transfer, alpha, Wrap, AlphaFactor);

    internal TextureAssetSemantics WithWrap(TextureWrapPolicy? wrap)
        => new(Channel, Transfer, Alpha, wrap, AlphaFactor);

    internal TextureAssetSemantics WithAlphaFactor(float alphaFactor)
        => new(Channel, Transfer, Alpha, Wrap, alphaFactor);

    internal string CacheKey
    {
        get
        {
            int cutoff = BitConverter.SingleToInt32Bits(Alpha.Cutoff);
            int factor = BitConverter.SingleToInt32Bits(AlphaFactor);
            return $"{Channel}:{Transfer}:{Alpha.Mode}:{cutoff:x8}:factor={factor:x8}:{Wrap?.CacheKey ?? "auto"}";
        }
    }

    internal static TextureTransferFunction InferTransfer(TextureAssetChannel channel)
        => channel is TextureAssetChannel.Albedo or TextureAssetChannel.Emissive or TextureAssetChannel.Effect
            ? TextureTransferFunction.Srgb : TextureTransferFunction.Linear;
}

/// <summary>
/// Opt-in CPU filtering for modern color and material assets. The normal upload
/// path remains unchanged; callers use this when they explicitly request
/// linear-light mip data. Existing authored mip bytes are retained exactly.
/// </summary>
internal static class ColorTexturePolicy
{
    private const float AlphaEpsilon = 1f / 255f;

    internal static PreparedTextureAsset PrepareForLinearFiltering(PreparedTextureAsset asset,
        TextureAlphaPolicy? alphaPolicy = null, TextureWrapPolicy? wrap = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        // A missing wrap policy means the texture can be drawn with different
        // per-material samplers. Keep it unknown instead of baking a guessed
        // edge continuation into transparent RGB.
        TextureAssetSemantics semantics = asset.Semantics.WithAlpha(alphaPolicy ?? asset.Semantics.Alpha)
            .WithWrap(wrap ?? asset.Semantics.Wrap);
        if (asset is RgbaMipTextureAsset or CompressedTextureAsset)
            return asset.WithSemantics(semantics);

        if (asset is not ModernTextureAsset rgba)
            throw new ArgumentException($"Prepared texture type {asset.GetType().Name} is unsupported.", nameof(asset));

        ModernTextureAsset fitted = FitForLinearFiltering(rgba, ModernTextureAsset.MaximumDimension, semantics);
        byte[] basePixels = fitted.Pixels;
        if (semantics.Alpha.Mode == TextureAlphaMode.Opaque)
        {
            basePixels = (byte[])fitted.Pixels.Clone();
            for (int i = 3; i < basePixels.Length; i += 4) basePixels[i] = 255;
        }
        else if (semantics.Alpha.Mode is TextureAlphaMode.Mask or TextureAlphaMode.Blend)
        {
            basePixels = (byte[])fitted.Pixels.Clone();
            DilateTransparentRgb(basePixels, fitted.Width, fitted.Height, semantics);
        }
        var mips = new List<CompressedTextureMip>(GpuCompressedTextureMath.MipLevelCount(fitted.Width, fitted.Height))
        {
            new(fitted.Width, fitted.Height, basePixels)
        };
        int baseCoverage = semantics.Alpha.Mode == TextureAlphaMode.Mask
            ? Coverage(fitted.Pixels, semantics.Alpha.Cutoff, semantics.AlphaFactor) : 0;
        int basePixelCount = fitted.Width * fitted.Height;
        byte[] previous = basePixels;
        int width = fitted.Width, height = fitted.Height;
        while (width > 1 || height > 1)
        {
            int nextWidth = Math.Max(1, width >> 1), nextHeight = Math.Max(1, height >> 1);
            byte[] next = Filter(previous, width, height, nextWidth, nextHeight, semantics);
            if (semantics.Alpha.Mode == TextureAlphaMode.Mask)
                PreserveAlphaCoverage(next, baseCoverage, basePixelCount, semantics.Alpha.Cutoff, semantics.AlphaFactor);
            mips.Add(new(nextWidth, nextHeight, next));
            previous = next; width = nextWidth; height = nextHeight;
        }
        return new RgbaMipTextureAsset(fitted.Key, fitted.AssetClass, fitted.Channel,
            mips.ToArray(), semantics);
    }

    internal static ModernTextureAsset FitForLinearFiltering(ModernTextureAsset asset,
        int maximumDimension, TextureAssetSemantics? semantics = null)
    {
        maximumDimension = Math.Clamp(maximumDimension, 1, ModernTextureAsset.MaximumDimension);
        semantics ??= asset.Semantics;
        if (asset.Width <= maximumDimension && asset.Height <= maximumDimension)
            return asset.Semantics == semantics ? asset : (ModernTextureAsset)asset.WithSemantics(semantics);
        double scale = Math.Min((double)maximumDimension / asset.Width,
            (double)maximumDimension / asset.Height);
        int width = Math.Max(1, (int)Math.Round(asset.Width * scale));
        int height = Math.Max(1, (int)Math.Round(asset.Height * scale));
        byte[] fitted = Filter(asset.Pixels, asset.Width, asset.Height, width, height, semantics);
        if (semantics.Alpha.Mode == TextureAlphaMode.Mask)
            PreserveAlphaCoverage(fitted, Coverage(asset.Pixels, semantics.Alpha.Cutoff, semantics.AlphaFactor),
                asset.Width * asset.Height, semantics.Alpha.Cutoff, semantics.AlphaFactor);
        return ModernTextureAsset.FromRgba(asset.Key, asset.AssetClass, asset.Channel,
            width, height, fitted, semantics);
    }

    internal static byte[] Filter(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight,
        int targetWidth, int targetHeight, TextureAssetSemantics semantics)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0
            || source.Length != checked(sourceWidth * sourceHeight * 4))
            throw new ArgumentException("RGBA filter dimensions do not match the source buffer.");
        byte[] result = new byte[checked(targetWidth * targetHeight * 4)];
        bool normal = semantics.Channel == TextureAssetChannel.Normal;
        bool material = semantics.Channel == TextureAssetChannel.Material;
        bool srgb = semantics.Transfer == TextureTransferFunction.Srgb;
        for (int y = 0; y < targetHeight; y++)
        {
            double scaleY = (double)sourceHeight / targetHeight;
            double centerY = ((y + 0.5) * scaleY) - 0.5;
            // Exact area footprints partition the source image without
            // reaching beyond its edge. Draw-time repeat/clamp/mirror state
            // therefore cannot change color or numeric mip values.
            double top = centerY - scaleY * 0.5;
            double bottom = centerY + scaleY * 0.5;
            int firstY = Math.Max(0, (int)Math.Floor(top + 0.5));
            int lastY = Math.Min(sourceHeight - 1, (int)Math.Ceiling(bottom + 0.5) - 1);
            for (int x = 0; x < targetWidth; x++)
            {
                double scaleX = (double)sourceWidth / targetWidth;
                double centerX = ((x + 0.5) * scaleX) - 0.5;
                double left = centerX - scaleX * 0.5;
                double right = centerX + scaleX * 0.5;
                int firstX = Math.Max(0, (int)Math.Floor(left + 0.5));
                int lastX = Math.Min(sourceWidth - 1, (int)Math.Ceiling(right + 0.5) - 1);
                double sumR = 0, sumG = 0, sumB = 0, sumA = 0, sumWeight = 0;
                double sumNx = 0, sumNy = 0, sumNz = 0, sumRoughnessSquared = 0;
                for (int sy = firstY; sy <= lastY; sy++)
                {
                    double wy = Math.Max(0, Math.Min(bottom, sy + 0.5) - Math.Max(top, sy - 0.5));
                    if (wy <= 0) continue;
                    for (int sx = firstX; sx <= lastX; sx++)
                    {
                        double wx = Math.Max(0, Math.Min(right, sx + 0.5) - Math.Max(left, sx - 0.5));
                        double weight = wx * wy;
                        if (weight <= 0) continue;
                        int from = (sy * sourceWidth + sx) * 4;
                        double r = source[from] / 255.0, g = source[from + 1] / 255.0;
                        double b = source[from + 2] / 255.0, a = source[from + 3] / 255.0;
                        if (normal)
                        {
                            sumNx += (r * 2 - 1) * weight;
                            sumNy += (g * 2 - 1) * weight;
                            sumNz += (b * 2 - 1) * weight;
                        }
                        else
                        {
                            if (srgb) { r = ToLinear(r); g = ToLinear(g); b = ToLinear(b); }
                            if (semantics.Alpha.Mode is TextureAlphaMode.Mask or TextureAlphaMode.Blend)
                            {
                                sumR += r * a * weight; sumG += g * a * weight; sumB += b * a * weight;
                            }
                            else
                            {
                                sumR += r * weight; sumG += g * weight; sumB += b * weight;
                            }
                            if (material) sumRoughnessSquared += g * g * weight;
                        }
                        sumA += a * weight;
                        sumWeight += weight;
                    }
                }
                if (sumWeight <= 0) throw new InvalidOperationException("Texture filter produced an empty footprint.");
                int to = (y * targetWidth + x) * 4;
                if (normal)
                {
                    double length = Math.Sqrt(sumNx * sumNx + sumNy * sumNy + sumNz * sumNz);
                    double nx = 0, ny = 0, nz = 1;
                    if (length > 1e-8) { nx = sumNx / length; ny = sumNy / length; nz = sumNz / length; }
                    result[to] = Byte(nx * 0.5 + 0.5); result[to + 1] = Byte(ny * 0.5 + 0.5);
                    result[to + 2] = Byte(nz * 0.5 + 0.5);
                }
                else
                {
                    double a = sumA / sumWeight;
                    double r = sumR / sumWeight, g = sumG / sumWeight, b = sumB / sumWeight;
                    if (semantics.Alpha.Mode is TextureAlphaMode.Mask or TextureAlphaMode.Blend)
                    {
                        if (a > AlphaEpsilon) { r /= a; g /= a; b /= a; }
                        else r = g = b = 0;
                    }
                    if (material) g = Math.Sqrt(sumRoughnessSquared / sumWeight);
                    if (srgb) { r = ToSrgb(r); g = ToSrgb(g); b = ToSrgb(b); }
                    result[to] = Byte(r); result[to + 1] = Byte(g); result[to + 2] = Byte(b);
                }
                result[to + 3] = semantics.Alpha.Mode == TextureAlphaMode.Opaque
                    ? (byte)255 : Byte(sumA / sumWeight);
            }
        }
        return result;
    }

    internal static int Coverage(ReadOnlySpan<byte> pixels, float cutoff, float alphaFactor)
    {
        int covered = 0;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] / 255f * alphaFactor >= cutoff) covered++;
        return covered;
    }

    internal static void PreserveAlphaCoverage(byte[] pixels, int targetCoverage, int targetPixels,
        float cutoff, float alphaFactor)
    {
        if (pixels.Length == 0) return;
        double target = targetPixels == 0 ? 0 : (double)targetCoverage / targetPixels;
        if (cutoff <= 0 || cutoff > 1 || alphaFactor <= 0) return;
        double lo = 0, hi = 16;
        for (int iteration = 0; iteration < 18; iteration++)
        {
            double scale = (lo + hi) * 0.5;
            int covered = 0;
            for (int i = 3; i < pixels.Length; i += 4)
                if (Math.Min(1, pixels[i] / 255.0 * scale) * alphaFactor >= cutoff) covered++;
            if ((double)covered / (pixels.Length / 4) < target) lo = scale;
            else hi = scale;
        }
        for (int i = 3; i < pixels.Length; i += 4)
            pixels[i] = Byte(Math.Min(1, pixels[i] / 255.0 * hi));
    }

    private static void DilateTransparentRgb(byte[] pixels, int width, int height,
        TextureAssetSemantics semantics)
    {
        bool hasTransparent = false;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] == 0) { hasTransparent = true; break; }
        if (!hasTransparent) return;

        if (semantics.Wrap is not { } wrap) return;
        bool srgb = semantics.Transfer == TextureTransferFunction.Srgb;
        byte[] source = (byte[])pixels.Clone();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int target = (y * width + x) * 4;
                if (source[target + 3] != 0) continue;
                double red = 0, green = 0, blue = 0, weight = 0;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        int sx = WrapIndex(x + ox, width, wrap.S);
                        int sy = WrapIndex(y + oy, height, wrap.T);
                        int from = (sy * width + sx) * 4;
                        double alpha = source[from + 3] / 255.0;
                        if (alpha <= 0) continue;
                        double r = source[from] / 255.0, g = source[from + 1] / 255.0;
                        double b = source[from + 2] / 255.0;
                        if (srgb) { r = ToLinear(r); g = ToLinear(g); b = ToLinear(b); }
                        red += r * alpha; green += g * alpha; blue += b * alpha;
                        weight += alpha;
                    }
                if (weight <= 0) continue;
                red /= weight; green /= weight; blue /= weight;
                if (srgb) { red = ToSrgb(red); green = ToSrgb(green); blue = ToSrgb(blue); }
                pixels[target] = Byte(red); pixels[target + 1] = Byte(green); pixels[target + 2] = Byte(blue);
            }
    }

    private static int WrapIndex(int value, int size, RepeatMode mode)
    {
        if (size <= 1 || mode == RepeatMode.Clamp) return Math.Clamp(value, 0, size - 1);
        if (mode == RepeatMode.Repeat) return ((value % size) + size) % size;
        int period = checked(size * 2);
        int mirrored = ((value % period) + period) % period;
        return mirrored < size ? mirrored : period - mirrored - 1;
    }

    private static double ToLinear(double value)
        => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double ToSrgb(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value <= 0.0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
    }

    private static byte Byte(double value)
        => (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0, 1) * 255,
            MidpointRounding.AwayFromZero), 0, 255);
}
