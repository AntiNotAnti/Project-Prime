using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Render;

internal sealed partial class HeadHitTrack
{
    internal void WriteSeed(BinaryWriter writer, ReplayHeadSeedCodec codec)
    {
        codec.Victim(writer, Victim); writer.Write(_age); writer.Write(_resolved); writer.Write(_step);
        writer.Write(_stepped); writer.Write(_active); ReplayHeadSeedCodec.Vector(writer, _direction);
        foreach (var seen in _seen) { ReplayHeadSeedCodec.Event(writer, seen.Event); writer.Write(seen.At); }
    }
    internal static HeadHitTrack ReadSeed(BinaryReader reader, ReplayHeadSeedCodec codec, int slot)
    {
        var value = new HeadHitTrack { Victim = codec.Victim(reader, slot), _age = reader.ReadInt32(),
            _resolved = codec.Past(reader), _step = codec.Past(reader), _stepped = ReplayHeadSeedCodec.Boolean(reader),
            _active = ReplayHeadSeedCodec.Boolean(reader), _direction = ReplayHeadSeedCodec.Vector(reader, true) };
        if (value._age is < 0 or > Duration || value._active && (!value._stepped || value._age == Duration || !value.Victim.IsValid))
            throw new InvalidDataException("Invalid head-hit track state.");
        var events = new HashSet<HeadHitEvent>();
        for (int i = 0; i < Capacity; i++)
        {
            var key = codec.Event(reader, value.Victim); uint at = codec.Past(reader);
            if (key.IsValid ? !events.Add(key) : at != 0) throw new InvalidDataException("Invalid head-hit deduplication ledger.");
            value._seen[i] = new() { Event = key, At = at };
        }
        return value;
    }
}

internal sealed partial class HeadDeathTrack
{
    internal void WriteSeed(BinaryWriter writer, ReplayHeadSeedCodec codec)
    {
        codec.Victim(writer, Victim); writer.Write(HasCause); writer.Write(BeganDead); ReplayHeadSeedCodec.Event(writer, Event);
        writer.Write(_allowed); writer.Write(_active); writer.Write(_offeredAt); writer.Write(_initialAge); writer.Write(_age);
        writer.Write(_side); ReplayHeadSeedCodec.Vector(writer, _direction);
    }
    internal static HeadDeathTrack ReadSeed(BinaryReader reader, ReplayHeadSeedCodec codec, int slot)
    {
        var value = new HeadDeathTrack { Victim = codec.Victim(reader, slot), HasCause = ReplayHeadSeedCodec.Boolean(reader),
            BeganDead = ReplayHeadSeedCodec.Boolean(reader) };
        value.Event = codec.Event(reader, value.Victim);
        value._allowed = ReplayHeadSeedCodec.Boolean(reader); value._active = ReplayHeadSeedCodec.Boolean(reader);
        value._offeredAt = codec.Past(reader); value._initialAge = reader.ReadUInt32(); value._age = reader.ReadUInt32();
        value._side = reader.ReadInt32(); value._direction = ReplayHeadSeedCodec.Vector(reader, true);
        if (value.HasCause != value.Event.IsValid || value.BeganDead && !value.HasCause
            || value._active && (!value._allowed || !value.BeganDead || !value.HasCause)
            || value._initialAge > MaximumDuration || value._age > MaximumDuration || value._side is < -1 or > 1)
            throw new InvalidDataException("Invalid head-death track state.");
        return value;
    }
}
