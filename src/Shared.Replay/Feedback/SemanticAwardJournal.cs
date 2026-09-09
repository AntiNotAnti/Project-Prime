using System;
using System.IO;
using MphRead.Mods.Network;

namespace MphRead.Replay;

/// <summary>
/// Bounded raw semantic-award history for replay consumers. It stores the
/// server fact as received; it never derives an award from kill snapshots.
/// The history retains the newest facts when full, while the separate bounded
/// identity window reconstructs duplicate behavior after a seek.
/// </summary>
public sealed class SemanticAwardJournal
{
    public const int Capacity = 256;
    private readonly MatchAward[] _awards = new MatchAward[Capacity];
    private readonly uint[] _seen = new uint[Capacity];
    private int _count, _head, _seenCount, _seenHead;
    private long _revision;

    public int Count => _count;
    /// <summary>Number of accepted facts since the last reset.</summary>
    public long Revision => _revision;
    public long DuplicateAwards { get; private set; }
    /// <summary>Number of valid facts evicted by the bounded history.</summary>
    public long DroppedAwards { get; private set; }
    public MatchAward this[int index] => (uint)index < (uint)_count
        ? _awards[(_head - _count + index + Capacity) % Capacity]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public bool Record(in MatchAward award)
    {
        if (!award.IsValid) return false;
        if (!Remember(award.AwardId))
        {
            if (DuplicateAwards < long.MaxValue) DuplicateAwards++;
            return false;
        }
        if (_count == Capacity && DroppedAwards < long.MaxValue) DroppedAwards++;
        else _count++;
        _awards[_head] = award;
        _head = (_head + 1) % Capacity;
        if (_revision < long.MaxValue) _revision++;
        return true;
    }

    public int CopyTo(Span<MatchAward> destination)
    {
        int count = Math.Min(destination.Length, _count);
        for (int i = 0; i < count; i++) destination[i] = this[i];
        return count;
    }

    /// <summary>
    /// Copies retained facts after a previously consumed revision. If the
    /// caller fell behind the bounded history, the oldest retained fact is the
    /// deterministic starting point; no old snapshot is reinterpreted.
    /// </summary>
    public int CopySince(long revision, Span<MatchAward> destination)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (destination.Length == 0 || revision >= _revision) return 0;
        long oldestCursor = _revision - _count;
        long firstCursor = Math.Max(revision, oldestCursor);
        int count = (int)Math.Min((long)destination.Length, _revision - firstCursor);
        int start = (int)(firstCursor - oldestCursor);
        for (int i = 0; i < count; i++) destination[i] = this[start + i];
        return count;
    }

    public int ResetAndReplay(ReadOnlySpan<MatchAward> facts)
    {
        Reset();
        int accepted = 0;
        foreach (MatchAward award in facts)
            if (Record(award)) accepted++;
        return accepted;
    }

    public void Reset()
    {
        Array.Clear(_awards);
        Array.Clear(_seen);
        _count = _head = _seenCount = _seenHead = 0;
        _revision = 0;
        DuplicateAwards = DroppedAwards = 0;
    }

    /// <summary>Checkpoint the raw facts and their dedup identity. This is
    /// deliberately numeric and bounded; no announcer strings enter replay.</summary>
    public void Write(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((byte)2);
        writer.Write(_count);
        for (int i = 0; i < _count; i++) WriteAward(writer, this[i]);
        writer.Write(_revision);
        writer.Write(_seenCount);
        writer.Write(_seenHead);
        for (int i = 0; i < _seen.Length; i++) writer.Write(_seen[i]);
    }

    public bool Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            byte version = reader.ReadByte();
            if (version is not (1 or 2)) return false;
            int count = reader.ReadInt32();
            if (count is < 0 or > Capacity) return false;
            var awards = new MatchAward[count];
            for (int i = 0; i < awards.Length; i++)
            {
                awards[i] = ReadAward(reader);
                if (!awards[i].IsValid) return false;
            }
            long revision = version == 2 ? reader.ReadInt64() : count;
            if (revision < count) return false;
            int seenCount = reader.ReadInt32();
            int seenHead = reader.ReadInt32();
            if (seenCount is < 0 or > Capacity || seenHead is < 0 or >= Capacity) return false;
            var seen = new uint[Capacity];
            for (int i = 0; i < seen.Length; i++) seen[i] = reader.ReadUInt32();
            Reset();
            awards.AsSpan().CopyTo(_awards);
            seen.AsSpan().CopyTo(_seen);
            _count = count; _head = count % Capacity; _revision = revision;
            _seenCount = seenCount; _seenHead = seenHead;
            return true;
        }
        catch (Exception error) when (error is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    private bool Remember(uint id)
    {
        for (int i = 0; i < _seenCount; i++)
            if (_seen[(_seenHead - 1 - i + Capacity) % Capacity] == id) return false;
        _seen[_seenHead] = id;
        _seenHead = (_seenHead + 1) % Capacity;
        if (_seenCount < Capacity) _seenCount++;
        return true;
    }

    private static void WriteAward(BinaryWriter writer, in MatchAward award)
    {
        MatchAwardPacket packet = MatchAwardPacketConversion.FromAward(award);
        Span<byte> bytes = stackalloc byte[MatchAwardPacket.Size];
        packet.Write(bytes);
        writer.Write(bytes);
    }

    private static MatchAward ReadAward(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(MatchAwardPacket.Size);
        if (bytes.Length != MatchAwardPacket.Size || !MatchAwardPacket.TryRead(bytes, out MatchAwardPacket packet)
            || !MatchAwardPacketConversion.TryToAward(packet, out MatchAward award))
            throw new InvalidDataException("Invalid semantic award checkpoint.");
        return award;
    }
}
