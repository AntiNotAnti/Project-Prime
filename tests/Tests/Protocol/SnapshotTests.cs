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
            Points = -1, Kills = 4, Deaths = 5, ChargeLevel = 73,
            DoubleDamageTicks = 301, CloakTicks = 302, DeathaltTicks = 303,
            EnhancedTargetSlot = 255
        };

        [Fact]
        public void HardMovementCorrectionPreservesLocalChargePrediction()
        {
            Assert.Equal((ushort)60, PlayerEntity.ResolveSnapshotCharge(
                current: 60, authoritative: 0, fullCharge: 60,
                predicted: false, preservePredictedCharge: true));
            Assert.Equal((ushort)0, PlayerEntity.ResolveSnapshotCharge(
                current: 60, authoritative: 0, fullCharge: 60,
                predicted: false, preservePredictedCharge: false));
        }

        [Fact]
        public void EightPlayersFitOneDatagramAndPreserveRecipientAcknowledgement()
        {
            var players = new SnapshotPlayer[8];
            for (byte i = 0; i < 8; i++) { players[i] = Player(i); }
            var packet = new SnapshotPacket(UInt32.MaxValue, 0, 42, UInt32.MaxValue, true, 123, 456);
            var bytes = new byte[SnapshotPacket.MaxSize];
            Assert.Equal(bytes.Length, packet.Write(bytes, players));
            Assert.True(bytes.Length + NetHeader.Size + NetAuthentication.TagSize
                <= NetConfig.MaxPacketSize);
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
        public void EnhancedSuffixRoundTripsAndRejectsNoncanonicalHunterState()
        {
            SnapshotPlayer samus = Player(0);
            samus.EnhancedTargetSlot = 7;
            samus.EnhancedTargetTicks = 150;
            samus.ChilledTicks = 90;
            byte[] bytes = new byte[SnapshotPlayer.Size];
            samus.Write(bytes);

            Assert.True(SnapshotPlayer.TryRead(bytes, out SnapshotPlayer decoded));
            Assert.Equal((byte)7, decoded.EnhancedTargetSlot);
            Assert.Equal((ushort)150, decoded.EnhancedTargetTicks);
            Assert.Equal((ushort)90, decoded.ChilledTicks);

            bytes[104] = 255;
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));
            samus.Write(bytes);
            bytes[104] = 8;
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));
            samus.Write(bytes);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(106), 151);
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));

            SnapshotPlayer sylux = Player(3);
            sylux.Overcharge = 12;
            sylux.Write(bytes);
            Assert.True(SnapshotPlayer.TryRead(bytes, out decoded));
            Assert.Equal((byte)12, decoded.Overcharge);
            bytes[105] = 13;
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));

            SnapshotPlayer trace = Player((byte)Hunter.Trace);
            trace.CloakFadeTicks = 30;
            trace.Write(bytes);
            Assert.True(SnapshotPlayer.TryRead(bytes, out decoded));
            bytes[110] = 31;
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));

            SnapshotPlayer spire = Player(5);
            spire.EnhancedTargetSlot = 1;
            spire.EnhancedTargetTicks = 1;
            spire.Write(bytes);
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));
        }

        [Fact]
        public void SpireAltAttackAndPowerupFlagsRoundTripAndInvalidCloakIsRejected()
        {
            SnapshotPlayer player = Player(5);
            player.Flags |= SnapshotPlayerFlags.AltForm | SnapshotPlayerFlags.AltAttack;
            byte[] bytes = new byte[SnapshotPlayer.Size];

            player.Write(bytes);

            Assert.True(SnapshotPlayer.TryRead(bytes, out SnapshotPlayer parsed));
            Assert.True((parsed.Flags & SnapshotPlayerFlags.AltAttack) != 0);
            Assert.Equal(player.DoubleDamageTicks, parsed.DoubleDamageTicks);
            Assert.Equal(player.CloakTicks, parsed.CloakTicks);
            Assert.Equal(player.DeathaltTicks, parsed.DeathaltTicks);
            player.Flags |= SnapshotPlayerFlags.Cloaking;
            player.CloakTicks = 0;
            player.Write(bytes);
            Assert.False(SnapshotPlayer.TryRead(bytes, out _));
            player.CloakTicks = 302;
            player.Write(bytes);
            Assert.True(SnapshotPlayer.TryRead(bytes, out parsed));
            Assert.True((parsed.Flags & SnapshotPlayerFlags.Cloaking) != 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Protocol21And22ReplaysKeepTheFrozen104ByteSnapshotDomain(
            bool allowGuardianAltAttack)
        {
            SnapshotPlayer player = Player(7);
            byte[] current = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
            new SnapshotPacket(41, 7, 9, 2, true, 11, 13).Write(current,
                new[] { player });
            byte[] historical = new byte[SnapshotPacket.HeaderSize + 104];
            current.AsSpan(0, SnapshotPacket.HeaderSize).CopyTo(historical);
            current.AsSpan(SnapshotPacket.HeaderSize, 104)
                .CopyTo(historical.AsSpan(SnapshotPacket.HeaderSize));
            var decoded = new SnapshotPlayer[8];

            Assert.True(Protocol21ReplayCodec.TryReadSnapshot(historical, decoded,
                allowGuardianAltAttack,
                out SnapshotPacket header, out int count));
            Assert.Equal(1, count);
            Assert.Equal(player.Slot, decoded[0].Slot);
            Assert.Equal(player.Hunter, decoded[0].Hunter);
            Assert.Equal(player.DoubleDamageTicks, decoded[0].DoubleDamageTicks);
            Assert.Equal(player.CloakTicks, decoded[0].CloakTicks);
            Assert.Equal(player.DeathaltTicks, decoded[0].DeathaltTicks);
            Assert.Equal((byte)255, decoded[0].EnhancedTargetSlot);
            Assert.Equal((byte)0, decoded[0].Overcharge);
            Assert.Equal((ushort)0, decoded[0].EnhancedTargetTicks);
            Assert.Equal((ushort)0, decoded[0].ChilledTicks);
            Assert.Equal((ushort)0, decoded[0].CloakFadeTicks);
            Assert.Equal((uint)41, header.ServerTick);

            byte[] guardianAlt = (byte[])historical.Clone();
            int flagsOffset = SnapshotPacket.HeaderSize + 4;
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(
                guardianAlt.AsSpan(flagsOffset));
            BinaryPrimitives.WriteUInt16LittleEndian(
                guardianAlt.AsSpan(flagsOffset),
                (ushort)(flags | (ushort)SnapshotPlayerFlags.AltForm));
            Assert.Equal(allowGuardianAltAttack,
                Protocol21ReplayCodec.TryReadSnapshot(guardianAlt, decoded,
                    allowGuardianAltAttack, out _, out _));

            byte[] random = (byte[])historical.Clone();
            random[SnapshotPacket.HeaderSize + 1] = (byte)Hunter.Random;
            Assert.False(Protocol21ReplayCodec.TryReadSnapshot(random, decoded,
                allowGuardianAltAttack, out _, out _));

            byte[] malformed = historical[..(SnapshotPacket.HeaderSize + 98)];
            Assert.False(Protocol21ReplayCodec.TryReadSnapshot(malformed, decoded,
                allowGuardianAltAttack, out _, out _));
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
            Assert.Equal((ushort)0, decoded[0].DoubleDamageTicks);
            Assert.Equal((ushort)0, decoded[0].CloakTicks);
            Assert.Equal((ushort)0, decoded[0].DeathaltTicks);
        }

        [Fact]
        public void Protocol20ReplaySnapshotPreservesChargeAndDefaultsPowerupSuffix()
        {
            SnapshotPlayer player = Player(3);
            var packet = new SnapshotPacket(41, 7, 9, 2, true, 11, 13);
            byte[] current = new byte[SnapshotPacket.HeaderSize + SnapshotPlayer.Size];
            packet.Write(current, new[] { player });
            byte[] historical = new byte[SnapshotPacket.HeaderSize + 98];
            current.AsSpan(0, SnapshotPacket.HeaderSize).CopyTo(historical);
            current.AsSpan(SnapshotPacket.HeaderSize, 98)
                .CopyTo(historical.AsSpan(SnapshotPacket.HeaderSize));
            var decoded = new SnapshotPlayer[8];

            Assert.True(Protocol20ReplayCodec.TryReadSnapshot(historical,
                decoded, out SnapshotPacket parsed, out int count));
            Assert.Equal(packet, parsed);
            Assert.Equal(1, count);
            Assert.Equal(player.ChargeLevel, decoded[0].ChargeLevel);
            Assert.Equal((ushort)0, decoded[0].DoubleDamageTicks);
            Assert.Equal((ushort)0, decoded[0].CloakTicks);
            Assert.Equal((ushort)0, decoded[0].DeathaltTicks);
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
