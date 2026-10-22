using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

/// <summary>
/// Decodes native model texels into the exact upload array used by OpenGL.
/// This keeps the established public List-backed GetPixels APIs available to
/// exporters while avoiding their intermediate list on the texture-bind path.
/// </summary>
internal static class NativeTexturePixelBuffer
{
    internal static ColorRgba[] Decode(Model model, int textureId, int paletteId, int recolorId)
        => Decode(model.Recolors[recolorId], textureId, paletteId);

    internal static ColorRgba[] Decode(Recolor recolor, int textureId, int paletteId)
    {
        if (textureId < 0 || textureId >= recolor.TextureData.Count)
            throw new System.ArgumentException(nameof(textureId));

        bool directRgb = recolor.Textures[textureId].Format == TextureFormat.DirectRgb;
        if (!directRgb && (paletteId < 0 || paletteId >= recolor.PaletteData.Count))
            throw new System.ArgumentException(nameof(paletteId));

        IReadOnlyList<TextureData> texels = recolor.TextureData[textureId];
        var pixels = new ColorRgba[texels.Count];
        if (directRgb)
        {
            for (int i = 0; i < texels.Count; i++)
            {
                TextureData texel = texels[i];
                pixels[i] = new ColorRgba(texel.Data, texel.Alpha);
            }
        }
        else
        {
            IReadOnlyList<PaletteData> palette = recolor.PaletteData[paletteId];
            for (int i = 0; i < texels.Count; i++)
            {
                TextureData texel = texels[i];
                int paletteIndex = (int)texel.Data;
                pixels[i] = new ColorRgba(palette[paletteIndex].Data, texel.Alpha);
            }
        }
        return pixels;
    }

    internal static uint[] Pack(ColorRgba[] pixels)
    {
        var packed = new uint[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            packed[i] = pixels[i].ToUint();
        return packed;
    }

    internal static Summary Summarize(ColorRgba[] pixels)
    {
        bool onlyOpaque = true;
        var average = new FlatColor();
        for (int i = 0; i < pixels.Length; i++)
        {
            ColorRgba pixel = pixels[i];
            onlyOpaque &= pixel.Alpha == 255;
            average.Add(pixel);
        }
        return new Summary(onlyOpaque, average.Result);
    }

    internal static Vector3 AverageOf(IReadOnlyList<ColorRgba> pixels)
    {
        var average = new FlatColor();
        for (int i = 0; i < pixels.Count; i++)
            average.Add(pixels[i]);
        return average.Result;
    }

    internal readonly struct Summary
    {
        internal readonly bool OnlyOpaque;
        internal readonly Vector3 FlatColor;

        internal Summary(bool onlyOpaque, Vector3 flatColor)
        {
            OnlyOpaque = onlyOpaque;
            FlatColor = flatColor;
        }
    }

    /// <summary>
    /// Preserves the renderer's original float operation order for alpha
    /// weighting and its all-transparent fallback.
    /// </summary>
    internal struct FlatColor
    {
        private float _red;
        private float _green;
        private float _blue;
        private float _weight;
        private float _count;
        private float _plainRed;
        private float _plainGreen;
        private float _plainBlue;

        internal void Add(ColorRgba pixel)
        {
            float alpha = pixel.Alpha / 255f;
            _red += pixel.Red * alpha;
            _green += pixel.Green * alpha;
            _blue += pixel.Blue * alpha;
            _weight += alpha;
            _plainRed += pixel.Red;
            _plainGreen += pixel.Green;
            _plainBlue += pixel.Blue;
            _count++;
        }

        internal Vector3 Result
        {
            get
            {
                if (_weight > 0.01f)
                    return new Vector3(_red, _green, _blue) / _weight / 255f;
                if (_count > 0)
                    return new Vector3(_plainRed, _plainGreen, _plainBlue) / _count / 255f;
                return Vector3.One;
            }
        }
    }
}
