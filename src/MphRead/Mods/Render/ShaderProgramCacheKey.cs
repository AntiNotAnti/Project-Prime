using System;
using System.Security.Cryptography;
using System.Text;

namespace MphRead.Mods.Render;

/// <summary>Pure, versioned key for one shader pair and one GL driver identity.</summary>
internal static class ShaderProgramCacheKey
{
    internal const int MaxBinaryBytes = 8 * 1024 * 1024;
    internal static bool AcceptBinaryLength(int length) => length > 0 && length <= MaxBinaryBytes;
    internal const string GlesLinkAbi = "gles3-explicit-layout-0:position,1:color,2:normal,3:uv,4:color-set;link-default-v1";
    internal static string Compute(string vertex, string fragment, string driver,
        string linkAbi = GlesLinkAbi, string translator = "es-shaders-v2")
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "project-prime-gl-program-v2\0" + driver + "\0" + translator + "\0" + linkAbi + "\0" + vertex + "\0" + fragment));
        return Convert.ToHexString(hash.AsSpan(0, 16));
    }
}
