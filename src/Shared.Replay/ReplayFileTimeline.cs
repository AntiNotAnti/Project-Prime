using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

/// <summary>Read-only timeline materialized from an existing indexed replay file.</summary>
public sealed class ReplayFileTimeline : IReplayTimeline
{
    private readonly ReplayTimelineRecord[] _records;
    private readonly ReplayRestorePoint[] _restorePoints;
    private readonly int _count;
    public int Count => _count;
    public long PayloadBytes { get; }
    public uint? FirstRecordingFrame { get; }
    public uint? LastRecordingFrame { get; }
    public uint? FirstServerTick { get; }
    public uint? LastServerTick { get; }

    private ReplayFileTimeline(ReplayTimelineRecord[] records,
        ReplayRestorePoint[] restorePoints, long payloadBytes)
    {
        _records = records;
        _restorePoints = restorePoints;
        PayloadBytes = payloadBytes;
        _count = records.Length;
        for (int i = 0; i < restorePoints.Length; i++)
            _count += restorePoints[i].Records.Count;
        uint? firstFrame = null, lastFrame = null, firstTick = null, lastTick = null;
        for (int i = 0; i < restorePoints.Length; i++)
            Observe(restorePoints[i].RecordingFrame, restorePoints[i].ServerTick,
                ref firstFrame, ref lastFrame, ref firstTick, ref lastTick);
        for (int i = 0; i < records.Length; i++)
            Observe(records[i].RecordingFrame, records[i].ServerTick,
                ref firstFrame, ref lastFrame, ref firstTick, ref lastTick);
        FirstRecordingFrame = firstFrame; LastRecordingFrame = lastFrame;
        FirstServerTick = firstTick; LastServerTick = lastTick;
    }

    public static ReplayFileTimeline? Open(string path)
    {
        using ReplayReader? reader = ReplayReader.Open(path);
        if (reader == null || !ReplayFile.IsAuthoritativeProtocol(reader.ProtocolVersion)) return null;
        var records = new List<ReplayTimelineRecord>();
        var restores = new List<ReplayRestorePoint>();
        long bytes = 0;
        uint tick = 0;
        while (reader.ReadNext() is ReplayRecord raw)
        {
            if (!ReplayTimelineTickReader.TryRead(raw.Data, tick, out uint recordTick)) return null;
            tick = recordTick;
            ReplayTimelineRecord record;
            try { record = new(raw.Frame, tick, raw.Data); }
            catch (ArgumentException) { return null; }
            records.Add(record);
            bytes += record.PayloadBytes;
            if (bytes > ReplayArchive.MaximumDecodedBytesForTimeline) return null;
        }
        if (reader.CanSeek)
        {
            foreach (ReplayIndexEntry entry in reader.Index)
            {
                if (!entry.Keyframe) continue;
                ReplayRecord[]? rawRestore = reader.Seek(entry.Frame, out uint restoredFrame);
                if (rawRestore == null || restoredFrame != entry.Frame) return null;
                var checkpoint = new ReplayTimelineRecord[rawRestore.Length];
                uint restoreTick = tick;
                for (int i = 0; i < rawRestore.Length; i++)
                {
                    if (!ReplayTimelineTickReader.TryRead(rawRestore[i].Data, restoreTick, out uint recordTick)) return null;
                    restoreTick = recordTick;
                    try { checkpoint[i] = new(entry.Frame, recordTick, rawRestore[i].Data); }
                    catch (ArgumentException) { return null; }
                }
                if (!ReplayRestorePoint.TryCreate(entry.Frame, restoreTick, checkpoint, out ReplayRestorePoint? point)
                    || point == null) return null;
                restores.Add(point);
                bytes += point.PayloadBytes;
                if (bytes > ReplayArchive.MaximumDecodedBytesForTimeline) return null;
            }
        }
        return new ReplayFileTimeline(records.ToArray(), restores.ToArray(), bytes);
    }

    public bool TryMapServerTickToRecordingFrame(uint serverTick, out uint recordingFrame)
    {
        for (int i = 0; i < _records.Length; i++)
            if (_records[i].ServerTick == serverTick)
            { recordingFrame = _records[i].RecordingFrame; return true; }
        recordingFrame = 0; return false;
    }

    public bool TryGetRestorePoint(uint recordingFrame, out ReplayRestorePoint? restorePoint)
    {
        restorePoint = null;
        for (int i = 0; i < _restorePoints.Length; i++)
        {
            if (_restorePoints[i].RecordingFrame > recordingFrame) break;
            restorePoint = _restorePoints[i];
        }
        return restorePoint != null;
    }

    public bool TryFreeze(uint startRecordingFrame, uint endRecordingFrame,
        out ReplayTimelineClip? clip)
    {
        clip = null;
        if (endRecordingFrame < startRecordingFrame
            || !TryGetRestorePoint(startRecordingFrame, out ReplayRestorePoint? restore)
            || restore == null) return false;
        var selected = new List<ReplayTimelineRecord>();
        for (int i = 0; i < _records.Length; i++)
        {
            ReplayTimelineRecord record = _records[i];
            if (record.RecordingFrame < restore.RecordingFrame) continue;
            if (record.RecordingFrame > endRecordingFrame) break;
            selected.Add(record);
        }
        clip = new ReplayTimelineClip(restore, selected.ToArray(), startRecordingFrame, endRecordingFrame);
        return true;
    }

    private static void Observe(uint frame, uint tick, ref uint? firstFrame,
        ref uint? lastFrame, ref uint? firstTick, ref uint? lastTick)
    {
        if (!firstFrame.HasValue || frame < firstFrame.Value) firstFrame = frame;
        if (!lastFrame.HasValue || frame > lastFrame.Value) lastFrame = frame;
        if (!firstTick.HasValue || IsNewer(firstTick.Value, tick)) firstTick = tick;
        if (!lastTick.HasValue || IsNewer(tick, lastTick.Value)) lastTick = tick;
    }

    private static bool IsNewer(uint value, uint previous)
        => value != previous && unchecked(value - previous) < 0x80000000u;
}
