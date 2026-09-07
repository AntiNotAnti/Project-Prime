using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    public enum WorldRecordKind : byte { Spawner = 1, Item, Node, Flag, Match, Score, Time, Lifecycle }

    // Version-7 adds explicit lifecycle facts; versions 5/6 are decoded only for demos. Scalar
    // fields have kind-specific meanings; float bits are transported exactly.
    public record struct WorldRecord(WorldRecordKind Kind, byte Slot, ushort Flags,
        uint Id, Vector3 Position, uint A, uint B, uint C, uint D, uint E)
    {
        public const int Size = 40;
        public static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
        public static float Float(uint value) => BitConverter.UInt32BitsToSingle(value);
        public void Write(Span<byte> data)
        {
            data[0] = (byte)Kind; data[1] = Slot;
            BinaryPrimitives.WriteUInt16LittleEndian(data[2..], Flags);
            Put(data, 4, Id); Put(data, 8, Bits(Position.X)); Put(data, 12, Bits(Position.Y)); Put(data, 16, Bits(Position.Z));
            Put(data, 20, A); Put(data, 24, B); Put(data, 28, C); Put(data, 32, D); Put(data, 36, E);
        }
        private static void Put(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);
        private static uint Get(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        public static bool TryRead(ReadOnlySpan<byte> data, out WorldRecord record)
            => TryRead(data, legacy: false, out record);
        internal static bool TryRead(ReadOnlySpan<byte> data, bool legacy, out WorldRecord record)
        {
            record = default;
            if (data.Length != Size) { return false; }
            record = new WorldRecord((WorldRecordKind)data[0], data[1], BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
                Get(data, 4), new Vector3(Float(Get(data, 8)), Float(Get(data, 12)), Float(Get(data, 16))),
                Get(data, 20), Get(data, 24), Get(data, 28), Get(data, 32), Get(data, 36));
            if (!float.IsFinite(record.Position.X) || !float.IsFinite(record.Position.Y) || !float.IsFinite(record.Position.Z)) { return false; }
            return record.Kind switch
            {
                WorldRecordKind.Spawner => record.Slot == 255 && record.Flags <= 1 && record.A <= ushort.MaxValue
                    && record.B <= ushort.MaxValue && (record.C < 8 || record.C == 255),
                WorldRecordKind.Item => record.Slot <= 21 && record.Flags == 0 && record.Id != 0,
                WorldRecordKind.Node => (record.Slot < 8 || record.Slot == 255) && record.Flags <= 7
                    && (record.A & 255) <= 8 && ((record.A >> 8) & 255) <= 8 && (record.A >> 24) == 0
                    && float.IsFinite(Float(record.C)) && Float(record.C) >= 0
                    && float.IsFinite(Float(record.D)) && float.IsFinite(Float(record.E)),
                WorldRecordKind.Flag => (record.Slot < 8 || record.Slot == 255) && record.Flags <= 3
                    && record.A <= 1 && float.IsFinite(Float(record.B)) && Float(record.B) >= 0,
                WorldRecordKind.Match => record.Id == 0 && record.Slot == 255 && record.Flags <= (legacy ? 1 : 3) && record.A is >= 3 and <= 14
                    // Disconnected is session state, never an authoritative match phase.
                    // Validate rule constructor bounds before any world assembly/scene writes.
                    && record.B <= (legacy ? 2u : (uint)MatchPhase.Intermission) && record.C <= int.MaxValue
                    && record.Position.Y >= 0 && record.Position.Y < TimeSpan.MaxValue.TotalSeconds
                    && (record.D < 8 || record.D == uint.MaxValue),
                WorldRecordKind.Lifecycle => !legacy && record.Id == 0 && record.Slot == 255 && record.Flags <= 1
                    && record.C != 0 && record.D == 0 && record.E == 0 && record.Position == Vector3.Zero
                    && (record.Flags == 0 || unchecked(record.B - record.A) <= int.MaxValue),
                WorldRecordKind.Score => record.Id == 0 && record.Slot < 8 && record.Flags == 0,
                WorldRecordKind.Time => record.Id == 0 && record.Slot < 8 && record.Flags == 0
                    && float.IsFinite(Float(record.B)) && float.IsFinite(Float(record.C)),
                _ => false
            };
        }
    }

    public static class WorldPacket
    {
        public const int HeaderSize = 20;
        public const int RecordsPerBatch = 24;
        public const int Capacity = 256;
        public const int MaxSize = HeaderSize + RecordsPerBatch * WorldRecord.Size;
        public static int Write(Span<byte> data, uint matchId, uint revision, uint tick, ReadOnlySpan<WorldRecord> records, int offset)
        {
            if (records.Length is < 1 or > Capacity || offset < 0 || offset >= records.Length) { throw new ArgumentOutOfRangeException(nameof(offset)); }
            int count = Math.Min(RecordsPerBatch, records.Length - offset);
            BinaryPrimitives.WriteUInt32LittleEndian(data, matchId);
            BinaryPrimitives.WriteUInt32LittleEndian(data[4..], revision);
            BinaryPrimitives.WriteUInt32LittleEndian(data[8..], tick);
            BinaryPrimitives.WriteUInt16LittleEndian(data[12..], (ushort)records.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(data[14..], (ushort)offset);
            data[16] = (byte)count; data[17] = data[18] = data[19] = 0;
            for (int i = 0; i < count; i++) { records[offset + i].Write(data.Slice(HeaderSize + i * WorldRecord.Size, WorldRecord.Size)); }
            return HeaderSize + count * WorldRecord.Size;
        }
        public static bool TryValidate(ReadOnlySpan<byte> data, uint matchId) => TryValidate(data, matchId, legacy: false);
        internal static bool TryValidate(ReadOnlySpan<byte> data, uint matchId, bool legacy)
        {
            if (data.Length < HeaderSize || data.Length > MaxSize || matchId == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(data) != matchId || (data[17] | data[18] | data[19]) != 0) { return false; }
            int total = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
            int count = data[16];
            if (total is < 1 or > Capacity || offset % RecordsPerBatch != 0 || offset >= total
                || count != Math.Min(RecordsPerBatch, total - offset) || data.Length != HeaderSize + count * WorldRecord.Size) { return false; }
            for (int i = 0; i < count; i++)
            {
                if (!WorldRecord.TryRead(data.Slice(HeaderSize + i * WorldRecord.Size, WorldRecord.Size), legacy, out _)) { return false; }
            }
            return true;
        }
    }
}
