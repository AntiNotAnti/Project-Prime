using MphRead.Mods.Render;

AuthoredRgbaMipFixtures.VerifyDecode();
Console.WriteLine("PASS: authored KTX2 odd/tall/one-wide/channel mip identity, capped suffixes, complete/partial sampling and residency bytes");

namespace MphRead.Mods.Render
{
    // These decoder checks deliberately exercise the portable RGBA fallback.
    // The native window suite exercises production adapter/upload selection.
    internal static class ModernGraphicsCompat
    {
        internal static GpuTextureCompressionFormat PreferredCharacterTextureCompression => GpuTextureCompressionFormat.None;
        internal static GpuTextureCompressionFormat PreferredTextureCompression => GpuTextureCompressionFormat.None;
        internal static bool TextureCompressionSupported(GpuTextureCompressionFormat format) => false;
    }
}
