using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Network;

internal sealed partial class ReplayPlaybackSession
{
    internal bool IsCustomFrozenClip => _customInspection && _clip != null;
    internal void AttachCustomClip(ReplayTimelineClip clip)
    {
        if (!_live || !_customInspection || !_started || _liveMetadata?.FormatVersion != 8
            || clip.CustomArchiveFormat is not (7 or 8) || _frame != clip.RestorePoint.RecordingFrame)
            throw new InvalidOperationException("A custom clip requires its exact restored private world.");
        if (clip.CustomArchiveFormat == 8)
            foreach (var record in clip.Records)
                if (!record.CapturePhase.HasValue)
                    throw new InvalidDataException("A phase clip cannot infer missing capture dispositions.");
        _clip = clip; _clipIndex = 0; _live = false; LastFrame = clip.EndRecordingFrame;
        // A complete WorldK represents ordinary K, while deferred K belongs to
        // the following step and remains retained even when playback is paused.
        while (_clipIndex < clip.Records.Count && clip.Records[_clipIndex].RecordingFrame == _frame
            && clip.Records[_clipIndex].CapturePhase != ReplayCapturePhase.PostWorld) _clipIndex++;
        if (_frame == LastFrame) ValidateCustomClipEnd(0, 0);
    }
    internal bool AdvanceCustomClip()
    {
        try { return AdvanceCustomClipCore(); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Stop(); LastResult = ReplayOpenResult.Corrupt;
            LastError = "Custom clip stopped: " + error.Message; throw;
        }
    }
    private bool AdvanceCustomClipCore()
    {
        if (_clip == null || !_customInspection || _live || _reader != null)
            throw new InvalidOperationException("No custom frozen clip is attached.");
        if (AtEnd) return false;
        uint next = checked(_frame + 1);
        var batch = new List<ReplayCaptureRecord>();
        while (_clipIndex < _clip.Records.Count)
        {
            var record = _clip.Records[_clipIndex];
            var phase = record.CapturePhase ?? ReplayCapturePhase.Ordinary;
            if (record.RecordingFrame > next || phase == ReplayCapturePhase.PostWorld && record.RecordingFrame == next) break;
            if (batch.Count >= ReplayPostWorldFrameState.MaximumRecords)
                throw new InvalidDataException("Custom clip exceeds its combined phase record budget.");
            batch.Add(new(record, phase)); _clipIndex++;
        }
        AdvancePostWorldInspection(next, batch);
        if (next == LastFrame)
        {
            int count = batch.Count, bytes = 0;
            foreach (var item in batch) bytes = checked(bytes + ReplayTimelineArchive.PhasePayload(item.Record).Length
                + (item.Phase == ReplayCapturePhase.PostWorld ? ReplayPostWorldRecord.HeaderBytes : 0));
            ValidateCustomClipEnd(count, bytes);
        }
        return true;
    }
    private void ValidateCustomClipEnd(int count, int bytes)
    {
        while (_clipIndex < _clip!.Records.Count)
        {
            var record = _clip.Records[_clipIndex++];
            if (record.RecordingFrame != LastFrame || record.CapturePhase != ReplayCapturePhase.PostWorld)
                throw new InvalidDataException("Custom clip has an invalid final deferred suffix.");
            var packet = ReplayTimelineArchive.PhasePayload(record);
            ReplayPostWorldRecord.ValidatePayload(packet, ((PassiveReplaySessionHost)_host).State.CustomWeapons!.Context);
            ReplayFormatV3.ValidateArchiveRecord(packet, ((PassiveReplaySessionHost)_host).State.CustomWeapons!.Context,
                8, _liveMetadata!.TimedShotCertificates);
            if (++count > ReplayPostWorldFrameState.MaximumRecords
                || (bytes = checked(bytes + packet.Length + ReplayPostWorldRecord.HeaderBytes)) > ReplayPostWorldFrameState.MaximumBytes)
                throw new InvalidDataException("Custom clip final phase exceeds its combined budget.");
        }
    }
}
