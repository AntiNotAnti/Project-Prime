using System;
using System.IO;
using MphRead.Mods.Replay;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.StudioReplay;

/// <summary>Content-addressed private recording bytes, captured away from the UI thread.</summary>
internal static class StudioReplaySnapshotCache
{
    internal static string Capture(string source, string root, CancellationToken cancellation, string fileName = "source.ppdemo")
    {
        using var captured = CapturePinned(source, root, cancellation, fileName);
        return captured.Path;
    }
    internal sealed class Snapshot(string path, IDisposable pin) : IDisposable
    {
        internal string Path { get; } = path;
        private IDisposable? _pin = pin;
        internal IDisposable TakePin() => Interlocked.Exchange(ref _pin, null)
            ?? throw new InvalidOperationException("The snapshot pin has already been transferred.");
        public void Dispose() => Interlocked.Exchange(ref _pin, null)?.Dispose();
    }
    internal static Snapshot CapturePinned(string source, string root, CancellationToken cancellation, string fileName = "source.ppdemo")
    {
        if (fileName is not "source.ppdemo" and not "source.ppclip") throw new ArgumentException("Unsupported immutable replay member.", nameof(fileName));
        cancellation.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = ReplaySourceHash.CopyAndHash(input, null, cancellation);
        using var lease = MapDiskCache.Acquire(root, hash, cancellation);
        string destination = Path.Combine(root, hash, fileName);
        if (File.Exists(destination))
        {
            using var existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!StringComparer.Ordinal.Equals(ReplaySourceHash.CopyAndHash(existing, null, cancellation), hash))
                throw new InvalidDataException("Private replay snapshot content differs from its immutable identity.");
            return new(destination, MapDiskCache.Pin(root, hash, cancellation));
        }
        string stage = Path.Combine(root, ".build-" + hash + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            input.Position = 0;
            string staged = Path.Combine(stage, fileName);
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!StringComparer.Ordinal.Equals(ReplaySourceHash.CopyAndHash(input, output, cancellation), hash))
                    throw new InvalidDataException("Replay source changed while capturing its immutable snapshot.");
                output.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(staged, destination);
            return new(destination, MapDiskCache.Pin(root, hash, cancellation));
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }
}
