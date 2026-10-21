using System;
using System.IO;
using MphRead.Formats;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Mods.Replay;

namespace MphRead;

public partial class Scene
{
    private sealed record HeadHistorySeed(uint Origin, HeadHitSlot?[]? Hits,
        HeadDeathSlot?[]? Deaths, PendingHeadHit[]? Pending);

    internal byte[] CaptureReplayHeadHistory(uint origin, uint frame)
    {
        if (Services is not ReplaySceneServices replay || !replay.Session.IsCustomInspection || _localHeadHitSerial != 0)
            throw new InvalidOperationException("Head history requires a private custom replica.");
        var codec = new ReplayHeadSeedCodec(replay.State, frame, this);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("HPS1"u8); writer.Write((ushort)1); writer.Write((ushort)0); writer.Write(origin); writer.Write(frame);
        writer.Write(_headHits != null);
        if (_headHits != null) foreach (var slot in _headHits)
        {
            writer.Write(slot != null); if (slot == null) continue;
            slot.Track.WriteSeed(writer, codec); ReplayHeadSeedCodec.Vector(writer, slot.Position);
            writer.Write(slot.NotBefore); writer.Write(slot.Terminated);
        }
        writer.Write(_headDeaths != null);
        if (_headDeaths != null) foreach (var slot in _headDeaths)
        {
            writer.Write(slot != null); if (slot == null) continue;
            slot.Track.WriteSeed(writer, codec); codec.Fact(writer, slot.Pending); writer.Write(slot.PendingAt);
            writer.Write(slot.PendingHead); writer.Write(slot.PendingEligible); writer.Write(slot.AliveSeen);
            writer.Write(slot.WasFrozen); writer.Write(slot.WasAlt); writer.Write(slot.Anchored);
            writer.Write(slot.DeadBlocked); writer.Write(slot.PhysicalAttempted);
            ReplayHeadSeedCodec.Vector(writer, slot.LastPosition); ReplayHeadSeedCodec.Vector(writer, slot.Position);
            ReplayHeadSeedCodec.Vector(writer, slot.Facing); writer.Write((byte)slot.Hunter);
        }
        writer.Write(_pendingHeadHits != null);
        if (_pendingHeadHits != null) foreach (var pending in _pendingHeadHits)
        { codec.Fact(writer, pending.Fact); writer.Write(pending.QueuedStep); writer.Write(pending.Eligible); }
        if (stream.Length > ReplayHeadSeedCodec.MaximumBytes) throw new InvalidDataException("Head-history budget exceeded.");
        var bytes = stream.ToArray();
        ReadHeadHistory(bytes, replay.State, frame); // Validate the same value contract the reader admits.
        return bytes;
    }

    internal static uint ValidateReplayHeadHistory(byte[] bytes, ReplayReplicaState state, uint frame)
        => ReadHeadHistory(bytes, state, frame).Origin;

    internal uint RestoreReplayHeadHistory(byte[] bytes, ReplayReplicaState state, uint frame)
    {
        // Decode every value before publishing any track. No clock advance,
        // current-eligibility re-evaluation or old contact re-offer occurs.
        var seed = ReadHeadHistory(bytes, state, frame, this);
        _headHits = seed.Hits; _headDeaths = seed.Deaths; _pendingHeadHits = seed.Pending; _localHeadHitSerial = 0;
        foreach (var player in Players.Items)
        {
            if (_headHits?[player.SlotIndex] != null) player.ModPrewarmHeadHitPose();
            if (_headDeaths?[player.SlotIndex] != null) player.ModPrewarmHeadDeathPose();
        }
        return seed.Origin;
    }

    private static HeadHistorySeed ReadHeadHistory(byte[] bytes, ReplayReplicaState state, uint frame, Scene? scene = null)
    {
        if (bytes.Length is < 19 or > ReplayHeadSeedCodec.MaximumBytes || !bytes.AsSpan(0, 4).SequenceEqual("HPS1"u8))
            throw new InvalidDataException("Invalid head-history signature or size.");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false); using var reader = new BinaryReader(stream);
            reader.ReadUInt32();
            if (reader.ReadUInt16() != 1 || reader.ReadUInt16() != 0) throw new InvalidDataException("Unsupported head-history schema.");
            uint origin = reader.ReadUInt32();
            if (origin > frame || reader.ReadUInt32() != frame) throw new InvalidDataException("Invalid head-history coverage clock.");
            var codec = new ReplayHeadSeedCodec(state, frame, scene);
            HeadHitSlot?[]? hits = null; HeadDeathSlot?[]? deaths = null; PendingHeadHit[]? pending = null;
            if (ReplayHeadSeedCodec.Boolean(reader))
            {
                hits = new HeadHitSlot?[8];
                for (int i = 0; i < 8; i++) if (ReplayHeadSeedCodec.Boolean(reader))
                    hits[i] = new(HeadHitTrack.ReadSeed(reader, codec, i)) { Position = ReplayHeadSeedCodec.Vector(reader),
                        NotBefore = codec.Past(reader), Terminated = ReplayHeadSeedCodec.Boolean(reader) };
            }
            if (ReplayHeadSeedCodec.Boolean(reader))
            {
                deaths = new HeadDeathSlot?[8];
                for (int i = 0; i < 8; i++) if (ReplayHeadSeedCodec.Boolean(reader))
                {
                    var slot = new HeadDeathSlot(HeadDeathTrack.ReadSeed(reader, codec, i)) { Pending = codec.Fact(reader), PendingAt = codec.Past(reader),
                        PendingHead = ReplayHeadSeedCodec.Boolean(reader), PendingEligible = ReplayHeadSeedCodec.Boolean(reader),
                        AliveSeen = ReplayHeadSeedCodec.Boolean(reader), WasFrozen = ReplayHeadSeedCodec.Boolean(reader),
                        WasAlt = ReplayHeadSeedCodec.Boolean(reader), Anchored = ReplayHeadSeedCodec.Boolean(reader),
                        DeadBlocked = ReplayHeadSeedCodec.Boolean(reader), PhysicalAttempted = ReplayHeadSeedCodec.Boolean(reader),
                        LastPosition = ReplayHeadSeedCodec.Vector(reader), Position = ReplayHeadSeedCodec.Vector(reader),
                        Facing = ReplayHeadSeedCodec.Vector(reader), Hunter = (Hunter)reader.ReadByte() };
                    if (!Enum.IsDefined(slot.Hunter) || slot.Pending.Event.IsValid && !slot.Pending.Victim.Matches(slot.Track.Victim))
                        throw new InvalidDataException("Invalid head-death owner/history.");
                    deaths[i] = slot;
                }
            }
            if (ReplayHeadSeedCodec.Boolean(reader))
            {
                pending = new PendingHeadHit[64];
                for (int i = 0; i < pending.Length; i++)
                {
                    var fact = codec.Fact(reader); uint queued = codec.Past(reader); bool eligible = ReplayHeadSeedCodec.Boolean(reader);
                    if (!fact.Event.IsValid && (queued != 0 || eligible)) throw new InvalidDataException("Invalid empty pending head hit.");
                    pending[i] = new(fact, queued, eligible);
                }
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing head-history data.");
            return new(origin, hits, deaths, pending);
        }
        catch (Exception error) when (error is EndOfStreamException or ArgumentException or OverflowException)
        { throw new InvalidDataException("Malformed or truncated head-history data.", error); }
    }
}
