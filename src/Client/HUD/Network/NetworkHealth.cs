using System;
using MphRead.Mods.Network;

namespace MphRead.Hud.Network
{
    public enum NetworkHealthState
    {
        Excellent,
        Good,
        Unstable,
        Poor,
        Reconnecting,
        // Transitional source aliases for older presentation/tests.
        HighLatency = Poor,
        Interrupted = Reconnecting
    }
    public static class NetworkHealthSettings { public static bool Advanced { get; set; } }
    public readonly record struct NetworkHealthReading(double Rtt, double Jitter, double SnapshotInterval,
        double? Silence, double? SnapshotGap, double QueueAge, long Packets, long Duplicates, long Reordered,
        double PredictionError, long HardCorrections, long Interpolated, long Extrapolated, long Held, bool HasRtt = true, bool HasSnapshotInterval = true)
    {
        public NetworkHealthState State => Classify(Rtt, Jitter, Silence, SnapshotGap, QueueAge);
        // Display thresholds only. None feed connection admission or interpolation policy.
        public static NetworkHealthState Classify(double rtt, double jitter, double? silence, double? gap, double queueAge)
        {
            if (silence >= 1000 || gap >= 1000) return NetworkHealthState.Reconnecting;
            if (rtt >= 150) return NetworkHealthState.Poor;
            if (jitter >= 30 || gap >= 250 || queueAge >= 100) return NetworkHealthState.Unstable;
            if (rtt <= 50 && jitter <= 8 && (gap is null || gap <= 75) && queueAge <= 25)
                return NetworkHealthState.Excellent;
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

    /// <summary>Small display-only hysteresis; authority and timing policy never read it.</summary>
    public sealed class NetworkHealthSmoother
    {
        private NetworkHealthState _current = NetworkHealthState.Good;
        private NetworkHealthState _candidate = NetworkHealthState.Good;
        private int _samples;
        public NetworkHealthState Current => _current;
        public NetworkHealthState Observe(NetworkHealthState value)
        {
            if (value == NetworkHealthState.Reconnecting)
                return _current = _candidate = value;
            if (value == _current) { _candidate = value; _samples = 0; return _current; }
            if (value != _candidate) { _candidate = value; _samples = 1; }
            else _samples++;
            bool worsening = value > _current;
            if (_samples >= (worsening ? 3 : 8))
            { _current = value; _samples = 0; }
            return _current;
        }
    }
}
