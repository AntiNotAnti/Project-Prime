using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace MphRead.Mods.Replay;

/// <summary>Bounded streamed source identity work for detached replay preparation.</summary>
internal static class ReplaySourceHash
{
    internal const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    internal static string Compute(string path, CancellationToken cancellation = default)
    { using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); return CopyAndHash(input, null, cancellation); }
    internal static string CopyAndHash(Stream input, Stream? output, CancellationToken cancellation)
    {
        if (input.CanSeek && input.Length > MaximumBytes) throw new InvalidDataException("Replay source exceeds the 16 GiB source verification budget.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536]; long total = 0; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellation.ThrowIfCancellationRequested(); total += count;
            if (total > MaximumBytes) throw new InvalidDataException("Replay source exceeds the 16 GiB source verification budget.");
            digest.AppendData(buffer, 0, count); output?.Write(buffer, 0, count);
        }
        cancellation.ThrowIfCancellationRequested();
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }
    internal static bool Valid(string? value) => value is { Length: 64 } && System.Linq.Enumerable.All(value, char.IsAsciiHexDigit);
    internal static void Verify(string path, string expected, CancellationToken cancellation = default)
    {
        if (!Valid(expected) || !StringComparer.OrdinalIgnoreCase.Equals(Compute(path, cancellation), expected))
            throw new InvalidDataException("The source recording was replaced or changed. This replay project requires its original exact recording.");
    }
}
