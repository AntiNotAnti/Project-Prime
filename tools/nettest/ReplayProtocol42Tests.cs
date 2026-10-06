using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ReplayProtocol42Tests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; NetArchitectureTests.Check(value, label); }
    private static byte[] Packet(PacketType type, ReadOnlySpan<byte> body)
    { byte[] bytes = new byte[1 + body.Length]; bytes[0] = (byte)type; body.CopyTo(bytes.AsSpan(1)); return bytes; }

    public static int Run()
    {
        try
        {
            _checks = 0;
            Snapshots(); Intent(); ConfigurationAndRoster(); Claims(); Checkpoints();
            Console.WriteLine($"PASS: {_checks} protocol-42 historical replay packet/checkpoint assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Snapshots()
    {
        foreach (int protocol in new[] { 24, 28, 29, 38, 39, 41 })
        foreach (bool fast in new[] { false, true })
        {
            int historical = protocol < 29 ? PlayerState.LegacySize : PlayerState.Protocol41Size;
            int oldSize = historical - (fast ? 7 : 0), currentSize = PlayerState.Size - (fast ? 7 : 0);
            byte[] old = new byte[SnapshotHeader.Size + 2 * oldSize + (fast ? 0 : NetMatchTimeSync.Size + NetHealthSync.HeaderSize)];
            new SnapshotHeader { MatchId = 7, AuthorityEpoch = 9, Frame = 100, PlayerCount = 2 }.Write(old);
            for (int i = 0; i < 2; i++)
            {
                byte[] current = new byte[PlayerState.Size];
                new PlayerState { SlotIndex = (byte)i, SlotGeneration = 1, LifeId = 2, Health = 99,
                    Flags = PlayerState.FlagSpawned | PlayerState.FlagActive, Position = new(i + 1, 2, 3), Facing = Vector3.UnitZ,
                    Hunter = (byte)Hunter.Weavel, FreezeEventId = 12, RespawnEligibleFrame = 900 }.Write(current);
                var row = old.AsSpan(SnapshotHeader.Size + i * oldSize, oldSize);
                if (fast) { current.AsSpan(0, 41).CopyTo(row); current.AsSpan(48, historical - 48).CopyTo(row[41..]); }
                else current.AsSpan(0, historical).CopyTo(row);
            }
            int tail = SnapshotHeader.Size + 2 * oldSize;
            if (!fast) BinaryPrimitives.WriteUInt16LittleEndian(old.AsSpan(tail + NetMatchTimeSync.Size), 7);
            byte[] converted = ReplayIdentityCompatibility.Convert(Packet(fast ? PacketType.SnapshotFast : PacketType.Snapshot, old), protocol).ToArray();
            Check(converted.Length == 1 + SnapshotHeader.Size + 2 * currentSize + old.Length - tail, "historical snapshot grows exactly once " + protocol);
            for (int i = 0; i < 2; i++)
            {
                var row = converted.AsSpan(1 + SnapshotHeader.Size + i * currentSize, currentSize);
                PlayerState player = fast ? PlayerState.ReadFast(row, new byte[7]) : PlayerState.Read(row);
                Check(player.Position == new Vector3(i + 1, 2, 3) && player.LifeId == 2 && player.Hunter == 255
                    && player.FreezeEventId == 0 && player.RespawnEligibleFrame == 0, "historical player values/defaults " + protocol);
            }
            Check(converted.AsSpan(1 + SnapshotHeader.Size + 2 * currentSize).SequenceEqual(old.AsSpan(tail)), "historical world tail preserved " + protocol);
            Check(ReplayIdentityCompatibility.Convert(converted, NetConfig.ProtocolVersion).SequenceEqual(converted), "modern boundary does not re-expand upgraded snapshot");
        }
    }

    private static void Intent()
    {
        var intent = new IntentPacket { Frame = 100, FireEventCount = 2, Position = new(1, 2, 3), Aim = Vector3.UnitZ };
        intent.FireEvents[0] = new(77, 99, 90, 128, FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0, 0,
            FireEvent.FlagPose, new(1, 2, 3), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ);
        intent.FireEvents[1] = new(78, 100, 91, 192, FireEventKind.ReleaseFire, (byte)BeamType.PowerBeam, 0, 0);
        byte[] current = new byte[IntentPacket.FullSize]; intent.Write(current);
        foreach (int protocol in new[] { 30, 38, 39, 41 })
        {
            int eventSize = protocol < 39 ? FireEvent.LegacySize : FireEvent.Protocol41Size;
            byte[] old = new byte[IntentPacket.LegacyFullSize + 1 + NetFireEvents.Capacity * eventSize];
            current.AsSpan(0, IntentPacket.LegacyFullSize + 1).CopyTo(old);
            for (int i = 0; i < 2; i++) current.AsSpan(IntentPacket.LegacyFullSize + 1 + i * FireEvent.Size, eventSize)
                .CopyTo(old.AsSpan(IntentPacket.LegacyFullSize + 1 + i * eventSize));
            byte[] converted = ReplayIdentityCompatibility.Convert(Packet(PacketType.Intent, old), protocol).ToArray();
            var read = IntentPacket.Read(converted.AsSpan(1));
            Check(converted.Length == 1 + IntentPacket.FullSize && read.FireEventCount == 2
                && read.FireEvents[0].ShotId == 77 && read.FireEvents[1].ShotId == 78
                && read.FireEvents[0].SourceFlags == 0, "historical fire ring stride/defaults " + protocol);
            Check(read.FireEvents[0].HasPose == (protocol >= 39), "historical source shot pose availability " + protocol);
        }
    }

    private static void ConfigurationAndRoster()
    {
        var session = new SessionStatePacket { MatchId = 7, AuthorityEpoch = 9, MaxPlayers = 8,
            Phase = SessionPhase.InMatch, Match = new() { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle },
            WorldProfile = MatchWorldProfile.Resolve(8) };
        byte[] current = new byte[SessionStatePacket.Size]; session.Write(current);
        var upgraded = ReplayIdentityCompatibility.Convert(Packet(PacketType.SessionState, current.AsSpan(0, SessionStatePacket.Protocol41Size)), 41);
        Check(SessionStatePacket.TryRead(upgraded[1..], out var read) && read.MatchId == 7 && read.StockGameplayHash.IsZero,
            "historical session appends absent stock identity");
        var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 9; roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Names[0] = "Historic"; roster.Teams[0] = -1; roster.Roles[0] = 1;
        current = new byte[RosterPacket.Size]; roster.Write(current);
        byte[] old = new byte[RosterPacket.Protocol41Size]; current.AsSpan(0, RosterPacket.HeaderSize).CopyTo(old);
        for (int i = 0; i < RosterPacket.MaxSlots; i++) current.AsSpan(RosterPacket.HeaderSize + i * RosterPacket.EntrySize, RosterPacket.Protocol41EntrySize)
            .CopyTo(old.AsSpan(RosterPacket.HeaderSize + i * RosterPacket.Protocol41EntrySize));
        upgraded = ReplayIdentityCompatibility.Convert(Packet(PacketType.Roster, old), 41);
        Check(RosterPacket.TryRead(upgraded[1..], out var decoded) && decoded.Names[0] == "Historic" && !decoded.IsSpectator(0),
            "historical roster preserves names and defaults missing role");
        var state = new ReplayReplicaState();
        byte[] match = new byte[MatchStatePacket.Size];
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 9, RoomKey = "MP1 SANCTORUS", NextRoomKey = "", Mode = (byte)GameMode.Battle }.Write(match);
        state.Accept(Packet(PacketType.MatchState, match), 0); state.Accept(Packet(PacketType.Roster, current), 0);
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(state.CaptureCheckpoint());
        Check(restored.Occupant(0).IsSpectator && restored.Occupant(0).Name == "Historic", "current checkpoint retains spectator role");
    }

    private static void Claims()
    {
        byte[] current = new byte[HitClaimPacket.Size]; new HitClaimPacket { ShotId = 77, AckSubFrame = 192 }.Write(current);
        var upgraded = ReplayIdentityCompatibility.Convert(Packet(PacketType.HitClaim, current.AsSpan(0, HitClaimPacket.Protocol41Size)), 41);
        Check(HitClaimPacket.Read(upgraded[1..]).ShotId == 77 && HitClaimPacket.Read(upgraded[1..]).AckSubFrame == 0,
            "historical claim retains shot identity with integral ACK default");
    }

    private static void Checkpoints()
    {
        foreach (int protocol in new[] { 24, 26, 28, 29, 30, 38, 39, 41 })
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(0x43525050u); writer.Write((ushort)1); writer.Write((byte)protocol);
            for (int i = 0; i < 5; i++) writer.Write(0u);
            writer.Write(0L); writer.Write(0L);
            writer.Write(false); writer.Write(false); writer.Write(false); writer.Write(false);
            int rosterSize = protocol >= 33 ? RosterPacket.Protocol41Size : protocol >= 27 ? RosterPacket.LegacySize
                : protocol == 26 ? 18 + 27 * RosterPacket.MaxSlots : 17 + 25 * RosterPacket.MaxSlots;
            writer.Write(new byte[rosterSize]);
            int playerSize = protocol >= 29 ? PlayerState.Protocol41Size : PlayerState.LegacySize;
            int intentSize = protocol >= 39 ? IntentPacket.Protocol41FullSize : protocol >= 30 ? IntentPacket.Protocol38FullSize : IntentPacket.LegacyFullSize;
            for (int i = 0; i < RosterPacket.MaxSlots; i++)
            {
                writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((byte)NetworkPlayerState.Empty); writer.Write(false);
                writer.Write(false); writer.Write(new byte[playerSize]); writer.Write(false); writer.Write(new byte[intentSize]); writer.Write(0u);
            }
            writer.Write(0); writer.Flush();
            var state = new ReplayReplicaState(); state.RestoreCheckpoint(new ReplayReplicaCheckpoint(stream.ToArray()));
            Check(state.AcceptedPackets == 0 && state.Occupant(0).Generation == 0, "historical checkpoint exact widths " + protocol);
        }
    }
}
