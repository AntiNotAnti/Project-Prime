using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

internal enum CommunityByteRangeKind
{
    Full,
    Partial,
    BadRequest,
    Unsatisfiable
}

internal readonly record struct CommunityByteRangeSelection(
    CommunityByteRangeKind Kind, long Start, long Count, long Total)
{
    public string ContentRange => "bytes "
        + Start.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-"
        + (Start + Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"
        + Total.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string UnsatisfiedContentRange => "bytes */"
        + Total.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Strict, bounded parser and streamer for one HTTP byte range.</summary>
internal static class CommunityByteRange
{
    private const int MaximumHeaderLength = 128;
    private const int CopyBufferSize = 65536;

    public static CommunityByteRangeSelection Select(string? header, long total)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (header == null)
            return new(CommunityByteRangeKind.Full, 0, total, total);
        if (header.Length > MaximumHeaderLength
            || header.Length < 7
            || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return new(CommunityByteRangeKind.BadRequest, 0, 0, total);

        ReadOnlySpan<char> range = header.AsSpan(6);
        int dash = range.IndexOf('-');
        if (dash < 0 || range[(dash + 1)..].IndexOf('-') >= 0
            || range.IndexOf(',') >= 0)
            return new(CommunityByteRangeKind.BadRequest, 0, 0, total);

        ReadOnlySpan<char> first = range[..dash];
        ReadOnlySpan<char> last = range[(dash + 1)..];
        if (first.IsEmpty)
        {
            if (!TryParseUnsigned(last, out long suffixLength))
                return new(CommunityByteRangeKind.BadRequest, 0, 0, total);
            if (suffixLength == 0 || total == 0)
                return new(CommunityByteRangeKind.Unsatisfiable, 0, 0, total);
            long start = suffixLength >= total ? 0 : total - suffixLength;
            return new(CommunityByteRangeKind.Partial, start, total - start, total);
        }

        if (!TryParseUnsigned(first, out long startOffset))
            return new(CommunityByteRangeKind.BadRequest, 0, 0, total);
        if (total == 0 || startOffset >= total)
            return new(CommunityByteRangeKind.Unsatisfiable, 0, 0, total);

        long endOffset;
        if (last.IsEmpty)
        {
            endOffset = total - 1;
        }
        else
        {
            if (!TryParseUnsigned(last, out endOffset))
                return new(CommunityByteRangeKind.BadRequest, 0, 0, total);
            if (endOffset < startOffset)
                return new(CommunityByteRangeKind.Unsatisfiable, 0, 0, total);
            endOffset = Math.Min(endOffset, total - 1);
        }

        return new(CommunityByteRangeKind.Partial, startOffset,
            endOffset - startOffset + 1, total);
    }

    public static async Task<long> CopySelectedAsync(Stream source,
        Stream destination, long count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (count == 0) return 0;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long remaining = count;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = (int)Math.Min(CopyBufferSize, remaining);
                int read = await source.ReadAsync(buffer.AsMemory(0, requested),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("The selected immutable object ended before its declared range.");
                await destination.WriteAsync(buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
            return count;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool TryParseUnsigned(ReadOnlySpan<char> text, out long value)
    {
        value = 0;
        if (text.IsEmpty) return false;
        foreach (char character in text)
            if (character is < '0' or > '9') return false;
        return long.TryParse(text, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
