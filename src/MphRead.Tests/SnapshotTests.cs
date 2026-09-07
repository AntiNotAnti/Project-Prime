using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class SnapshotTests
    {
        private static SnapshotPlayer Player(byte slot) => new()
        {
            Slot = slot, Hunter = (Hunter)slot, TeamIndex = slot, Weapon = 0,
            Flags = SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned,
            Health = 99, AmmoUa = 43, AmmoMissiles = 17, Life = 2,
            ConnectionId = 0xFEDCBA9876543200UL + slot,
            Position = new Vector3(1.5f, -2, 3), Speed = Vector3.UnitX / 8,
            Aim = -Vector3.UnitZ, Facing = Vector3.UnitX, AvailableWeapons = 3,
            Points = -1, Kills = 4, Deaths = 5
        };

        [Fact]
        public void EightPlayersFitOneDatagramAndPreserveRecipientAcknowledgement()
        {
            var players = new SnapshotPlayer[8];
            for (byte i = 0; i < 8; i++) { players[i] = Player(i); }
            var packet = new SnapshotPacket(UInt32.MaxValue, 0, 42, UInt32.MaxValue, true, 123, 456);
            var bytes = new byte[SnapshotPacket.MaxSize];
            Assert.Equal(bytes.Length, packet.Write(bytes, players));
            Assert.True(bytes.Length + NetHeader.Size <= NetConfig.MaxPacketSize);
            Assert.Equal(0xFEDCBA9876543207UL,
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(SnapshotPacket.HeaderSize + 7 * SnapshotPlayer.Size + 16)));
            var decoded = new SnapshotPlayer[8];
            Assert.True(SnapshotPacket.TryRead(bytes, decoded, out SnapshotPacket header, out int count));
            Assert.Equal(packet, header);
            Assert.Equal(8, count);
            Assert.Equal(players, decoded);
            for (int length = 0; length < bytes.Length; length++)
            {
                Assert.False(SnapshotPacket.TryRead(bytes.AsSpan(0, length), decoded, out _, out _));
            }
        }

        [Fact]
        public void RejectsInvalidPlayersDuplicateSlotsAndTrailingBytes()
        {
            var packet = new SnapshotPacket(1, 2, 3, 0, false, 0, 0);
            var bytes = new byte[SnapshotPacket.HeaderSize + 2 * SnapshotPlayer.Size];
            var players = new[] { Player(0), Player(1) };
            var decoded = new SnapshotPlayer[8];
            packet.Write(bytes, players);
            bytes[SnapshotPacket.HeaderSize + SnapshotPlayer.Size] = 0;
            Assert.False(SnapshotPacket.TryRead(bytes, decoded, out _, out int count));
            Assert.Equal(0, count);
            foreach (int offset in new[] { 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68 })
            {
                packet.Write(bytes, players);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(SnapshotPacket.HeaderSize + offset), Single.NaN);
                Assert.False(SnapshotPacket.TryRead(bytes, decoded, out _, out _));
            }
            foreach (int offset in new[] { 0, 1, 2, 3, 5, 85 })
            {
                packet.Write(bytes, players);
                bytes[SnapshotPacket.HeaderSize + offset] = 255;
                Assert.False(SnapshotPacket.TryRead(bytes, decoded, out _, out _));
            }
            packet.Write(bytes, players);
            Assert.False(SnapshotPacket.TryRead(bytes, decoded.AsSpan(0, 1), out _, out _));
            Assert.False(SnapshotPacket.TryRead(new byte[bytes.Length + 1], decoded, out _, out _));
        }

        [Fact]
        public void ArbitraryWireDataNeverThrows()
        {
            var random = new Random(54321);
            var bytes = new byte[1025];
            var players = new SnapshotPlayer[8];
            for (int i = 0; i < 20000; i++)
            {
                random.NextBytes(bytes);
                SnapshotPacket.TryRead(bytes.AsSpan(0, random.Next(bytes.Length + 1)), players, out _, out _);
            }
        }
    }
}
