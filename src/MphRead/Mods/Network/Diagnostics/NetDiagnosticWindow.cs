using System;
using System.Diagnostics;

namespace MphRead.Mods.Network
{
    public readonly record struct NetDiagnosticCounters(ulong ConnectionId, uint MatchId,
        NetConnectionState State, bool HasSnapshot, uint SnapshotSequence, long Snapshots,
        long BytesReceived, long BytesSent);

    public readonly record struct NetDiagnosticRates(double SnapshotHz, double BytesReceivedPerSecond,
        double BytesSentPerSecond, long MissingOrStaleSnapshots, uint SnapshotSequenceSpan)
    {
        // Gaps include network loss, local drops and discarded late snapshots.
        // They cannot distinguish those causes and are not a WAN loss estimate.
        public double? MissingOrStalePercent => SnapshotSequenceSpan == 0 ? null
            : MissingOrStaleSnapshots * 100.0 / SnapshotSequenceSpan;
    }

    /// <summary>Monotonic one-second reporting window; no per-frame allocation.</summary>
    public sealed class NetDiagnosticWindow
    {
        private bool _initialized;
        private long _lastTime;
        private NetDiagnosticCounters _previous;

        public bool TrySample(long now, in NetDiagnosticCounters counters, out NetDiagnosticRates rates)
        {
            rates = default;
            if (now < 0) return false;
            if (!_initialized || now < _lastTime || counters.ConnectionId != _previous.ConnectionId
                || counters.MatchId != _previous.MatchId || counters.State != _previous.State
                || counters.HasSnapshot != _previous.HasSnapshot || counters.Snapshots < _previous.Snapshots
                || counters.BytesReceived < _previous.BytesReceived || counters.BytesSent < _previous.BytesSent)
            {
                _initialized = true;
                _lastTime = now;
                _previous = counters;
                return false;
            }
            double elapsed = (now - _lastTime) / (double)Stopwatch.Frequency;
            if (elapsed < 1) return false;
            long received = counters.Snapshots - _previous.Snapshots;
            uint span = counters.HasSnapshot
                && Sequence32.IsNewer(counters.SnapshotSequence, _previous.SnapshotSequence)
                ? unchecked(counters.SnapshotSequence - _previous.SnapshotSequence) : 0;
            // A counter/sequence disagreement makes the gap statistic unknown.
            if (received > span) span = 0;
            rates = new NetDiagnosticRates(received / elapsed,
                (counters.BytesReceived - _previous.BytesReceived) / elapsed,
                (counters.BytesSent - _previous.BytesSent) / elapsed,
                span == 0 ? 0 : span - received, span);
            _lastTime = now;
            _previous = counters;
            return true;
        }

        public static double? EstimatedSnapshotAgeMs(double estimatedServerTick, uint snapshotTick)
        {
            if (!Double.IsFinite(estimatedServerTick)) return null;
            const double space = 4294967296.0;
            double ticks = (estimatedServerTick - snapshotTick) % space;
            if (ticks >= space / 2) ticks -= space;
            else if (ticks < -space / 2) ticks += space;
            // Preserve negative estimates: clock uncertainty is not zero age.
            return ticks * (1000.0 / 60);
        }
    }
}
