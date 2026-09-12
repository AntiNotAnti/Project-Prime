using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MphRead.Backend.Matches;

namespace MphRead.Backend;

/// <summary>Body and response boundaries are route contracts, not one global
/// exception. Kestrel is configured to the largest bounded route and the
/// request middleware applies the smaller endpoint bound before model binding.</summary>
public static class BackendRequestLimits
{
    public const int DefaultJsonBytes = 16 * 1024;
    public const int NodeRegistrationBytes = 64 * 1024;
    public const int PresenceReportBytes = 512 * 1024;
    public const int MaximumKestrelRequestBytes =
        PresenceReportBytes > ReportValidation.MaximumBytes
            ? PresenceReportBytes : ReportValidation.MaximumBytes;

    public static int ForPath(PathString path)
        => path.Equals("/v1/server/matches", StringComparison.OrdinalIgnoreCase)
            ? ReportValidation.MaximumBytes
            : path.Equals("/v1/node/registration", StringComparison.OrdinalIgnoreCase)
                ? NodeRegistrationBytes
                : path.Equals("/v1/node/presence", StringComparison.OrdinalIgnoreCase)
                    ? PresenceReportBytes
                    : DefaultJsonBytes;

    public static bool IsBoundedPayloadRoute(PathString path)
        => path.Equals("/v1/server/matches", StringComparison.OrdinalIgnoreCase);

    public static bool IsBodyless(HttpContext http)
        => http.GetEndpoint()?.Metadata.GetMetadata<BackendBodyMetadata>()?.ReadsBody == false;

    /// <summary>
    /// Read one request payload without installing request buffering. The
    /// caller owns the returned byte array and may pass it directly to the
    /// ingestion boundary. A null result means the stream contained more than
    /// <paramref name="maximumBytes"/> bytes.
    /// </summary>
    public static async Task<byte[]?> ReadBoundedPayloadAsync(Stream body, int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        using var payload = new MemoryStream(capacity: Math.Min(maximumBytes, 8192));
        byte[] buffer = new byte[Math.Min(maximumBytes, 8192)];
        while (true)
        {
            int read = await body.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return payload.ToArray();
            if (payload.Length > maximumBytes - read) return null;
            await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

/// <summary>Endpoint metadata for routes whose handler genuinely never reads
/// the request body. This is deliberately explicit: a route is not treated as
/// bodyless merely because its current request happens to omit a body.</summary>
public sealed class BackendBodyMetadata(bool readsBody)
{
    public static readonly BackendBodyMetadata Bodyless = new(false);
    public bool ReadsBody { get; } = readsBody;
}

public static class BackendEndpointConventions
{
    public static TBuilder Bodyless<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(BackendBodyMetadata.Bodyless));
        return builder;
    }
}

/// <summary>Raised only when a body stream actually produces bytes beyond its
/// route's limit. Content-Length checks and Kestrel's own limit remain the
/// cheaper first line of defense.</summary>
public sealed class BackendRequestBodyLimitExceededException : IOException
{
    public BackendRequestBodyLimitExceededException(long maximumBytes)
        : base($"The request body exceeds the configured {maximumBytes}-byte limit.") { }
}

/// <summary>
/// Streaming request-boundary guard for TestServer and reverse proxies that do
/// not enforce Kestrel's MaxRequestBodySize on chunked requests. It never
/// buffers or rewinds the body; callers observe the original bytes until the
/// first byte beyond the route limit, at which point a stable boundary
/// exception is raised.
/// </summary>
public sealed class BoundedRequestBodyStream(Stream inner, long maximumBytes) : Stream
{
    private long _bytesRead;

    public long BytesRead => _bytesRead;
    public long MaximumBytes => maximumBytes;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty) return 0;
        int maxRead = ReadSize(buffer.Length);
        int read = inner.Read(buffer[..maxRead]);
        Advance(read);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return 0;
        int maxRead = ReadSize(buffer.Length);
        int read = await inner.ReadAsync(buffer[..maxRead], cancellationToken);
        Advance(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int ReadByte()
    {
        Span<byte> one = stackalloc byte[1];
        return Read(one) == 0 ? -1 : one[0];
    }

    public override void Flush() => throw new NotSupportedException();
    public override Task FlushAsync(CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException());
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();
    public override Task WriteAsync(byte[] buffer, int offset, int count,
        CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException());
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
        => ValueTask.FromException(new NotSupportedException());

    protected override void Dispose(bool disposing)
    {
        // The request owns the underlying stream. Replacing Request.Body for a
        // middleware scope must never close the server-owned stream.
        base.Dispose(disposing);
    }

    private int ReadSize(int requested)
    {
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        long remaining = maximumBytes - _bytesRead;
        if (remaining < 0) throw new BackendRequestBodyLimitExceededException(maximumBytes);
        // Ask for one extra byte at the boundary. This keeps the actual-byte
        // check streaming and lets exact-limit payloads remain valid.
        return (int)Math.Min(requested, remaining + 1);
    }

    private void Advance(int read)
    {
        if (read <= 0) return;
        long remaining = maximumBytes - _bytesRead;
        if (read > remaining)
        {
            _bytesRead = maximumBytes;
            throw new BackendRequestBodyLimitExceededException(maximumBytes);
        }
        _bytesRead += read;
    }
}
