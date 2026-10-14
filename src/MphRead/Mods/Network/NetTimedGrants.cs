using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

internal enum TimedPowerUp : byte { DoubleDamage, Cloak, DeathAlt }
internal enum TimedGrantEnd : byte { Active, Expired, Renewed, Death, Retired, Room, Session }
internal enum TimedGrantResult : byte { Granted, Taken, Gone, WrongLife, Capacity }
internal enum TimedGrantKnowledge : byte { Unavailable, NotGranted, Granted }
internal readonly record struct PowerUpSpawnKey(ulong Epoch, ushort Match, uint Room, int SpawnId, uint Generation);
internal readonly record struct TimedGrantAnswer(TimedGrantKnowledge Knowledge, ulong Sequence = 0);
internal sealed record TimedGrantRecord(ulong Sequence, PowerUpSpawnKey Spawn, ShotKey Life,
    TimedPowerUp Type, uint ContactFrame, uint? SourceContactFrame, uint ExpiresAtFrame)
{
    // Contact runs after input. All three native 1800-tick effects can authorize
    // subsequent input from contact+1 through contact+1799, never contact itself.
    internal uint EffectiveFrame => unchecked(ContactFrame + 1);
    internal uint? SourceEffectiveFrame => SourceContactFrame is uint source ? unchecked(source + 1) : null;
    internal uint EndFrame { get; set; } = ExpiresAtFrame;
    internal TimedGrantEnd EndReason { get; set; }
}

/// <summary>Pure, authority-owned reservations and timed history. Source-frame answers
/// require an actual authority input observation; numerical source timestamps alone
/// cannot extend a grant past its independent authority expiry. This is ownership
/// evidence, not proof that an arbitrary claimed shot was actually authored.</summary>
internal sealed class NetTimedGrants
{
    internal const uint Duration = 1800, Retention = 1024;
    internal const int SpawnCapacity = 128, GrantCapacity = 512, ObservationCapacity = 16384;
    private readonly record struct Spawn(TimedPowerUp Type, bool Taken);
    private readonly record struct ObservationKey(ShotKey Life, uint Source);
    private readonly record struct Observation(uint Authority, TimedGrantAnswer Double,
        TimedGrantAnswer Cloak, TimedGrantAnswer DeathAlt);
    private struct Clock { internal bool Set; internal uint Source, Authority; }
    private readonly Dictionary<PowerUpSpawnKey, Spawn> _spawns = new();
    private readonly Dictionary<ulong, TimedGrantRecord> _history = new();
    private readonly Dictionary<ObservationKey, Observation> _observations = new();
    private readonly Queue<(ObservationKey Key, uint Authority)> _observationOrder = new();
    private readonly ShotKey[] _lives = new ShotKey[8];
    private readonly Clock[] _clocks = new Clock[8];
    private readonly ulong[,] _active = new ulong[8, 3];
    private readonly List<ulong> _expired = new();
    private readonly ulong _epoch;
    private readonly ushort _match;
    private readonly uint _room;
    private readonly Action<TimedGrantRecord>? _changed;
    private uint _spawnSequence;
    private ulong _grantSequence;
    private bool _advanced;
    private uint _advancedFrame;
    internal long CapacityRefused { get; private set; }
    internal int SpawnCount => _spawns.Count;
    internal int HistoryCount => _history.Count;
    internal int ObservationCount => _observations.Count;
    internal IEnumerable<TimedGrantRecord> History => _history.Values;

    internal NetTimedGrants(ulong epoch, ushort match, uint room, Action<TimedGrantRecord>? changed = null)
    { _epoch = epoch; _match = match; _room = room; _changed = changed; }
    private static bool Before(uint a, uint b) => unchecked((int)(a - b)) < 0;
    private static bool Old(uint now, uint frame) => unchecked((int)(now - frame)) > Retention;
    private bool ValidLife(ShotKey life) => life.AuthorityEpoch == _epoch && life.MatchId == _match
        && (uint)life.ShooterSlot < 8 && life.Generation != 0 && life.LifeId != 0 && life.ShotId == 0;

    internal bool RegisterSpawn(int spawnId, TimedPowerUp type, out PowerUpSpawnKey key)
    {
        key = default;
        if ((uint)type >= 3 || _spawns.Count == SpawnCapacity || _spawnSequence == uint.MaxValue)
        { CapacityRefused++; return false; }
        key = new(_epoch, _match, _room, spawnId, ++_spawnSequence);
        _spawns.Add(key, new(type, false));
        return true;
    }
    internal void RemoveSpawn(PowerUpSpawnKey key) => _spawns.Remove(key);

    internal void BeginLife(ShotKey life, uint authorityFrame)
    {
        if (!ValidLife(life)) return;
        if (_lives[life.ShooterSlot] == life) return;
        RetireSlot(life.ShooterSlot, authorityFrame, TimedGrantEnd.Retired);
        _lives[life.ShooterSlot] = life;
        _clocks[life.ShooterSlot] = default;
    }

    internal TimedGrantResult TryGrant(PowerUpSpawnKey spawn, ShotKey life, uint authorityFrame,
        uint? sourceContactFrame, out TimedGrantRecord? grant)
    {
        grant = null;
        Advance(authorityFrame);
        if (!ValidLife(life) || _lives[life.ShooterSlot] != life) return TimedGrantResult.WrongLife;
        if (!_spawns.TryGetValue(spawn, out var item)) return TimedGrantResult.Gone;
        if (item.Taken) return TimedGrantResult.Taken;
        if (_history.Count == GrantCapacity || _grantSequence == ulong.MaxValue)
        { CapacityRefused++; return TimedGrantResult.Capacity; }
        // Reserve before any caller can invoke native callbacks. Failed capacity
        // checks leave the item available, and a second contender cannot renew it.
        _spawns[spawn] = item with { Taken = true };
        int slot = life.ShooterSlot, type = (int)item.Type;
        Close(_active[slot, type], unchecked(authorityFrame + 1), TimedGrantEnd.Renewed);
        grant = new(++_grantSequence, spawn, life, item.Type, authorityFrame,
            sourceContactFrame, unchecked(authorityFrame + Duration));
        _history.Add(grant.Sequence, grant);
        _active[slot, type] = grant.Sequence;
        _changed?.Invoke(grant);
        return TimedGrantResult.Granted;
    }

    internal void RetireSlot(int slot, uint authorityFrame, TimedGrantEnd reason)
    {
        if ((uint)slot >= 8) return;
        Advance(authorityFrame);
        for (int type = 0; type < 3; type++)
        {
            Close(_active[slot, type], authorityFrame, reason);
            _active[slot, type] = 0;
        }
        _lives[slot] = default;
        _clocks[slot] = default;
        // Retain pre-retirement observations for delayed, independently admitted shots.
    }

    private void Close(ulong sequence, uint frame, TimedGrantEnd reason)
    {
        if (sequence != 0 && _history.TryGetValue(sequence, out var record)
            && record.EndReason == TimedGrantEnd.Active)
        { record.EndFrame = frame; record.EndReason = reason; _changed?.Invoke(record); }
    }

    internal void CloseAll(uint authorityFrame, TimedGrantEnd reason)
    {
        for (int slot = 0; slot < 8; slot++) RetireSlot(slot, authorityFrame, reason);
    }

    internal void Advance(uint authorityFrame)
    {
        if (_advanced && _advancedFrame == authorityFrame) return;
        _advanced = true; _advancedFrame = authorityFrame;
        for (int slot = 0; slot < 8; slot++)
        for (int type = 0; type < 3; type++)
        {
            ulong sequence = _active[slot, type];
            if (sequence != 0 && _history.TryGetValue(sequence, out var record)
                && !Before(authorityFrame, record.ExpiresAtFrame))
            {
                Close(sequence, record.ExpiresAtFrame, TimedGrantEnd.Expired);
                _active[slot, type] = 0;
            }
        }
        while (_observationOrder.TryPeek(out var first) && Old(authorityFrame, first.Authority))
        { _observationOrder.Dequeue(); _observations.Remove(first.Key); }
        _expired.Clear();
        foreach (var pair in _history)
            if (pair.Value.EndReason != TimedGrantEnd.Active && Old(authorityFrame, pair.Value.EndFrame))
                _expired.Add(pair.Key);
        foreach (ulong sequence in _expired) _history.Remove(sequence);
    }

    internal void ObserveInput(ShotKey life, uint sourceFrame, uint authorityFrame)
    {
        Advance(authorityFrame);
        if (!ValidLife(life) || _lives[life.ShooterSlot] != life) return;
        var key = new ObservationKey(life, sourceFrame);
        if (_observations.ContainsKey(key)) return; // first application is immutable
        if (_observations.Count == ObservationCapacity) { CapacityRefused++; return; }
        ref Clock clock = ref _clocks[life.ShooterSlot];
        if (!clock.Set) clock = new() { Set = true, Source = sourceFrame, Authority = authorityFrame };
        long sourceElapsed = unchecked((int)(sourceFrame - clock.Source));
        long authorityElapsed = unchecked((int)(authorityFrame - clock.Authority));
        bool plausible = sourceElapsed <= authorityElapsed + 32
            && sourceElapsed >= authorityElapsed - Retention;
        TimedGrantAnswer Answer(TimedPowerUp type)
        {
            if (!plausible) return new(TimedGrantKnowledge.Unavailable);
            ulong sequence = _active[life.ShooterSlot, (int)type];
            if (sequence == 0) return new(TimedGrantKnowledge.NotGranted);
            var grant = _history[sequence];
            if (Before(authorityFrame, grant.EffectiveFrame)) return new(TimedGrantKnowledge.NotGranted);
            if (grant.SourceEffectiveFrame is not uint start) return new(TimedGrantKnowledge.Unavailable);
            return Before(sourceFrame, start) ? new(TimedGrantKnowledge.NotGranted)
                : new(TimedGrantKnowledge.Granted, sequence);
        }
        _observations.Add(key, new(authorityFrame, Answer(TimedPowerUp.DoubleDamage),
            Answer(TimedPowerUp.Cloak), Answer(TimedPowerUp.DeathAlt)));
        _observationOrder.Enqueue((key, authorityFrame));
    }

    internal TimedGrantAnswer GrantedAt(ShotKey life, uint shotSourceFrame, TimedPowerUp type, uint now)
    {
        Advance(now);
        life = life with { ShotId = 0 };
        if (!ValidLife(life) || !_observations.TryGetValue(new(life, shotSourceFrame), out var sample))
            return new(TimedGrantKnowledge.Unavailable);
        return type switch
        {
            TimedPowerUp.DoubleDamage => sample.Double,
            TimedPowerUp.Cloak => sample.Cloak,
            TimedPowerUp.DeathAlt => sample.DeathAlt,
            _ => new(TimedGrantKnowledge.Unavailable)
        };
    }
}
