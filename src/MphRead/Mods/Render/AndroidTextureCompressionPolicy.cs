#if ANDROID
using System;
using System.Threading;
using ES = OpenTK.Graphics.ES30;

namespace MphRead.Mods.Render;

/// <summary>GPU block format selected by EGL owner; decoders only read the snapshot.</summary>
internal static class AndroidTextureCompressionPolicy
{
    internal static readonly bool Enabled = OpenGlOptimizationPolicy.AndroidPerformanceOptedIn
        && Environment.GetEnvironmentVariable("PROJECT_PRIME_ANDROID_GPU_TEXTURES") != "0";
    private static int _astc;
    internal static bool SupportsAstc => Volatile.Read(ref _astc) != 0;
    internal static GpuTextureCompressionFormat Preferred => SupportsAstc
        ? GpuTextureCompressionFormat.Astc4x4Rgba
        : GpuTextureCompressionFormat.Etc2Rgba8;

    // Executed on the current GLES 3 context owner, never from decoder threads.
    internal static void ProbeCurrentContext()
    {
        int available = 0;
        if (Enabled)
        {
            try
            {
                string ext = ES.GL.GetString(ES.StringName.Extensions) ?? "";
                available = ext.Contains("GL_KHR_texture_compression_astc_ldr", StringComparison.Ordinal)
                    || ext.Contains("GL_OES_texture_compression_astc", StringComparison.Ordinal) ? 1 : 0;
            }
            catch { /* missing capability means guaranteed GLES3 ETC2 instead */ }
        }
        Volatile.Write(ref _astc, available);
    }
}
#endif
