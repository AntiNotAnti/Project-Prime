#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MphRead.Mods.Render;

// Owns only diagnostic bytes. It never creates or retains a graphics handle.
internal sealed class ShaderDiagnosticSourceRetention
{
    internal const int MaximumSourceBytes = 2 * 1024 * 1024;
    internal const int MaximumDistinctRecords = 16;
    internal const string VertexHeader = "Naga generated shader for \"main\" at Vertex:\n";
    internal const string FragmentHeader = "Naga generated shader for \"main\" at Fragment:\n";
    internal const string NativeVertexHeader = "Naga generated shader for \"vs_main\" at Vertex:\n";
    internal const string NativeFragmentHeader = "Naga generated shader for \"fs_main\" at Fragment:\n";
    private readonly object _lock = new();
    private readonly HashSet<string> _attemptedHashes = new(StringComparer.Ordinal);
    private long _scopeSequence;
    private Scope? _active;
    private int _sourceEventOrdinal;

    private sealed class Scope : IDisposable
    {
        internal readonly ShaderDiagnosticSourceRetention Owner;
        internal readonly long Ordinal;
        internal readonly string Directory;
        internal readonly string? GithubSha, GithubRun;
        internal Scope(ShaderDiagnosticSourceRetention owner, long ordinal, string directory,
            string? githubSha, string? githubRun)
            => (Owner, Ordinal, Directory, GithubSha, GithubRun) = (owner, ordinal, directory, githubSha, githubRun);
        public void Dispose()
        {
            try { lock (Owner._lock) { if (ReferenceEquals(Owner._active, this)) Owner._active = null; } }
            catch { } // Even diagnostic scope teardown cannot replace the fixture failure.
        }
    }
    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }

    internal IDisposable BeginLayeredPbrScope(string? diagnostic, string? validation, string? gpuValidation,
        string? outputDirectory, string? runnerTemp, string? githubSha, string? githubRun)
    {
        try
        {
            if (!ShaderDiagnosticPolicy.Enabled(diagnostic, validation, gpuValidation)
                || string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(runnerTemp)
                || !Path.IsPathFullyQualified(outputDirectory) || !Path.IsPathFullyQualified(runnerTemp))
                return EmptyScope.Instance;
            string directory = Path.GetFullPath(outputDirectory);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(runnerTemp)) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(root, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return EmptyScope.Instance;
            lock (_lock)
            {
                if (_active != null) return EmptyScope.Instance;
                return _active = new Scope(this, ++_scopeSequence, directory, githubSha, githubRun);
            }
        }
        catch { return EmptyScope.Instance; }
    }

    internal bool TryRetain(string nativeLevel, string nativeRecord, Action<string, byte[]>? write = null)
    {
        try
        {
            (string? stage, string? entrypoint) = nativeRecord.StartsWith(VertexHeader, StringComparison.Ordinal) ? ("Vertex", "main")
                : nativeRecord.StartsWith(FragmentHeader, StringComparison.Ordinal) ? ("Fragment", "main")
                : nativeRecord.StartsWith(NativeVertexHeader, StringComparison.Ordinal) ? ("Vertex", "vs_main")
                : nativeRecord.StartsWith(NativeFragmentHeader, StringComparison.Ordinal) ? ("Fragment", "fs_main") : (null, null);
            if (stage == null || (nativeLevel != "Info" && nativeLevel != "Warn")
                || nativeRecord.Length > MaximumSourceBytes) return false;
            lock (_lock)
            {
                Scope? scope = _active;
                if (scope == null || _attemptedHashes.Count >= MaximumDistinctRecords) return false;
                int eventOrdinal = ++_sourceEventOrdinal;
                byte[] original = new UTF8Encoding(false, true).GetBytes(nativeRecord);
                if (original.Length > MaximumSourceBytes) return false;
                string digest = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
                if (!_attemptedHashes.Add(digest)) return false;
                int distinctOrdinal = _attemptedHashes.Count;
                string name = $"layered-pbr-{distinctOrdinal:00}-{stage.ToLowerInvariant()}";
                byte[] receipt = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    scope = "verbatim actual native Naga HLSL callback during the check-owned layered-PBR fixture; no pixel acceptance",
                    nativeLevel, stage, entrypoint,
                    nativeRecordBytes = original.Length, nativeRecordSha256 = digest,
                    maximumDistinctRecords = MaximumDistinctRecords, maximumSourceBytes = MaximumSourceBytes,
                    firstObservedSourceEventOrdinal = eventOrdinal, distinctSourceOrdinal = distinctOrdinal,
                    fixtureScopeOrdinal = scope.Ordinal,
                    sourceIdentity = $"actual {entrypoint}/{stage} native event header",
                    failedPipelineAssociation = "Not proven: the native event has no managed failing-PSO identity; earlier or other fixture PSOs may emit records",
                    emissionTiming = "Native emits this complete record after its shader compiler returns; a blocked compiler may not emit the current source",
                    githubSha = scope.GithubSha, githubRun = scope.GithubRun
                });
                write ??= static (path, bytes) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    stream.Write(bytes);
                    stream.Flush(true);
                };
                write(Path.Combine(scope.Directory, name + ".native.txt"), original);
                // A complete receipt certifies the file; a timeout can leave an
                // unreceipted partial file, which must not be treated as verified.
                write(Path.Combine(scope.Directory, name + ".receipt.json"), receipt);
                return true;
            }
        }
        catch { return false; } // No diagnostic I/O failure may unwind into native code.
    }
}
#endif
