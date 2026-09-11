using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Presentation telemetry bands are deliberately stricter than the escalation
/// thresholds. Only an Excellent interval is eligible to advance the slow
/// recovery dwell; Healthy and Degraded intervals are observable but remain
/// non-clean, while Unstable can immediately add a safety step.
/// </summary>
internal enum TimingTelemetryBand : byte
{
    Excellent,
    Healthy,
    Degraded,
    Unstable
}

/// <summary>
/// One connection's server-owned timing policy. Client observations may make
/// presentation safer, but only trusted RTT/input starvation may expand the
/// rewind-eligible presentation allowance.
/// </summary>
internal sealed class ServerNetworkTimingController
{
    internal const double EvaluationIntervalSeconds = 1;
    internal const double CleanDwellSeconds = 8;
    internal const double TransitionTimeoutSeconds = 5;
    internal const double ExcellentMaxUnderrunRate = 0.0025;
    internal const double ExcellentMaxExtrapolationRate = 0.005;
    internal const double MaximumTelemetryAgeSeconds = 2.5;
    internal const double HealthyMaxUnderrunRate = 0.01;
    internal const double HealthyMaxExtrapolationRate = 0.02;
    internal const double UnstableUnderrunRate = 0.02;
    internal const double UnstableExtrapolationRate = 0.05;

    private readonly bool _enabled;
    private readonly bool _requireFreshTelemetry;
    private uint _nextRevision = 1;
    private double _nextEvaluation;
    private double _cleanSince = -1;
    private double _clientUnstableUntil;
    private long _lastStarvedTicks;
    private byte _trustedRewindDelay = NetworkTimingProfile.Compatibility.PresentationDelayTicks;
    private bool _telemetryObserved;
    private double _lastTelemetryAt = -1;
    private bool _telemetryStale;
    private bool _telemetrySampleSufficient;
    private long _telemetryStaleIntervals;
    private long _downshiftBlockedByStaleTelemetry;

    public NetworkTimingProfile Active { get; private set; } = NetworkTimingProfile.Compatibility;
    public NetworkTimingProfile? Offered { get; private set; }
    public double OfferedAt { get; private set; }
    public double LastUnderrunRate { get; private set; }
    public double LastExtrapolationRate { get; private set; }
    public double LastTelemetryAt => _lastTelemetryAt;
    public bool TelemetryObserved => _telemetryObserved;
    public bool TelemetryStale => _telemetryStale;
    public bool TelemetrySampleSufficient => _telemetrySampleSufficient;
    public long TelemetryStaleIntervals => _telemetryStaleIntervals;
    public long DownshiftBlockedByStaleTelemetry => _downshiftBlockedByStaleTelemetry;
    internal TimingTelemetryBand LastTelemetryBand { get; private set; } = TimingTelemetryBand.Excellent;
    public byte RewindPresentationDelayTicks => _enabled
        ? _trustedRewindDelay : NetworkTimingProfile.Compatibility.PresentationDelayTicks;
    public bool Enabled => _enabled;

    public ServerNetworkTimingController(bool enabled, bool requireFreshTelemetry = false)
    {
        _enabled = enabled;
        _requireFreshTelemetry = requireFreshTelemetry;
    }

    public void Reset()
    {
        Active = NetworkTimingProfile.Compatibility;
        Offered = null;
        OfferedAt = 0;
        _nextRevision = 1;
        _nextEvaluation = 0;
        _cleanSince = -1;
        _clientUnstableUntil = 0;
        _lastStarvedTicks = 0;
        _trustedRewindDelay = NetworkTimingProfile.Compatibility.PresentationDelayTicks;
        _telemetryObserved = false;
        _lastTelemetryAt = -1;
        _telemetryStale = false;
        _telemetrySampleSufficient = false;
        _telemetryStaleIntervals = 0;
        _downshiftBlockedByStaleTelemetry = 0;
        LastUnderrunRate = LastExtrapolationRate = 0;
        LastTelemetryBand = TimingTelemetryBand.Excellent;
    }

    public bool ObserveTelemetry(in NetworkTimingTelemetry telemetry, double now)
    {
        if (!_enabled || !telemetry.IsValid || !Double.IsFinite(now) || now < 0
            || telemetry.ProfileRevision != Active.Revision
                && telemetry.ProfileRevision != Offered?.Revision)
            return false;
        bool staleGap = _lastTelemetryAt >= 0 && now - _lastTelemetryAt > MaximumTelemetryAgeSeconds;
        if (_lastTelemetryAt < 0 || staleGap)
            _cleanSince = -1;
        if (staleGap && !_telemetryStale)
            _telemetryStaleIntervals++;
        _lastTelemetryAt = now;
        _telemetryStale = false;
        _telemetrySampleSufficient = telemetry.PresentedFrames >= 30;
        LastUnderrunRate = telemetry.SnapshotUnderruns / (double)telemetry.PresentedFrames;
        LastExtrapolationRate = telemetry.ExtrapolatedFrames / (double)telemetry.PresentedFrames;
        LastTelemetryBand = ClassifyTelemetry(LastUnderrunRate, LastExtrapolationRate);
        if (telemetry.SnapshotJitterTenthsMs >= 100)
            LastTelemetryBand = TimingTelemetryBand.Unstable;
        _telemetryObserved = true;
        if (LastTelemetryBand == TimingTelemetryBand.Unstable
            || telemetry.SnapshotJitterTenthsMs >= 100)
            _clientUnstableUntil = Math.Max(_clientUnstableUntil, now + 2);
        return true;
    }

    /// <summary>Returns at most one adjacent proposal and never mutates state until admitted.</summary>
    public bool TrySelectOffer(double now, double smoothedRttMs, long starvedTicks,
        out NetworkTimingProfile profile, out bool replaceTimedOut)
    {
        profile = default;
        replaceTimedOut = false;
        if (!_enabled || !Double.IsFinite(now) || now < 0) return false;
        if (Offered is not null)
        {
            if (now - OfferedAt < TransitionTimeoutSeconds) return false;
            replaceTimedOut = true;
            profile = Active.Revision == 0
                ? NetworkTimingProfile.Create(NextRevision(), NetworkTimingLevel.Recovery)
                : new(NextRevision(), Active.PresentationDelayTicks, Active.InputPlayoutTicks);
            return true;
        }
        // Enabled peers must receive a versioned starting policy even when the
        // safe Recovery presentation delay matches compatibility behavior.
        if (Active.Revision == 0)
        {
            _nextEvaluation = now + EvaluationIntervalSeconds;
            profile = NetworkTimingProfile.Create(NextRevision(), NetworkTimingLevel.Recovery);
            return true;
        }
        if (now < _nextEvaluation) return false;
        _nextEvaluation = now + EvaluationIntervalSeconds;

        bool starved = starvedTicks > _lastStarvedTicks;
        _lastStarvedTicks = starvedTicks;
        NetworkTimingLevel trusted = TrustedLevel(smoothedRttMs, starved);
        byte trustedDelay = NetworkTimingProfile.Create(1, trusted).PresentationDelayTicks;
        NetworkTimingLevel current = NetworkTimingProfile.LevelFor(Active.PresentationDelayTicks);
        NetworkTimingLevel desired = trusted;
        if (now < _clientUnstableUntil && desired < NetworkTimingLevel.Recovery)
            desired++;

        double telemetryAge = TelemetryAge(now);
        if (telemetryAge > MaximumTelemetryAgeSeconds)
        {
            if (!_telemetryStale) _telemetryStaleIntervals++;
            _telemetryStale = true;
        }

        if (desired > current)
        {
            _cleanSince = -1;
            profile = NetworkTimingProfile.Create(NextRevision(), (NetworkTimingLevel)((byte)current + 1));
        }
        else if (desired < current)
        {
            // Healthy, Degraded, and Unstable intervals are intentionally not
            // clean recovery evidence. This prevents rates in the gaps between
            // the escalation thresholds from advancing the downward dwell.
            if (_requireFreshTelemetry
                && (!_telemetryObserved || telemetryAge > MaximumTelemetryAgeSeconds
                    || !_telemetrySampleSufficient))
            {
                _cleanSince = -1;
                _downshiftBlockedByStaleTelemetry++;
                return false;
            }
            if (_telemetryObserved && LastTelemetryBand != TimingTelemetryBand.Excellent)
            {
                _cleanSince = -1;
                return false;
            }
            if (_cleanSince < 0) _cleanSince = now;
            if (now - _cleanSince < CleanDwellSeconds) return false;
            _cleanSince = now;
            profile = NetworkTimingProfile.Create(NextRevision(), (NetworkTimingLevel)((byte)current - 1));
        }
        else
        {
            _cleanSince = -1;
            return false;
        }

        // Trusted observations may widen the security allowance immediately;
        // client-only instability never changes this value.
        if (trustedDelay > _trustedRewindDelay) _trustedRewindDelay = trustedDelay;
        return true;
    }

    private static TimingTelemetryBand ClassifyTelemetry(double underrunRate, double extrapolationRate)
    {
        if (underrunRate >= UnstableUnderrunRate || extrapolationRate >= UnstableExtrapolationRate)
            return TimingTelemetryBand.Unstable;
        if (underrunRate >= HealthyMaxUnderrunRate || extrapolationRate >= HealthyMaxExtrapolationRate)
            return TimingTelemetryBand.Degraded;
        if (underrunRate >= ExcellentMaxUnderrunRate || extrapolationRate >= ExcellentMaxExtrapolationRate)
            return TimingTelemetryBand.Healthy;
        return TimingTelemetryBand.Excellent;
    }

    public void MarkOffered(in NetworkTimingProfile profile, double now)
    {
        if (!profile.IsValid || Offered is not null && profile.Revision == Offered.Value.Revision)
            throw new ArgumentException("Invalid timing proposal.");
        Offered = profile;
        OfferedAt = now;
    }

    public bool TryAcknowledge(uint revision)
    {
        if (!_enabled || Offered is not NetworkTimingProfile offered || offered.Revision != revision)
            return false;
        Active = offered;
        Offered = null;
        OfferedAt = 0;
        _cleanSince = -1;
        // A new profile revision is a new presentation epoch. Evidence from
        // the previous revision must not authorize its first downshift.
        _telemetryObserved = false;
        _lastTelemetryAt = -1;
        _telemetryStale = false;
        _telemetrySampleSufficient = false;
        // A completed downward presentation transition proves future inputs no
        // longer legitimately refer to the older, deeper presentation buffer.
        _trustedRewindDelay = Math.Min(_trustedRewindDelay, offered.PresentationDelayTicks);
        return true;
    }

    public double TelemetryAge(double now)
    {
        if (!Double.IsFinite(now) || now < 0 || _lastTelemetryAt < 0 || now < _lastTelemetryAt)
            return double.PositiveInfinity;
        return now - _lastTelemetryAt;
    }

    private static NetworkTimingLevel TrustedLevel(double rttMs, bool starved)
    {
        NetworkTimingLevel level = !Double.IsFinite(rttMs) || rttMs <= 0
            ? NetworkTimingLevel.Recovery
            : rttMs < 50 ? NetworkTimingLevel.Excellent
            : rttMs < 100 ? NetworkTimingLevel.Good
            : rttMs < 180 ? NetworkTimingLevel.Normal
            : rttMs < 250 ? NetworkTimingLevel.Unstable
            : NetworkTimingLevel.Recovery;
        if (starved && level < NetworkTimingLevel.Recovery) level++;
        return level;
    }

    private uint NextRevision()
    {
        uint revision = _nextRevision++;
        if (revision == 0) revision = _nextRevision++;
        return revision;
    }
}
