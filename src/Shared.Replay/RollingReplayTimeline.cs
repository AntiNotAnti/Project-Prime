using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

/// <summary>In-memory authoritative replay facts. Single-owner; no I/O or compression.</summary>
public sealed class RollingReplayTimeline : IReplayTimeline
{
    public const uint DefaultTargetServerTicks = 45 * 60;
    public const long DefaultMaximumPayloadBytes = 64L * 1024 * 1024;

    private sealed class Segment
    {
        internal readonly ReplayRestorePoint Restore;
        internal readonly List<ReplayTimelineRecord> Records = new();
        internal long Bytes;
        internal Segment(ReplayRestorePoint restore) { Restore = restore; Bytes = restore.PayloadBytes; }
    }

    private readonly List<ReplayTimelineRecord> _prefix = new();
    private readonly List<Segment> _segments = new();
    private readonly uint _targetTicks;
    private readonly long _maximumBytes;
    private long _bytes;
    private int _count;
    private uint _lastFrame;
    private uint _newestTick;
    private bool _hasFrame;
    private bool _hasNewestTick;

    public int Count => _count;
    public long PayloadBytes => _bytes;
    public int RestorePointCount => _segments.Count;
    public uint? FirstRecordingFrame => _segments.Count != 0 ? _segments[0].Restore.RecordingFrame
        : _prefix.Count != 0 ? _prefix[0].RecordingFrame : null;
    public uint? LastRecordingFrame => _hasFrame ? _lastFrame : null;
    public uint? FirstServerTick => _segments.Count != 0 ? _segments[0].Restore.ServerTick
        : _prefix.Count != 0 ? _prefix[0].ServerTick : null;
    public uint? LastServerTick => _hasNewestTick ? _newestTick : null;

    public RollingReplayTimeline(uint targetServerTicks = DefaultTargetServerTicks,
        long maximumPayloadBytes = DefaultMaximumPayloadBytes)
    {
        if (targetServerTicks == 0) throw new ArgumentOutOfRangeException(nameof(targetServerTicks));
        if (maximumPayloadBytes is < 1 or > DefaultMaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        _targetTicks = targetServerTicks;
        _maximumBytes = maximumPayloadBytes;
    }

    public bool Append(ReplayTimelineRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (_hasFrame && record.RecordingFrame < _lastFrame) return false;
        if (record.PayloadBytes > _maximumBytes || _bytes + record.PayloadBytes > _maximumBytes)
        {
            EvictForBytes(record.PayloadBytes, preserveNewestRestore: true);
            if (_bytes + record.PayloadBytes > _maximumBytes) return false;
        }
        if (_segments.Count == 0) _prefix.Add(record);
        else
        {
            _segments[^1].Records.Add(record);
            _segments[^1].Bytes += record.PayloadBytes;
        }
        _bytes += record.PayloadBytes;
        _count++;
        Observe(record);
        EvictForAge();
        return true;
    }

    public bool AppendRestorePoint(ReplayRestorePoint restorePoint)
    {
        ArgumentNullException.ThrowIfNull(restorePoint);
        if (_hasFrame && restorePoint.RecordingFrame < _lastFrame
            || restorePoint.PayloadBytes > _maximumBytes) return false;
        while (_bytes + restorePoint.PayloadBytes > _maximumBytes && _segments.Count != 0)
            RemoveOldestSegment();
        if (_bytes + restorePoint.PayloadBytes > _maximumBytes)
        {
            ClearPrefix();
            if (restorePoint.PayloadBytes > _maximumBytes) return false;
        }
        // Facts before the first complete baseline cannot form a restorable clip.
        if (_segments.Count == 0) ClearPrefix();
        _segments.Add(new Segment(restorePoint));
        _bytes += restorePoint.PayloadBytes;
        _count += restorePoint.Records.Count;
        _lastFrame = restorePoint.RecordingFrame;
        _hasFrame = true;
        ObserveTick(restorePoint.ServerTick);
        EvictForAge();
        return true;
    }

    public bool TryMapServerTickToRecordingFrame(uint serverTick, out uint recordingFrame)
    {
        for (int i = 0; i < _prefix.Count; i++)
            if (_prefix[i].ServerTick == serverTick) { recordingFrame = _prefix[i].RecordingFrame; return true; }
        for (int i = 0; i < _segments.Count; i++)
        {
            Segment segment = _segments[i];
            for (int j = 0; j < segment.Restore.Records.Count; j++)
                if (segment.Restore.Records[j].ServerTick == serverTick)
                { recordingFrame = segment.Restore.Records[j].RecordingFrame; return true; }
            for (int j = 0; j < segment.Records.Count; j++)
                if (segment.Records[j].ServerTick == serverTick)
                { recordingFrame = segment.Records[j].RecordingFrame; return true; }
        }
        recordingFrame = 0; return false;
    }

    public bool TryGetRestorePoint(uint recordingFrame, out ReplayRestorePoint? restorePoint)
    {
        restorePoint = null;
        for (int i = 0; i < _segments.Count; i++)
        {
            ReplayRestorePoint candidate = _segments[i].Restore;
            if (candidate.RecordingFrame > recordingFrame) break;
            restorePoint = candidate;
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
        var frozen = new List<ReplayTimelineRecord>();
        for (int i = 0; i < _segments.Count; i++)
        {
            Segment segment = _segments[i];
            if (segment.Restore.RecordingFrame < restore.RecordingFrame) continue;
            if (segment.Restore.RecordingFrame > endRecordingFrame) break;
            // Later restore points are index entries, not sequential records.
            for (int j = 0; j < segment.Records.Count; j++)
            {
                ReplayTimelineRecord record = segment.Records[j];
                if (record.RecordingFrame > endRecordingFrame) break;
                frozen.Add(record);
            }
        }
        clip = new ReplayTimelineClip(restore, frozen.ToArray(), startRecordingFrame, endRecordingFrame);
        return true;
    }

    public void Reset()
    {
        _prefix.Clear(); _segments.Clear();
        _bytes = 0; _count = 0; _lastFrame = 0; _newestTick = 0;
        _hasFrame = false; _hasNewestTick = false;
    }

    private void Observe(ReplayTimelineRecord record)
    {
        _lastFrame = record.RecordingFrame; _hasFrame = true;
        ObserveTick(record.ServerTick);
    }

    private void ObserveTick(uint tick)
    {
        if (!_hasNewestTick || IsNewer(tick, _newestTick))
        { _newestTick = tick; _hasNewestTick = true; }
    }

    private void EvictForAge()
    {
        while (_segments.Count > 1
            && IsNewer(_newestTick, _segments[1].Restore.ServerTick)
            && unchecked(_newestTick - _segments[1].Restore.ServerTick) > _targetTicks)
            RemoveOldestSegment();
    }

    private void EvictForBytes(long incoming, bool preserveNewestRestore)
    {
        int minimum = preserveNewestRestore ? 1 : 0;
        while (_bytes + incoming > _maximumBytes && _segments.Count > minimum)
            RemoveOldestSegment();
        if (_segments.Count == 0) ClearPrefix();
    }

    private void RemoveOldestSegment()
    {
        Segment segment = _segments[0];
        _bytes -= segment.Bytes;
        _count -= segment.Restore.Records.Count + segment.Records.Count;
        _segments.RemoveAt(0);
    }

    private void ClearPrefix()
    {
        for (int i = 0; i < _prefix.Count; i++) _bytes -= _prefix[i].PayloadBytes;
        _count -= _prefix.Count;
        _prefix.Clear();
    }

    private static bool IsNewer(uint value, uint previous)
        => value != previous && unchecked(value - previous) < 0x80000000u;
}
