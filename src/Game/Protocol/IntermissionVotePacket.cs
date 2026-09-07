using System;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace MphRead.Mods.Network
{
    public enum IntermissionChoice : byte { Map, Rematch, NextMap, Lobby }
    public readonly record struct IntermissionOption(byte Id, IntermissionChoice Kind, byte Votes, string Label);
    public sealed record IntermissionBallot(uint MatchId, uint PhaseRevision, uint Revision, uint DeadlineTick,
        MatchPhase Phase, bool HasDeadline, byte SelectedId, byte EligiblePlayers, ImmutableArray<IntermissionOption> Options, uint UpdateRevision = 1)
    {
        public const int MaximumOptions = 8, HeaderSize = 28, OptionSize = 36;
        public const int MaximumSize = HeaderSize + MaximumOptions * OptionSize;
        public int Write(Span<byte> bytes)
        {
            int length = HeaderSize + Options.Length * OptionSize;
            if (bytes.Length < length || Options.Length is < 1 or > MaximumOptions) throw new ArgumentException("Invalid ballot.");
            bytes = bytes[..length]; bytes.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], PhaseRevision);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], DeadlineTick);
            bytes[16] = (byte)Phase; bytes[17] = (byte)Options.Length; bytes[18] = SelectedId; bytes[19] = EligiblePlayers; bytes[20] = HasDeadline ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[24..], UpdateRevision);
            for (int i = 0; i < Options.Length; i++)
            {
                var option = Options[i]; Span<byte> entry = bytes.Slice(HeaderSize + i * OptionSize, OptionSize);
                entry[0] = option.Id; entry[1] = (byte)option.Kind; entry[2] = option.Votes;
                NetText.Write(entry[4..], option.Label);
            }
            if (!Validate(bytes)) throw new ArgumentException("Invalid ballot.");
            return length;
        }
        private static bool Validate(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) == 0 || bytes[20] > 1 || (bytes[21] | bytes[22] | bytes[23]) != 0
                || bytes[17] is < 1 or > MaximumOptions || bytes[19] > 8
                || bytes.Length != HeaderSize + bytes[17] * OptionSize
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]) == 0
                || (MatchPhase)bytes[16] is not (MatchPhase.Intermission or MatchPhase.WaitingForPlayers)) return false;
            int ids = 0, votes = 0;
            for (int i = 0; i < bytes[17]; i++)
            {
                var entry = bytes.Slice(HeaderSize + i * OptionSize, OptionSize);
                if (entry[0] is < 1 or > MaximumOptions || (ids & (1 << entry[0])) != 0
                    || entry[1] > (byte)IntermissionChoice.Lobby || entry[2] > 8 || entry[3] != 0 || entry[4] == 0) return false;
                bool ended = false;
                foreach (byte c in entry[4..])
                {
                    if (c == 0) ended = true;
                    else if (ended || c is < 32 or > 126) return false;
                }
                ids |= 1 << entry[0]; votes += entry[2];
            }
            return votes <= bytes[19] && (bytes[18] == 0 || bytes[18] <= MaximumOptions && (ids & (1 << bytes[18])) != 0);
        }
        public static bool TryRead(ReadOnlySpan<byte> bytes, out IntermissionBallot? ballot)
        {
            ballot = null; if (!Validate(bytes)) return false;
            var options = ImmutableArray.CreateBuilder<IntermissionOption>(bytes[17]);
            for (int i = 0; i < bytes[17]; i++)
            {
                var entry = bytes.Slice(HeaderSize + i * OptionSize, OptionSize);
                options.Add(new(entry[0], (IntermissionChoice)entry[1], entry[2], NetText.Read(entry[4..])));
            }
            ballot = new(BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
                (MatchPhase)bytes[16], bytes[20] != 0, bytes[18], bytes[19], options.MoveToImmutable(), BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]));
            return true;
        }
    }
    public readonly record struct IntermissionVoteRequest(uint MatchId, uint PhaseRevision, uint BallotRevision, byte OptionId)
    {
        public const int Size = 16;
        public void Write(Span<byte> bytes)
        {
            if (bytes.Length != Size || MatchId == 0 || PhaseRevision == 0 || BallotRevision == 0 || OptionId is < 1 or > 8)
                throw new ArgumentException("Invalid vote.");
            bytes.Clear(); BinaryPrimitives.WriteUInt32LittleEndian(bytes, MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], PhaseRevision);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], BallotRevision); bytes[12] = OptionId;
        }
        public static bool TryRead(ReadOnlySpan<byte> bytes, out IntermissionVoteRequest vote)
        {
            vote = default;
            if (bytes.Length != Size || (bytes[13] | bytes[14] | bytes[15]) != 0 || bytes[12] is < 1 or > 8) return false;
            vote = new(BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), bytes[12]);
            return vote.MatchId != 0 && vote.PhaseRevision != 0 && vote.BallotRevision != 0;
        }
    }
}
