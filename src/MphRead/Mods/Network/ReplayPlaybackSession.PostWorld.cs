using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Network;

internal sealed partial class ReplayPlaybackSession
{
    // Borrowed synchronous batch. Its capture owner keeps deferred current-frame
    // records for the following step and retains/releases every queued payload.
    internal void AdvancePostWorldInspection(uint frame, IReadOnlyList<ReplayCaptureRecord> records)
    {
        if ((!_live && _clip == null) || !_customInspection || _liveMetadata?.FormatVersion != 8)
            throw new InvalidOperationException("No explicit live post-world inspection is active.");
        try
        {
            bool initial = !_started && frame == _frame && records.Count == 0;
            if (!initial && (ulong)frame != (ulong)_frame + 1)
                throw new InvalidDataException("Post-world inspection must advance exactly one contiguous step.");
            var state = ((PassiveReplaySessionHost)_host).State;
            var context = state.CustomWeapons?.Context
                ?? throw new InvalidDataException("Post-world inspection lost its exact custom scope.");
            int bytes = 0;
            bool ordinary = false;
            if (records.Count > ReplayPostWorldFrameState.MaximumRecords)
                throw new InvalidDataException("Post-world inspection exceeds its combined record budget.");
            // Validate the complete batch before exposing any prefix to the
            // private decoder. Native/custom state acceptance remains its owner.
            foreach (var item in records)
            {
                bool deferred = item.Phase == ReplayCapturePhase.PostWorld;
                if (!Enum.IsDefined(item.Phase) || deferred && ordinary
                    || item.Record.RecordingFrame != (deferred ? _frame : frame))
                    throw new InvalidDataException("Post-world inspection batch has invalid source/phase order.");
                ordinary |= !deferred;
                var packet = ReplayTimelineArchive.PhasePayload(item.Record);
                ReplayPostWorldRecord.ValidatePayload(packet, context);
                ReplayFormatV3.ValidateArchiveRecord(packet, context, 8, _liveMetadata.TimedShotCertificates);
                bytes = checked(bytes + packet.Length + (deferred ? ReplayPostWorldRecord.HeaderBytes : 0));
                if (bytes > ReplayPostWorldFrameState.MaximumBytes)
                    throw new InvalidDataException("Post-world inspection exceeds its combined byte budget.");
            }
            foreach (var item in records)
            {
                if (item.Record.Marker.HasValue) continue;
                var packet = item.Record.Payload;
                if (packet[0] == 254) continue; // Semantic metadata is never gameplay input.
                if (item.Phase == ReplayCapturePhase.PostWorld)
                    state.AcceptPostWorld(packet, item.Record.RecordingFrame, frame);
                else _host.Inject(packet, item.Record.RecordingFrame);
            }
            _frame = frame;
            if (_clip == null) LastFrame = frame;
            _started = true;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Stop(); LastResult = ReplayOpenResult.Corrupt;
            LastError = "Post-world inspection stopped: " + error.Message; throw;
        }
    }

    private bool AdvancePostWorldArchiveInspection()
    {
        uint next = checked(_frame + 1), origin = _reader!.Metadata!.OriginRecordingFrame;
        var state = ((PassiveReplaySessionHost)_host).State;
        var context = state.CustomWeapons?.Context
            ?? throw new InvalidDataException("Post-world inspection lost its exact custom scope.");
        int count = 0, bytes = 0;
        while (_pending is { } record)
        {
            uint source = checked(origin + record.Frame);
            bool deferred = record.Data.Length > 0 && record.Data[0] == ReplayPostWorldRecord.Tag;
            // A post-world next-frame suffix belongs to the following step,
            // even if its storage bytes are already available in lookahead.
            if (source > next || deferred && source == next) break;
            if (deferred ? source != _frame : source != next)
                throw new InvalidDataException("Post-world inspection records regress across their declared phase.");
            if (++count > ReplayPostWorldFrameState.MaximumRecords
                || (bytes = checked(bytes + record.Data.Length)) > ReplayPostWorldFrameState.MaximumBytes)
                throw new InvalidDataException("Post-world inspection exceeds its combined frame budget.");
            ReadOnlySpan<byte> packet = deferred
                ? ReplayPostWorldRecord.ValidateAndGetPayload(record.Data, context) : record.Data;
            if (packet.IsEmpty || packet[0] == 253)
                throw new InvalidDataException("Construction cannot replace an active post-world inspection.");
            if (packet[0] == 255)
            {
                if (packet.Length != 1 || (ulong)record.Frame != (ulong)LeadInFrames + _reader.DurationFrames)
                    throw new InvalidDataException("Post-world quiet markers must close the declared final frame.");
            }
            else if (packet[0] == 254)
            {
                var marker = ReplayTimelineArchive.DecodeMarker(source, packet.ToArray());
                marker.Release();
            }
            else
            {
                var converted = ReplayIdentityCompatibility.NormalizeLegacyParkourPose(packet, _reader.ProtocolVersion);
                if (!converted.IsEmpty)
                {
                    if (deferred) state.AcceptPostWorld(converted, source, next);
                    else _host.Inject(converted, source);
                }
            }
            _pending = _reader.ReadNext();
        }
        if ((ulong)next == (ulong)origin + LeadInFrames + _reader.DurationFrames)
            ConsumePostWorldEnd(_reader, ref _pending, next, count, bytes);
        if (_reader.LastResult != ReplayOpenResult.Success)
            throw new InvalidDataException("Post-world inspection reader failed: " + _reader.LastResult);
        _frame = next; _started = true;
        return true;
    }

    private static void ConsumePostWorldEnd(DemoReader reader, ref DemoRecord? pending, uint frame,
        int count = 0, int bytes = 0)
    {
        // The final deferred phase never changes the held world, but its entire
        // storage suffix must still pass validation, including later chunks.
        while (pending is { } record)
        {
            if ((ulong)record.Frame + reader.Metadata!.OriginRecordingFrame != frame
                || !(record.Data.Length > 0 && record.Data[0] == ReplayPostWorldRecord.Tag
                    || record.Data.Length == 1 && record.Data[0] == 255))
                throw new InvalidDataException("Invalid inert post-world final suffix.");
            if (++count > ReplayPostWorldFrameState.MaximumRecords
                || (bytes = checked(bytes + record.Data.Length)) > ReplayPostWorldFrameState.MaximumBytes)
                throw new InvalidDataException("Post-world final suffix exceeds its combined frame budget.");
            pending = reader.ReadNext();
        }
        if (reader.LastResult != ReplayOpenResult.Success)
            throw new InvalidDataException("Post-world final suffix failed integrity validation: " + reader.LastResult);
    }
}
