using System;
using System.IO;
using System.Runtime.Loader;
using System.Security.Cryptography;
using MphRead.Mods.Diagnostics;

if (args.Length != 1 || args[0] is not ("assembly" or "process"))
    throw new ArgumentException("Pass assembly for a dotnet launch or process for a single-file apphost.");
string expectedKind = args[0];
var assembly = typeof(RuntimeApplicationImage).Assembly;
string expectedImage = expectedKind == "assembly"
    ? Path.Combine(AppContext.BaseDirectory, "application-image-check.dll")
    : Environment.ProcessPath ?? throw new IOException("Missing process image.");
string expectedHash;
using (var source = File.OpenRead(expectedImage))
    expectedHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
var original = RuntimeApplicationImage.Capture(assembly);
Require(original.Kind == expectedKind, "deployed image kind");
Require(original.Sha256 == expectedHash, "deployed image checksum");
string originalAssemblyPath = RuntimeApplicationImage.AssemblyPath(assembly);
string originalDirectory = Environment.CurrentDirectory;
string unrelated = Path.Combine(Path.GetTempPath(), "prime image check " + Guid.NewGuid());
Directory.CreateDirectory(unrelated);
try
{
    Environment.CurrentDirectory = unrelated;
    Require(RuntimeApplicationImage.AssemblyPath(assembly) == originalAssemblyPath
        && (expectedKind == "process" ? originalAssemblyPath.Length == 0
            : Path.IsPathFullyQualified(originalAssemblyPath)), "assembly restart path survives changed working directory");
    Require(RuntimeApplicationImage.Capture(assembly) == original, "image witness survives changed working directory");
    if (expectedKind == "assembly")
    {
        // The same assembly loaded from a different, renamed file must identify
        // that file, rather than a namesake under AppContext.BaseDirectory.
        string alternate = Path.Combine(unrelated, "renamed application image.dll");
        File.Copy(originalAssemblyPath, alternate);
        using (var append = new FileStream(alternate, FileMode.Append))
            append.Write(new byte[] { 0x41, 0x55, 0x44, 0x49, 0x54 });
        var unloaded = VerifyExternal(alternate, original.Sha256);
        for (int i = 0; unloaded.IsAlive && i < 10; i++)
        { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Require(!unloaded.IsAlive, "external assembly context releases file before cleanup");
    }
    else
    {
        string[] decoys = {
            Path.Combine(AppContext.BaseDirectory, "application-image-check.dll"),
            Path.Combine(AppContext.BaseDirectory, "ProjectPrime.dll")
        };
        foreach (string decoy in decoys)
            if (File.Exists(decoy)) throw new IOException("Refusing to overwrite an existing witness file: " + decoy);
        try
        {
            foreach (string decoy in decoys) File.WriteAllText(decoy, "unrelated adjacent assembly witness");
            Require(RuntimeApplicationImage.AssemblyPath(assembly).Length == 0, "bundled assembly remains locationless beside decoys");
            Require(RuntimeApplicationImage.Capture(assembly) == original, "bundled identity ignores unrelated adjacent DLLs");
        }
        finally { foreach (string decoy in decoys) File.Delete(decoy); }
    }
}
finally
{
    Environment.CurrentDirectory = originalDirectory;
    Directory.Delete(unrelated, recursive: true);
}
Console.WriteLine($"PASS: six application image assertions ({original.Kind}, {original.Sha256})");

static void Require(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static WeakReference VerifyExternal(string alternate, string originalHash)
{
    var loader = new AssemblyLoadContext("external application image", isCollectible: true);
    var weak = new WeakReference(loader);
    try
    {
        var external = loader.LoadFromAssemblyPath(alternate);
        Require(RuntimeApplicationImage.AssemblyPath(external) == alternate, "actual external assembly location");
        using var externalFile = File.OpenRead(alternate);
        string externalHash = Convert.ToHexString(SHA256.HashData(externalFile)).ToLowerInvariant();
        Require(RuntimeApplicationImage.Capture(external) == (externalHash, "assembly")
            && externalHash != originalHash, "actual external assembly checksum");
    }
    finally { loader.Unload(); }
    return weak;
}
