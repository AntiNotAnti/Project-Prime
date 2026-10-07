#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace MphRead.Mods.Render;

// Shared by the diagnostic implementation and its asset-free policy checks.
internal static class ShaderDiagnosticPolicy
{
    internal const string EnvironmentVariable = "PRIME_WGPU_SHADER_DIAGNOSTICS";
    internal const int PostProcessBytes = 1360;
    internal const int ProbeBudgetMilliseconds = 10_000;
    internal static bool Enabled(string? diagnostic, string? validation, string? gpuValidation)
        => diagnostic == "1" && validation == "1" && gpuValidation == "1";
    internal static bool Write(System.IO.TextWriter writer, string message)
    {
        try { writer.WriteLine(message); return true; }
        catch { return false; } // Diagnostics cannot replace the original failure or interrupt cleanup.
    }
    internal static bool WriteException(System.IO.TextWriter writer, string scope, Exception exception)
    {
        try
        {
            writer.WriteLine($"DIAGNOSTIC exception scope={scope}: {exception}");
            return true;
        }
        catch { return false; }
    }
    internal static void ValidateBinding(ulong offset, int bytes, int words)
    {
        if (bytes != PostProcessBytes || words != bytes / 4 || offset % 256 != 0)
            throw new InvalidOperationException("Diagnostic binding does not match the original aligned PostProcess allocation.");
    }
}

internal sealed class ShaderDiagnosticMapLease
{
    private int _status = -1;
    internal int Status => Volatile.Read(ref _status);
    internal bool Complete(int status) => Interlocked.CompareExchange(ref _status, status, -1) == -1;
}

internal static class ShaderDiagnosticMapRegistry
{
    private static readonly ConcurrentDictionary<nint, ShaderDiagnosticMapLease> Leases = new();
    private static long _sequence;
    internal static nint Register(ShaderDiagnosticMapLease lease)
    {
        nint token = checked((nint)Interlocked.Increment(ref _sequence));
        if (!Leases.TryAdd(token, lease)) throw new InvalidOperationException("Diagnostic map token collision.");
        return token;
    }
    internal static bool Complete(nint token, int status)
        => Leases.TryRemove(token, out var lease) && lease.Complete(status);
    internal static void Cancel(nint token) => Leases.TryRemove(token, out _);
    internal static int PendingCount => Leases.Count;
}
#endif
