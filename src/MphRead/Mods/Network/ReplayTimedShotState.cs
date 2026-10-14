using System;
using System.IO;

namespace MphRead.Mods.Network;

// Detached, bounded facts. Capability records presence of this producer schema;
// missing/unsupported/capacity-refused launches remain unavailable evidence.
internal sealed class ReplayTimedShotState
{
    internal const int Slots = 8, PerSlot = 256, MaximumFacts = Slots * PerSlot;
    internal const uint Retention = 1024;
    private ReplayTimedShotFact?[,]? _cells;
    internal bool Enabled { get; private set; }
    internal ulong Epoch { get; private set; }
    internal ushort Match { get; private set; }
    internal uint Room { get; private set; }
    internal TimedGrantProofMode CaptureMode { get; private set; }
    internal uint AuthorityFrame { get; private set; }
    private bool _hasClock;
    internal int Count { get; private set; }
    internal bool Begin(ulong epoch, ushort match, uint room, TimedGrantProofMode mode)
    {
        if (epoch == 0 || match == 0 || mode is not (TimedGrantProofMode.Shadow or TimedGrantProofMode.Enforce)) return false;
        if (Enabled && Epoch == epoch && Match == match && Room == room) return true;
        Reset(); _cells = new ReplayTimedShotFact?[Slots, PerSlot];
        Enabled = true; Epoch = epoch; Match = match; Room = room; CaptureMode = mode; return true;
    }
    internal void Reset()
    {
        _cells = null; Enabled = false; Epoch = 0; Match = 0; Room = 0;
        CaptureMode = default; AuthorityFrame = 0; _hasClock = false; Count = 0;
    }
    private static bool Retained(uint now, uint at) => unchecked(now - at) <= Retention;
    internal void AdvanceAuthority(uint frame)
    {
        if (!Enabled || _hasClock && !NetLifecycleTracker.Newer(frame, AuthorityFrame)) return;
        AuthorityFrame = frame; _hasClock = true;
        for (int slot = 0; slot < Slots; slot++) for (int index = 0; index < PerSlot; index++)
            if (_cells![slot, index] is { } value && !Retained(frame, value.AuthorityFrame))
            { _cells![slot, index] = null; Count--; }
    }
    internal TimedShotRetention Remember(in ReplayTimedShotFact value, uint authorityNow)
    {
        var key = value.Key;
        if (!Enabled || !value.IsValid || key.AuthorityEpoch != Epoch || key.MatchId != Match
            || value.Room != Room || !Retained(authorityNow, value.AuthorityFrame)) return TimedShotRetention.Invalid;
        int free = -1;
        for (int index = 0; index < PerSlot; index++)
        {
            var cell = _cells![key.ShooterSlot, index];
            if (cell is { } prior && Retained(authorityNow, prior.AuthorityFrame) && prior.Key == key)
                return prior.Fire == value.Fire ? TimedShotRetention.Duplicate : TimedShotRetention.Alias;
            if (free < 0 && (cell is not { } retained || !Retained(authorityNow, retained.AuthorityFrame)
                || retained.Key.Generation != key.Generation)) free = index;
        }
        if (free < 0) return TimedShotRetention.Capacity;
        if (!_cells![key.ShooterSlot, free].HasValue) Count++;
        _cells![key.ShooterSlot, free] = value;
        // An out-of-order carrier cannot replace the detached authority clock.
        if (!_hasClock || NetLifecycleTracker.Newer(authorityNow, AuthorityFrame))
        { AuthorityFrame = authorityNow; _hasClock = true; }
        return TimedShotRetention.Stored;
    }
    internal bool TryGet(ShotKey key, uint sourceFrame, uint room, uint authorityNow, out ReplayTimedShotFact value)
    {
        value = default;
        if (!Enabled || (uint)key.ShooterSlot >= Slots || key.AuthorityEpoch != Epoch || key.MatchId != Match || room != Room) return false;
        for (int index = 0; index < PerSlot; index++)
            if (_cells![key.ShooterSlot, index] is { } retained && retained.Key == key && retained.Room == room
                && retained.Fire.SourceFrame == sourceFrame && Retained(authorityNow, retained.AuthorityFrame))
            { value = retained; return true; }
        return false;
    }
    internal void Visit(Action<ReplayTimedShotFact> accept)
    {
        if (!Enabled) return;
        for (int slot = 0; slot < Slots; slot++) for (int index = 0; index < PerSlot; index++)
            if (_cells![slot, index] is { } value && (!_hasClock || Retained(AuthorityFrame, value.AuthorityFrame))) accept(value);
    }
    internal void Write(BinaryWriter writer)
    {
        if (!Enabled) throw new InvalidDataException("No timed shot recording capability.");
        writer.Write(Epoch); writer.Write(Match); writer.Write(Room); writer.Write((byte)CaptureMode);
        writer.Write(_hasClock); writer.Write(AuthorityFrame);
        int count = 0; Visit(_ => count++); writer.Write(count);
        Visit(value => writer.Write(value.Packet()));
    }
    internal static ReplayTimedShotState Read(BinaryReader reader)
    {
        var state = new ReplayTimedShotState();
        ulong epoch = reader.ReadUInt64(); ushort match = reader.ReadUInt16(); uint room = reader.ReadUInt32();
        var mode = (TimedGrantProofMode)reader.ReadByte(); byte clock = reader.ReadByte(); uint now = reader.ReadUInt32();
        int count = reader.ReadInt32();
        if (!state.Begin(epoch, match, room, mode) || clock > 1 || count is < 0 or > MaximumFacts || clock == 0 && (count != 0 || now != 0))
            throw new InvalidDataException("Invalid timed shot checkpoint scope.");
        for (int i = 0; i < count; i++)
        {
            byte[] bytes = reader.ReadBytes(ReplayTimedShotFact.Size);
            if (!ReplayTimedShotFact.TryRead(bytes, out var value) || state.Remember(value, now) != TimedShotRetention.Stored)
                throw new InvalidDataException("Invalid or aliased timed shot checkpoint fact.");
        }
        state._hasClock = clock == 1; state.AuthorityFrame = now; return state;
    }
}
