using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    [Flags]
    public enum SnapshotPlayerFlags : ushort
    {
        None = 0,
        Active = 1,
        Spawned = 2,
        AltForm = 4,
        Morphing = 8,
        Unmorphing = 16,
        Frozen = 32,
        Spectating = 64,
        Grounded = 128,
        Burning = 256,
        Disrupted = 512,
        Zoomed = 1024,
        RadarReveal = 2048,
        RadarRevealPrevious = 4096,
        WaitingForMatch = 8192,
        SpireAltAttack = 16384,
        All = 32767
    }

    public struct SnapshotPlayer
    {
        public const int Size = 98;
        public byte Slot;
        public Hunter Hunter;
        public byte TeamIndex;
        public byte Weapon;
        public SnapshotPlayerFlags Flags;
        public ushort Health;
        public ushort AmmoUa;
        public ushort AmmoMissiles;
        public uint Life;
        public ulong ConnectionId;
        public Vector3 Position;
        public Vector3 Speed;
        public Vector3 Aim;
        public Vector3 Facing;
        public int Points;
        public int Kills;
        public int Deaths;
        public ushort AvailableWeapons;
        public ushort FrozenTicks;
        public ushort BurnTicks;
        public ushort DisruptTicks;
        public int Assists;
        /// <summary>
        /// Authoritative weapon charge in 60 Hz simulation ticks. Remote
        /// presentation cannot reconstruct this from intermittent snapshots:
        /// without it, every non-local gun remains visually uncharged.
        /// </summary>
        public ushort ChargeLevel;

        public readonly void Write(Span<byte> destination)
        {
            if (destination.Length != Size) throw new ArgumentException("Snapshot player requires exactly 98 bytes.", nameof(destination));
            destination[0] = Slot;
            destination[1] = (byte)Hunter;
            destination[2] = TeamIndex;
            destination[3] = Weapon;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], Health);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], AmmoUa);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[10..], AmmoMissiles);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], Life);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], ConnectionId);
            WriteVector(destination[24..], Position);
            WriteVector(destination[36..], Speed);
            WriteVector(destination[48..], Aim);
            WriteVector(destination[60..], Facing);
            BinaryPrimitives.WriteInt32LittleEndian(destination[72..], Points);
            BinaryPrimitives.WriteInt32LittleEndian(destination[76..], Kills);
            BinaryPrimitives.WriteInt32LittleEndian(destination[80..], Deaths);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[84..], AvailableWeapons);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[86..], FrozenTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[88..], BurnTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[90..], DisruptTicks);
            BinaryPrimitives.WriteInt32LittleEndian(destination[92..], Assists);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[96..], ChargeLevel);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out SnapshotPlayer player)
        {
            player = default;
            if (source.Length != Size || source[0] >= 8 || source[1] > (byte)Hunter.Guardian
                || source[2] >= 8 || source[3] > 8
                || (BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & ~(ushort)SnapshotPlayerFlags.All) != 0
                || BinaryPrimitives.ReadUInt64LittleEndian(source[16..]) == 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[76..]) < 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[80..]) < 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[92..]) < 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[12..]) == 0
                || ((BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & (ushort)SnapshotPlayerFlags.WaitingForMatch) != 0
                    && ((BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & (ushort)(SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned)) != 0
                        || (BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & (ushort)SnapshotPlayerFlags.Spectating) == 0
                        || BinaryPrimitives.ReadUInt16LittleEndian(source[6..]) != 0))
                || (((SnapshotPlayerFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & SnapshotPlayerFlags.Burning) != 0) != (BinaryPrimitives.ReadUInt16LittleEndian(source[88..]) > 0)
                || (((SnapshotPlayerFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & SnapshotPlayerFlags.Disrupted) != 0) != (BinaryPrimitives.ReadUInt16LittleEndian(source[90..]) > 0)
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
                Slot = source[0], Hunter = (Hunter)source[1], TeamIndex = source[2], Weapon = source[3],
                Flags = (SnapshotPlayerFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[4..]),
                Health = BinaryPrimitives.ReadUInt16LittleEndian(source[6..]),
                AmmoUa = BinaryPrimitives.ReadUInt16LittleEndian(source[8..]),
                AmmoMissiles = BinaryPrimitives.ReadUInt16LittleEndian(source[10..]),
                Life = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
                ConnectionId = BinaryPrimitives.ReadUInt64LittleEndian(source[16..]),
                Position = position, Speed = speed, Aim = aim, Facing = facing,
                Points = BinaryPrimitives.ReadInt32LittleEndian(source[72..]),
                Kills = BinaryPrimitives.ReadInt32LittleEndian(source[76..]),
                Deaths = BinaryPrimitives.ReadInt32LittleEndian(source[80..]),
                AvailableWeapons = BinaryPrimitives.ReadUInt16LittleEndian(source[84..]),
                FrozenTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[86..]),
                BurnTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[88..]),
                DisruptTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[90..]),
                Assists = BinaryPrimitives.ReadInt32LittleEndian(source[92..]),
                ChargeLevel = BinaryPrimitives.ReadUInt16LittleEndian(source[96..])
            };
            return true;
        }

        private static bool Finite(Vector3 value) => Single.IsFinite(value.X)
            && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);

        private static Vector3 ReadVector(ReadOnlySpan<byte> source) => new(
            BinaryPrimitives.ReadSingleLittleEndian(source), BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(source[8..]));

        private static void WriteVector(Span<byte> destination, Vector3 value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(destination, value.X);
            BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value.Y);
            BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value.Z);
        }
    }

    public readonly record struct SnapshotPacket(uint ServerTick, uint Sequence, uint MatchId,
        uint LastProcessedInput, bool HasProcessedInput, uint Rng1, uint Rng2)
    {
        public const int HeaderSize = 26;
        public const int MaxSize = HeaderSize + 8 * SnapshotPlayer.Size;

        public int Write(Span<byte> destination, ReadOnlySpan<SnapshotPlayer> players)
        {
            if (players.Length > 8) { throw new ArgumentOutOfRangeException(nameof(players)); }
            BinaryPrimitives.WriteUInt32LittleEndian(destination, ServerTick);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], LastProcessedInput);
            destination[16] = HasProcessedInput ? (byte)1 : (byte)0;
            destination[17] = (byte)players.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[18..], Rng1);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[22..], Rng2);
            for (int i = 0; i < players.Length; i++)
            {
                players[i].Write(destination.Slice(HeaderSize + i * SnapshotPlayer.Size, SnapshotPlayer.Size));
            }
            return HeaderSize + players.Length * SnapshotPlayer.Size;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, Span<SnapshotPlayer> players,
            out SnapshotPacket packet, out int count)
        {
            packet = default;
            count = 0;
            if (source.Length < HeaderSize || source[16] > 1 || source[17] > 8 || source[17] > players.Length
                || source.Length != HeaderSize + source[17] * SnapshotPlayer.Size)
            {
                return false;
            }
            int mask = 0;
            for (int i = 0; i < source[17]; i++)
            {
                if (!SnapshotPlayer.TryRead(source.Slice(HeaderSize + i * SnapshotPlayer.Size, SnapshotPlayer.Size), out SnapshotPlayer decoded)
                    || (mask & (1 << decoded.Slot)) != 0)
                {
                    return false;
                }
                mask |= 1 << decoded.Slot;
            }
            for (int i = 0; i < source[17]; i++)
                SnapshotPlayer.TryRead(source.Slice(HeaderSize + i * SnapshotPlayer.Size, SnapshotPlayer.Size), out players[i]);
            packet = new SnapshotPacket(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[12..]), source[16] != 0,
                BinaryPrimitives.ReadUInt32LittleEndian(source[18..]), BinaryPrimitives.ReadUInt32LittleEndian(source[22..]));
            count = source[17];
            return true;
        }
    }
}
