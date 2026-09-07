using System;
using MphRead.Mods.Network;

namespace MphRead.Hud.Network
{
    public enum NetworkHealthState { Good, Unstable, HighLatency, Interrupted }
    public static class NetworkHealthSettings { public static bool Advanced { get; set; } }
    public readonly record struct NetworkHealthReading(double Rtt, double Jitter, double SnapshotInterval,
        double? Silence, double? SnapshotGap, double QueueAge, long Packets, long Duplicates, long Reordered,
        double PredictionError, long HardCorrections, long Interpolated, long Extrapolated, long Held, bool HasRtt = true, bool HasSnapshotInterval = true)
    {
        public NetworkHealthState State => Classify(Rtt, Jitter, Silence, SnapshotGap, QueueAge);
        // Display thresholds only. None feed connection admission or interpolation policy.
        public static NetworkHealthState Classify(double rtt, double jitter, double? silence, double? gap, double queueAge)
        {
            if (silence >= 1000 || gap >= 1000) return NetworkHealthState.Interrupted;
            if (rtt >= 150) return NetworkHealthState.HighLatency;
            if (jitter >= 30 || gap >= 250 || queueAge >= 100) return NetworkHealthState.Unstable;
            return NetworkHealthState.Good;
        }
        public double ExtrapolationPercent => Percent(Extrapolated);
        public double HoldPercent => Percent(Held);
        private double Percent(long value)
        {
            double total = (double)Interpolated + Extrapolated + Held;
            return total > 0 ? 100 * value / total : 0;
        }
        public static NetworkHealthReading Read(NetMetrics metrics, ClientPrediction prediction, SnapshotInterpolation interpolation, long now)
            => new(metrics.SmoothedRttMs, metrics.JitterMs, metrics.SnapshotIntervalMs.Last,
                metrics.SilenceMs(now), metrics.SnapshotSilenceMs(now), metrics.QueueAgeMs.Last,
                metrics.PacketsReceived, metrics.DuplicateSnapshots, metrics.ReorderedSnapshots,
                prediction.Error.Last, prediction.HardCorrections, interpolation.InterpolatedSamples,
                interpolation.ExtrapolatedSamples, interpolation.HeldSamples, metrics.Rtt.Count > 0, metrics.SnapshotIntervalMs.Count > 0);
    }
}
