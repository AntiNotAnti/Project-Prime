using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

/// <summary>An accepted replay fact at its presentation frame and original authoritative tick.</summary>
public sealed class ReplayTimelineRecord
{
    private readonly byte[] _data;

    public uint RecordingFrame { get; }
    public uint ServerTick { get; }
    public ReplayRecordKind Kind { get; }
    public ReplayMarker Marker { get; }
    public ReadOnlyMemory<byte> Data => _data;
    public int PayloadBytes => _data.Length;

    public ReplayTimelineRecord(uint recordingFrame, uint serverTick, ReadOnlySpan<byte> data,
        ReplayMarker marker = ReplayMarker.None)
    {
        if (recordingFrame > ReplayArchive.MaximumFrame)
            throw new ArgumentOutOfRangeException(nameof(recordingFrame));
        if (data.Length is < 1 or > NetConfig.MaxPacketSize
            || !Enum.IsDefined((ReplayRecordKind)data[0]))
            throw new ArgumentException("Invalid replay record envelope.", nameof(data));
        RecordingFrame = recordingFrame;
        ServerTick = serverTick;
        Kind = (ReplayRecordKind)data[0];
        Marker = marker;
        _data = data.ToArray();
    }

    internal ReadOnlySpan<byte> Span => _data;
}

/// <summary>A validated, immutable baseline from which replay can resume without earlier facts.</summary>
public sealed class ReplayRestorePoint
{
    private const int RequiredKinds = (1 << (int)ReplayRecordKind.Match)
        | (1 << (int)ReplayRecordKind.Snapshot) | (1 << (int)ReplayRecordKind.World)
        | (1 << (int)ReplayRecordKind.Roster) | (1 << (int)ReplayRecordKind.Presentation)
        | (1 << (int)ReplayRecordKind.Clock) | (1 << (int)ReplayRecordKind.Perspective);
    private readonly ReplayTimelineRecord[] _records;

    public uint RecordingFrame { get; }
    public uint ServerTick { get; }
    public IReadOnlyList<ReplayTimelineRecord> Records => _records;
    public long PayloadBytes { get; }

    private ReplayRestorePoint(uint recordingFrame, uint serverTick,
        ReplayTimelineRecord[] records, long payloadBytes)
    {
        RecordingFrame = recordingFrame;
        ServerTick = serverTick;
        _records = records;
        PayloadBytes = payloadBytes;
    }

    public static bool TryCreate(uint recordingFrame, uint serverTick,
        IReadOnlyList<ReplayTimelineRecord> records, out ReplayRestorePoint? restorePoint)
    {
        restorePoint = null;
        if (records == null || records.Count is < 1 or > 2048) return false;
        int kinds = 0;
        long bytes = 0;
        int presentationCount = 0;
        int presentationFragments = 0;
        int presentationLength = -1;
        bool[]? seenFragments = null;
        var copy = new ReplayTimelineRecord[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            ReplayTimelineRecord? record = records[i];
            if (record == null || record.RecordingFrame != recordingFrame
                || record.PayloadBytes is < 1 or > NetConfig.MaxPacketSize)
                return false;
            copy[i] = record;
            bytes += record.PayloadBytes;
            if (bytes > ReplayArchive.MaximumRawBytes) return false;
            kinds |= 1 << (int)record.Kind;
            if (record.Kind != ReplayRecordKind.Presentation) continue;
            ReadOnlySpan<byte> data = record.Span;
            if (data.Length < 9) return false;
            int fragment = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
            int fragments = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(data[5..]);
            if (fragments is < 1 or > 2048 || fragment >= fragments
                || length < 0 || length > ReplayArchive.MaximumRawBytes)
                return false;
            if (seenFragments == null)
            {
                seenFragments = new bool[fragments];
                presentationFragments = fragments;
                presentationLength = length;
            }
            else if (presentationFragments != fragments || presentationLength != length)
                return false;
            if (seenFragments[fragment]) return false;
            seenFragments[fragment] = true;
            presentationCount += data.Length - 9;
        }
        if ((kinds & RequiredKinds) != RequiredKinds || seenFragments == null
            || presentationCount != presentationLength)
            return false;
        for (int i = 0; i < seenFragments.Length; i++)
            if (!seenFragments[i]) return false;
        restorePoint = new ReplayRestorePoint(recordingFrame, serverTick, copy, bytes);
        return true;
    }
}

/// <summary>Read-only replay timeline contract shared by file and rolling sources.</summary>
public interface IReplayTimeline
{
    int Count { get; }
    long PayloadBytes { get; }
    uint? FirstRecordingFrame { get; }
    uint? LastRecordingFrame { get; }
    uint? FirstServerTick { get; }
    uint? LastServerTick { get; }
    bool TryMapServerTickToRecordingFrame(uint serverTick, out uint recordingFrame);
    bool TryGetRestorePoint(uint recordingFrame, out ReplayRestorePoint? restorePoint);
    bool TryFreeze(uint startRecordingFrame, uint endRecordingFrame, out ReplayTimelineClip? clip);
}

/// <summary>An immutable clip that remains usable after its source timeline is evicted or reset.</summary>
public sealed class ReplayTimelineClip
{
    private readonly ReplayTimelineRecord[] _records;
    public ReplayRestorePoint RestorePoint { get; }
    public IReadOnlyList<ReplayTimelineRecord> Records => _records;
    public uint StartRecordingFrame { get; }
    public uint EndRecordingFrame { get; }

    internal ReplayTimelineClip(ReplayRestorePoint restorePoint,
        ReplayTimelineRecord[] records, uint startRecordingFrame, uint endRecordingFrame)
    {
        RestorePoint = restorePoint;
        _records = records;
        StartRecordingFrame = startRecordingFrame;
        EndRecordingFrame = endRecordingFrame;
    }
}

internal static class ReplayTimelineTickReader
{
    internal static bool TryRead(ReadOnlySpan<byte> data, uint fallback, out uint tick)
    {
        tick = fallback;
        if (data.Length < 2 || !Enum.IsDefined((ReplayRecordKind)data[0])) return false;
        ReadOnlySpan<byte> body = data[1..];
        switch ((ReplayRecordKind)data[0])
        {
            case ReplayRecordKind.Match:
                if (!MatchTransitionPacket.TryRead(body, out MatchTransitionPacket match)) return false;
                tick = match.ServerTick; return true;
            case ReplayRecordKind.Snapshot:
                if (body.Length < SnapshotPacket.HeaderSize) return false;
                tick = BinaryPrimitives.ReadUInt32LittleEndian(body); return true;
            case ReplayRecordKind.World:
                if (body.Length < WorldPacket.HeaderSize) return false;
                tick = BinaryPrimitives.ReadUInt32LittleEndian(body[8..]); return true;
            case ReplayRecordKind.Event:
                return TryReadEvent(body, fallback, out tick);
            default:
                return true;
        }
    }

    private static bool TryReadEvent(ReadOnlySpan<byte> body, uint fallback, out uint tick)
    {
        tick = fallback;
        if (body.Length < 6) return false;
        ReliableEventType type = (ReliableEventType)body[4];
        ReadOnlySpan<byte> payload = body[5..];
        switch (type)
        {
            case ReliableEventType.Combat:
                Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
                if (!CombatEventBatch.TryRead(payload, events, out int count)) return false;
                tick = events[0].Tick;
                for (int i = 1; i < count; i++)
                    if (IsNewer(events[i].Tick, tick)) tick = events[i].Tick;
                return true;
            case ReliableEventType.Kill:
                if (!KillEvent.TryRead(payload, out KillEvent kill)) return false;
                tick = kill.Tick; return true;
            case ReliableEventType.WorldEvent:
                if (!WorldEvent.TryRead(payload, out WorldEvent world)) return false;
                tick = world.Tick; return true;
            case ReliableEventType.MatchAward:
                if (!MatchAwardPacket.TryRead(payload, out MatchAwardPacket award)) return false;
                tick = award.ServerTick; return true;
            case ReliableEventType.MatchSemantic:
                if (!MatchSemanticEventPacket.TryRead(payload, out MatchSemanticEventPacket semantic)) return false;
                tick = semantic.ServerTick; return true;
            default:
                return true;
        }
    }

    private static bool IsNewer(uint value, uint previous)
        => value != previous && unchecked(value - previous) < 0x80000000u;
}
