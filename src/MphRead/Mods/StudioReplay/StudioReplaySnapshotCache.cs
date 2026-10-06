using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.StudioReplay;

/// <summary>Content-addressed private recording bytes, captured away from the UI thread.</summary>
internal static class StudioReplaySnapshotCache
{
    internal static string Capture(string source, string root, CancellationToken cancellation, string fileName = "source.ppdemo")
    {
        if (fileName is not "source.ppdemo" and not "source.ppclip") throw new ArgumentException("Unsupported immutable replay member.", nameof(fileName));
        cancellation.ThrowIfCancellationRequested();
        root = Path.GetFullPath(root);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        string hash = Hash(input, cancellation);
        using var lease = MapDiskCache.Acquire(root, hash, cancellation);
        string destination = Path.Combine(root, hash, fileName);
        if (File.Exists(destination))
        {
            using var existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!StringComparer.Ordinal.Equals(Hash(existing, cancellation), hash))
                throw new InvalidDataException("Private replay snapshot content differs from its immutable identity.");
            return destination;
        }
        string stage = Path.Combine(root, ".build-" + hash + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            input.Position = 0;
            string staged = Path.Combine(stage, fileName);
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                { cancellation.ThrowIfCancellationRequested(); digest.AppendData(buffer, 0, count); output.Write(buffer, 0, count); }
                if (!StringComparer.Ordinal.Equals(Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), hash))
                    throw new InvalidDataException("Replay source changed while capturing its immutable snapshot.");
                output.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(staged, destination);
            return destination;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }
    private static string Hash(Stream input, CancellationToken cancellation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536]; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        { cancellation.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        cancellation.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
