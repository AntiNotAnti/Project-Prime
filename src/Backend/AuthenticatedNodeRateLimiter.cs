using System.Threading.RateLimiting;

namespace MphRead.Backend;

/// <summary>
/// Bounded post-authentication limiter for Node control-plane updates. The
/// pre-auth machine policy remains IP-only; this limiter is reached only after
/// the supplied Node credential has authenticated successfully.
/// </summary>
public sealed class AuthenticatedNodeRateLimiter : IDisposable
{
    private sealed class Bucket
    {
        public FixedWindowRateLimiter Limiter { get; } = new(new FixedWindowRateLimiterOptions
        {
            PermitLimit = BackendRoutePolicy.AuthenticatedNodePermitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
        public long LastUsed { get; set; }
    }

    private const int MaximumBuckets = 2048;
    private readonly object _gate = new();
    private readonly Dictionary<(Guid NodeId, string Operation), Bucket> _buckets = [];
    private long _sequence;

    public bool TryAcquire(Guid nodeId, string operation)
    {
        if (nodeId == Guid.Empty || operation is not { Length: > 0 and <= 32 }
            || operation.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
        Bucket bucket;
        lock (_gate)
        {
            var key = (nodeId, operation);
            if (!_buckets.TryGetValue(key, out bucket!))
            {
                if (_buckets.Count >= MaximumBuckets)
                {
                    var oldest = _buckets.MinBy(pair => pair.Value.LastUsed);
                    if (oldest.Value != null)
                        _buckets.Remove(oldest.Key);
                }
                bucket = new();
                _buckets.Add(key, bucket);
            }
            bucket.LastUsed = ++_sequence;
        }
        using RateLimitLease lease = bucket.Limiter.AttemptAcquire(1);
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (Bucket bucket in _buckets.Values) bucket.Limiter.Dispose();
            _buckets.Clear();
        }
    }
}
