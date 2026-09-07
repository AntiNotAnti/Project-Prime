using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Checked readers for protocol 4 gameplay payloads. Validate the entire
    /// packet before a caller changes state. Existing writers and wire layouts
    /// are unchanged. Zero aim is legal for a loading/neutral legacy client.
    /// </summary>
    public static class NetPacketReader
    {
        private const uint KnownButtons = (1u << 21) - 1;

        public static bool TryReadIntent(ReadOnlySpan<byte> source, out IntentPacket packet)
        {
            packet = default;
            if (source.Length != IntentPacket.Size
                || (BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) & ~KnownButtons) != 0
                || !FiniteVector(source[8..]) || !FiniteVector(source[53..])
                || !Weapon(source[20], allowNone: true))
            {
                return false;
            }
            for (int i = 0; i < IntentPacket.PressHistory; i++)
            {
                if ((BinaryPrimitives.ReadUInt32LittleEndian(source[(21 + i * 4)..]) & ~KnownButtons) != 0)
                {
                    return false;
                }
            }
            // Allocate legacy press history only after malformed input has
            // been refused. P3 replaces this array along with IntentPacket.
            packet = IntentPacket.Read(source);
            return true;
        }

        public static bool TryReadPlayerState(ReadOnlySpan<byte> source, out PlayerState packet)
        {
            packet = default;
            if (source.Length != PlayerState.Size || source[0] >= RosterPacket.MaxSlots
                || (source[1] & ~0x3F) != 0 || !Weapon(source[40], allowNone: false)
                || source[41] > (byte)Team.Green
                || (source[43] != 0xFF && source[43] >= RosterPacket.MaxSlots)
                || (source[44] != 0xFF && source[44] > (byte)BeamType.Enemy)
                || !FiniteVector(source[2..]) || !FiniteVector(source[14..])
                || !FiniteVector(source[26..]) || !FiniteVector(source[46..]))
            {
                return false;
            }
            packet = PlayerState.Read(source);
            return true;
        }

        public static bool TryReadSnapshot(ReadOnlySpan<byte> source, out SnapshotHeader header)
        {
            header = default;
            if (source.Length < SnapshotHeader.Size || source.Length >= NetConfig.MaxPacketSize)
            {
                return false;
            }
            int count = source[12];
            if (count > RosterPacket.MaxSlots
                || source.Length != SnapshotHeader.Size + count * PlayerState.Size)
            {
                return false;
            }
            uint slots = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryReadPlayerState(source.Slice(SnapshotHeader.Size + i * PlayerState.Size,
                    PlayerState.Size), out PlayerState state))
                {
                    return false;
                }
                uint bit = 1u << state.SlotIndex;
                if ((slots & bit) != 0)
                {
                    return false;
                }
                slots |= bit;
            }
            header = SnapshotHeader.Read(source);
            return true;
        }

        public static bool TryReadMatchState(ReadOnlySpan<byte> source, out MatchStatePacket packet)
        {
            packet = default;
            if (source.Length != MatchStatePacket.Size
                || source[0] == 1 || source[0] > (byte)GameMode.Unknown15
                || source[9] > RosterPacket.MaxSlots || (source[10] & ~7) != 0)
            {
                return false;
            }
            float remaining = BinaryPrimitives.ReadSingleLittleEndian(source[1..]);
            float elapsed = BinaryPrimitives.ReadSingleLittleEndian(source[5..]);
            if (!Single.IsFinite(remaining) || !Single.IsFinite(elapsed) || remaining < 0 || elapsed < 0)
            {
                return false;
            }
            packet = MatchStatePacket.Read(source);
            return true;
        }

        public static bool TryReadRoster(ReadOnlySpan<byte> source, out RosterPacket packet)
        {
            packet = default;
            if (source.Length != RosterPacket.Size || source[0] > RosterPacket.MaxSlots)
            {
                return false;
            }
            uint slots = 0;
            for (int i = 0; i < source[0]; i++)
            {
                int offset = 1 + i * RosterPacket.EntrySize;
                byte slot = source[offset];
                if (slot >= RosterPacket.MaxSlots || source[offset + 1] > (byte)Hunter.Random
                    || (slots & (1u << slot)) != 0)
                {
                    return false;
                }
                slots |= 1u << slot;
            }
            packet = RosterPacket.Read(source);
            return true;
        }

        private static bool Weapon(byte weapon, bool allowNone)
        {
            return weapon <= (byte)BeamType.OmegaCannon || (allowNone && weapon == 0xFF);
        }

        private static bool FiniteVector(ReadOnlySpan<byte> source)
        {
            return Single.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(source))
                && Single.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(source[4..]))
                && Single.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(source[8..]));
        }
    }
}
