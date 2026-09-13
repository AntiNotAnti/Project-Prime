using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public readonly record struct PredictedImpulseIdentity(
    uint MatchId, CombatActor Actor, uint CommandSequence);

public readonly record struct SelfImpulseMetrics(
    long Predicted,
    long Confirmed,
    long Expired,
    long AuthoritativeApplied,
    long DuplicatePrevented,
    long BombJumpsPredicted,
    long ImpulseSamples,
    double ImpulseMagnitude,
    double ImpulseMagnitudeMax,
    long CorrectionSamples,
    double CorrectionDistance,
    double CorrectionDistanceMax,
    long HardCorrections,
    int Pending)
{
    public double MeanImpulseMagnitude => ImpulseSamples == 0
        ? 0 : ImpulseMagnitude / ImpulseSamples;
    public double MeanCorrectionDistance => CorrectionSamples == 0
        ? 0 : CorrectionDistance / CorrectionSamples;
}

/// <summary>Bounded, game-thread-owned ledger for presentation-time local self impulse.</summary>
public sealed class PredictedSelfImpulse
{
    public const int Capacity = 64;
    public const int LifetimeFrames = 120;
    public static bool DefaultEnabled { get; set; }

    private readonly Entry[] _entries = new Entry[Capacity];
    private readonly RetiredEntry[] _retired = new RetiredEntry[Capacity];
    private readonly BombEntry[] _bombs = new BombEntry[Capacity];
    private int _count;
    private int _bombCursor;
    private int _retiredCursor;
    private long _frame;
    private uint _matchId;
    private CombatActor _actor = CombatActor.None;
    private bool _hasAppliedSnapshot;
    private uint _appliedSnapshotTick;
    private long _lastPredictedFrame = Int64.MinValue;

    public PredictedSelfImpulse() => Enabled = DefaultEnabled;
    public bool Enabled { get; set; }
    public SelfImpulseMetrics Metrics => new(Predicted, Confirmed, Expired,
        AuthoritativeApplied, DuplicatePrevented, BombJumpsPredicted,
        ImpulseSamples, ImpulseMagnitude, ImpulseMagnitudeMax,
        CorrectionSamples, CorrectionDistance, CorrectionDistanceMax,
        HardCorrections, _count);

    public long Predicted { get; private set; }
    public long Confirmed { get; private set; }
    public long Expired { get; private set; }
    public long AuthoritativeApplied { get; private set; }
    public long DuplicatePrevented { get; private set; }
    public long BombJumpsPredicted { get; private set; }
    public long ImpulseSamples { get; private set; }
    public double ImpulseMagnitude { get; private set; }
    public double ImpulseMagnitudeMax { get; private set; }
    public long CorrectionSamples { get; private set; }
    public double CorrectionDistance { get; private set; }
    public double CorrectionDistanceMax { get; private set; }
    public long HardCorrections { get; private set; }

    public void SetContext(uint matchId, in CombatActor actor)
    {
        if (_matchId == matchId && _actor == actor)
            return;
        ResetEpoch();
        _matchId = matchId;
        _actor = actor;
    }

    public void Clear()
    {
        ResetEpoch();
        Predicted = Confirmed = Expired = AuthoritativeApplied = 0;
        DuplicatePrevented = BombJumpsPredicted = CorrectionSamples = 0;
        ImpulseSamples = 0;
        ImpulseMagnitude = ImpulseMagnitudeMax = 0;
        CorrectionDistance = CorrectionDistanceMax = 0;
        HardCorrections = 0;
    }

    /// <summary>Clears identity-fenced transient state without losing run telemetry.</summary>
    public void ResetEpoch()
    {
        Array.Clear(_entries);
        Array.Clear(_retired);
        Array.Clear(_bombs);
        _count = 0;
        _bombCursor = 0;
        _retiredCursor = 0;
        _frame = 0;
        _matchId = 0;
        _actor = CombatActor.None;
        _hasAppliedSnapshot = false;
        _appliedSnapshotTick = 0;
        _lastPredictedFrame = Int64.MinValue;
    }

    public void Advance()
    {
        _frame++;
        for (int i = _count - 1; i >= 0; i--)
        {
            if (_frame - _entries[i].PredictedFrame < LifetimeFrames) continue;
            Expired++;
            Retire(_entries[i].Identity);
            RemoveAt(i);
        }
    }

    public void NoteAppliedSnapshot(uint tick)
    {
        _hasAppliedSnapshot = true;
        _appliedSnapshotTick = tick;
    }

    public bool TryPredict(in CombatShot shot, Vector3 currentSpeed, Vector3 direction,
        bool altForm, out Vector3 predictedSpeed)
    {
        predictedSpeed = currentSpeed;
        if (!Enabled || _matchId == 0 || shot.Actor != _actor || !shot.Actor.IsValid
            || !Finite(currentSpeed) || !Finite(direction)) return false;
        PredictedImpulseIdentity identity = new(_matchId, shot.Actor, shot.CommandSequence);
        if (Find(identity) >= 0 || ContainsRetired(identity))
        {
            DuplicatePrevented++;
            return false;
        }
        if (_count == Capacity)
        {
            Expired++;
            Retire(_entries[0].Identity);
            RemoveAt(0);
        }
        predictedSpeed = DamageImpulse.Apply(currentSpeed, direction, altForm, halfturret: false);
        _entries[_count++] = new Entry
        {
            Identity = identity,
            PredictedFrame = _frame,
            Impulse = predictedSpeed - currentSpeed
        };
        _lastPredictedFrame = _frame;
        NoteImpulseMagnitude(_entries[_count - 1].Impulse.Length);
        Predicted++;
        return true;
    }

    public bool NoteBombJump(in CombatShot shot, float impulseMagnitude)
    {
        if (!Enabled || _matchId == 0 || shot.Actor != _actor || !shot.Actor.IsValid)
            return false;
        PredictedImpulseIdentity identity = new(_matchId, shot.Actor, shot.CommandSequence);
        for (int i = 0; i < _bombs.Length; i++)
        {
            if (_bombs[i].Used && _bombs[i].Identity == identity
                && _frame - _bombs[i].Frame < LifetimeFrames)
            {
                DuplicatePrevented++;
                return false;
            }
        }
        _bombs[_bombCursor] = new BombEntry { Used = true, Identity = identity, Frame = _frame };
        _bombCursor = (_bombCursor + 1) % _bombs.Length;
        _lastPredictedFrame = _frame;
        // Bomb jumps have no Damage event. Count their immediate floor
        // separately and let normal snapshots reconcile them.
        BombJumpsPredicted++;
        NoteImpulseMagnitude(Math.Max(0, impulseMagnitude));
        return true;
    }

    /// <summary>
    /// Handles an already deduplicated authoritative self-Damage fact. A
    /// snapshot applied at or after the event already covers its impulse, so
    /// current velocity is retained; otherwise the event impulse is applied once.
    /// </summary>
    public bool ApplyAuthoritative(in CombatEvent value, Vector3 currentSpeed,
        bool altForm, out Vector3 resultingSpeed)
    {
        resultingSpeed = currentSpeed;
        if (!Enabled || _matchId == 0 || value.Kind != CombatEventKind.Damage
            || value.Amount == 0 || value.Actor != _actor || value.Target != _actor
            || !Finite(value.Direction) || !SupportedWeapon(value.Weapon)) return false;
        PredictedImpulseIdentity identity = new(_matchId, value.Actor, value.CommandSequence);
        int index = Find(identity);
        if (index >= 0)
        {
            Confirmed++;
            DuplicatePrevented++;
            Retire(identity);
            RemoveAt(index);
            return true;
        }
        if (ContainsRetired(identity))
        {
            DuplicatePrevented++;
            return false;
        }
        bool mayApply = value.Health > 0 && value.FrozenTicks == 0;
        if (mayApply && (!_hasAppliedSnapshot || Sequence32.IsNewer(value.Tick, _appliedSnapshotTick)))
        {
            resultingSpeed = DamageImpulse.Apply(currentSpeed, value.Direction, altForm, halfturret: false);
            AuthoritativeApplied++;
        }
        Retire(identity);
        return true;
    }

    public void NoteCorrection(float distance, bool hard)
    {
        if (_lastPredictedFrame == Int64.MinValue
            || _frame - _lastPredictedFrame >= LifetimeFrames
            || !Single.IsFinite(distance) || distance < 0) return;
        CorrectionSamples++;
        CorrectionDistance += distance;
        CorrectionDistanceMax = Math.Max(CorrectionDistanceMax, distance);
        if (hard) HardCorrections++;
    }

    private int Find(in PredictedImpulseIdentity identity)
    {
        for (int i = 0; i < _count; i++)
            if (_entries[i].Identity == identity) return i;
        return -1;
    }

    private bool ContainsRetired(in PredictedImpulseIdentity identity)
    {
        for (int i = 0; i < _retired.Length; i++)
            if (_retired[i].Used && _retired[i].Identity == identity
                && _frame - _retired[i].Frame < LifetimeFrames) return true;
        return false;
    }

    private void Retire(in PredictedImpulseIdentity identity)
    {
        _retired[_retiredCursor] = new RetiredEntry { Used = true, Identity = identity, Frame = _frame };
        _retiredCursor = (_retiredCursor + 1) % _retired.Length;
    }

    private void RemoveAt(int index)
    {
        _count--;
        if (index < _count) Array.Copy(_entries, index + 1, _entries, index, _count - index);
        _entries[_count] = default;
    }

    private void NoteImpulseMagnitude(float magnitude)
    {
        if (!Single.IsFinite(magnitude)) return;
        ImpulseSamples++;
        ImpulseMagnitude += magnitude;
        ImpulseMagnitudeMax = Math.Max(ImpulseMagnitudeMax, magnitude);
    }

    private static bool SupportedWeapon(byte weapon) => weapon is
        (byte)BeamType.Missile or (byte)BeamType.Battlehammer or (byte)BeamType.Magmaul;
    private static bool Finite(Vector3 value) => Single.IsFinite(value.X)
        && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);

    private struct Entry
    {
        public PredictedImpulseIdentity Identity;
        public long PredictedFrame;
        public Vector3 Impulse;
    }

    private struct BombEntry
    {
        public bool Used;
        public PredictedImpulseIdentity Identity;
        public long Frame;
    }

    private struct RetiredEntry
    {
        public bool Used;
        public PredictedImpulseIdentity Identity;
        public long Frame;
    }
}
