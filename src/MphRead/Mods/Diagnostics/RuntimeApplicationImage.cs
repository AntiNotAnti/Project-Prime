using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace MphRead.Mods.Diagnostics;

/// <summary>Identify the actual loaded assembly or its single-file process image.</summary>
internal static class RuntimeApplicationImage
{
    internal static string AssemblyPath(Assembly assembly)
    {
        // Empty Location is the intentional single-file signal. A guessed DLL
        // beside the host could be unrelated, or the loaded assembly could be
        // in another directory. Callers handle the empty result explicitly.
#pragma warning disable IL3000
        return assembly.Location;
#pragma warning restore IL3000
    }

    internal static (string Sha256, string Kind) Capture(Assembly assembly)
    {
        string image = AssemblyPath(assembly);
        string kind = "assembly";
        if (image.Length == 0)
        {
            image = Environment.ProcessPath
                ?? throw new IOException("Cannot locate the bundled application executable.");
            if (Path.GetFileNameWithoutExtension(image).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Cannot identify the application image beside its dotnet host.");
            kind = "process";
        }
        using var stream = File.OpenRead(image);
        return (Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), kind);
    }
}
