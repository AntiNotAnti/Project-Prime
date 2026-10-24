#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>Render-owner capability snapshot; decoder workers never call GL.</summary>
internal static class DesktopTextureCompressionPolicy
{
    internal const int Bc7InternalFormat = 0x8E8C; // COMPRESSED_RGBA_BPTC_UNORM
    internal static readonly bool Enabled =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_BC7_TEXTURES") == "1"
        && !OpenGlOptimizationPolicy.ForceLegacy;
    private static int _bc7;
    internal static bool SupportsBc7 => Volatile.Read(ref _bc7) != 0;

    internal static void ProbeCurrentContext()
    {
        int supported = 0;
        if (Enabled)
        {
            try
            {
                string version = GL.GetString(StringName.Version) ?? "";
                // Some drivers omit BPTC from COMPRESSED_TEXTURE_FORMATS even
                // while advertising its mandatory decoder. Use the actual
                // core/extension contract, then validate storage in native QA.
                bool core = SupportsFormat(version, "");
                supported = core || SupportsFormat(version, GL.GetString(StringName.Extensions) ?? "") ? 1 : 0;
            }
            catch { /* Portable Basis-to-RGBA remains available. */ }
        }
        Volatile.Write(ref _bc7, supported);
    }

    internal static bool SupportsFormat(string version, string extensions)
    {
        string number = version.Split(' ', 2)[0];
        if (Version.TryParse(number, out Version? parsed)
            && (parsed.Major > 4 || parsed.Major == 4 && parsed.Minor >= 2)) return true;
        return (" " + extensions + " ").Contains(" GL_ARB_texture_compression_bptc ", StringComparison.Ordinal);
    }
}
#endif
