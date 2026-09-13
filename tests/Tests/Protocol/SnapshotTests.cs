using System;
using System.Buffers.Binary;
using MphRead.Entities;
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
            Points = -1, Kills = 4, Deaths = 5, ChargeLevel = 73
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
        public void SpireAltAttackFlagRoundTripsAndUnknownFlagIsRejected()
        {
            SnapshotPlayer player = Player(5);
            player.Flags |= SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.SpireAltAttack;
            byte[] bytes = new byte[SnapshotPlayer.Size];

            player.Write(bytes);

            Assert.True(SnapshotPlayer.TryRead(bytes, out SnapshotPlayer parsed));
            Assert.True((parsed.Flags & SnapshotPlayerFlags.SpireAltAttack) != 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4),
                (ushort)((ushort)player.Flags | 0x8000));
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));
        }

        [Fact]
        public void Protocol16ReplaySnapshotDefaultsMissingChargeLevelToZero()
        {
            SnapshotPlayer player = Player(3);
            var packet = new SnapshotPacket(41, 7, 9, 2, true, 11, 13);
            byte[] current = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
            packet.Write(current, new[] { player });
            byte[] historical = new byte[SnapshotPacket.HeaderSize + 96];
            current.AsSpan(0, SnapshotPacket.HeaderSize).CopyTo(historical);
            current.AsSpan(SnapshotPacket.HeaderSize, 96)
                .CopyTo(historical.AsSpan(SnapshotPacket.HeaderSize));
            var decoded = new SnapshotPlayer[8];

            Assert.True(Protocol16ReplayCodec.TryReadSnapshot(historical,
                decoded, out SnapshotPacket parsed, out int count));
            Assert.Equal(packet, parsed);
            Assert.Equal(1, count);
            Assert.Equal(player.Points, decoded[0].Points);
            Assert.Equal((ushort)0, decoded[0].ChargeLevel);
        }

        [Theory]
        [InlineData(19, false)]
        [InlineData(20, true)]
        [InlineData(21, true)]
        [InlineData(59, true)]
        [InlineData(60, false)]
        public void RemoteChargeEffectStartsEvenWhenSnapshotSkipsExactThreshold(
            int chargeLevel, bool expected)
        {
            Assert.Equal(expected, PlayerEntity.ShouldStartChargeEffect(
                chargeLevel, minCharge: 20, fullCharge: 60,
                hasEffect: false, fullChargeEffect: false));
            Assert.False(PlayerEntity.ShouldStartChargeEffect(chargeLevel,
                minCharge: 20, fullCharge: 60,
                hasEffect: true, fullChargeEffect: false));
            Assert.False(PlayerEntity.ShouldStartChargeEffect(chargeLevel,
                minCharge: 20, fullCharge: 60,
                hasEffect: false, fullChargeEffect: true));
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
