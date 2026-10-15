#if MPHREAD_STUDIO
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.StudioReplay;

/// <summary>One immutable recording copy owned by a document or export. Never a rewritten recording.</summary>
internal sealed class StudioReplaySnapshot : IDisposable
{
    private readonly string _directory;
    private readonly object _lifetime = new();
    private int _references = 1;
    private bool _ownerDisposed;
    // An export may outlive its tab. Keep the immutable bytes until its last reader finishes.
    internal IDisposable Acquire()
    {
        lock (_lifetime)
        {
            ObjectDisposedException.ThrowIf(_ownerDisposed, this);
            _references++;
            return new Lease(this);
        }
    }
    private sealed class Lease(StudioReplaySnapshot snapshot) : IDisposable
    {
        private StudioReplaySnapshot? _snapshot = snapshot;
        public void Dispose() => Interlocked.Exchange(ref _snapshot, null)?.Release();
    }
    private void Release()
    {
        lock (_lifetime)
            if (--_references == 0 && Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
    internal string PlaybackPath { get; }
    internal string SourceHash { get; }
    internal string PlaybackHash { get; }
    private StudioReplaySnapshot(string directory, string path, string sourceHash)
    {
        _directory = directory; PlaybackPath = path; SourceHash = sourceHash;
        using var input = File.OpenRead(path); PlaybackHash = Convert.ToHexString(SHA256.HashData(input));
    }
    internal static async Task<StudioReplaySnapshot> CreateAsync(string logicalPath, string cacheRoot, CancellationToken token)
    {
        string directory = Path.Combine(cacheRoot, "replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string sourcePath = logicalPath;
            ReplayVirtualClipDocument? clip = null;
            if (logicalPath.EndsWith(ReplayVirtualClips.Extension, StringComparison.OrdinalIgnoreCase))
            {
                if (!ReplayVirtualClips.TryLoad(logicalPath, out clip) || clip == null) throw new InvalidDataException("Invalid replay clip descriptor.");
                sourcePath = clip.SourceReplay;
            }
            string copied = Path.Combine(directory, "recording.ppdemo");
            await CopyVerifiedAsync(sourcePath, copied, null, token).ConfigureAwait(false);
            string hash;
            using (var input = File.OpenRead(copied)) hash = Convert.ToHexString(SHA256.HashData(input));
            string playback = copied;
            if (clip != null)
            {
                playback = Path.Combine(directory, "clip.ppdemo");
                var result = await Task.Run(() => ReplayArchive.Extract(copied, clip.StartFrame, clip.EndFrame, playback, token), token).ConfigureAwait(false);
                if (result != ReplayOpenResult.Success) throw new InvalidDataException("Cannot materialize replay clip: " + result);
                File.Delete(copied);
            }
            token.ThrowIfCancellationRequested();
            return new(directory, playback, hash);
        }
        catch { Directory.Delete(directory, true); throw; }
    }
    internal static async Task CopyVerifiedAsync(string source, string destination, string? expectedHash, CancellationToken token)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] buffer = new byte[65536]; int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                { hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(true);
            }
            if (expectedHash != null && !StringComparer.OrdinalIgnoreCase.Equals(expectedHash, Convert.ToHexString(hash.GetHashAndReset())))
                throw new InvalidDataException("Replay snapshot content changed; export was not started.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination); // never overwrite a recording or another job
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose()
    {
        lock (_lifetime)
        {
            if (_ownerDisposed) return;
            _ownerDisposed = true;
            Release();
        }
    }
}

#endif
