using System;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace MphRead.Mods.Network
{
    [Flags]
    public enum MatchSummaryFlags : byte
    {
        None = 0,
        RatingPending = 1,
        RatingEligible = 2
    }

    public readonly record struct MatchSummaryRow(byte Slot, string Name, Hunter Hunter, byte Team,
        byte Placement, bool Bot, int Points, int Kills, int Deaths, int Assists, int Damage,
        int Headshots, int ObjectivePrimary, int ObjectiveSecondary, int ObjectiveTertiary);

    public sealed record MatchSummaryPacket(uint SessionId, uint LobbyRevision, uint MatchId,
        uint DurationSeconds, MatchMode Mode, MatchEndReason EndReason, MatchSummaryFlags Flags,
        string Map, ImmutableArray<MatchSummaryRow> Rows)
    {
        public const int HeaderSize = 60;
        public const int RowSize = 60;
        public const int MaximumRows = 8;
        public const int MaximumSize = HeaderSize + MaximumRows * RowSize;

        public int Write(Span<byte> destination)
        {
            int length = HeaderSize + Rows.Length * RowSize;
            if (destination.Length < length || Rows.Length > MaximumRows)
                throw new ArgumentException("Invalid match summary capacity.", nameof(destination));
            if (!ValidText(Map, 40)) throw new ArgumentException("Map must be bounded printable ASCII.", nameof(Map));
            foreach (MatchSummaryRow row in Rows)
                if (!ValidText(row.Name, ChatPacket.MaxNameBytes))
                    throw new ArgumentException("Player names must be bounded printable ASCII.", nameof(Rows));
            Span<byte> packet = destination[..length];
            packet.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(packet, SessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[4..], LobbyRevision);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[8..], MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet[12..], DurationSeconds);
            packet[16] = (byte)Mode;
            packet[17] = (byte)EndReason;
            packet[18] = (byte)Rows.Length;
            packet[19] = (byte)Flags;
            NetText.Write(packet.Slice(20, 40), Map);
            for (int i = 0; i < Rows.Length; i++) WriteRow(packet.Slice(HeaderSize + i * RowSize, RowSize), Rows[i]);
            if (!Validate(packet)) throw new ArgumentException("Invalid match summary.", nameof(destination));
            return length;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out MatchSummaryPacket? summary)
        {
            summary = null;
            if (!Validate(source)) return false;
            var rows = ImmutableArray.CreateBuilder<MatchSummaryRow>(source[18]);
            for (int i = 0; i < source[18]; i++)
            {
                ReadOnlySpan<byte> row = source.Slice(HeaderSize + i * RowSize, RowSize);
                rows.Add(new MatchSummaryRow(row[0], NetText.Read(row.Slice(8, ChatPacket.MaxNameBytes)),
                    (Hunter)row[1], row[2], row[3], row[4] == 1,
                    BinaryPrimitives.ReadInt32LittleEndian(row[24..]), BinaryPrimitives.ReadInt32LittleEndian(row[28..]),
                    BinaryPrimitives.ReadInt32LittleEndian(row[32..]), BinaryPrimitives.ReadInt32LittleEndian(row[36..]),
                    BinaryPrimitives.ReadInt32LittleEndian(row[40..]), BinaryPrimitives.ReadInt32LittleEndian(row[44..]),
                    BinaryPrimitives.ReadInt32LittleEndian(row[48..]), BinaryPrimitives.ReadInt32LittleEndian(row[52..]),
                    BinaryPrimitives.ReadInt32LittleEndian(row[56..])));
            }
            summary = new MatchSummaryPacket(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[12..]), (MatchMode)source[16], (MatchEndReason)source[17],
                (MatchSummaryFlags)source[19], NetText.Read(source.Slice(20, 40)), rows.MoveToImmutable());
            return true;
        }

        private static void WriteRow(Span<byte> row, MatchSummaryRow value)
        {
            row[0] = value.Slot; row[1] = (byte)value.Hunter; row[2] = value.Team;
            row[3] = value.Placement; row[4] = value.Bot ? (byte)1 : (byte)0;
            NetText.Write(row.Slice(8, ChatPacket.MaxNameBytes), value.Name);
            BinaryPrimitives.WriteInt32LittleEndian(row[24..], value.Points);
            BinaryPrimitives.WriteInt32LittleEndian(row[28..], value.Kills);
            BinaryPrimitives.WriteInt32LittleEndian(row[32..], value.Deaths);
            BinaryPrimitives.WriteInt32LittleEndian(row[36..], value.Assists);
            BinaryPrimitives.WriteInt32LittleEndian(row[40..], value.Damage);
            BinaryPrimitives.WriteInt32LittleEndian(row[44..], value.Headshots);
            BinaryPrimitives.WriteInt32LittleEndian(row[48..], value.ObjectivePrimary);
            BinaryPrimitives.WriteInt32LittleEndian(row[52..], value.ObjectiveSecondary);
            BinaryPrimitives.WriteInt32LittleEndian(row[56..], value.ObjectiveTertiary);
        }

        private static bool Validate(ReadOnlySpan<byte> source)
        {
            if (source.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(source) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) == 0
                || source[16] > (byte)MatchMode.PrimeHunter || !Enum.IsDefined((MatchEndReason)source[17])
                || source[18] > MaximumRows
                || (source[19] & ~(byte)(MatchSummaryFlags.RatingPending | MatchSummaryFlags.RatingEligible)) != 0
                || source.Length != HeaderSize + source[18] * RowSize
                || !SessionRosterPacket.IsText(source.Slice(20, 40), required: true)) return false;
            int slots = 0;
            int placements = 0;
            for (int i = 0; i < source[18]; i++)
            {
                ReadOnlySpan<byte> row = source.Slice(HeaderSize + i * RowSize, RowSize);
                if (row[0] >= MaximumRows || (slots & (1 << row[0])) != 0 || row[1] > (byte)Hunter.Guardian
                    || row[2] >= MaximumRows || row[3] is < 1 || row[3] > source[18]
                    || (placements & (1 << row[3])) != 0 || row[4] > 1 || (row[5] | row[6] | row[7]) != 0
                    || !SessionRosterPacket.IsText(row.Slice(8, ChatPacket.MaxNameBytes), required: true)
                    || BinaryPrimitives.ReadInt32LittleEndian(row[28..]) < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(row[32..]) < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(row[36..]) < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(row[40..]) < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(row[44..]) < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(row[48..]) < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(row[52..]) < 0
                    || BinaryPrimitives.ReadInt32LittleEndian(row[56..]) < 0) return false;
                slots |= 1 << row[0];
                placements |= 1 << row[3];
            }
            return true;
        }

        private static bool ValidText(string? value, int maximum)
        {
            if (value == null || value.Length > maximum || String.IsNullOrWhiteSpace(value)) return false;
            foreach (char character in value) if (character is < ' ' or > '~') return false;
            return true;
        }
    }
}
