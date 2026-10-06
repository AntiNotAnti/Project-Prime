using System;
using System.IO;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Replay;

/// <summary>A bound clip parses the exact bytes it verified, even if its durable
/// source path is renamed or replaced while extraction is running.</summary>
internal sealed class ReplayVirtualClipSource : IDisposable
{
    internal string Path { get; }
    private readonly string? _stage;
    private IDisposable? _lease;
    private ReplayVirtualClipSource(string path, string? stage = null, IDisposable? lease = null)
    { Path = path; _stage = stage; _lease = lease; }
    internal static ReplayVirtualClipSource Open(ReplayVirtualClipDocument clip, string cacheRoot, CancellationToken cancellation = default)
    {
        if (clip.SourceContentHash == null) return new(clip.SourceReplay);
        if (!ReplaySourceHash.Valid(clip.SourceContentHash)) throw new InvalidDataException("Invalid clip source identity.");
        string expected = clip.SourceContentHash.ToLowerInvariant();
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(expected + Guid.NewGuid().ToString("N")))).ToLowerInvariant();
        cacheRoot = System.IO.Path.GetFullPath(cacheRoot);
        // Reclaim abandoned verification stages only when no extraction owns
        // their writer lease. Successful and canceled operations clean up below.
        MapDiskCache.Prune(cacheRoot, 0, TimeSpan.Zero, cancellation: cancellation);
        IDisposable? lease = MapDiskCache.Acquire(cacheRoot, key, cancellation);
        string stage = System.IO.Path.Combine(cacheRoot, ".build-" + key + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            string target = System.IO.Path.Combine(stage, "source.ppdemo");
            using (var input = new FileStream(clip.SourceReplay, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                if (!StringComparer.OrdinalIgnoreCase.Equals(ReplaySourceHash.CopyAndHash(input, output, cancellation), expected))
                    throw new InvalidDataException("The source recording was replaced or changed. This replay project requires its original exact recording.");
            cancellation.ThrowIfCancellationRequested();
            var captured = new ReplayVirtualClipSource(target, stage, lease); lease = null; return captured;
        }
        finally
        {
            if (lease != null) { try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } finally { lease.Dispose(); } }
        }
    }
    public void Dispose()
    {
        var lease = Interlocked.Exchange(ref _lease, null); if (lease == null) return;
        try { if (_stage != null && Directory.Exists(_stage)) Directory.Delete(_stage, true); }
        finally { lease.Dispose(); }
    }
}
