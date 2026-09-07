using System;
using System.Buffers.Binary;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests
{
    public sealed class ProtocolTests
    {
        internal static IntentPacket SampleIntent => new()
        {
            Frame = 0x12345678,
            Buttons = IntentButtons.MoveUp | IntentButtons.Shoot,
            Aim = Vector3.UnitZ,
            Position = new Vector3(12.5f, -4, 9),
            WeaponSelect = 0xFF,
            AmmoUa = 150,
            AmmoMissiles = 10,
            Presses = new uint[] { 64, 128, 0, 16, 0, 0, 512, 0 }
        };

        internal static PlayerState SampleState => new()
        {
            SlotIndex = 7,
            Flags = PlayerState.FlagActive | PlayerState.FlagSpawned,
            Position = new Vector3(3, 4, 5),
            Speed = new Vector3(-1, 0.5f, 2),
            Facing = Vector3.UnitZ,
            Health = 99,
            CurrentWeapon = (byte)BeamType.Imperialist,
            Team = (byte)Team.Orange,
            DamageSeq = 255,
            AttackerSlot = 0xFF,
            DamageBeam = 0xFF,
            HitDirection = -Vector3.UnitX,
            Points = -2,
            Kills = 35,
            Deaths = 40
        };

        [Fact]
        public void IntentRoundTripPreservesAllFieldsAndLittleEndianLayout()
        {
            byte[] bytes = new byte[IntentPacket.Size];
            SampleIntent.Write(bytes);
            Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, bytes[..4]);
            Assert.Equal(1f, BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(16)));
            Assert.True(NetPacketReader.TryReadIntent(bytes, out IntentPacket read));
            Assert.Equal(SampleIntent.Frame, read.Frame);
            Assert.Equal(SampleIntent.Buttons, read.Buttons);
            Assert.Equal(SampleIntent.Presses, read.Presses);
            Assert.Equal(SampleIntent.Aim, read.Aim);
            Assert.Equal(SampleIntent.Position, read.Position);
            Assert.Equal(SampleIntent.AmmoUa, read.AmmoUa);
            Assert.Equal(SampleIntent.AmmoMissiles, read.AmmoMissiles);
            Assert.Equal(SampleIntent.WeaponSelect, read.WeaponSelect);
            byte[] encoded = new byte[bytes.Length];
            read.Write(encoded);
            Assert.Equal(bytes, encoded);
        }

        [Fact]
        public void EightPlayerSnapshotFitsMtuAndRoundTrips()
        {
            byte[] bytes = Snapshot();
            Assert.True(bytes.Length + 1 <= NetConfig.MaxPacketSize);
            Assert.True(NetPacketReader.TryReadSnapshot(bytes, out SnapshotHeader header));
            Assert.Equal(RosterPacket.MaxSlots, header.PlayerCount);
            Assert.Equal(UInt32.MaxValue, header.Frame);
            Assert.Equal(0x12345678u, header.Rng1);
            Assert.Equal(0xDEADBEEFu, header.Rng2);
            for (int slot = 0; slot < RosterPacket.MaxSlots; slot++)
            {
                ReadOnlySpan<byte> entry = bytes.AsSpan(SnapshotHeader.Size + slot * PlayerState.Size,
                    PlayerState.Size);
                Assert.True(NetPacketReader.TryReadPlayerState(entry, out PlayerState state));
                Assert.Equal(slot, state.SlotIndex);
                byte[] encoded = new byte[PlayerState.Size];
                state.Write(encoded);
                Assert.Equal(entry.ToArray(), encoded);
                Assert.Equal(SampleState.Position, state.Position);
                Assert.Equal(SampleState.Points, state.Points);
            }
        }

        [Fact]
        public void EveryShortIntentAndSnapshotIsRejectedWithoutExceptions()
        {
            byte[] intent = new byte[IntentPacket.Size];
            SampleIntent.Write(intent);
            for (int length = 0; length < intent.Length; length++)
            {
                Assert.False(NetPacketReader.TryReadIntent(intent.AsSpan(0, length), out _));
            }
            byte[] snapshot = Snapshot();
            for (int length = 0; length < snapshot.Length; length++)
            {
                Assert.False(NetPacketReader.TryReadSnapshot(snapshot.AsSpan(0, length), out _));
            }
            Assert.False(NetPacketReader.TryReadIntent(new byte[IntentPacket.Size + 1], out _));
            Assert.False(NetPacketReader.TryReadSnapshot(new byte[NetConfig.MaxPacketSize + 1], out _));
        }

        [Theory]
        [InlineData(Single.NaN)]
        [InlineData(Single.PositiveInfinity)]
        [InlineData(Single.NegativeInfinity)]
        public void NonfiniteVectorsAreRejected(float invalid)
        {
            byte[] bytes = new byte[IntentPacket.Size];
            foreach (int offset in new[] { 8, 12, 16, 53, 57, 61 })
            {
                SampleIntent.Write(bytes);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), invalid);
                Assert.False(NetPacketReader.TryReadIntent(bytes, out _));
            }
            bytes = new byte[PlayerState.Size];
            foreach (int offset in new[] { 2, 6, 10, 14, 18, 22, 26, 30, 34, 46, 50, 54 })
            {
                SampleState.Write(bytes);
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), invalid);
                Assert.False(NetPacketReader.TryReadPlayerState(bytes, out _));
            }
        }

        [Fact]
        public void UnknownButtonsWeaponsSlotsAndDuplicateSnapshotSlotsAreRejected()
        {
            byte[] bytes = new byte[IntentPacket.Size];
            SampleIntent.Write(bytes);
            bytes[7] = 0x80;
            Assert.False(NetPacketReader.TryReadIntent(bytes, out _));
            SampleIntent.Write(bytes);
            bytes[20] = 254;
            Assert.False(NetPacketReader.TryReadIntent(bytes, out _));
            SampleIntent.Write(bytes);
            bytes[24] = 0x80;
            Assert.False(NetPacketReader.TryReadIntent(bytes, out _));

            bytes = Snapshot();
            bytes[SnapshotHeader.Size] = (byte)RosterPacket.MaxSlots;
            Assert.False(NetPacketReader.TryReadSnapshot(bytes, out _));
            bytes = Snapshot();
            bytes[SnapshotHeader.Size + PlayerState.Size] = 0;
            Assert.False(NetPacketReader.TryReadSnapshot(bytes, out _));
            bytes = Snapshot();
            bytes[12] = (byte)(RosterPacket.MaxSlots + 1);
            Assert.False(NetPacketReader.TryReadSnapshot(bytes, out _));
        }

        [Fact]
        public void MatchAndRosterRejectInvalidCountsEnumsAndTimes()
        {
            var match = new MatchStatePacket
            {
                Mode = (byte)GameMode.Battle,
                RoomKey = "MP1 SANCTORUS",
                NextRoomKey = "MP3 PROVING GROUND",
                TimeElapsed = 42,
                TimeRemaining = 60,
                PlayerCount = 8,
                PointGoal = 15,
                MatchId = 123,
                Flags = MatchStatePacket.FlagInProgress
            };
            byte[] bytes = new byte[MatchStatePacket.Size];
            match.Write(bytes);
            Assert.True(NetPacketReader.TryReadMatchState(bytes, out MatchStatePacket read));
            Assert.Equal(match.RoomKey, read.RoomKey);
            Assert.Equal(match.MatchId, read.MatchId);
            Assert.False(NetPacketReader.TryReadMatchState(bytes.AsSpan(1), out _));
            bytes[0] = 1; // Gap in GameMode, not a valid enum member.
            Assert.False(NetPacketReader.TryReadMatchState(bytes, out _));
            match.Write(bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(5), Single.NaN);
            Assert.False(NetPacketReader.TryReadMatchState(bytes, out _));

            RosterPacket roster = RosterPacket.Create();
            roster.Count = 8;
            for (byte slot = 0; slot < roster.Count; slot++)
            {
                roster.Slots[slot] = slot;
                roster.Hunters[slot] = slot;
                roster.Pings[slot] = (ushort)(slot * 10);
                roster.Names[slot] = $"Player{slot}";
            }
            bytes = new byte[RosterPacket.Size];
            roster.Write(bytes);
            Assert.True(NetPacketReader.TryReadRoster(bytes, out RosterPacket result));
            Assert.Equal(roster.Names, result.Names);
            Assert.Equal(roster.Pings, result.Pings);
            bytes[0] = 9;
            Assert.False(NetPacketReader.TryReadRoster(bytes, out _));
            roster.Write(bytes);
            bytes[2] = 255;
            Assert.False(NetPacketReader.TryReadRoster(bytes, out _));
        }

        [Fact]
        public void ParsersTolerateRandomMalformedBytes()
        {
            var random = new Random(0xF017);
            byte[] data = new byte[NetConfig.MaxPacketSize + 32];
            for (int i = 0; i < 20000; i++)
            {
                random.NextBytes(data);
                ReadOnlySpan<byte> source = data.AsSpan(0, random.Next(data.Length));
                NetPacketReader.TryReadIntent(source, out _);
                NetPacketReader.TryReadSnapshot(source, out _);
                NetPacketReader.TryReadPlayerState(source, out _);
                NetPacketReader.TryReadMatchState(source, out _);
                NetPacketReader.TryReadRoster(source, out _);
            }
        }

        internal static byte[] Snapshot()
        {
            byte[] bytes = new byte[SnapshotHeader.Size + RosterPacket.MaxSlots * PlayerState.Size];
            new SnapshotHeader
            {
                Frame = UInt32.MaxValue,
                Rng1 = 0x12345678,
                Rng2 = 0xDEADBEEF,
                PlayerCount = RosterPacket.MaxSlots
            }.Write(bytes);
            for (byte slot = 0; slot < RosterPacket.MaxSlots; slot++)
            {
                PlayerState state = SampleState;
                state.SlotIndex = slot;
                state.Write(bytes.AsSpan(SnapshotHeader.Size + slot * PlayerState.Size));
            }
            return bytes;
        }
    }
}
