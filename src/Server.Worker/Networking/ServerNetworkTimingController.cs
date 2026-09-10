using System;

namespace MphRead.Mods.Network;

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

    private readonly bool _enabled;
    private uint _nextRevision = 1;
    private double _nextEvaluation;
    private double _cleanSince = -1;
    private double _clientUnstableUntil;
    private long _lastStarvedTicks;
    private byte _trustedRewindDelay = NetworkTimingProfile.Compatibility.PresentationDelayTicks;

    public NetworkTimingProfile Active { get; private set; } = NetworkTimingProfile.Compatibility;
    public NetworkTimingProfile? Offered { get; private set; }
    public double OfferedAt { get; private set; }
    public byte RewindPresentationDelayTicks => _enabled
        ? _trustedRewindDelay : NetworkTimingProfile.Compatibility.PresentationDelayTicks;
    public bool Enabled => _enabled;

    public ServerNetworkTimingController(bool enabled) => _enabled = enabled;

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
    }

    public bool ObserveTelemetry(in NetworkTimingTelemetry telemetry, double now)
    {
        if (!_enabled || !telemetry.IsValid || !Double.IsFinite(now) || now < 0
            || telemetry.ProfileRevision != Active.Revision
                && telemetry.ProfileRevision != Offered?.Revision)
            return false;
        if (telemetry.SnapshotUnderruns > 0 || telemetry.ExtrapolatedFrames > 2
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

        if (desired > current)
        {
            _cleanSince = -1;
            profile = NetworkTimingProfile.Create(NextRevision(), (NetworkTimingLevel)((byte)current + 1));
        }
        else if (desired < current)
        {
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
        // A completed downward presentation transition proves future inputs no
        // longer legitimately refer to the older, deeper presentation buffer.
        _trustedRewindDelay = Math.Min(_trustedRewindDelay, offered.PresentationDelayTicks);
        return true;
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
