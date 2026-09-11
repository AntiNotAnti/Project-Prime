using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>
/// Immutable, value-only identity and pose data for one successfully presented
/// remote player. It intentionally does not claim to contain the full hunter
/// collision geometry; the current measurement covers the network position and
/// its identity/continuity facts only.
/// </summary>
public readonly record struct PresentedPlayerCollisionState(
    CombatActor Actor,
    uint MatchId,
    uint Generation,
    uint PresentedTick,
    Hunter Hunter,
    byte TeamIndex,
    Vector3 Position,
    bool Active,
    bool Spawned,
    bool Alive,
    bool AltForm,
    bool Spectating)
{
    public uint Life => Actor.Life;
    public bool CanBeMeasured => Actor.IsValid && Active && Spawned && Alive && !Spectating;

    public bool MatchesCurrent(in CombatActor actor, Hunter hunter, byte teamIndex,
        bool active, bool spawned, bool alive, bool altForm, bool spectating)
        => Actor == actor && Hunter == hunter && TeamIndex == teamIndex
            && Active == active && Spawned == spawned && Alive == alive
            && AltForm == altForm && Spectating == spectating && CanBeMeasured;
}

/// <summary>Bounded measurement-only presentation metrics.</summary>
public readonly record struct PresentedCollisionMetrics(
    NetSample PresentedPoseError,
    NetSample PresentedVerticalError,
    NetSample SpeculativeHitPresentedPoseDistance,
    NetSample PresentedTickVsShotTickMismatch,
    NetSample PresentedTickVsSimulationTickMismatch,
    long PresentedPoseUnavailable,
    long Aligned,
    long Minor,
    long Material,
    long Severe)
{
    public long PresentedPoseSamples => PresentedPoseError.Count;
    public double? PresentedPoseErrorMean => PresentedPoseError.Count == 0 ? null : PresentedPoseError.Mean;
    public double? PresentedPoseErrorP95 => Percentile(PresentedPoseError, 0.95);
    public double? PresentedPoseErrorP99 => Percentile(PresentedPoseError, 0.99);
    public double? PresentedPoseErrorMax => PresentedPoseError.Count == 0 ? null : PresentedPoseError.Max;
    public double? PresentedVerticalErrorMean => PresentedVerticalError.Count == 0 ? null : PresentedVerticalError.Mean;
    public double? PresentedVerticalErrorP95 => Percentile(PresentedVerticalError, 0.95);
    public double? PresentedVerticalErrorMax => PresentedVerticalError.Count == 0 ? null : PresentedVerticalError.Max;

    private static double? Percentile(NetSample sample, double percentile)
    {
        if (sample.Count == 0) return null;
        BoundedPercentileSnapshot values = sample.Percentiles;
        return percentile >= 0.99 ? values.P99 : values.P95;
    }
}

/// <summary>
/// Value-only report snapshot. Unlike <see cref="PresentedCollisionMetrics"/>,
/// this does not retain the live percentile samplers across an epoch reset.
/// </summary>
public readonly record struct PresentedCollisionMetricsSnapshot(
    long PresentedPoseSamples,
    double? PresentedPoseErrorMean,
    BoundedPercentileSnapshot PresentedPoseErrorPercentiles,
    double? PresentedPoseErrorMax,
    long PresentedVerticalSamples,
    double? PresentedVerticalErrorMean,
    BoundedPercentileSnapshot PresentedVerticalErrorPercentiles,
    double? PresentedVerticalErrorMax,
    long SpeculativeHitSamples,
    double? SpeculativeHitPresentedPoseDistanceMean,
    BoundedPercentileSnapshot SpeculativeHitPresentedPoseDistancePercentiles,
    double? SpeculativeHitPresentedPoseDistanceMax,
    long ShotTickMismatchSamples,
    double? PresentedTickVsShotTickMismatchMean,
    BoundedPercentileSnapshot PresentedTickVsShotTickMismatchPercentiles,
    double? PresentedTickVsShotTickMismatchMax,
    long SimulationTickMismatchSamples,
    double? PresentedTickVsSimulationTickMismatchMean,
    BoundedPercentileSnapshot PresentedTickVsSimulationTickMismatchPercentiles,
    double? PresentedTickVsSimulationTickMismatchMax,
    long PresentedPoseUnavailable,
    long Aligned,
    long Minor,
    long Material,
    long Severe)
{
    public static PresentedCollisionMetricsSnapshot Capture(
        in PresentedCollisionMetrics value)
        => new(
            value.PresentedPoseError.Count,
            Mean(value.PresentedPoseError),
            value.PresentedPoseError.Percentiles,
            Maximum(value.PresentedPoseError),
            value.PresentedVerticalError.Count,
            Mean(value.PresentedVerticalError),
            value.PresentedVerticalError.Percentiles,
            Maximum(value.PresentedVerticalError),
            value.SpeculativeHitPresentedPoseDistance.Count,
            Mean(value.SpeculativeHitPresentedPoseDistance),
            value.SpeculativeHitPresentedPoseDistance.Percentiles,
            Maximum(value.SpeculativeHitPresentedPoseDistance),
            value.PresentedTickVsShotTickMismatch.Count,
            Mean(value.PresentedTickVsShotTickMismatch),
            value.PresentedTickVsShotTickMismatch.Percentiles,
            Maximum(value.PresentedTickVsShotTickMismatch),
            value.PresentedTickVsSimulationTickMismatch.Count,
            Mean(value.PresentedTickVsSimulationTickMismatch),
            value.PresentedTickVsSimulationTickMismatch.Percentiles,
            Maximum(value.PresentedTickVsSimulationTickMismatch),
            value.PresentedPoseUnavailable, value.Aligned, value.Minor,
            value.Material, value.Severe);

    private static double? Mean(in NetSample sample)
        => sample.Count == 0 ? null : sample.Mean;

    private static double? Maximum(in NetSample sample)
        => sample.Count == 0 ? null : sample.Max;
}

/// <summary>
/// Reuses two fixed presentation buffers. A prepared frame is invisible to
/// readers until the renderer reports a successful presentation commit.
/// </summary>
public sealed class PresentedCollisionFrame
{
    public const int SlotCapacity = 8;
    private const int ShotCapacity = 128;
    private const float AlignedThreshold = 0.001f;
    private const float MinorThreshold = 0.10f;
    private const float MaterialThreshold = 0.30f;
    private readonly PresentedPlayerCollisionState[] _pending = new PresentedPlayerCollisionState[SlotCapacity];
    private readonly PresentedPlayerCollisionState[] _committed = new PresentedPlayerCollisionState[SlotCapacity];
    private readonly ShotIdentity[] _shots = new ShotIdentity[ShotCapacity];
    private int _shotCursor;
    private byte _pendingMask;
    private byte _committedMask;
    private bool _pendingActive;
    private uint _pendingMatchId;
    private uint _pendingGeneration;
    private uint _pendingTick;
    private uint _committedMatchId;
    private uint _committedGeneration;
    private uint _committedTick;

    public NetSample PresentedPoseError;
    public NetSample PresentedVerticalError;
    public NetSample SpeculativeHitPresentedPoseDistance;
    public NetSample PresentedTickVsShotTickMismatch;
    public NetSample PresentedTickVsSimulationTickMismatch;
    public long PresentedPoseUnavailable { get; private set; }
    public long Aligned { get; private set; }
    public long Minor { get; private set; }
    public long Material { get; private set; }
    public long Severe { get; private set; }
    public bool HasCommittedFrame => _committedMask != 0;
    public uint CommittedMatchId => _committedMatchId;
    public uint CommittedGeneration => _committedGeneration;
    public uint CommittedTick => _committedTick;

    public PresentedCollisionFrame()
    {
        ReserveSamples();
    }

    public PresentedCollisionMetrics Metrics => new(PresentedPoseError,
        PresentedVerticalError, SpeculativeHitPresentedPoseDistance,
        PresentedTickVsShotTickMismatch, PresentedTickVsSimulationTickMismatch,
        PresentedPoseUnavailable, Aligned,
        Minor, Material, Severe);

    /// <summary>Resets both buffers and this match-scoped measurement window.</summary>
    public void Clear()
    {
        _pendingMask = _committedMask = 0;
        _pendingActive = false;
        _pendingMatchId = _pendingGeneration = _pendingTick = 0;
        _committedMatchId = _committedGeneration = _committedTick = 0;
        _shotCursor = 0;
        Array.Clear(_shots);
        PresentedPoseUnavailable = Aligned = Minor = Material = Severe = 0;
        PresentedPoseError.Clear();
        PresentedVerticalError.Clear();
        SpeculativeHitPresentedPoseDistance.Clear();
        PresentedTickVsShotTickMismatch.Clear();
        PresentedTickVsSimulationTickMismatch.Clear();
        ReserveSamples();
    }

    /// <summary>Begins a candidate frame without changing the committed frame.</summary>
    public void Begin(in SnapshotPresentation presentation)
    {
        _pendingActive = Double.IsFinite(presentation.Tick);
        _pendingMatchId = presentation.MatchId;
        _pendingGeneration = presentation.Generation;
        _pendingTick = _pendingActive ? FloorTick(presentation.Tick) : 0;
        _pendingMask = 0;
    }

    /// <summary>Stages one sampled remote state while its temporary pose is active.</summary>
    public void Stage(in SnapshotPlayer state)
    {
        if (!_pendingActive || state.Slot >= SlotCapacity || state.ConnectionId == 0 || state.Life == 0)
            return;
        SnapshotPlayerFlags flags = state.Flags;
        _pending[state.Slot] = new PresentedPlayerCollisionState(
            new CombatActor(state.Slot, state.ConnectionId, state.Life),
            _pendingMatchId, _pendingGeneration, _pendingTick, state.Hunter,
            state.TeamIndex, state.Position,
            (flags & SnapshotPlayerFlags.Active) != 0,
            (flags & SnapshotPlayerFlags.Spawned) != 0,
            state.Health > 0,
            (flags & SnapshotPlayerFlags.AltForm) != 0,
            (flags & SnapshotPlayerFlags.Spectating) != 0);
        _pendingMask |= (byte)(1 << state.Slot);
    }

    /// <summary>Abandons a prepared frame without touching the visible frame.</summary>
    public void AbortPending()
    {
        _pendingActive = false;
        _pendingMask = 0;
    }

    /// <summary>
    /// Publishes only the frame whose identity still matches the renderer's
    /// successfully presented presentation token.
    /// </summary>
    public bool Commit(in SnapshotPresentation presentation)
    {
        if (!_pendingActive || !Double.IsFinite(presentation.Tick)
            || presentation.MatchId != _pendingMatchId
            || presentation.Generation != _pendingGeneration
            || FloorTick(presentation.Tick) != _pendingTick)
        {
            AbortPending();
            return false;
        }
        Array.Copy(_pending, _committed, SlotCapacity);
        _committedMask = _pendingMask;
        _committedMatchId = _pendingMatchId;
        _committedGeneration = _pendingGeneration;
        _committedTick = _pendingTick;
        _pendingActive = false;
        return true;
    }

    public bool TryGet(in CombatActor actor, out PresentedPlayerCollisionState state)
    {
        if (actor.IsValid && (uint)actor.Slot < SlotCapacity
            && (_committedMask & (1 << actor.Slot)) != 0
            && _committed[actor.Slot].Actor == actor)
        {
            state = _committed[actor.Slot];
            return true;
        }
        state = default;
        return false;
    }

    /// <summary>Returns true once for each locally owned shot identity.</summary>
    public bool BeginLocalShot(in CombatShot shot)
    {
        if (!shot.IsValid) return false;
        for (int i = 0; i < _shots.Length; i++)
        {
            if (_shots[i].Used && _shots[i].Actor == shot.Actor
                && _shots[i].CommandSequence == shot.CommandSequence)
                return false;
        }
        _shots[_shotCursor] = new ShotIdentity(true, shot.Actor, shot.CommandSequence);
        _shotCursor = (_shotCursor + 1) % _shots.Length;
        return true;
    }

    /// <summary>
    /// Records simulation-vs-presented position for one candidate remote
    /// player. Current identity/form/lifecycle facts must all still match the
    /// committed frame or the sample is rejected as unavailable.
    /// </summary>
    public void RecordShotCandidate(in CombatShot shot, in CombatActor candidate,
        Vector3 simulationPosition, Hunter hunter, byte teamIndex, bool active,
        bool spawned, bool alive, bool altForm, bool spectating, uint simulationTick)
    {
        if (!HasShot(shot) || !TryGet(candidate, out PresentedPlayerCollisionState presented)
            || !presented.MatchesCurrent(candidate, hunter, teamIndex, active, spawned,
                alive, altForm, spectating))
        {
            PresentedPoseUnavailable++;
            return;
        }
        RecordPose(presented.Position, simulationPosition, shot.ViewServerTick,
            presented.PresentedTick, simulationTick);
    }

    /// <summary>Records the presented distance for an actual speculative hit.</summary>
    public void RecordSpeculativeHit(in CombatShot shot, in CombatActor candidate,
        Vector3 simulationPosition, Hunter hunter, byte teamIndex, bool active,
        bool spawned, bool alive, bool altForm, bool spectating, uint simulationTick)
    {
        if (!HasShot(shot) || !TryGet(candidate, out PresentedPlayerCollisionState presented)
            || !presented.MatchesCurrent(candidate, hunter, teamIndex, active, spawned,
                alive, altForm, spectating))
        {
            PresentedPoseUnavailable++;
            return;
        }
        Vector3 delta = presented.Position - simulationPosition;
        float distance = delta.Length;
        if (Single.IsFinite(distance) && distance >= 0)
            SpeculativeHitPresentedPoseDistance.Record(distance);
        RecordTickMismatch(shot.ViewServerTick, presented.PresentedTick);
        RecordSimulationTickMismatch(simulationTick, presented.PresentedTick);
    }

    private bool HasShot(in CombatShot shot)
    {
        for (int i = 0; i < _shots.Length; i++)
            if (_shots[i].Used && _shots[i].Actor == shot.Actor
                && _shots[i].CommandSequence == shot.CommandSequence) return true;
        return false;
    }

    private void RecordPose(Vector3 presentedPosition, Vector3 simulationPosition,
        uint shotTick, uint presentedTick, uint simulationTick)
    {
        Vector3 delta = presentedPosition - simulationPosition;
        float total = delta.Length;
        float vertical = MathF.Abs(delta.Y);
        if (!Single.IsFinite(total) || total < 0 || !Single.IsFinite(vertical) || vertical < 0)
            return;
        PresentedPoseError.Record(total);
        PresentedVerticalError.Record(vertical);
        RecordTickMismatch(shotTick, presentedTick);
        RecordSimulationTickMismatch(simulationTick, presentedTick);
        if (total < AlignedThreshold) Aligned++;
        else if (total < MinorThreshold) Minor++;
        else if (total <= MaterialThreshold) Material++;
        else Severe++;
    }

    private void RecordTickMismatch(uint shotTick, uint presentedTick)
    {
        PresentedTickVsShotTickMismatch.Record(TickDistance(shotTick, presentedTick));
    }

    private void RecordSimulationTickMismatch(uint simulationTick, uint presentedTick)
        => PresentedTickVsSimulationTickMismatch.Record(TickDistance(simulationTick, presentedTick));

    private static uint TickDistance(uint first, uint second)
    {
        uint forward = unchecked(first - second);
        uint backward = unchecked(second - first);
        return Math.Min(forward, backward);
    }

    private void ReserveSamples()
    {
        PresentedPoseError.Reserve();
        PresentedVerticalError.Reserve();
        SpeculativeHitPresentedPoseDistance.Reserve();
        PresentedTickVsShotTickMismatch.Reserve();
        PresentedTickVsSimulationTickMismatch.Reserve();
    }

    private static uint FloorTick(double tick)
        => unchecked((uint)(long)Math.Floor(tick));

    private readonly record struct ShotIdentity(bool Used, CombatActor Actor, uint CommandSequence);
}
