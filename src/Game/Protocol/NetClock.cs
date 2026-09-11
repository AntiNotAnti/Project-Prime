using System;
using System.Diagnostics;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Estimates a continuous server tick from validated ping replies. Offset
    /// includes the unavoidable path-symmetry assumption; it is a presentation
    /// hint only and never selects authoritative historical hitboxes.
    /// </summary>
    public sealed class NetClock
    {
        private const int SampleCapacity = 16;
        private const double TickRate = 60.0;
        private const double MinimumLowRttAllowanceMs = 5.0;
        private const double JitterAllowanceFactor = 4.0;
        private const double SlowSlewTicksPerSecond = 2.0;
        private const double FastSlewTicksPerSecond = 8.0;
        private const double FastSlewThresholdTicks = 4.0;
        private const double MadToJitterFactor = 1.4826;
        private const double RecentWeightDecay = 0.75;
        private const double SustainedShiftThresholdTicks = 2.0;
        private const int SustainedShiftSamples = 2;
        private const int PathChangeSamples = 3;
        private const double PathChangeMaxRatio = 8.0;
        private const double ActivePathIncreaseMaxRatio = 2.0;
        private const double PathRttBandRatio = 0.25;
        private const double PathRttBandMinimumMs = 5.0;

        private readonly ClockSample[] _samples = new ClockSample[SampleCapacity];
        private bool _synchronized;
        private uint _lastServerTick;
        private double _unwrappedServerTick;
        private double _offsetTicks;
        private double _targetOffsetTicks;
        private int _sampleCount;
        private int _sampleCursor;
        private int _shiftSign;
        private int _shiftStreak;
        private bool _hasPathRtt;
        private bool _pathChangeActive;
        private double _pathRttMs;
        private double _candidatePathRttMs;
        private int _candidatePathDirection;
        private int _candidatePathStreak;
        private long _lastReceivedAt;
        private long _lastSlewAt;
        private bool _hasSlewTime;
        private long _lastEstimateAt;
        private double _lastEstimate;
        private bool _hasEstimate;

        public NetMetrics Metrics { get; } = new();
        public bool Synchronized => _synchronized;

        public NetClock()
        {
            // NetMetrics retains its existing public diagnostics, but warm its
            // bounded RTT sampler here so Observe remains allocation-free.
            Metrics.Rtt.Reserve();
        }

        /// <summary>Clears clock state while retaining the fixed buffers.</summary>
        public void Reset()
        {
            Array.Clear(_samples);
            _synchronized = false;
            _lastServerTick = 0;
            _unwrappedServerTick = 0;
            _offsetTicks = 0;
            _targetOffsetTicks = 0;
            _sampleCount = 0;
            _sampleCursor = 0;
            _shiftSign = 0;
            _shiftStreak = 0;
            _hasPathRtt = false;
            _pathChangeActive = false;
            _pathRttMs = 0;
            _candidatePathRttMs = 0;
            _candidatePathDirection = 0;
            _candidatePathStreak = 0;
            _lastReceivedAt = 0;
            _lastSlewAt = 0;
            _hasSlewTime = false;
            _lastEstimateAt = 0;
            _lastEstimate = 0;
            _hasEstimate = false;
            Metrics.ResetRtt();
        }

        public bool Observe(long sentAt, long receivedAt, uint serverTick)
        {
            if (sentAt < 0 || receivedAt < sentAt
                || _synchronized && receivedAt < _lastReceivedAt
                || (_synchronized && serverTick != _lastServerTick
                    && !Sequence32.IsNewer(serverTick, _lastServerTick)))
            {
                return false;
            }

            long elapsedStopwatchTicks = receivedAt - sentAt;
            double elapsed = elapsedStopwatchTicks / (double)Stopwatch.Frequency;
            if (!Double.IsFinite(elapsed) || elapsed > NetConfig.TimeoutSeconds)
                return false;

            bool wasSynchronized = _synchronized;
            double midpointTicks = (sentAt / (double)Stopwatch.Frequency + elapsed / 2) * TickRate;
            if (!wasSynchronized)
            {
                _unwrappedServerTick = serverTick;
            }
            else
            {
                _unwrappedServerTick += unchecked(serverTick - _lastServerTick);
            }

            double offset = _unwrappedServerTick - midpointTicks;
            double rttMs = elapsed * 1000;

            // Apply elapsed-time slew before changing the target. This keeps a
            // correction bounded even when a burst of replies arrives together.
            AdvanceSlew(receivedAt);
            _samples[_sampleCursor] = new ClockSample(rttMs, offset);
            _sampleCursor = (_sampleCursor + 1) % SampleCapacity;
            if (_sampleCount < SampleCapacity) _sampleCount++;
            ClockSample newest = _samples[(_sampleCursor + SampleCapacity - 1) % SampleCapacity];
            UpdatePathRttRegime(newest.RttMs);
            double robustTarget = AggregateOffset(false, out double eligibilityThreshold,
                out double robustMedian);
            int sign = newest.RttMs <= eligibilityThreshold
                ? Math.Sign(newest.OffsetTicks - robustMedian) : 0;
            if (sign != 0 && Math.Abs(newest.OffsetTicks - robustMedian) >= SustainedShiftThresholdTicks)
            {
                if (_shiftSign == sign) _shiftStreak++;
                else { _shiftSign = sign; _shiftStreak = 1; }
            }
            else
            {
                _shiftSign = 0;
                _shiftStreak = 0;
            }
            _targetOffsetTicks = _pathChangeActive
                ? AggregateOffset(true, out _, out _, _pathRttMs)
                : _shiftStreak >= SustainedShiftSamples
                    ? AggregateOffset(true, out _, out _)
                    : robustTarget;

            if (!wasSynchronized)
            {
                _offsetTicks = _targetOffsetTicks;
                _lastSlewAt = receivedAt;
                _hasSlewTime = true;
                _synchronized = true;
            }

            _lastServerTick = serverTick;
            _lastReceivedAt = receivedAt;
            Metrics.RecordRtt(rttMs);
            return true;
        }

        /// <summary>
        /// Returns a monotonic presentation estimate. Corrections are slewed
        /// toward the robust sample aggregate and can never step the estimate
        /// backward as local time advances.
        /// </summary>
        public double EstimateServerTick(long now)
        {
            if (!_synchronized) return now * (TickRate / Stopwatch.Frequency);
            if (_hasEstimate && now <= _lastEstimateAt) return _lastEstimate;

            AdvanceSlew(now);
            double estimate = now * (TickRate / Stopwatch.Frequency) + _offsetTicks;
            if (_hasEstimate && estimate < _lastEstimate) estimate = _lastEstimate;
            _lastEstimate = estimate;
            _lastEstimateAt = now;
            _hasEstimate = true;
            return estimate;
        }

        private void AdvanceSlew(long now)
        {
            if (!_hasSlewTime)
            {
                if (_synchronized)
                {
                    _lastSlewAt = now;
                    _hasSlewTime = true;
                }
                return;
            }
            if (now <= _lastSlewAt)
            {
                return;
            }

            double elapsed = (now - _lastSlewAt) / (double)Stopwatch.Frequency;
            if (!Double.IsFinite(elapsed) || elapsed <= 0)
            {
                _lastSlewAt = now;
                return;
            }

            double correction = _targetOffsetTicks - _offsetTicks;
            double rate = Math.Abs(correction) >= FastSlewThresholdTicks
                ? FastSlewTicksPerSecond : SlowSlewTicksPerSecond;
            double maximum = rate * elapsed;
            _offsetTicks += Math.Clamp(correction, -maximum, maximum);
            _lastSlewAt = now;
        }

        private void UpdatePathRttRegime(double rttMs)
        {
            // Require a bounded, repeated RTT regime before aging out the
            // low-RTT population. This admits a real path change while keeping
            // isolated or extreme high-delay bursts out of the clock target.
            if (!_hasPathRtt)
            {
                _pathRttMs = rttMs;
                _hasPathRtt = true;
                ResetPathCandidate();
                return;
            }

            if (IsRttInPathBand(rttMs, _pathRttMs))
            {
                ResetPathCandidate();
                return;
            }

            int direction = rttMs > _pathRttMs ? 1 : -1;
            double baselineRtt = Math.Max(_pathRttMs, PathRttBandMinimumMs);
            double candidateRtt = Math.Max(rttMs, PathRttBandMinimumMs);
            double ratio = direction > 0 ? candidateRtt / baselineRtt : baselineRtt / candidateRtt;
            if (!Double.IsFinite(ratio) || ratio > PathChangeMaxRatio
                || (direction > 0 && _pathChangeActive && ratio > ActivePathIncreaseMaxRatio))
            {
                ResetPathCandidate();
                return;
            }

            if (_candidatePathDirection == direction
                && IsRttInPathBand(rttMs, _candidatePathRttMs))
            {
                _candidatePathStreak++;
                _candidatePathRttMs += (rttMs - _candidatePathRttMs) / _candidatePathStreak;
            }
            else
            {
                _candidatePathDirection = direction;
                _candidatePathRttMs = rttMs;
                _candidatePathStreak = 1;
            }

            if (_candidatePathStreak >= PathChangeSamples)
            {
                _pathRttMs = _candidatePathRttMs;
                _pathChangeActive = true;
                ResetPathCandidate();
            }
        }

        private void ResetPathCandidate()
        {
            _candidatePathRttMs = 0;
            _candidatePathDirection = 0;
            _candidatePathStreak = 0;
        }

        private static bool IsRttInPathBand(double rttMs, double pathRttMs)
        {
            double tolerance = Math.Max(PathRttBandMinimumMs, pathRttMs * PathRttBandRatio);
            return Math.Abs(rttMs - pathRttMs) <= tolerance;
        }

        private double AggregateOffset(bool preferRecent, out double eligibilityThreshold,
            out double medianOffset, double pathRttMs = -1)
        {
            Span<double> sortedRtts = stackalloc double[SampleCapacity];
            int count = 0;
            double minimum = Double.PositiveInfinity;
            for (int index = 0; index < _sampleCount; index++)
            {
                ClockSample sample = _samples[index];
                sortedRtts[count++] = sample.RttMs;
                minimum = Math.Min(minimum, sample.RttMs);
            }
            InsertionSort(sortedRtts[..count]);
            // Estimate jitter from the faster half. This keeps a burst that
            // occupies half the ring from inflating its own eligibility band.
            int jitterCount = Math.Max(1, (count + 1) / 2);
            double medianRtt = Median(sortedRtts[..jitterCount]);

            Span<double> deviations = stackalloc double[SampleCapacity];
            for (int index = 0; index < jitterCount; index++)
                deviations[index] = Math.Abs(sortedRtts[index] - medianRtt);
            InsertionSort(deviations[..jitterCount]);
            double jitter = Median(deviations[..jitterCount]) * MadToJitterFactor;
            double allowance = Math.Max(MinimumLowRttAllowanceMs,
                jitter * JitterAllowanceFactor);
            double threshold = minimum + allowance;
            eligibilityThreshold = threshold;

            Span<double> eligibleOffsets = stackalloc double[SampleCapacity];
            int eligible = 0;
            for (int index = 0; index < _sampleCount; index++)
            {
                ClockSample sample = _samples[index];
                bool eligibleSample = pathRttMs >= 0
                    ? IsRttInPathBand(sample.RttMs, pathRttMs)
                    : sample.RttMs <= threshold;
                if (eligibleSample)
                    eligibleOffsets[eligible++] = sample.OffsetTicks;
            }
            if (eligible == 0)
            {
                medianOffset = _samples[(_sampleCursor + SampleCapacity - 1) % SampleCapacity].OffsetTicks;
                return medianOffset;
            }
            InsertionSort(eligibleOffsets[..eligible]);
            medianOffset = Median(eligibleOffsets[..eligible]);
            if (!preferRecent) return medianOffset;

            double weightedOffset = 0;
            double totalWeight = 0;
            for (int index = 0; index < _sampleCount; index++)
            {
                ClockSample sample = _samples[index];
                bool eligibleSample = pathRttMs >= 0
                    ? IsRttInPathBand(sample.RttMs, pathRttMs)
                    : sample.RttMs <= threshold;
                if (!eligibleSample) continue;
                int age = _sampleCursor - 1 - index;
                if (age < 0) age += SampleCapacity;
                double weight = 1;
                for (int step = 0; step < age; step++) weight *= RecentWeightDecay;
                weightedOffset += sample.OffsetTicks * weight;
                totalWeight += weight;
            }
            return totalWeight > 0 ? weightedOffset / totalWeight : medianOffset;
        }

        private static void InsertionSort(Span<double> values)
        {
            for (int index = 1; index < values.Length; index++)
            {
                double value = values[index];
                int previous = index - 1;
                while (previous >= 0 && values[previous] > value)
                {
                    values[previous + 1] = values[previous];
                    previous--;
                }
                values[previous + 1] = value;
            }
        }

        private static double Median(ReadOnlySpan<double> values)
            => values.Length == 0 ? 0
                : values.Length % 2 == 0
                    ? (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2
                    : values[values.Length / 2];

        private readonly record struct ClockSample(double RttMs, double OffsetTicks);
    }
}
