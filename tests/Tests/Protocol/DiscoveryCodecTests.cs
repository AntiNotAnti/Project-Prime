using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests
{
    public sealed class DiscoveryCodecTests
    {
        [Fact]
        public void CompatibilityRequiresFamilyAndProtocolTogether()
        {
            Assert.True(NetWireIdentity.IsCompatible(NetWireFamily.Authoritative, NetHeader.Version));
            Assert.Empty(NetWireIdentity.IncompatibilityReason(NetWireFamily.Authoritative, NetHeader.Version));
            Assert.False(NetWireIdentity.IsCompatible(NetWireFamily.LegacyRelay, NetHeader.Version));
            Assert.False(NetWireIdentity.IsCompatible(NetWireFamily.Unknown, NetHeader.Version));
            Assert.False(NetWireIdentity.IsCompatible(NetWireFamily.Authoritative, NetHeader.Version - 1));
            Assert.Contains("legacy relay", NetWireIdentity.IncompatibilityReason(NetWireFamily.LegacyRelay, 4));
            Assert.Contains("unknown", NetWireIdentity.IncompatibilityReason(NetWireFamily.Unknown, 0));
            Assert.Contains("protocol", NetWireIdentity.IncompatibilityReason(NetWireFamily.Authoritative, 4));
        }

        [Fact]
        public void StatusPreservesLegacyOffsetsAndValidatesTheCompleteMatch()
        {
            var packet = new ServerStatusPacket
            {
                Match = new MatchStatePacket
                {
                    Mode = (byte)GameMode.Battle, TimeRemaining = 120, TimeElapsed = 30,
                    PlayerCount = 2, RoomKey = "MP1 SANCTORUS", NextRoomKey = "MP2 HARAS",
                    MatchId = 42, PointGoal = 10, Flags = MatchStatePacket.FlagInProgress
                },
                MaxPlayers = 8, Protocol = NetHeader.Version, ServerName = "TEST"
            };
            byte[] bytes = new byte[ServerStatusPacket.Size];
            packet.Write(bytes);
            Assert.Equal(130, bytes.Length);
            Assert.Equal(8, bytes[95]);
            Assert.Equal(NetHeader.Version, bytes[96]);
            Assert.Equal((byte)'T', bytes[97]);
            Assert.Equal((byte)NetWireFamily.Authoritative, bytes[129]);
            Assert.True(ServerStatusPacket.TryRead(bytes, out var read));
            Assert.Equal("MP1 SANCTORUS", read.Match.RoomKey);
            Assert.Equal("TEST", read.ServerName);
            Assert.Equal((ushort)42, read.Match.MatchId);
            Assert.Equal(NetWireFamily.Authoritative, read.Family);
            Assert.True(ServerStatusPacket.TryRead(bytes.AsSpan(0, ServerStatusPacket.LegacySize), out read));
            Assert.Equal(NetWireFamily.LegacyRelay, read.Family);
            CheckLengths(bytes, ServerStatusPacket.LegacySize,
                value => ServerStatusPacket.TryRead(value, out _));
            CheckMutations(bytes, value => ServerStatusPacket.TryRead(value, out _),
                (0, 1), (0, 16), (9, 9), (10, 8), (95, 0), (95, 1), (95, 9),
                (15, 0xFF), (55, 1), (97, 0x80), (102, (byte)'X'), (129, 0), (129, 3));
            foreach (float invalid in new[] { Single.NaN, Single.PositiveInfinity, Single.NegativeInfinity, -1f })
            {
                foreach (int offset in new[] { 1, 5 })
                {
                    var bad = (byte[])bytes.Clone();
                    BinaryPrimitives.WriteSingleLittleEndian(bad.AsSpan(offset), invalid);
                    Assert.False(ServerStatusPacket.TryRead(bad, out _));
                }
            }
            Assert.Throws<ArgumentException>(() => ServerStatusPacket.Read(Array.Empty<byte>()));
        }

        [Fact]
        public void MasterEntryChecksFieldsAndClassifiesMissingFamilyAsLegacy()
        {
            var packet = new MasterEntryPacket
            {
                Address = 0x7F000001, Port = 27015, Players = 2, MaxPlayers = 8,
                Mode = (byte)GameMode.Battle, Protocol = NetHeader.Version,
                ServerName = "TEST", RoomKey = "MP1 SANCTORUS"
            };
            byte[] bytes = new byte[MasterEntryPacket.Size];
            packet.Write(bytes);
            Assert.Equal(83, bytes.Length);
            Assert.Equal(new byte[] { 127, 0, 0, 1, 0x87, 0x69, 2, 8 }, bytes[..8]);
            Assert.Equal((byte)'T', bytes[10]);
            Assert.Equal((byte)'M', bytes[42]);
            Assert.True(MasterEntryPacket.TryRead(bytes, out var read));
            Assert.Equal(packet.Address, read.Address);
            Assert.Equal(packet.Port, read.Port);
            Assert.Equal(packet.RoomKey, read.RoomKey);
            Assert.Equal(NetWireFamily.Authoritative, read.Family);
            Assert.True(MasterEntryPacket.TryRead(bytes.AsSpan(0, MasterEntryPacket.LegacySize), out read));
            Assert.Equal(NetWireFamily.LegacyRelay, read.Family);
            CheckLengths(bytes, MasterEntryPacket.LegacySize, value => MasterEntryPacket.TryRead(value, out _));
            CheckMutations(bytes, value => MasterEntryPacket.TryRead(value, out _),
                (6, 9), (7, 0), (7, 1), (7, 9), (8, 1), (8, 16), (10, 0xFF),
                (15, (byte)'X'), (42, 1), (82, 0), (82, 255));
            Array.Clear(bytes, 4, 2);
            Assert.False(MasterEntryPacket.TryRead(bytes, out _));
        }

        [Fact]
        public void HeartbeatAllowsSourcePortFallbackAndChecksLegacyAndCurrentLayouts()
        {
            var packet = new MasterHeartbeatPacket
            {
                Protocol = NetHeader.Version, Port = 0, Players = 0, MaxPlayers = 8,
                Mode = (byte)GameMode.Battle, ServerName = "TEST", RoomKey = "MP1 SANCTORUS"
            };
            byte[] bytes = new byte[MasterHeartbeatPacket.Size];
            packet.Write(bytes);
            Assert.Equal(79, bytes.Length);
            Assert.Equal((byte)'T', bytes[6]);
            Assert.Equal((byte)'M', bytes[38]);
            Assert.True(MasterHeartbeatPacket.TryRead(bytes, out var read));
            Assert.Equal((ushort)0, read.Port);
            Assert.Equal(NetWireFamily.Authoritative, read.Family);
            Assert.True(MasterHeartbeatPacket.TryRead(bytes.AsSpan(0, MasterHeartbeatPacket.LegacySize), out read));
            Assert.Equal(NetWireFamily.LegacyRelay, read.Family);
            CheckLengths(bytes, MasterHeartbeatPacket.LegacySize, value => MasterHeartbeatPacket.TryRead(value, out _));
            CheckMutations(bytes, value => MasterHeartbeatPacket.TryRead(value, out _),
                (3, 9), (4, 0), (4, 9), (5, 1), (5, 16), (6, 0xFF),
                (11, (byte)'X'), (38, 1), (78, 0), (78, 255));
            packet.Family = NetWireFamily.LegacyRelay;
            packet.Write(bytes);
            Assert.True(MasterHeartbeatPacket.TryRead(bytes, out read));
            Assert.False(NetWireIdentity.IsCompatible(read.Family, read.Protocol));
        }

        [Fact]
        public void HostRequestRejectsLegacyMutationAndMalformedSettings()
        {
            MatchRules rules = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS", 8);
            var packet = new HostRequestPacket
            {
                Protocol = NetHeader.Version, RequestNonce = Guid.NewGuid(), Rules = rules,
                LobbyPolicy = LobbyPolicyKind.PersistentLobby, ReadyRequired = true,
                HostMayForceStart = true, MinimumPlayers = 1, BotSkill = 1,
                MaxObservers = 4, ServerName = "TEST"
            };
            byte[] bytes = new byte[HostRequestPacket.Size];
            packet.Write(bytes);
            Assert.Equal(144, bytes.Length);
            Assert.Equal(HostRequestPacket.CurrentVersion, bytes[0]);
            Assert.Equal((byte)'M', bytes[HostRequestPacket.HeaderSize + 28]);
            Assert.Equal((byte)'T', bytes[HostRequestPacket.HeaderSize + MatchRulesWire.Size]);
            Assert.True(HostRequestPacket.TryRead(bytes, out var read));
            Assert.Equal(packet.RequestNonce, read.RequestNonce);
            Assert.Equal(rules, read.Rules);
            Assert.Equal(LobbyPolicyKind.PersistentLobby, read.LobbyPolicy);
            Assert.Equal(NetWireFamily.Authoritative, read.Family);
            CheckLengths(bytes, -1, value => HostRequestPacket.TryRead(value, out _));
            CheckMutations(bytes, value => HostRequestPacket.TryRead(value, out _),
                (0, 2), (1, 0), (1, 3), (3, 2), (4, 3), (5, 4), (6, 0),
                (8, 3), (9, 17), (10, 31), (11, 1),
                (HostRequestPacket.HeaderSize + 28, 0xFF),
                (HostRequestPacket.HeaderSize + MatchRulesWire.Size, 1));
            Array.Clear(bytes, 12, 16);
            Assert.False(HostRequestPacket.TryRead(bytes, out _));
        }

        [Fact]
        public void HostReplyRequiresFullIdentityAndValidStartedPort()
        {
            var packet = new HostReplyPacket { Started = true, Port = 27015,
                RequestNonce = Guid.NewGuid(), OwnerToken = Guid.NewGuid(), Reason = "OK" };
            byte[] bytes = new byte[HostReplyPacket.Size];
            packet.Write(bytes);
            Assert.Equal(134, bytes.Length);
            Assert.Equal((byte)NetWireFamily.Authoritative, bytes[1]);
            Assert.Equal(NetHeader.Version, bytes[2]);
            Assert.True(HostReplyPacket.TryRead(bytes, out var read));
            Assert.True(read.Started);
            Assert.Equal(packet.Port, read.Port);
            Assert.Equal(packet.RequestNonce, read.RequestNonce);
            Assert.Equal(packet.OwnerToken, read.OwnerToken);
            Assert.Equal("OK", read.Reason);
            Assert.True(NetWireIdentity.IsCompatible(read.Family, read.Protocol));
            CheckLengths(bytes, -1, value => HostReplyPacket.TryRead(value, out _));
            CheckMutations(bytes, value => HostReplyPacket.TryRead(value, out _),
                (0, 2), (1, 0), (1, 3), (3, 2), (HostReplyPacket.HeaderSize, 0xFF));
            Array.Clear(bytes, 4, 2);
            Assert.False(HostReplyPacket.TryRead(bytes, out _));
            bytes[3] = 0;
            Array.Clear(bytes, 22, 16);
            Assert.True(HostReplyPacket.TryRead(bytes, out _));
            bytes[2]--;
            Assert.True(HostReplyPacket.TryRead(bytes, out read));
            Assert.False(NetWireIdentity.IsCompatible(read.Family, read.Protocol));
        }

        private delegate bool Decoder(ReadOnlySpan<byte> bytes);

        private static void CheckLengths(byte[] bytes, int legacySize, Decoder decode)
        {
            for (int size = 0; size < bytes.Length; size++)
            {
                Assert.Equal(size == legacySize, decode(bytes.AsSpan(0, size)));
            }
            byte[] trailing = new byte[bytes.Length + 1];
            bytes.CopyTo(trailing, 0);
            Assert.False(decode(trailing));
        }

        private static void CheckMutations(byte[] bytes, Decoder decode, params (int Offset, byte Value)[] mutations)
        {
            foreach (var (offset, value) in mutations)
            {
                byte[] bad = (byte[])bytes.Clone();
                bad[offset] = value;
                Assert.False(decode(bad), $"Accepted invalid byte {value} at offset {offset}.");
            }
        }
    }
}
