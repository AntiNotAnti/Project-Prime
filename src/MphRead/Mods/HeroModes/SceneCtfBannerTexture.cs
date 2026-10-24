using System.Collections.Generic;
using MphRead.Formats;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

namespace MphRead;

public partial class Scene
{
    /// <summary>Exact-source objective cloth, with the existing modern-material sampling policy.</summary>
    internal int BindCtfBannerTexture(IReadOnlyList<ColorRgba> pixels, int width, int height)
    {
        int binding = BindGetTexture(pixels, width, height);
        // RegisterModernTexture expects the modern upload path to have supplied
        // a mip chain. Build it before registration; otherwise minification would
        // sample an incomplete texture. Keep authored resolution, not an upscale.
        GL.BindTexture(TextureTarget.Texture2D, binding);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        byte[] rgba = new byte[checked(width * height * 4)];
        for (int i = 0; i < pixels.Count; i++)
        {
            var pixel = pixels[i];
            rgba[i * 4] = pixel.Red;
            rgba[i * 4 + 1] = pixel.Green;
            rgba[i * 4 + 2] = pixel.Blue;
            rgba[i * 4 + 3] = pixel.Alpha;
        }
        var asset = ModernTextureAsset.FromRgba("ctf:banner", TextureAssetClass.Weapon,
            TextureAssetChannel.Albedo, width, height, rgba);
        RegisterModernTexture(binding, TextureAssetClass.Weapon, TextureAssetChannel.Albedo, asset);
        RegisterSceneColorAsset(binding, asset);
        return binding;
    }
}
