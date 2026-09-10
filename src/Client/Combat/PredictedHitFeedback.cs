using System;
using MphRead.Entities;

namespace MphRead.Mods.Network;

public readonly record struct PredictedHitIdentity(
    uint MatchId, CombatActor Attacker, CombatActor Target, uint CommandSequence);

public readonly record struct HitPredictionMetrics(
    long Predicted,
    long Confirmed,
    long Denied,
    long AuthoritativeUnpredicted,
    long DuplicatePrevented,
    long ContinuousPredicted,
    long ContinuousConfirmed,
    long ContinuousDenied,
    long ContinuousAuthoritativeUnpredicted,
    long CapacityEvicted,
    long HeadshotPromotions,
    long KillPromotions,
    long ShotToPredictionFrames,
    long ShotToConfirmationFrames,
    long PredictionLeadFrames,
    long TimingSamples,
    int Pending,
    int ContinuousPending)
{
    public double ConfirmationRate => Predicted == 0 ? 0 : Confirmed / (double)Predicted;
    public double DenialRate => Predicted == 0 ? 0 : Denied / (double)Predicted;
    public double AuthoritativeUnpredictedRate
        => Confirmed + AuthoritativeUnpredicted == 0 ? 0
        : AuthoritativeUnpredicted / (double)(Confirmed + AuthoritativeUnpredicted);
    public double ContinuousConfirmationRate => ContinuousPredicted == 0
        ? 0 : ContinuousConfirmed / (double)ContinuousPredicted;
    public double MeanShotToPredictionFrames
        => TimingSamples == 0 ? 0 : ShotToPredictionFrames / (double)TimingSamples;
    public double MeanShotToConfirmationFrames
        => TimingSamples == 0 ? 0 : ShotToConfirmationFrames / (double)TimingSamples;
    public double MeanPredictionLeadFrames
        => TimingSamples == 0 ? 0 : PredictionLeadFrames / (double)TimingSamples;
}

/// <summary>
/// Game-thread-owned, presentation-only correlation of local collision attempts
/// with authoritative Damage facts. It never retains entity references and all
/// storage is fixed-size.
/// </summary>
public sealed class PredictedHitFeedback
{
    public static bool DefaultEnabled { get; set; } = true;
    public const int DefaultCapacity = 128;
    public const int MaxLifetimeFrames = 120;
    public const int ContinuousPulseFrames = 8; // 60 Hz / 8 pulses per second, rounded up.
    private const int ContinuousCapacity = 16;
    private const int ContinuousPulseCapacity = 16;
    private const int ShotCapacity = DefaultCapacity;

    private readonly PendingEntry[] _pending;
    private readonly RetiredEntry[] _retired;
    private readonly ShotEntry[] _shots = new ShotEntry[ShotCapacity];
    private readonly ContinuousEntry[] _continuous = new ContinuousEntry[ContinuousCapacity];
    private readonly long[,] _continuousPulseFrames
        = new long[ContinuousCapacity, ContinuousPulseCapacity];
    private int _pendingCount;
    private int _retiredCursor;
    private int _shotCursor;
    private long _frame;
    private uint _matchId;
    private CombatActor _localActor = CombatActor.None;

    public PredictedHitFeedback(int capacity = DefaultCapacity)
    {
        int bounded = Math.Clamp(capacity, 1, DefaultCapacity);
        _pending = new PendingEntry[bounded];
        _retired = new RetiredEntry[bounded];
        Enabled = DefaultEnabled;
    }

    public bool Enabled { get; set; }
    public uint MatchId => _matchId;
    public CombatActor LocalActor => _localActor;
    public long Frame => _frame;
    public HitPredictionMetrics Metrics => new(Predicted, Confirmed, Denied,
        AuthoritativeUnpredicted, DuplicatePrevented, ContinuousPredicted,
        ContinuousConfirmed, ContinuousDenied, ContinuousAuthoritativeUnpredicted, CapacityEvicted,
        HeadshotPromotions, KillPromotions, ShotToPredictionFrames,
        ShotToConfirmationFrames, PredictionLeadFrames, TimingSamples, _pendingCount,
        ContinuousPendingCount());

    public long Predicted { get; private set; }
    public long Confirmed { get; private set; }
    public long Denied { get; private set; }
    public long AuthoritativeUnpredicted { get; private set; }
    public long DuplicatePrevented { get; private set; }
    public long ContinuousPredicted { get; private set; }
    public long ContinuousConfirmed { get; private set; }
    public long ContinuousDenied { get; private set; }
    public long ContinuousAuthoritativeUnpredicted { get; private set; }
    public long CapacityEvicted { get; private set; }
    public long HeadshotPromotions { get; private set; }
    public long KillPromotions { get; private set; }
    public long ShotToPredictionFrames { get; private set; }
    public long ShotToConfirmationFrames { get; private set; }
    public long PredictionLeadFrames { get; private set; }
    public long TimingSamples { get; private set; }

    public void SetContext(uint matchId, in CombatActor localActor)
    {
        if (_matchId == matchId && _localActor == localActor)
            return;
        ResetEpoch();
        _matchId = matchId;
        _localActor = localActor;
    }

    public void Clear()
    {
        ResetEpoch();
        Predicted = Confirmed = Denied = AuthoritativeUnpredicted = 0;
        DuplicatePrevented = ContinuousPredicted = ContinuousConfirmed = ContinuousDenied = 0;
        ContinuousAuthoritativeUnpredicted = 0;
        CapacityEvicted = HeadshotPromotions = KillPromotions = 0;
        ShotToPredictionFrames = ShotToConfirmationFrames = 0;
        PredictionLeadFrames = TimingSamples = 0;
    }

    /// <summary>Clears identity-fenced transient state without losing run telemetry.</summary>
    public void ResetEpoch()
    {
        Array.Clear(_pending);
        Array.Clear(_retired);
        Array.Clear(_shots);
        Array.Clear(_continuous);
        Array.Clear(_continuousPulseFrames);
        _pendingCount = _retiredCursor = _shotCursor = 0;
        _frame = 0;
        _matchId = 0;
        _localActor = CombatActor.None;
    }

    public void Advance()
    {
        _frame++;
        for (int i = _pendingCount - 1; i >= 0; i--)
        {
            if (_frame - _pending[i].PredictedFrame < MaxLifetimeFrames)
                continue;
            Denied++;
            Retire(_pending[i].Identity);
            RemovePendingAt(i);
        }
        for (int i = 0; i < _continuous.Length; i++)
        {
            ExpireContinuousPulses(i);
            if (_continuous[i].Used
                && _frame - _continuous[i].LastContactFrame >= MaxLifetimeFrames)
                _continuous[i] = default;
        }
    }

    public void ObserveShot(in CombatShot shot)
    {
        if (_matchId == 0 || shot.Actor != _localActor || !shot.Actor.IsValid)
            return;
        for (int i = 0; i < _shots.Length; i++)
        {
            if (_shots[i].Used && _shots[i].Actor == shot.Actor
                && _shots[i].CommandSequence == shot.CommandSequence)
                return;
        }
        _shots[_shotCursor] = new ShotEntry
        {
            Used = true,
            Actor = shot.Actor,
            CommandSequence = shot.CommandSequence,
            Frame = _frame
        };
        _shotCursor = (_shotCursor + 1) % _shots.Length;
    }

    public bool OwnsCurrentShot(in CombatShot shot)
    {
        if (_matchId == 0 || shot.Actor != _localActor || !shot.Actor.IsValid)
            return false;
        for (int i = 0; i < _shots.Length; i++)
        {
            if (_shots[i].Used && _shots[i].Actor == shot.Actor
                && _shots[i].CommandSequence == shot.CommandSequence)
                return true;
        }
        return false;
    }

    public bool ObserveDamageAttempt(in CombatShot shot, in CombatActor target,
        byte weapon, bool continuous)
    {
        if (!Enabled || _matchId == 0 || shot.Actor != _localActor
            || !shot.Actor.IsValid || !target.IsValid || target == _localActor)
            return false;

        if (continuous)
            return ObserveContinuous(shot.Actor, target, weapon);

        PredictedHitIdentity identity = new(_matchId, shot.Actor, target, shot.CommandSequence);
        if (FindPending(identity) >= 0 || ContainsRetired(identity))
        {
            DuplicatePrevented++;
            return false;
        }
        if (_pendingCount == _pending.Length)
        {
            int oldest = FindOldestPending();
            CapacityEvicted++;
            Retire(_pending[oldest].Identity);
            RemovePendingAt(oldest);
        }
        long shotFrame = FindShotFrame(shot.Actor, shot.CommandSequence);
        _pending[_pendingCount++] = new PendingEntry
        {
            Identity = identity,
            Weapon = weapon,
            ShotFrame = shotFrame,
            PredictedFrame = _frame
        };
        Predicted++;
        return true;
    }

    /// <summary>Consumes an already deduplicated authoritative Damage fact.</summary>
    public bool Confirm(in CombatEvent value)
    {
        if (_matchId == 0 || value.Kind != CombatEventKind.Damage || value.Amount == 0
            || !value.IsValid || value.Actor != _localActor || !value.Target.IsValid
            || value.Target == _localActor)
            return false;

        if (value.Weapon == (byte)BeamType.ShockCoil)
        {
            for (int i = 0; i < _continuous.Length; i++)
            {
                ref ContinuousEntry contact = ref _continuous[i];
                if (contact.Used && contact.Attacker == value.Actor && contact.Target == value.Target
                    && contact.Weapon == value.Weapon
                    && _frame - contact.LastContactFrame < MaxLifetimeFrames)
                {
                    if (contact.PendingPulses > 0)
                    {
                        contact.PulseStart = (contact.PulseStart + 1) % ContinuousPulseCapacity;
                        contact.PendingPulses--;
                        ContinuousConfirmed++;
                    }
                    else
                        ContinuousAuthoritativeUnpredicted++;
                    contact.LastAuthoritativeFrame = _frame;
                    return true;
                }
            }
            ContinuousAuthoritativeUnpredicted++;
            return false;
        }

        PredictedHitIdentity identity = new(_matchId, value.Actor, value.Target, value.CommandSequence);
        int index = FindPending(identity);
        if (index < 0)
        {
            AuthoritativeUnpredicted++;
            return false;
        }
        PendingEntry pending = _pending[index];
        if (pending.Weapon != 255 && value.Weapon != 255 && pending.Weapon != value.Weapon)
        {
            AuthoritativeUnpredicted++;
            return false;
        }
        Confirmed++;
        if ((value.Flags & CombatEventFlags.Headshot) != 0) HeadshotPromotions++;
        if (value.Health == 0) KillPromotions++;
        long shotToPrediction = Math.Max(0, pending.PredictedFrame - pending.ShotFrame);
        long shotToConfirmation = Math.Max(0, _frame - pending.ShotFrame);
        ShotToPredictionFrames += shotToPrediction;
        ShotToConfirmationFrames += shotToConfirmation;
        PredictionLeadFrames += Math.Max(0, _frame - pending.PredictedFrame);
        TimingSamples++;
        Retire(pending.Identity);
        RemovePendingAt(index);
        return true;
    }

    private bool ObserveContinuous(CombatActor attacker, CombatActor target, byte weapon)
    {
        int free = -1;
        int oldest = 0;
        for (int i = 0; i < _continuous.Length; i++)
        {
            ref ContinuousEntry value = ref _continuous[i];
            if (!value.Used)
            {
                if (free < 0) free = i;
                continue;
            }
            if (value.Attacker == attacker && value.Target == target && value.Weapon == weapon)
            {
                value.LastContactFrame = _frame;
                if (_frame - value.LastMarkerFrame < ContinuousPulseFrames)
                    return false;
                value.LastMarkerFrame = _frame;
                EnqueueContinuousPulse(i);
                ContinuousPredicted++;
                return true;
            }
            if (_continuous[i].LastContactFrame < _continuous[oldest].LastContactFrame)
                oldest = i;
        }
        int index = free >= 0 ? free : oldest;
        _continuous[index] = new ContinuousEntry
        {
            Used = true,
            Attacker = attacker,
            Target = target,
            Weapon = weapon,
            LastContactFrame = _frame,
            LastMarkerFrame = _frame
        };
        EnqueueContinuousPulse(index);
        ContinuousPredicted++;
        return true;
    }

    private void EnqueueContinuousPulse(int index)
    {
        ref ContinuousEntry contact = ref _continuous[index];
        if (contact.PendingPulses == ContinuousPulseCapacity)
        {
            contact.PulseStart = (contact.PulseStart + 1) % ContinuousPulseCapacity;
            contact.PendingPulses--;
            ContinuousDenied++;
        }
        int position = (contact.PulseStart + contact.PendingPulses) % ContinuousPulseCapacity;
        _continuousPulseFrames[index, position] = _frame;
        contact.PendingPulses++;
    }

    private void ExpireContinuousPulses(int index)
    {
        ref ContinuousEntry contact = ref _continuous[index];
        while (contact.PendingPulses > 0
            && _frame - _continuousPulseFrames[index, contact.PulseStart] >= MaxLifetimeFrames)
        {
            contact.PulseStart = (contact.PulseStart + 1) % ContinuousPulseCapacity;
            contact.PendingPulses--;
            ContinuousDenied++;
        }
    }

    private int ContinuousPendingCount()
    {
        int count = 0;
        for (int i = 0; i < _continuous.Length; i++) count += _continuous[i].PendingPulses;
        return count;
    }

    private long FindShotFrame(CombatActor actor, uint command)
    {
        for (int i = 0; i < _shots.Length; i++)
            if (_shots[i].Used && _shots[i].Actor == actor && _shots[i].CommandSequence == command)
                return _shots[i].Frame;
        return _frame;
    }

    private int FindPending(in PredictedHitIdentity identity)
    {
        for (int i = 0; i < _pendingCount; i++)
            if (_pending[i].Identity == identity) return i;
        return -1;
    }

    private bool ContainsRetired(in PredictedHitIdentity identity)
    {
        for (int i = 0; i < _retired.Length; i++)
            if (_retired[i].Used && _retired[i].Identity == identity
                && _frame - _retired[i].Frame < MaxLifetimeFrames) return true;
        return false;
    }

    private int FindOldestPending()
    {
        int oldest = 0;
        for (int i = 1; i < _pendingCount; i++)
            if (_pending[i].PredictedFrame < _pending[oldest].PredictedFrame) oldest = i;
        return oldest;
    }

    private void Retire(in PredictedHitIdentity identity)
    {
        _retired[_retiredCursor] = new RetiredEntry { Used = true, Identity = identity, Frame = _frame };
        _retiredCursor = (_retiredCursor + 1) % _retired.Length;
    }

    private void RemovePendingAt(int index)
    {
        _pendingCount--;
        if (index < _pendingCount)
            Array.Copy(_pending, index + 1, _pending, index, _pendingCount - index);
        _pending[_pendingCount] = default;
    }

    private struct PendingEntry
    {
        public PredictedHitIdentity Identity;
        public byte Weapon;
        public long ShotFrame;
        public long PredictedFrame;
    }

    private struct RetiredEntry
    {
        public bool Used;
        public PredictedHitIdentity Identity;
        public long Frame;
    }

    private struct ShotEntry
    {
        public bool Used;
        public CombatActor Actor;
        public uint CommandSequence;
        public long Frame;
    }

    private struct ContinuousEntry
    {
        public bool Used;
        public CombatActor Attacker;
        public CombatActor Target;
        public byte Weapon;
        public long LastContactFrame;
        public long LastMarkerFrame;
        public long LastAuthoritativeFrame;
        public int PulseStart;
        public int PendingPulses;
    }
}
