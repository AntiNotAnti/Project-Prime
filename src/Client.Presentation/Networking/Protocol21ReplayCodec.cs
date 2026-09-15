// Frozen protocol-21/22 snapshot layout. Never called by live networking.
using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    internal static class Protocol21ReplayCodec
    {
        private const int HistoricalHeaderSize = 26;
        private const int HistoricalPlayerSize = 104;

        internal static bool TryReadCombatBatch(ReadOnlySpan<byte> bytes,
            Span<CombatEvent> events, out int count)
        {
            if (!CombatEventBatch.TryRead(bytes, events, out count)) return false;
            for (int i = 0; i < count; i++)
            {
                CombatEvent value = events[i];
                if (value.Kind > CombatEventKind.Bomb
                    || (value.Flags & ~(CombatEventFlags)63) != 0)
                {
                    count = 0;
                    return false;
                }
            }
            return true;
        }

        internal static bool TryReadSnapshot(ReadOnlySpan<byte> bytes,
            Span<SnapshotPlayer> players, bool allowGuardianAltAttack,
            out SnapshotPacket packet, out int count)
        {
            packet = default;
            count = 0;
            if (bytes.Length < HistoricalHeaderSize || bytes[16] > 1
                || bytes[17] > 8 || bytes[17] > players.Length
                || bytes.Length != HistoricalHeaderSize + bytes[17] * HistoricalPlayerSize)
            {
                return false;
            }

            int slotMask = 0;
            for (int i = 0; i < bytes[17]; i++)
            {
                ReadOnlySpan<byte> source = bytes.Slice(
                    HistoricalHeaderSize + i * HistoricalPlayerSize,
                    HistoricalPlayerSize);
                if (!TryReadPlayer(source, allowGuardianAltAttack,
                        out SnapshotPlayer decoded)
                    || (slotMask & (1 << decoded.Slot)) != 0)
                {
                    return false;
                }
                slotMask |= 1 << decoded.Slot;
                players[i] = decoded;
            }

            packet = new SnapshotPacket(
                BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
                bytes[16] != 0,
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[18..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[22..]));
            count = bytes[17];
            return true;
        }

        private static bool TryReadPlayer(ReadOnlySpan<byte> source,
            bool allowGuardianAltAttack, out SnapshotPlayer player)
        {
            player = default;
            SnapshotPlayerFlags flags = (SnapshotPlayerFlags)
                BinaryPrimitives.ReadUInt16LittleEndian(source[4..]);
            if (source.Length != HistoricalPlayerSize || source[0] >= 8
                // Protocol 21 already accepted the internal Guardian enum in
                // recorded state. Protocol 22 made its alternate form live.
                || source[1] > (byte)Hunter.Guardian
                || source[2] >= 8 || source[3] > 8
                || ((ushort)flags & ~(ushort)SnapshotPlayerFlags.All) != 0
                // Guardian existed as an internal biped-only value in protocol
                // 21. Reject states that would acquire protocol-22 Psycho Bit
                // semantics when played by the current client.
                || !allowGuardianAltAttack && source[1] == (byte)Hunter.Guardian
                    && (flags & (SnapshotPlayerFlags.AltForm
                        | SnapshotPlayerFlags.Morphing
                        | SnapshotPlayerFlags.Unmorphing
                        | SnapshotPlayerFlags.AltAttack)) != 0
                || BinaryPrimitives.ReadUInt64LittleEndian(source[16..]) == 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[76..]) < 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[80..]) < 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[92..]) < 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[12..]) == 0
                || (flags.TestFlag(SnapshotPlayerFlags.WaitingForMatch)
                    && (flags.TestFlag(SnapshotPlayerFlags.Active)
                        || flags.TestFlag(SnapshotPlayerFlags.Spawned)
                        || !flags.TestFlag(SnapshotPlayerFlags.Spectating)
                        || BinaryPrimitives.ReadUInt16LittleEndian(source[6..]) != 0))
                || (flags.TestFlag(SnapshotPlayerFlags.Burning)
                    != (BinaryPrimitives.ReadUInt16LittleEndian(source[88..]) > 0))
                || (flags.TestFlag(SnapshotPlayerFlags.Disrupted)
                    != (BinaryPrimitives.ReadUInt16LittleEndian(source[90..]) > 0))
                || (flags.TestFlag(SnapshotPlayerFlags.Cloaking)
                    && BinaryPrimitives.ReadUInt16LittleEndian(source[100..]) == 0)
                || (BinaryPrimitives.ReadUInt16LittleEndian(source[84..]) & ~0x1FF) != 0)
            {
                return false;
            }

            Vector3 position = ReadVector(source[24..]);
            Vector3 speed = ReadVector(source[36..]);
            Vector3 aim = ReadVector(source[48..]);
            Vector3 facing = ReadVector(source[60..]);
            if (!Finite(position) || !Finite(speed) || !Finite(aim) || !Finite(facing)
                || aim.LengthSquared < 0.5f || aim.LengthSquared > 1.5f
                || facing.LengthSquared < 0.5f || facing.LengthSquared > 1.5f)
            {
                return false;
            }

            player = new SnapshotPlayer
            {
                Slot = source[0],
                Hunter = (Hunter)source[1],
                TeamIndex = source[2],
                Weapon = source[3],
                Flags = flags,
                Health = BinaryPrimitives.ReadUInt16LittleEndian(source[6..]),
                AmmoUa = BinaryPrimitives.ReadUInt16LittleEndian(source[8..]),
                AmmoMissiles = BinaryPrimitives.ReadUInt16LittleEndian(source[10..]),
                Life = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
                ConnectionId = BinaryPrimitives.ReadUInt64LittleEndian(source[16..]),
                Position = position,
                Speed = speed,
                Aim = aim,
                Facing = facing,
                Points = BinaryPrimitives.ReadInt32LittleEndian(source[72..]),
                Kills = BinaryPrimitives.ReadInt32LittleEndian(source[76..]),
                Deaths = BinaryPrimitives.ReadInt32LittleEndian(source[80..]),
                AvailableWeapons = BinaryPrimitives.ReadUInt16LittleEndian(source[84..]),
                FrozenTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[86..]),
                BurnTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[88..]),
                DisruptTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[90..]),
                Assists = BinaryPrimitives.ReadInt32LittleEndian(source[92..]),
                ChargeLevel = BinaryPrimitives.ReadUInt16LittleEndian(source[96..]),
                DoubleDamageTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[98..]),
                CloakTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[100..]),
                DeathaltTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[102..]),
                EnhancedTargetSlot = 255,
                AltAction = AltActionState.FromLegacy((Hunter)source[1], flags)
            };
            return true;
        }

        private static bool Finite(Vector3 value) => Single.IsFinite(value.X)
            && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);

        private static Vector3 ReadVector(ReadOnlySpan<byte> source) => new(
            BinaryPrimitives.ReadSingleLittleEndian(source),
            BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(source[8..]));
    }
}
