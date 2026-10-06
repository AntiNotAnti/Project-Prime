using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

/// <summary>Single-owner segmented history. Frozen clips can be read on another thread.</summary>
public sealed class RollingReplayTimeline : IReplayTimeline
{
    public const uint DefaultHistoryFrames = 45 * 60;
    public const long DefaultMaximumBytes = 64L * 1024 * 1024;
    private sealed class Segment
    {
        public readonly ReplayRestorePoint Restore;
        public readonly List<ReplayTimelineRecord> Records = new();
        public long Bytes;
        public Segment(ReplayRestorePoint restore) { Restore = restore; Bytes = restore.PayloadBytes; }
    }
    private readonly LinkedList<Segment> _segments = new();
    private uint _historyFrames;
    private readonly long _maximumBytes;
    private uint? _frontier;
    public long PayloadBytes { get; private set; }
    public int RecordCount { get; private set; }
    public int RestorePointCount => _segments.Count;
    public long EvictedSegmentCount { get; private set; }
    public long FreezeFailures { get; private set; }
    public long RejectedRecords { get; private set; }
    public uint? FirstRecordingFrame => _segments.First?.Value.Restore.RecordingFrame;
    public uint? LastRecordingFrame { get; private set; }
    public uint? FirstServerTick => _segments.First?.Value.Restore.ServerTick;
    public uint? LastServerTick { get; private set; }
    public bool NeedsRestorePoint => _segments.Count == 0;

    public RollingReplayTimeline(uint historyFrames = DefaultHistoryFrames, long maximumBytes = DefaultMaximumBytes)
    {
        if (historyFrames == 0) throw new ArgumentOutOfRangeException(nameof(historyFrames));
        if (maximumBytes < 1 || maximumBytes > DefaultMaximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _historyFrames = historyFrames; _maximumBytes = maximumBytes;
    }

    internal void SetHistoryFrames(uint frames)
    {
        frames = Math.Clamp(frames, DefaultHistoryFrames, 125 * 60);
        if (_historyFrames == frames) return;
        _historyFrames = frames;
        TrimAge();
    }

    public bool AdvanceFrame(uint frame, uint serverTick)
    {
        if (_frontier is uint previous && frame < previous) return false;
        _frontier = frame;
        if (_segments.Count == 0) return false;
        Observe(frame, serverTick); TrimAge();
        return true;
    }

    public bool Append(ReplayTimelineRecord record)
    {
        if (_frontier is uint previous && record.RecordingFrame < previous)
        { RejectedRecords++; return false; }
        _frontier = record.RecordingFrame;
        while (_segments.Count > 1 && PayloadBytes + record.PayloadBytes > _maximumBytes) Evict();
        if (_segments.Last == null || PayloadBytes + record.PayloadBytes > _maximumBytes)
        {
            // Never retain a seemingly playable segment containing a dropped fact.
            while (_segments.Count != 0) Evict();
            RejectedRecords++; return false;
        }
        var tail = _segments.Last.Value;
        record.Retain();
        tail.Records.Add(record); tail.Bytes += record.PayloadBytes;
        PayloadBytes += record.PayloadBytes; RecordCount++;
        Observe(record.RecordingFrame, record.ServerTick); TrimAge();
        return true;
    }

    public bool AppendRestorePoint(ReplayRestorePoint restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        if ((_frontier is uint previous && restore.RecordingFrame < previous)
            || (_segments.Last is { } last && restore.RecordingFrame <= last.Value.Restore.RecordingFrame)
            || restore.PayloadBytes > _maximumBytes) { restore.Dispose(); return false; }
        while (_segments.Count != 0 && PayloadBytes + restore.PayloadBytes > _maximumBytes) Evict();
        _segments.AddLast(new Segment(restore));
        PayloadBytes += restore.PayloadBytes; RecordCount += restore.Records.Count;
        _frontier = restore.RecordingFrame;
        Observe(restore.RecordingFrame, restore.ServerTick); TrimAge();
        return true;
    }

    public bool TryGetRestorePoint(uint frame, out ReplayRestorePoint? restorePoint)
    {
        restorePoint = null;
        if (LastRecordingFrame is not uint last || frame > last) return false;
        foreach (var segment in _segments)
        {
            if (segment.Restore.RecordingFrame > frame) break;
            restorePoint = segment.Restore;
        }
        return restorePoint != null;
    }

    // A first-seen repeated fire can reveal that a later world capsule omitted
    // a source-frame shot. Keep the accepted sequential facts, but never let a
    // future freeze restore one of those invalid later worlds.
    internal bool InvalidateWorldRestorePointsFrom(uint frame)
    {
        LinkedListNode<Segment>? valid = null;
        for (var node = _segments.First; node != null && node.Value.Restore.RecordingFrame < frame; node = node.Next)
            valid = node;
        if (valid == null) return false;
        while (valid.Next is { } stale)
        {
            var removed = stale.Value;
            valid.Value.Records.AddRange(removed.Records);
            valid.Value.Bytes += removed.Bytes - removed.Restore.PayloadBytes;
            PayloadBytes -= removed.Restore.PayloadBytes;
            RecordCount -= removed.Restore.Records.Count;
            removed.Records.Clear(); // ownership moved, not retained or released
            removed.Restore.Dispose();
            _segments.Remove(stale);
        }
        return true;
    }

    // Only native fire actually reconstructed by a private correction may fill
    // an absent source-frame marker. Do not announce it again to live subscribers
    // or move the accepted-fact frontier backwards.
    internal bool RecordRecoveredWeaponMarker(uint frame, uint tick, byte slot, int weapon)
    {
        if (LastRecordingFrame is not uint last || frame > last || PayloadBytes + 128 > _maximumBytes) return false;
        Segment? owner = null;
        foreach (var segment in _segments)
        { if (segment.Restore.RecordingFrame > frame) break; owner = segment; }
        if (owner == null) return false;
        int at = 0;
        while (at < owner.Records.Count && owner.Records[at].RecordingFrame < frame) at++;
        for (int i = at; i < owner.Records.Count && owner.Records[i].RecordingFrame == frame; i++)
            if (owner.Records[i].Marker is { Kind: ReplayMarkerKind.WeaponFired } marker
                && marker.Actor == slot && marker.Value == weapon) return false;
        var record = new ReplayTimelineRecord(frame, tick, ReplayFactKind.Event, ReadOnlySpan<byte>.Empty,
            new(ReplayMarkerKind.WeaponFired, slot, byte.MaxValue, weapon));
        owner.Records.Insert(at, record); owner.Bytes += record.PayloadBytes;
        PayloadBytes += record.PayloadBytes; RecordCount++;
        return true;
    }

    public bool TryFreeze(uint startFrame, uint endFrame, out ReplayTimelineClip? clip)
    {
        clip = null;
        if (endFrame < startFrame || LastRecordingFrame is not uint last || endFrame > last
            || !TryGetRestorePoint(startFrame, out var restore) || restore == null)
        { FreezeFailures++; return false; }
        var records = new List<ReplayTimelineRecord>();
        bool include = false;
        foreach (var segment in _segments)
        {
            include |= ReferenceEquals(segment.Restore, restore);
            if (!include) continue;
            if (segment.Restore.RecordingFrame > endFrame) break;
            foreach (var record in segment.Records)
            {
                if (record.RecordingFrame > endFrame) break;
                // A recorder may index an accepted fact and also retain it in
                // the sequential stream for clips starting at earlier baselines.
                // The chosen baseline already applied that exact immutable fact.
                bool inBaseline = false;
                if (ReferenceEquals(segment.Restore, restore))
                    foreach (var baselineRecord in restore.Records)
                        inBaseline |= baselineRecord.SameFact(record);
                if (!inBaseline) records.Add(record);
            }
        }
        clip = new ReplayTimelineClip(restore, records.ToArray(), startFrame, endFrame);
        return true;
    }

    public bool TryMapServerTickToRecordingFrame(uint tick, out uint frame)
    {
        foreach (var segment in _segments)
        {
            if (segment.Restore.ServerTick == tick) { frame = segment.Restore.RecordingFrame; return true; }
            foreach (var record in segment.Records)
                if (record.ServerTick == tick) { frame = record.RecordingFrame; return true; }
        }
        frame = 0; return false;
    }

    public bool TryMapKillToRecordingFrame(ReplayKillIdentity kill, out uint frame)
    {
        foreach (var segment in _segments)
            foreach (var record in segment.Records)
                if (record.Marker is { Kind: ReplayMarkerKind.Kill, Kill: { } identity } && identity == kill)
                { frame = record.RecordingFrame; return true; }
        frame = 0; return false;
    }

    public void Reset()
    {
        while (_segments.Count != 0) Evict();
        _frontier = null;
        PayloadBytes = 0; RecordCount = 0; LastRecordingFrame = LastServerTick = null;
        EvictedSegmentCount = FreezeFailures = RejectedRecords = 0;
    }
    private void Observe(uint frame, uint tick)
    {
        LastRecordingFrame = frame;
        if (LastServerTick is not uint last || unchecked((int)(tick - last)) > 0) LastServerTick = tick;
    }
    private void TrimAge()
    {
        // Keep the baseline immediately preceding the requested history window.
        while (_segments.First?.Next is { } next && LastRecordingFrame is uint last
            && last >= next.Value.Restore.RecordingFrame
            && last - next.Value.Restore.RecordingFrame >= _historyFrames) Evict();
    }
    private void Evict()
    {
        var first = _segments.First!.Value;
        PayloadBytes -= first.Bytes; RecordCount -= first.Restore.Records.Count + first.Records.Count;
        foreach (var record in first.Records) record.Release();
        first.Restore.Dispose();
        _segments.RemoveFirst(); EvictedSegmentCount++;
        if (_segments.Count == 0) LastRecordingFrame = LastServerTick = null;
    }
}
