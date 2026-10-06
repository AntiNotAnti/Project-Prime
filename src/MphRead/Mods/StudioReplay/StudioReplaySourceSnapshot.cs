#if !ANDROID && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.StudioReplay;

public sealed partial class StudioReplayPlayer
{
    private string? _playbackContentHash;
    /// <summary>The existing detached preparation worker captures one stable private source.</summary>
    private PreparedReplaySource PrepareImmutablePlaybackSource(CancellationToken cancellation)
    {
        if (_playbackPath is { } prepared) return PreparedReplaySource.File(prepared, cancellation: cancellation);
        string snapshots = Path.Combine(_cacheRoot, "sources");
        if (!LogicalPath.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
            return PrepareSnapshot(StudioReplaySnapshotCache.Capture(LogicalPath, snapshots, cancellation), cancellation);

        if (new FileInfo(LogicalPath).Length > 512 * 1024) throw new InvalidDataException("Virtual replay clip descriptor exceeds its size limit.");
        string descriptor = StudioReplaySnapshotCache.Capture(LogicalPath, Path.Combine(_cacheRoot, "clip-descriptors"), cancellation, "source.ppclip");
        if (!ReplayVirtualClips.TryLoad(descriptor, out var clip) || clip == null)
            throw new InvalidDataException("Virtual replay clip descriptor is invalid.");
        string original = StudioReplaySnapshotCache.Capture(clip.SourceReplay, snapshots, cancellation);
        string directory = Path.Combine(_cacheRoot, "clip-extraction", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string output = Path.Combine(directory, "source.ppdemo");
            var result = ReplayArchive.Extract(original, clip.StartFrame, clip.EndFrame, output, cancellation);
            if (result != ReplayOpenResult.Success) throw new IOException("Cannot materialize replay clip: " + result);
            string immutable = StudioReplaySnapshotCache.Capture(output, snapshots, cancellation);
            return PrepareSnapshot(immutable, cancellation);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    private PreparedReplaySource PrepareSnapshot(string path, CancellationToken cancellation)
    {
        _playbackContentHash = Path.GetFileName(Path.GetDirectoryName(path)!);
        return PreparedReplaySource.File(path, cancellation: cancellation);
    }
}
#endif
