using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Tool-only observation of the library the runtime ALREADY loaded. No Load,
// dlopen, resolver, preload, native-host initialization, window or GL calls.
internal static class LoadedNativeModuleCheck
{
    internal const string ExpectedShaEnvironment = "PROJECT_PRIME_EXPECTED_RMLUI_NATIVE_SHA256";
    private static string Name => OperatingSystem.IsWindows() ? "ProjectPrime.RmlUi.Native.dll" : "libProjectPrime.RmlUi.Native.dylib";
    private const string Dyld = "/usr/lib/system/libdyld.dylib";
    private const string System = "/usr/lib/libSystem.B.dylib";
    internal sealed record Expected(string CanonicalPath, string Sha256);
    internal sealed record Attestation(string Scope, string Platform, string ExpectedPath,
        string ActualLoadedPath, string CanonicalLoadedPath, string OnDiskSha256,
        int MatchingLoadedImages, uint ProcessImageCount, DateTimeOffset ObservedUtc,
        string Limit);
    private sealed record Image(string Path, nint Header);
    private sealed record Snapshot(uint Count, Image[] Matches);

    internal static Expected CaptureExpectedPackage(string packageDirectory)
    {
        RequirePlatform();
        string? configured = Environment.GetEnvironmentVariable(ExpectedShaEnvironment);
        if (configured is null || configured.Length != 64 || !configured.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Qualification requires an independent frozen native SHA256 in " + ExpectedShaEnvironment + ".");
        string path = Canonical(Path.Combine(Path.GetFullPath(packageDirectory), Name));
        string sha = configured.ToLowerInvariant();
        if (!string.Equals(Hash(path), sha, StringComparison.Ordinal))
            throw new InvalidOperationException("Bundled native bridge does not match the independently pinned qualification SHA256.");
        return new(path, sha);
    }

    internal static Attestation RequireLoaded(Expected expected)
    {
        RequirePlatform();
        // dyld permits unrelated images to load while the process runs. Require
        // two bounded stable snapshots; never fall back to the package manifest.
        Snapshot? stable = null;
        try
        {
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                Snapshot a = ReadImages(), b = ReadImages();
                if (a.Count == b.Count && a.Matches.SequenceEqual(b.Matches)) { stable = b; break; }
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { throw new PlatformNotSupportedException("macOS dyld enumeration unavailable; loaded-module qualification cannot pass.", error); }
        if (stable is null) throw new InvalidOperationException("dyld image list did not stabilize; loaded-module identity is unverified.");
        if (stable.Matches.Length != 1)
            throw new InvalidOperationException("Expected exactly one loaded ProjectPrime.RmlUi.Native image; observed " + stable.Matches.Length + ".");
        Image loaded = stable.Matches[0];
        if (loaded.Header == 0) throw new InvalidOperationException("Loaded native image header unavailable.");
        string actual = Canonical(loaded.Path);
        if (!string.Equals(actual, expected.CanonicalPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The loaded native module is outside the exact frozen package path.");
        string sha = Hash(actual);
        if (!string.Equals(sha, expected.Sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Loaded native module path has unexpected on-disk bytes.");
        return new(OperatingSystem.IsWindows() ? "Actual Windows process module path plus independently pinned frozen file SHA256"
                : "Actual loaded macOS dyld path plus independently pinned frozen file SHA256",
            OperatingSystem.IsWindows() ? "Windows" : "macOS", expected.CanonicalPath, loaded.Path, actual, sha, 1, stable.Count,
            DateTimeOffset.UtcNow,
            "Hashes the file at the loaded image path, not mapped memory. Requires immutable pre/post-qualified package bytes; no historical or other-process loader claim.");
    }

    internal static void WriteNew(string path, Attestation attestation)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, attestation, new JsonSerializerOptions { WriteIndented = true });
        output.Flush(true);
    }
    private static Snapshot ReadImages()
    {
        if(OperatingSystem.IsWindows())
        {
            using var process=Process.GetCurrentProcess();
            var modules=process.Modules;
            if(modules.Count is <1 or >16384)throw new InvalidOperationException("Unexpected Windows native module count.");
            var matches=new List<Image>();
            foreach(ProcessModule module in modules)
                if(Path.GetFileName(module.FileName).Equals(Name,StringComparison.OrdinalIgnoreCase))
                    matches.Add(new(module.FileName,module.BaseAddress));
            return new((uint)modules.Count,matches.ToArray());
        }
        uint count = ImageCount();
        if (count is 0 or > 16384) throw new InvalidOperationException("Unexpected dyld image count.");
        var images = new List<Image>();
        for (uint index = 0; index < count; ++index)
        {
            nint name = ImageName(index);
            if (name == 0) throw new InvalidOperationException("dyld image list changed during enumeration.");
            string path = Marshal.PtrToStringUTF8(name) ?? throw new InvalidOperationException("dyld image name unavailable.");
            if (path.Length is 0 or > 32768) throw new InvalidOperationException("Unbounded dyld image name.");
            string file = Path.GetFileName(path);
            if (file.Equals(Name, StringComparison.OrdinalIgnoreCase)
                || file.Equals("ProjectPrime.RmlUi.Native.dylib", StringComparison.OrdinalIgnoreCase))
                images.Add(new(path, ImageHeader(index)));
        }
        if (ImageCount() != count) throw new InvalidOperationException("dyld image list changed during enumeration.");
        return new(count, images.ToArray());
    }
    private static string Canonical(string path)
    {
        if(OperatingSystem.IsWindows())
        {
            using var handle=File.OpenHandle(Path.GetFullPath(path),FileMode.Open,FileAccess.Read,FileShare.Read);
            var buffer=new StringBuilder(32768);
            uint length=GetFinalPathNameByHandle(handle,buffer,(uint)buffer.Capacity,0);
            if(length==0||length>=buffer.Capacity)throw new IOException("Cannot canonicalize Windows native module path.");
            string value=buffer.ToString();
            if(value.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase))return @"\\"+value[8..];
            return value.StartsWith(@"\\?\",StringComparison.Ordinal)?value[4..]:value;
        }
        nint resolved = RealPath(Path.GetFullPath(path), 0);
        if (resolved == 0) throw new IOException("Cannot canonicalize native module path.");
        try { return Marshal.PtrToStringUTF8(resolved) ?? throw new IOException("Canonical module path unavailable."); }
        finally { Free(resolved); }
    }
    private static string Hash(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
    private static void RequirePlatform()
    { if (!OperatingSystem.IsMacOS()&&!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Loaded-native qualification requires actual macOS dyld or Windows process module enumeration; no package-only fallback."); }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle,StringBuilder path,uint length,uint flags);
    [DllImport(Dyld, EntryPoint = "_dyld_image_count")] private static extern uint ImageCount();
    [DllImport(Dyld, EntryPoint = "_dyld_get_image_name")] private static extern nint ImageName(uint index);
    [DllImport(Dyld, EntryPoint = "_dyld_get_image_header")] private static extern nint ImageHeader(uint index);
    [DllImport(System, EntryPoint = "realpath", SetLastError = true)] private static extern nint RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint buffer);
    [DllImport(System, EntryPoint = "free")] private static extern void Free(nint pointer);
}
