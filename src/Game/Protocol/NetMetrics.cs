using System;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// A fixed-size rolling sample window. Recording is allocation-free after
    /// construction; percentile sorting happens only when a snapshot is read.
    /// The window is deliberately bounded so diagnostics cannot grow with the
    /// lifetime of a worker or connection.
    /// </summary>
    public sealed class BoundedPercentileSampler
    {
        public const int DefaultCapacity = 256;
        private readonly double[] _values;
        private readonly double[] _sorted;
        private int _next;
        private int _count;
        private long _totalCount;
        private long _evictions;
        private int _sequence;
        private readonly object _snapshotGate = new();

        public BoundedPercentileSampler(int capacity = DefaultCapacity)
        {
            if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
            _values = new double[capacity];
            _sorted = new double[capacity];
        }

        /// <summary>Number of valid values currently retained in the window.</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>Total valid values recorded, including values evicted from the window.</summary>
        public long TotalCount => Volatile.Read(ref _totalCount);

        /// <summary>Number of valid values replaced after the window became full.</summary>
        public long Evictions => Volatile.Read(ref _evictions);

        public int Capacity => _values.Length;

        public void Record(double value)
        {
            if (!Double.IsFinite(value) || value < 0) return;
            int sequence = Interlocked.Increment(ref _sequence);
            if (_count == _values.Length) _evictions++;
            _values[_next] = value;
            _next++;
            if (_next == _values.Length) _next = 0;
            if (_count < _values.Length) _count++;
            _totalCount++;
            Volatile.Write(ref _sequence, unchecked(sequence + 1));
        }

        public void Clear()
        {
            int sequence = Interlocked.Increment(ref _sequence);
            Array.Clear(_values);
            _next = 0;
            _count = 0;
            _totalCount = 0;
            _evictions = 0;
            Volatile.Write(ref _sequence, unchecked(sequence + 1));
        }

        /// <summary>
        /// Reads a stable bounded snapshot while the owner records on another
        /// thread. The writer never takes <see cref="_snapshotGate"/>; the
        /// reader-only gate only serializes readers sharing this sampler's
        /// scratch array. A failed bounded retry leaves the caller's previous
        /// snapshot eligible for fallback.
        /// </summary>
        public bool TrySnapshot(out BoundedPercentileSnapshot snapshot, int maxAttempts = 3)
        {
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            lock (_snapshotGate)
            {
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    int sequence = Volatile.Read(ref _sequence);
                    if ((sequence & 1) != 0) continue;
                    int count = _count;
                    long totalCount = _totalCount;
                    long evictions = _evictions;
                    Array.Copy(_values, _sorted, count);
                    Thread.MemoryBarrier();
                    int completed = Volatile.Read(ref _sequence);
                    if (sequence != completed || (completed & 1) != 0) continue;
                    if (count == 0)
                    {
                        snapshot = default;
                        return true;
                    }
                    Array.Sort(_sorted, 0, count);
                    snapshot = new BoundedPercentileSnapshot(count, totalCount, evictions,
                        _sorted[Rank(count, 0.50)], _sorted[Rank(count, 0.95)],
                        _sorted[Rank(count, 0.99)], _sorted[Rank(count, 0.999)], _sorted[count - 1]);
                    return true;
                }
            }
            snapshot = default;
            return false;
        }

        public BoundedPercentileSnapshot Snapshot()
        {
            if (_count == 0) return default;
            Array.Copy(_values, _sorted, _count);
            Array.Sort(_sorted, 0, _count);
            return new BoundedPercentileSnapshot(_count, _totalCount, _evictions,
                _sorted[Rank(_count, 0.50)], _sorted[Rank(_count, 0.95)],
                _sorted[Rank(_count, 0.99)], _sorted[Rank(_count, 0.999)], _sorted[_count - 1]);
        }

        private static int Rank(int count, double percentile)
        {
            // Nearest-rank keeps the result deterministic and avoids an
            // interpolation allocation or floating-point tie ambiguity.
            int index = (int)Math.Ceiling(count * percentile) - 1;
            return Math.Clamp(index, 0, count - 1);
        }
    }

    public readonly record struct BoundedPercentileSnapshot(int Count, long TotalCount,
        long Evictions, double P50, double P95, double P99, double P999, double Max);

    /// <summary>
    /// Single-writer running statistics with a lazily initialized bounded
    /// percentile window. Recording is allocation-free after the first valid
    /// sample (callers that require a warmed path can record one sample during
    /// setup).
    /// </summary>
    public struct NetSample
    {
        public long Count { get; private set; }
        public double Last { get; private set; }
        public double Mean { get; private set; }
        public double Min { get; private set; }
        public double Max { get; private set; }
        private BoundedPercentileSampler? _percentiles;

        public long PercentileCount => _percentiles?.Count ?? 0;
        public long PercentileEvictions => _percentiles?.Evictions ?? 0;
        public BoundedPercentileSnapshot Percentiles
            => _percentiles is { } sampler && sampler.TrySnapshot(out BoundedPercentileSnapshot snapshot)
                ? snapshot : default;

        public void Record(double value)
        {
            if (!Double.IsFinite(value) || value < 0)
            {
                return;
            }
            Count++;
            Last = value;
            Mean += (value - Mean) / Count;
            Min = Count == 1 ? value : Math.Min(Min, value);
            Max = Math.Max(Max, value);
            (_percentiles ??= new BoundedPercentileSampler()).Record(value);
        }
    }

    /// <summary>
    /// Counters at the UDP boundary. Workers update atomically; a diagnostic
    /// read is approximate while traffic is flowing. Bytes include the packet
    /// type, but exclude IP/UDP headers. Drops are local, never inferred WAN loss.
    /// </summary>
    public sealed class NetTrafficMetrics
    {
        private long _sent;
        private long _received;
        private long _bytesSent;
        private long _bytesReceived;
        private long _rejected;
        private long _queueDrops;
        private long _simulatedDrops;
        private long _sendErrors;
        private long _queueHighWater;
        private long _flushes;

        public long PacketsSent => Interlocked.Read(ref _sent);
        public long PacketsReceived => Interlocked.Read(ref _received);
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public long PacketsRejected => Interlocked.Read(ref _rejected);
        public long QueueDrops => Interlocked.Read(ref _queueDrops);
        public long SimulatedDrops => Interlocked.Read(ref _simulatedDrops);
        public long SendErrors => Interlocked.Read(ref _sendErrors);
        public long QueueHighWater => Interlocked.Read(ref _queueHighWater);
        public long Flushes => Interlocked.Read(ref _flushes);

        public void Sent(int bytes)
        {
            Interlocked.Increment(ref _sent);
            Interlocked.Add(ref _bytesSent, bytes);
        }

        public void Received(int bytes)
        {
            Interlocked.Increment(ref _received);
            Interlocked.Add(ref _bytesReceived, bytes);
        }

        public void Reject() => Interlocked.Increment(ref _rejected);
        public void DropQueued() => Interlocked.Increment(ref _queueDrops);
        public void DropSimulated() => Interlocked.Increment(ref _simulatedDrops);
        public void SendFailed() => Interlocked.Increment(ref _sendErrors);

        /// <summary>Publishes an observed queue depth without changing queue policy.</summary>
        public void ObserveQueueDepth(int depth)
        {
            if (depth < 0) return;
            long observed = depth;
            while (true)
            {
                long current = Interlocked.Read(ref _queueHighWater);
                if (observed <= current || Interlocked.CompareExchange(ref _queueHighWater, observed, current) == current)
                    return;
            }
        }

        public void Flushed() => Interlocked.Increment(ref _flushes);

        public string Describe()
        {
            return $"packets in/out {PacketsReceived}/{PacketsSent}"
                + $" bytes in/out {BytesReceived}/{BytesSent} rejected {PacketsRejected}"
                + $" queue-drops {QueueDrops} simulated-drops {SimulatedDrops} send-errors {SendErrors}";
        }
    }

    /// <summary>
    /// Per-connection observations owned by the session/relay thread. None of
    /// these measurements change admission, ordering, ping display or gameplay.
    /// Protocol 4 has no synchronized send clock: queue age and time since the
    /// last update are measurable; one-way input/snapshot age is not yet known.
    /// </summary>
    public sealed class NetMetrics
    {
        public NetSample Rtt;
        public NetSample PacketReceiveIntervalMs;
        public NetSample QueueAgeMs;
        public NetSample SnapshotIntervalMs;
        public double SnapshotIntervalJitterMs { get; private set; }
        public NetSample InputIntervalMs;
        public NetSample WorkDurationMs;
        public double SmoothedRttMs { get; private set; }
        public double JitterMs { get; private set; }
        public long PacketsReceived { get; private set; }
        public long BytesReceived { get; private set; }
        public long PacketsRejected { get; private set; }
        public long DuplicateInputs { get; private set; }
        public long ReorderedInputs { get; private set; }
        public long DuplicateSnapshots { get; private set; }
        public long ReorderedSnapshots { get; private set; }
        private long _lastReceived;
        private long _lastPacketReceived;
        private long _lastSnapshot;
        private long _lastInput;
        private long _workStarted;
        private bool _working;

        public void Receive(in ReceivedPacket packet, long now)
        {
            PacketsReceived++;
            BytesReceived += packet.Length;
            long receivedAt = packet.ReceivedAt > 0 ? packet.ReceivedAt : now;
            if (_lastPacketReceived > 0 && receivedAt >= _lastPacketReceived)
            {
                PacketReceiveIntervalMs.Record(Milliseconds(receivedAt - _lastPacketReceived));
            }
            if (receivedAt > _lastPacketReceived) _lastPacketReceived = receivedAt;
            if (now > _lastReceived) _lastReceived = now;
            if (packet.ReceivedAt > 0 && now >= packet.ReceivedAt)
            {
                QueueAgeMs.Record(Milliseconds(now - packet.ReceivedAt));
            }
        }

        public void RecordRtt(double milliseconds)
        {
            if (!Double.IsFinite(milliseconds) || milliseconds < 0)
            {
                return;
            }
            if (Rtt.Count == 0)
            {
                SmoothedRttMs = milliseconds;
            }
            else
            {
                JitterMs += (Math.Abs(milliseconds - Rtt.Last) - JitterMs) / 16;
                SmoothedRttMs += (milliseconds - SmoothedRttMs) / 8;
            }
            Rtt.Record(milliseconds);
        }

        public void Input(long now)
        {
            if (_lastInput > 0 && now >= _lastInput)
            {
                InputIntervalMs.Record(Milliseconds(now - _lastInput));
            }
            _lastInput = now;
        }

        public void Snapshot(long now)
        {
            if (_lastSnapshot > 0 && now >= _lastSnapshot)
            {
                double interval = Milliseconds(now - _lastSnapshot);
                if (SnapshotIntervalMs.Count > 0)
                    SnapshotIntervalJitterMs += (Math.Abs(interval - SnapshotIntervalMs.Last)
                        - SnapshotIntervalJitterMs) / 16;
                SnapshotIntervalMs.Record(interval);
            }
            _lastSnapshot = now;
        }

        public void Reject() => PacketsRejected++;

        public void LateInput(bool duplicate)
        {
            if (duplicate)
            {
                DuplicateInputs++;
            }
            else
            {
                ReorderedInputs++;
            }
        }

        public void LateSnapshot(bool duplicate)
        {
            if (duplicate)
            {
                DuplicateSnapshots++;
            }
            else
            {
                ReorderedSnapshots++;
            }
        }

        public void BeginWork(long now)
        {
            _workStarted = now;
            _working = true;
        }

        public void EndWork(long now)
        {
            if (_working && now >= _workStarted)
            {
                WorkDurationMs.Record(Milliseconds(now - _workStarted));
            }
            _working = false;
        }

        public double? SilenceMs(long now) => Age(now, _lastReceived);
        public double? SnapshotSilenceMs(long now) => Age(now, _lastSnapshot);
        public double? InputSilenceMs(long now) => Age(now, _lastInput);

        private static double? Age(long now, long previous)
        {
            return previous > 0 ? Milliseconds(Math.Max(0, now - previous)) : null;
        }

        private static double Milliseconds(long ticks) => ticks * (1000.0 / Stopwatch.Frequency);

        public string Describe(long now)
        {
            static string Sample(double? value) => value?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
            string rtt = Rtt.Count == 0 ? "n/a"
                : $"{Rtt.Last:0.0}/{SmoothedRttMs:0.0}/{Rtt.Min:0.0}";
            return $"RTT last/smoothed/min {rtt} ms jitter {Sample(Rtt.Count > 0 ? JitterMs : null)} ms"
                + $" input-gap {Sample(InputSilenceMs(now))} ms snapshot-gap {Sample(SnapshotSilenceMs(now))} ms"
                + $" snapshot-interval avg {Sample(SnapshotIntervalMs.Count > 0 ? SnapshotIntervalMs.Mean : null)} ms"
                + $" queue-age avg/max {QueueAgeMs.Mean:0.0}/{QueueAgeMs.Max:0.0} ms"
                + $" silence {Sample(SilenceMs(now))} ms"
                + $" input duplicate/reordered {DuplicateInputs}/{ReorderedInputs}"
                + $" snapshot duplicate/reordered {DuplicateSnapshots}/{ReorderedSnapshots}"
                + $" received {PacketsReceived} bytes {BytesReceived} rejected {PacketsRejected}";
        }
    }
}
