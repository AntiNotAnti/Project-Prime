using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Lightweight catalog/auth calls have their own capacity, independent from
/// long HTTP bodies and expensive package verification. Admission is immediate:
/// rejected requests receive Retry-After instead of joining an unbounded queue.
/// </summary>
internal sealed class MapCommunityServiceAdmission : IDisposable
{
    public const int MetadataConcurrency = 8;
    public const int UploadConcurrency = 3;
    public const int DownloadConcurrency = 4;
    public const int VerificationConcurrency = 1;

    internal enum Lane { Metadata, Upload, Download, Verification }
    internal enum Refusal { None, Busy, RateLimited }

    private readonly SemaphoreSlim[] _slots =
    {
        new(MetadataConcurrency),
        new(UploadConcurrency),
        new(DownloadConcurrency),
        new(VerificationConcurrency)
    };

    // Explicit opt-in. Reverse proxies can send thousands of legitimate
    // clients from one peer IP. Only enable at a trusted proxy/origin that
    // knows its actual per-peer source addresses.
    private readonly int _perIpPerMinute;
    private readonly Dictionary<string, (long Minute, int Count)> _perIp = new(StringComparer.Ordinal);
    private readonly object _rateGate = new();
    private readonly long[] _accepted = new long[4];
    private readonly long[] _completed = new long[4];
    private readonly long[] _busy = new long[4];
    private readonly long[] _rateRejected = new long[4];
    private readonly int[] _active = new int[4];

    internal MapCommunityServiceAdmission(int perIpPerMinute = 0)
    {
        if (perIpPerMinute is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(perIpPerMinute));
        _perIpPerMinute = perIpPerMinute;
    }

    internal static MapCommunityServiceAdmission FromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable("PROJECT_PRIME_MAP_IP_REQUESTS_PER_MINUTE");
        if (string.IsNullOrWhiteSpace(value)) return new();
        if (!int.TryParse(value, out int limit) || limit is < 60 or > 100000)
            throw new InvalidOperationException(
                "PROJECT_PRIME_MAP_IP_REQUESTS_PER_MINUTE must be 60-100000.");
        return new(limit);
    }

    internal static Lane Classify(HttpListenerRequest request, string root)
    {
        string path = request.Url?.AbsolutePath ?? "";
        if (!path.StartsWith(root + "/", StringComparison.Ordinal)) return Lane.Metadata;
        string relative = path[(root.Length + 1)..].TrimEnd('/');
        string[] parts = relative.Split('/');
        string method = request.HttpMethod;
        if (method == "PUT" && parts.Length >= 2 && parts[0] == "uploads")
            return Lane.Upload;
        if (method == "POST" && parts.Length == 3
            && parts[0] == "uploads" && parts[2] == "complete")
            return Lane.Verification;
        if (method == "POST" && (relative == "maps"
            || relative.StartsWith("maps/", StringComparison.Ordinal)
                && relative.EndsWith("/versions", StringComparison.Ordinal)))
            return Lane.Verification;
        bool packageFamily = parts[0] is "maps" or "packages";
        bool archiveRequest = parts.Length == 2
            && MapCommunityClient.ValidHash(parts[1]);
        bool previewRequest = parts.Length == 3 && parts[2] == "preview"
            && MapCommunityClient.ValidHash(parts[1]);
        if (method == "GET" && packageFamily && (archiveRequest || previewRequest))
            return Lane.Download;
        return Lane.Metadata;
    }

    internal bool TryEnter(Lane lane, IPAddress? peer, out IDisposable? lease, out Refusal reason)
    {
        lease = null;
        int i = (int)lane;
        if (!_slots[i].Wait(0))
        {
            Interlocked.Increment(ref _busy[i]);
            reason = Refusal.Busy;
            return false;
        }
        if (_perIpPerMinute > 0 && !ConsumeRate(peer, lane))
        {
            _slots[i].Release();
            Interlocked.Increment(ref _rateRejected[i]);
            reason = Refusal.RateLimited;
            return false;
        }
        Interlocked.Increment(ref _accepted[i]);
        Interlocked.Increment(ref _active[i]);
        reason = Refusal.None;
        lease = new Lease(this, i);
        return true;
    }

    private bool ConsumeRate(IPAddress? peer, Lane lane)
    {
        string key = (peer?.ToString() ?? "unknown") + ":" + lane;
        long minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        lock (_rateGate)
        {
            if (!_perIp.TryGetValue(key, out var window) || window.Minute != minute)
            {
                if (_perIp.Count >= 4096)
                {
                    foreach (string expired in _perIp
                        .Where(p => p.Value.Minute != minute).Select(p => p.Key).ToArray())
                        _perIp.Remove(expired);
                    if (_perIp.Count >= 4096) return false;
                }
                _perIp[key] = (minute, 1);
                return true;
            }
            int maximum = lane switch
            {
                Lane.Upload or Lane.Download => Math.Max(15, _perIpPerMinute / 4),
                Lane.Verification => Math.Max(10, _perIpPerMinute / 8),
                _ => _perIpPerMinute
            };
            if (window.Count >= maximum) return false;
            _perIp[key] = (minute, window.Count + 1);
            return true;
        }
    }

    internal object Snapshot() => new
    {
        Metadata = Counters(0),
        Uploads = Counters(1),
        Downloads = Counters(2),
        Verification = Counters(3),
        PerIpRateLimitEnabled = _perIpPerMinute > 0,
        PerIpMetadataPerMinute = _perIpPerMinute
    };

    private object Counters(int lane) => new
    {
        Active = Volatile.Read(ref _active[lane]),
        Accepted = Interlocked.Read(ref _accepted[lane]),
        Completed = Interlocked.Read(ref _completed[lane]),
        BusyRefusals = Interlocked.Read(ref _busy[lane]),
        RateRefusals = Interlocked.Read(ref _rateRejected[lane]),
        Capacity = lane switch
        {
            0 => MetadataConcurrency,
            1 => UploadConcurrency,
            2 => DownloadConcurrency,
            _ => VerificationConcurrency
        }
    };

    private sealed class Lease(MapCommunityServiceAdmission owner, int lane) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            Interlocked.Decrement(ref owner._active[lane]);
            Interlocked.Increment(ref owner._completed[lane]);
            owner._slots[lane].Release();
        }
    }

    public void Dispose()
    {
        foreach (var slot in _slots) slot.Dispose();
    }
}

/// <summary>
/// Full expected sizes are reserved once per resumable upload session. Since
/// offset writes are sequential and bounded by that size, no request needs to
/// scan every .part file before appending a chunk.
/// </summary>
internal sealed class MapCommunityUploadReservations
{
    internal const int MaxTotalSessions = 16;
    internal const int MaxCreatorSessions = 8;
    internal const long MaxTotalBytes = 2L * 1024 * 1024 * 1024;
    internal const long MaxCreatorBytes = 1024L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Creator, long Bytes)> _reserved
        = new(StringComparer.Ordinal);
    private long _orphanPhysicalBytes;
    private long _totalBytes;

    internal MapCommunityUploadReservations(
        IEnumerable<(string Key, string Creator, long Bytes)> retained,
        long orphanPhysicalBytes)
    {
        _orphanPhysicalBytes = Math.Max(0, orphanPhysicalBytes);
        foreach (var entry in retained)
        {
            if (entry.Bytes is <= 0 or > MapPackageReader.MaxArchiveBytes
                || string.IsNullOrWhiteSpace(entry.Key)
                || string.IsNullOrWhiteSpace(entry.Creator)) continue;
            if (_reserved.TryAdd(entry.Key, (entry.Creator, entry.Bytes)))
                _totalBytes += entry.Bytes;
        }
    }

    internal bool TryReserve(string key, string creator, long bytes,
        out HttpStatusCode failure)
    {
        lock (_gate)
        {
            failure = HttpStatusCode.InsufficientStorage;
            if (bytes is <= 0 or > MapPackageReader.MaxArchiveBytes) return false;
            if (_reserved.TryGetValue(key, out var prior))
                return prior.Creator == creator && prior.Bytes == bytes;
            if (_reserved.Count >= MaxTotalSessions)
            {
                failure = HttpStatusCode.TooManyRequests;
                return false;
            }
            int owned = 0;
            long ownBytes = 0;
            foreach (var reservation in _reserved.Values)
                if (reservation.Creator == creator)
                {
                    owned++;
                    ownBytes += reservation.Bytes;
                }
            if (owned >= MaxCreatorSessions)
            {
                failure = HttpStatusCode.TooManyRequests;
                return false;
            }
            if (bytes > MaxTotalBytes - _totalBytes - _orphanPhysicalBytes
                || bytes > MaxCreatorBytes - ownBytes)
                return false;
            _reserved.Add(key, (creator, bytes));
            _totalBytes += bytes;
            return true;
        }
    }

    internal void Release(string key)
    {
        lock (_gate)
        {
            if (_reserved.Remove(key, out var reservation))
                _totalBytes -= reservation.Bytes;
        }
    }

    internal object Snapshot()
    {
        lock (_gate)
        {
            return new
            {
                Sessions = _reserved.Count,
                ReservedBytes = _totalBytes,
                OrphanPhysicalBytes = _orphanPhysicalBytes,
                MaximumSessions = MaxTotalSessions,
                MaximumReservedBytes = MaxTotalBytes,
                PerCreatorMaximumSessions = MaxCreatorSessions,
                PerCreatorMaximumReservedBytes = MaxCreatorBytes
            };
        }
    }
}
