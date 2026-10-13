using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Network;

internal enum TimedEffectTransition { None, Start, Keep, Retime, Resume, End }
internal readonly record struct TimedEffectView(bool Covered, ushort Remaining, ushort ConfirmedRemaining,
    ulong GrantSequence, uint PredictionSequence, bool Resume = false)
{
    internal bool Active => Remaining > 0;
    internal TimedEffectTransition TransitionFrom(TimedEffectView previous, bool quiet)
        => !Active ? previous.Active ? TimedEffectTransition.End : TimedEffectTransition.None
        : !previous.Active ? quiet ? TimedEffectTransition.Resume : TimedEffectTransition.Start
        : GrantSequence == previous.GrantSequence && PredictionSequence == previous.PredictionSequence
            && Math.Abs(Remaining - previous.Remaining) <= 1 ? TimedEffectTransition.Keep : TimedEffectTransition.Retime;
}

/// <summary>Bounded value-only presentation, shared by a live client and each private replay.
/// Predictions overlay truth; removing one cannot undo another grant. This is never
/// authority ownership evidence or a source-time damage proof.</summary>
internal sealed class TimedGrantPresentation
{
    internal const int PredictionCapacity = 16;
    internal const uint PredictionDeadline = 900;
    private readonly TimedGrantFact?[,] _facts = new TimedGrantFact?[8, 3];
    private readonly PickupVerdict?[,] _acks = new PickupVerdict?[8, 3];
    private readonly bool[,] _resumeCoverage = new bool[8, 3];
    private readonly Dictionary<PickupClaimKey, (PickupClaim Claim, uint Frame)> _pending = new();
    private readonly Dictionary<PowerUpSpawnKey, uint> _consumed = new();
    private ulong _epoch;
    private ushort _match;
    private uint _room;
    // Discovery can fence an older room before any A3 fact arrives. Keep that
    // watermark separate from coverage so historical item facts invent no grant.
    private ulong _fenceEpoch;
    private ushort _fenceMatch;
    private uint _roomFence;
    private bool _clock;
    private uint _authorityAnchor, _localAnchor;
    internal int PendingCount => _pending.Count;
    internal IEnumerable<TimedGrantFact> Facts
    { get { foreach (var fact in _facts) if (fact.HasValue) yield return fact.Value; } }
    internal bool HasCoverage => _room != 0;
    internal uint RoomIdentity => _roomFence;
    private static bool Before(uint a, uint b) => unchecked((int)(a - b)) < 0;
    private static bool OlderLife(ShotKey candidate, ShotKey known)
        => candidate.Generation != known.Generation ? !NetLifecycleTracker.Newer(candidate.Generation, known.Generation)
            : candidate.LifeId != known.LifeId && !NetLifecycleTracker.Newer(candidate.LifeId, known.LifeId);
    internal void Rebase(uint from, uint to)
    {
        for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
            _resumeCoverage[slot, type] = _facts[slot, type].HasValue || _acks[slot, type].HasValue;
        uint delta = unchecked(to - from);
        _localAnchor = unchecked(_localAnchor + delta);
        foreach (var key in new List<PickupClaimKey>(_pending.Keys))
        {
            var pending = _pending[key];
            _resumeCoverage[pending.Claim.Key.Slot, (int)pending.Claim.Type] = true;
            _pending[key] = (pending.Claim, unchecked(pending.Frame + delta));
        }
    }
    internal void Reset()
    {
        Array.Clear(_facts); Array.Clear(_acks); Array.Clear(_resumeCoverage); _pending.Clear(); _consumed.Clear();
        _epoch = 0; _match = 0; _room = 0; _clock = false; _authorityAnchor = _localAnchor = 0;
        _fenceEpoch = 0; _fenceMatch = 0; _roomFence = 0;
    }
    internal bool ObserveRoom(ulong epoch, ushort match, uint room)
    {
        if (epoch == 0 || match == 0 || room == 0) return false;
        if (_roomFence != 0 && (_fenceEpoch != epoch || _fenceMatch != match || room < _roomFence)) return false;
        if (room > _roomFence)
        { Reset(); _fenceEpoch = epoch; _fenceMatch = match; _roomFence = room; }
        return true;
    }
    private bool Scope(ulong epoch, ushort match, uint room)
    {
        if (!ObserveRoom(epoch, match, room)) return false;
        _epoch = epoch; _match = match; _room = room;
        return true;
    }
    internal uint AuthorityNow(uint localFrame) => _clock
        ? unchecked(_authorityAnchor + (uint)Math.Max(0, unchecked((int)(localFrame - _localAnchor)))) : localFrame;
    internal void ObserveClock(uint authority, uint localFrame)
    {
        if (!_clock || !Before(authority, AuthorityNow(localFrame)))
        { _clock = true; _authorityAnchor = authority; _localAnchor = localFrame; }
    }
    internal bool Accept(TimedGrantFact fact, uint localFrame)
    {
        if (!fact.IsValid || !Scope(fact.Life.AuthorityEpoch, fact.Life.MatchId, fact.Room)) return false;
        int slot = fact.Life.ShooterSlot, type = (int)fact.Type;
        if (_facts[slot, type] is { } prior)
        {
            if (OlderLife(fact.Life, prior.Life)) return false;
            if (fact.Revision < prior.Revision) return false;
            if (fact.Revision == prior.Revision)
            { if (fact != prior) throw new InvalidDataException("Conflicting timed grant revision."); return false; }
            if (fact.Life == prior.Life && fact.Sequence < prior.Sequence)
                throw new InvalidDataException("Timed grant sequence moved backward.");
        }
        if (_facts[slot, type] is not { } previous || previous.Life != fact.Life || previous.Sequence != fact.Sequence)
            _resumeCoverage[slot, type] = false;
        _facts[slot, type] = fact;
        if (fact.Sequence != 0) Consume(fact.Spawn, fact.ContactFrame, fact.AuthorityFrame);
        ObserveClock(fact.AuthorityFrame, localFrame);
        // A matching delta can confirm before the terminal response arrives.
        // Delivery still retains its independent pending claim obligation.
        var remove = new List<PickupClaimKey>();
        foreach (var pair in _pending)
            if (pair.Value.Claim.Key.LifeKey == fact.Life && pair.Value.Claim.Type == fact.Type
                && fact.Sequence != 0 && pair.Value.Claim.Spawn == fact.Spawn) remove.Add(pair.Key);
        foreach (var key in remove) _pending.Remove(key);
        return true;
    }
    internal bool Predict(PickupClaim claim, uint localFrame)
    {
        if (!claim.IsValid || !Scope(claim.Key.Epoch, claim.Key.Match, claim.Spawn.Room)) return false;
        int slot = claim.Key.Slot, type = (int)claim.Type;
        if (_facts[slot, type] is { } fact && (OlderLife(claim.Key.LifeKey, fact.Life)
            || fact.Life == claim.Key.LifeKey && fact.Sequence != 0 && fact.Spawn == claim.Spawn)) return false;
        if (_acks[slot, type] is { } ack && (OlderLife(claim.Key.LifeKey, ack.Claim.Key.LifeKey)
            || ack.Claim.Key.LifeKey == claim.Key.LifeKey && ack.Claim.Spawn == claim.Spawn)) return false;
        if (_pending.TryGetValue(claim.Key, out var old))
        { if (old.Claim != claim) throw new InvalidDataException("Conflicting pickup prediction."); return false; }
        if (_pending.Count >= PredictionCapacity) return false;
        _resumeCoverage[slot, type] = false;
        _pending.Add(claim.Key, (claim, localFrame)); return true;
    }
    internal bool Settle(PickupVerdict verdict, uint localFrame)
    {
        if (!verdict.IsValid || !Scope(verdict.Claim.Key.Epoch, verdict.Claim.Key.Match, verdict.Claim.Spawn.Room)) return false;
        if (_pending.TryGetValue(verdict.Claim.Key, out var pending) && pending.Claim != verdict.Claim)
            throw new InvalidDataException("Pickup terminal does not match prediction.");
        bool changed = _pending.Remove(verdict.Claim.Key);
        if (verdict.Result == PickupResult.Grant)
        {
            Consume(verdict.Claim.Spawn, verdict.GrantFrame, verdict.DecisionFrame);
            int slot = verdict.Claim.Key.Slot, type = (int)verdict.Claim.Type;
            var old = _acks[slot, type];
            if (_facts[slot, type] is { } fact && OlderLife(verdict.Claim.Key.LifeKey, fact.Life)
                || old is { } known && OlderLife(verdict.Claim.Key.LifeKey, known.Claim.Key.LifeKey)) return changed;
            if (old == null || old.Value.Claim.Key.LifeKey != verdict.Claim.Key.LifeKey
                || old.Value.GrantSequence < verdict.GrantSequence)
            {
                if (old is { } priorAck && (priorAck.Claim.Key.LifeKey != verdict.Claim.Key.LifeKey
                    || priorAck.GrantSequence < verdict.GrantSequence)
                    || _facts[slot, type] is { } priorFact && (priorFact.Life != verdict.Claim.Key.LifeKey
                        || priorFact.Sequence < verdict.GrantSequence)) _resumeCoverage[slot, type] = false;
                _acks[slot, type] = verdict; changed = true;
            }
            ObserveClock(verdict.DecisionFrame, localFrame);
        }
        return changed;
    }
    internal void Advance(uint localFrame, Func<ShotKey, bool>? alive = null)
    {
        var remove = new List<PickupClaimKey>();
        foreach (var pair in _pending)
            if (unchecked((uint)(localFrame - pair.Value.Frame)) >= PredictionDeadline
                || alive != null && !alive(pair.Key.LifeKey)) remove.Add(pair.Key);
        foreach (var key in remove) _pending.Remove(key);
    }
    private static ushort Remaining(uint end, uint now)
        => (ushort)Math.Clamp(unchecked((int)(end - now)), 0, (int)NetTimedGrants.Duration);
    internal TimedEffectView View(ShotKey life, TimedPowerUp type, uint localFrame)
    {
        if ((uint)life.ShooterSlot >= 8 || (uint)type >= 3 || life.AuthorityEpoch != _epoch || life.MatchId != _match)
            return default;
        var fact = _facts[life.ShooterSlot, (int)type];
        var ack = _acks[life.ShooterSlot, (int)type];
        if (fact is { } latestFact && OlderLife(life, latestFact.Life)
            || ack is { } latestAck && OlderLife(life, latestAck.Claim.Key.LifeKey)) return default;
        bool covered = fact is { } f && f.Life == life;
        ulong sequence = covered ? fact!.Value.Sequence : 0;
        uint now = AuthorityNow(localFrame);
        ushort remaining = covered && (fact!.Value.Reason == TimedGrantEnd.Active
            || fact.Value.Reason == TimedGrantEnd.Renewed && Before(now, fact.Value.EndFrame))
            ? Remaining(unchecked(fact.Value.ContactFrame + NetTimedGrants.Duration), now) : (ushort)0;
        ushort confirmed = remaining > 0 && unchecked((int)(now - fact!.Value.ContactFrame)) > 0 ? remaining : (ushort)0;
        if (ack is { } a && a.Claim.Key.LifeKey == life && (!covered || a.GrantSequence > sequence))
        { covered = true; sequence = a.GrantSequence; remaining = Remaining(a.ExpiresAtFrame, now);
            confirmed = unchecked((int)(now - a.GrantFrame)) > 0 ? remaining : (ushort)0; }
        uint predicted = 0;
        uint newest = 0;
        ushort predictedRemaining = 0;
        foreach (var entry in _pending.Values)
        {
            if (entry.Claim.Key.LifeKey != life || entry.Claim.Type != type
                || unchecked((uint)(localFrame - entry.Frame)) >= PredictionDeadline) continue;
            if (predicted == 0 || !Before(entry.Frame, newest))
            { newest = entry.Frame; predicted = entry.Claim.Key.Sequence;
                predictedRemaining = Remaining(unchecked(entry.Frame + NetTimedGrants.Duration), localFrame); }
        }
        remaining = Math.Max(remaining, predictedRemaining);
        return new(covered || predicted != 0, remaining, confirmed, sequence, predicted,
            _resumeCoverage[life.ShooterSlot, (int)type] || predicted == 0 && fact is { Resume: true });
    }
    private void Consume(PowerUpSpawnKey spawn, uint contact, uint now)
    {
        var old = new List<PowerUpSpawnKey>();
        foreach (var pair in _consumed)
            if (unchecked((int)(now - pair.Value)) > NetTimedGrants.Retention) old.Add(pair.Key);
        foreach (var key in old) _consumed.Remove(key);
        if (unchecked((int)(now - contact)) > NetTimedGrants.Retention) return;
        if (!_consumed.ContainsKey(spawn) && _consumed.Count >= NetTimedGrants.GrantCapacity)
            throw new InvalidDataException("Timed pickup consumption history exceeded its bound.");
        _consumed[spawn] = contact;
    }
    internal bool Hides(PowerUpSpawnKey spawn, uint localFrame)
    {
        if (_consumed.ContainsKey(spawn)) return true;
        foreach (var fact in _facts) if (fact is { Sequence: > 0 } f && f.Spawn == spawn) return true;
        foreach (var ack in _acks) if (ack is { } a && a.Claim.Spawn == spawn) return true;
        foreach (var entry in _pending.Values)
            if (entry.Claim.Spawn == spawn && unchecked((uint)(localFrame - entry.Frame)) < PredictionDeadline) return true;
        return false;
    }
    internal void Write(BinaryWriter writer)
    {
        writer.Write(_epoch); writer.Write(_match); writer.Write(_room); writer.Write(_clock);
        writer.Write(_authorityAnchor); writer.Write(_localAnchor);
        Span<byte> bytes = stackalloc byte[PickupVerdict.Size];
        for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
        {
            writer.Write(_facts[slot, type].HasValue);
            if (_facts[slot, type] is { } fact) { fact.Write(bytes[..TimedGrantFact.Size]); writer.Write(bytes[..TimedGrantFact.Size]); }
            writer.Write(_acks[slot, type].HasValue);
            if (_acks[slot, type] is { } ack) { ack.Write(bytes); writer.Write(bytes); }
        }
        writer.Write(_pending.Count);
        foreach (var pending in _pending.Values)
        { pending.Claim.Write(bytes[..PickupClaim.Size]); writer.Write(bytes[..PickupClaim.Size]); writer.Write(pending.Frame); }
        writer.Write(_consumed.Count);
        foreach (var pair in _consumed)
        {
            writer.Write(pair.Key.SpawnId); writer.Write(pair.Key.Generation); writer.Write(pair.Value);
        }
        uint resumeMask = 0;
        for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
            if (_resumeCoverage[slot, type]) resumeMask |= 1u << (slot * 3 + type);
        writer.Write(resumeMask);
    }
    internal static TimedGrantPresentation Read(BinaryReader reader)
    {
        bool Flag()
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("Invalid grant checkpoint boolean.");
            return value == 1;
        }
        var state = new TimedGrantPresentation();
        ulong epoch = reader.ReadUInt64(); ushort match = reader.ReadUInt16(); uint room = reader.ReadUInt32();
        bool clock = Flag(); uint authority = reader.ReadUInt32(), local = reader.ReadUInt32();
        if (room != 0 && !state.Scope(epoch, match, room)) throw new InvalidDataException("Invalid grant scope.");
        for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
        {
            if (Flag())
            {
                if (!TimedGrantFact.TryRead(reader.ReadBytes(TimedGrantFact.Size), out var fact)
                    || fact.Life.ShooterSlot != slot || (int)fact.Type != type || fact.Room != room
                    || fact.Life.AuthorityEpoch != epoch || fact.Life.MatchId != match || !state.Accept(fact, local))
                    throw new InvalidDataException("Invalid grant checkpoint.");
            }
            if (Flag())
            {
                if (!PickupVerdict.TryRead(reader.ReadBytes(PickupVerdict.Size), out var ack)
                    || ack.Result != PickupResult.Grant || ack.Claim.Key.Slot != slot || (int)ack.Claim.Type != type
                    || ack.Claim.Spawn.Room != room || ack.Claim.Key.Epoch != epoch || ack.Claim.Key.Match != match)
                    throw new InvalidDataException("Invalid grant acknowledgment checkpoint.");
                state._acks[slot, type] = ack;
            }
        }
        int count = reader.ReadInt32();
        if (count < 0 || count > PredictionCapacity) throw new InvalidDataException("Invalid prediction count.");
        for (int i = 0; i < count; i++)
        {
            if (!PickupClaim.TryRead(reader.ReadBytes(PickupClaim.Size), out var claim)) throw new InvalidDataException("Invalid prediction.");
            uint frame = reader.ReadUInt32();
            if (claim.Spawn.Room != room || claim.Key.Epoch != epoch || claim.Key.Match != match
                || !state.Predict(claim, frame)) throw new InvalidDataException("Invalid prediction scope.");
        }
        int consumed = reader.ReadInt32();
        if (consumed < 0 || consumed > NetTimedGrants.GrantCapacity) throw new InvalidDataException("Invalid consumed item count.");
        state._consumed.Clear();
        for (int i = 0; i < consumed; i++)
        {
            int spawn = reader.ReadInt32(); uint generation = reader.ReadUInt32(), contact = reader.ReadUInt32();
            if (room == 0 || spawn < -1 || generation == 0 || !state._consumed.TryAdd(new(epoch, match, room, spawn, generation), contact))
                throw new InvalidDataException("Invalid consumed item identity.");
        }
        uint resumeMask = reader.ReadUInt32();
        if ((resumeMask & 0xff000000u) != 0) throw new InvalidDataException("Invalid grant resume mask.");
        for (int slot = 0; slot < 8; slot++) for (int type = 0; type < 3; type++)
        {
            bool resume = (resumeMask & (1u << (slot * 3 + type))) != 0;
            bool pendingOwner = false;
            foreach (var pending in state._pending.Values)
                if (pending.Claim.Key.Slot == slot && (int)pending.Claim.Type == type) { pendingOwner = true; break; }
            if (resume && !state._facts[slot, type].HasValue && !state._acks[slot, type].HasValue && !pendingOwner)
                throw new InvalidDataException("Unowned grant resume marker.");
            state._resumeCoverage[slot, type] = resume;
        }
        if (room == 0 && (epoch != 0 || match != 0 || clock || authority != 0 || local != 0))
            throw new InvalidDataException("Nonempty absent grant scope.");
        state._clock = clock; state._authorityAnchor = authority; state._localAnchor = local;
        return state;
    }
}
