using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Network;

/// <summary>Detached decoder state only. A complete scene checkpoint must also
/// supply entity, simulation and presentation state; this is never one by itself.</summary>
internal sealed class ReplayReplicaCheckpoint
{
    internal const int MaximumBytes = 128 * 1024;
    private readonly byte[] _bytes;
    internal ReadOnlySpan<byte> Bytes => _bytes;
    internal ReplayReplicaCheckpoint(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Replica checkpoint is too large.");
        _bytes = bytes.ToArray();
    }
}

internal sealed partial class ReplayReplicaState
{
    private const uint CheckpointMagic = 0x43525050; // PPRC, independent of demo/wire formats
    private const ushort CheckpointVersion = 4;
    internal ReplayReplicaCheckpoint CaptureCheckpoint()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteCheckpoint(writer);
        return new(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
    internal void WriteCheckpoint(BinaryWriter writer)
    {
        writer.Write(CheckpointMagic); writer.Write(CheckpointVersion); writer.Write((byte)NetConfig.ProtocolVersion);
        writer.Write(RecordingFrame); writer.Write(MatchRecordingFrame); writer.Write(ServerTick);
        writer.Write(Rng1); writer.Write(Rng2);
        writer.Write(AcceptedPackets); writer.Write(IgnoredPackets);
        writer.Write(_hasSnapshot); writer.Write(_rosterRevision.HasValue);
        if (_rosterRevision.HasValue) writer.Write(_rosterRevision.Value);
        writer.Write(Match.HasValue);
        if (Match is { } match)
        {
            Span<byte> bytes = stackalloc byte[MatchStatePacket.Size]; match.Write(bytes); writer.Write(bytes);
        }
        writer.Write(Configuration.HasValue);
        if (Configuration is { } configuration)
        {
            Span<byte> bytes = stackalloc byte[SessionStatePacket.Size]; configuration.Write(bytes); writer.Write(bytes);
        }
        var roster = RosterPacket.Create();
        roster.MatchId = Match?.MatchId ?? 0; roster.AuthorityEpoch = Match?.AuthorityEpoch ?? 0;
        roster.Revision = _rosterRevision ?? 0;
        roster.ContainsBots = ContainsBots;
        for (int i = 0; i < _roster.Length; i++)
        {
            var occupant = _roster[i];
            if (occupant.Generation == 0) continue;
            int index = roster.Count++;
            roster.Slots[index] = (byte)i; roster.Generations[index] = occupant.Generation;
            roster.Hunters[index] = (byte)occupant.Hunter; roster.Colors[index] = occupant.Color;
            roster.Teams[index] = occupant.Team; roster.Names[index] = occupant.Name;
            roster.Flags[index] = occupant.IsBot ? (byte)1 : (byte)0; roster.BotLevels[index] = occupant.BotLevel;
        }
        Span<byte> rosterBytes = stackalloc byte[RosterPacket.Size]; roster.Write(rosterBytes); writer.Write(rosterBytes);
        Span<byte> playerBytes = stackalloc byte[PlayerState.Size];
        Span<byte> intentBytes = stackalloc byte[IntentPacket.FullSize];
        for (int i = 0; i < _roster.Length; i++)
        {
            var life = _lives[i].Capture();
            writer.Write(life.Generation); writer.Write(life.LifeId); writer.Write((byte)life.State); writer.Write(life.Dead);
            writer.Write(_hasPlayer[i]); _players[i].Write(playerBytes); writer.Write(playerBytes);
            writer.Write(_hasIntent[i]); _intents[i].Write(intentBytes); writer.Write(intentBytes);
            writer.Write(_intentReceivedFrame[i]);
        }
        writer.Write(_worldTail.Length); writer.Write(_worldTail);
        long authorityAt = Replay.ReplayCheckpointWriter.BeginComponent(writer);
        AuthorityWorld?.Encode(writer);
        Replay.ReplayCheckpointWriter.EndComponent(writer, authorityAt);
        writer.Write(AuthorityAppliedTick.HasValue); if (AuthorityAppliedTick is uint applied) writer.Write(applied);
        Span<byte> cosmetics = stackalloc byte[Entities.PlayerEntity.SlotCapacity * CosmeticStatePacket.Size];
        int cosmeticBytes = Match is { } cosmeticMatch ? Cosmetics.Write(cosmetics, cosmeticMatch.MatchId, cosmeticMatch.AuthorityEpoch) : 0;
        writer.Write(cosmeticBytes); writer.Write(cosmetics[..cosmeticBytes]);
        writer.Write(ChatLines.Count);
        Span<byte> chatBytes = stackalloc byte[ChatPacket.Size];
        foreach (var line in ChatLines) { writer.Write(line.Frame); line.Packet.Write(chatBytes); writer.Write(chatBytes); }

    }

    internal void RestoreCheckpoint(ReplayReplicaCheckpoint checkpoint)
    {
        // Parse into a temporary owner first: an invalid checkpoint must not
        // partially replace a running session's decoder/lifecycle state.
        var restored = new ReplayReplicaState();
        using var stream = new MemoryStream(checkpoint.Bytes.ToArray(), writable: false);
        using var reader = new BinaryReader(stream);
        byte[] Read(int count)
        {
            if (count < 0 || count > stream.Length - stream.Position) throw new InvalidDataException("Truncated replica checkpoint.");
            return reader.ReadBytes(count);
        }
        try
        {
            if (reader.ReadUInt32() != CheckpointMagic) throw new InvalidDataException("Incompatible replica checkpoint.");
            ushort version = reader.ReadUInt16();
            int protocol = reader.ReadByte();
            if (version is < 1 or > CheckpointVersion || !ReplayIdentityCompatibility.Supports(protocol)) throw new InvalidDataException("Incompatible replica checkpoint.");
            restored.RecordingFrame = reader.ReadUInt32(); restored.MatchRecordingFrame = reader.ReadUInt32();
            restored.ServerTick = reader.ReadUInt32(); restored.Rng1 = reader.ReadUInt32(); restored.Rng2 = reader.ReadUInt32();
            restored.AcceptedPackets = reader.ReadInt64(); restored.IgnoredPackets = reader.ReadInt64();
            restored._hasSnapshot = reader.ReadBoolean();
            restored._rosterRevision = reader.ReadBoolean() ? reader.ReadUInt32() : null;
            if (reader.ReadBoolean())
            {
                var packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
                Read(MatchStatePacket.Size).CopyTo(packet, 1);
                restored.Match = MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(packet, protocol)[1..]);
            }
            if (reader.ReadBoolean())
            {
                int size = protocol == 24 ? 41 + HostRequestPacket.MaxRoomBytes : protocol < 29 ? SessionStatePacket.Protocol28Size : SessionStatePacket.Size;
                byte[] packet = new byte[size + 1]; packet[0] = (byte)PacketType.SessionState;
                Read(size).CopyTo(packet, 1);
                if (!SessionStatePacket.TryRead(ReplayIdentityCompatibility.Convert(packet, protocol)[1..], out var configuration)) throw Malformed();
                restored.Configuration = configuration;
            }
            int rosterSize = protocol >= 27 ? RosterPacket.Size : protocol == 26 ? 18 + 27 * RosterPacket.MaxSlots : 17 + 25 * RosterPacket.MaxSlots;
            byte[] rosterPacket = new byte[rosterSize + 1]; rosterPacket[0] = (byte)PacketType.Roster;
            Read(rosterSize).CopyTo(rosterPacket, 1);
            if (!RosterPacket.TryRead(ReplayIdentityCompatibility.Convert(rosterPacket, protocol)[1..], out var roster)) throw Malformed();
            if (restored.Match is { } current
                && (string.IsNullOrEmpty(current.RoomKey) || !Enum.IsDefined(typeof(GameMode), current.Mode)
                    || !float.IsFinite(current.TimeRemaining) || !float.IsFinite(current.TimeElapsed)
                    || roster.MatchId != current.MatchId || roster.AuthorityEpoch != current.AuthorityEpoch
                    || restored.Configuration is { } rules && !restored.Matches(rules.MatchId, rules.AuthorityEpoch))) throw Malformed();
            restored.ContainsBots = roster.ContainsBots;
            for (int i = 0; i < roster.Count; i++)
                restored._roster[roster.Slots[i]] = new(roster.Generations[i], (Hunter)roster.Hunters[i],
                    roster.Colors[i], roster.Teams[i], roster.Names[i], roster.IsBot(i), roster.BotLevels[i]);
            for (int i = 0; i < _roster.Length; i++)
            {
                restored._lives[i].Restore(new(reader.ReadUInt16(), reader.ReadUInt16(),
                    (NetworkPlayerState)reader.ReadByte(), reader.ReadBoolean()));
                restored._hasPlayer[i] = reader.ReadBoolean(); restored._players[i] = PlayerState.Read(Read(PlayerState.Size));
                restored._hasIntent[i] = reader.ReadBoolean(); restored._intents[i] = IntentPacket.Read(Read(IntentPacket.FullSize));
                restored._intentReceivedFrame[i] = reader.ReadUInt32();
                if (restored._lives[i].Generation != restored._roster[i].Generation
                    || restored._hasPlayer[i] && (!restored.MatchesLife(i, restored._players[i].SlotGeneration, restored._players[i].LifeId)
                        || restored._players[i].SlotIndex != i || !Sane(restored._players[i].Position)
                        || !Sane(restored._players[i].Speed) || !Sane(restored._players[i].Facing))
                    || restored._hasIntent[i] && (!restored.MatchesLife(i, restored._intents[i].SlotGeneration, restored._intents[i].LifeId)
                        || !restored.Matches(restored._intents[i].MatchId, restored._intents[i].AuthorityEpoch)
                        || restored._intentReceivedFrame[i] > restored.RecordingFrame)) throw Malformed();
            }
            restored._worldTail = Read(reader.ReadInt32());
            if (!restored._hasSnapshot && restored._worldTail.Length != 0) throw Malformed();
            if (restored._hasSnapshot)
            {
                var tail = restored.WorldTail;
                if (tail.Length < NetMatchTimeSync.Size + NetHealthSync.HeaderSize
                    || !NetMatchTimeSync.Validate(tail[..NetMatchTimeSync.Size])
                    || !NetHealthSync.Validate(tail[NetMatchTimeSync.Size..])) throw Malformed();
                var health = tail[NetMatchTimeSync.Size..];
                if (BinaryPrimitives.ReadUInt16LittleEndian(health) != restored.Match?.MatchId) throw Malformed();
                for (int offset = NetHealthSync.HeaderSize; offset < health.Length; offset += NetHealthSync.EntrySize)
                {
                    byte flags = health[offset + 2];
                    restored._healthSpawns.Add(BinaryPrimitives.ReadInt16LittleEndian(health[offset..]), new(
                        (flags & 1) != 0, (flags & 2) != 0, BinaryPrimitives.ReadUInt16LittleEndian(health[(offset + 3)..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(health[(offset + 5)..]), (sbyte)(((flags >> 2) & 15) - 1)));
                }
            }
            if (version >= 2)
            {
                byte[] authority = Read(reader.ReadInt32());
                if (authority.Length > 0)
                {
                    restored.AuthorityWorld = ReplayAuthorityWorld.Decode(authority);
                    if (!restored.Matches(restored.AuthorityWorld.MatchId, restored.AuthorityWorld.Epoch)) throw Malformed();
                }
                restored.AuthorityAppliedTick = reader.ReadBoolean() ? reader.ReadUInt32() : null;
                if (restored.AuthorityAppliedTick.HasValue && (restored.AuthorityWorld == null || restored.AuthorityAppliedTick > restored.AuthorityWorld.Tick)) throw Malformed();
            }
            if (version >= 3)
            {
                byte[] cosmetics = Read(reader.ReadInt32());
                if (cosmetics.Length % CosmeticStatePacket.Size != 0
                    || cosmetics.Length > Entities.PlayerEntity.SlotCapacity * CosmeticStatePacket.Size) throw Malformed();
                for (int at = 0; at < cosmetics.Length; at += CosmeticStatePacket.Size)
                    if (CosmeticStatePacket.TryRead(cosmetics.AsSpan(at, CosmeticStatePacket.Size), out var cosmetic)
                        && restored.Match is { } match)
                        restored.Cosmetics.Accept(cosmetic, match.MatchId, match.AuthorityEpoch, restored._roster[cosmetic.Slot].Generation);
            }
            if (version >= 4)
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > Chat.ChatBox.HistoryCapacity) throw Malformed();
                for (int i = 0; i < count; i++)
                {
                    uint frame = reader.ReadUInt32();
                    if (frame > restored.RecordingFrame) throw Malformed();
                    restored.ChatLines.Add((frame, ChatPacket.Read(Read(ChatPacket.Size))));
                }
            }
            if (stream.Position != stream.Length || restored.MatchRecordingFrame > restored.RecordingFrame
                || restored.AcceptedPackets < 0 || restored.IgnoredPackets < 0) throw Malformed();
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Truncated replica checkpoint.", ex); }
        ChatLines.Clear(); ChatLines.AddRange(restored.ChatLines);
        ContainsBots = restored.ContainsBots;
        Cosmetics = restored.Cosmetics;
        Match = restored.Match; Configuration = restored.Configuration;
        RecordingFrame = restored.RecordingFrame; MatchRecordingFrame = restored.MatchRecordingFrame;
        ServerTick = restored.ServerTick; Rng1 = restored.Rng1; Rng2 = restored.Rng2;
        AcceptedPackets = restored.AcceptedPackets; IgnoredPackets = restored.IgnoredPackets;
        _hasSnapshot = restored._hasSnapshot; _rosterRevision = restored._rosterRevision;
        Array.Copy(restored._roster, _roster, _roster.Length); Array.Copy(restored._players, _players, _players.Length);
        Array.Copy(restored._intents, _intents, _intents.Length); Array.Copy(restored._hasPlayer, _hasPlayer, _hasPlayer.Length);
        Array.Copy(restored._hasIntent, _hasIntent, _hasIntent.Length);
        Array.Copy(restored._intentReceivedFrame, _intentReceivedFrame, _intentReceivedFrame.Length);
        for (int i = 0; i < _lives.Length; i++) _lives[i].Restore(restored._lives[i].Capture());
        _worldTail = restored._worldTail; AuthorityWorld = restored.AuthorityWorld; AuthorityAppliedTick = restored.AuthorityAppliedTick; _authorityWire.Reset();
        _healthSpawns.Clear(); foreach (var pair in restored._healthSpawns) _healthSpawns.Add(pair.Key, pair.Value);
    }
}
