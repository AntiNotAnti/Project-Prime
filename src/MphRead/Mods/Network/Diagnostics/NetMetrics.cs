using System;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods.Network
{
    /// <summary>Bounded, allocation-free running statistics. Single writer.</summary>
    public struct NetSample
    {
        public long Count { get; private set; }
        public double Last { get; private set; }
        public double Mean { get; private set; }
        public double Min { get; private set; }
        public double Max { get; private set; }

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

        public long PacketsSent => Interlocked.Read(ref _sent);
        public long PacketsReceived => Interlocked.Read(ref _received);
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public long PacketsRejected => Interlocked.Read(ref _rejected);
        public long QueueDrops => Interlocked.Read(ref _queueDrops);
        public long SimulatedDrops => Interlocked.Read(ref _simulatedDrops);
        public long SendErrors => Interlocked.Read(ref _sendErrors);

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
        public NetSample QueueAgeMs;
        public NetSample SnapshotIntervalMs;
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
        private long _lastSnapshot;
        private long _lastInput;
        private long _workStarted;
        private bool _working;

        public void Receive(in ReceivedPacket packet, long now)
        {
            PacketsReceived++;
            BytesReceived += packet.Length;
            _lastReceived = now;
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
                SnapshotIntervalMs.Record(Milliseconds(now - _lastSnapshot));
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
