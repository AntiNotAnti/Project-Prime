using System;
using System.IO;

namespace MphRead.Mods.Network;

internal sealed partial class ReplayPlaybackSession
{
    // CPU session projection only. Scene admission remains closed until both
    // independent pose/fire readers implement the same visibility boundary.
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
                if (packet.Length != 1 || record.Frame != _reader.DurationFrames)
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
        if ((ulong)next == (ulong)origin + _reader.DurationFrames)
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
