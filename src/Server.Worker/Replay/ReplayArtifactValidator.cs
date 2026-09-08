using MphRead.Mods.Network;

namespace MphRead.Replay;

public sealed record ReplayArtifactSummary(long RecordCount, int CheckpointCount, uint FirstFrame, uint LastFrame);

/// <summary>Offline structural validation of completed Worker archives using the playback decoder's size,
/// decompression and checksum bounds. This does not certify rendered playback or gameplay outcomes.</summary>
public static class ReplayArtifactValidator
{
    public static ReplayArtifactSummary Validate(string path)
    {
        using var reader = DemoReader.Open(path) ?? throw new InvalidDataException("Invalid replay header or archive.");
        if (reader.FormatVersion != DemoFile.IndexedFormatVersion || reader.ProtocolVersion is not (8 or 9)
            || reader.RecoveredTail)
            throw new InvalidDataException("Expected a complete indexed Worker replay using protocol 8 or 9.");
        long count = 0;
        uint first = 0, last = 0;
        while (reader.ReadNext() is DemoRecord record)
        {
            ValidateRecord(record);
            if (count == 0) first = record.Frame;
            else if (record.Frame < last) throw new InvalidDataException("Replay frames regress.");
            last = record.Frame;
            count++;
        }
        if (count == 0 || last != reader.LastFrame)
            throw new InvalidDataException("Replay has no records or did not decode through its final frame.");
        int checkpoints = 0;
        foreach (ReplayIndexEntry entry in reader.Index)
        {
            if (!entry.Keyframe) continue;
            DemoRecord[] records = reader.Seek(entry.Frame, out uint restored)
                ?? throw new InvalidDataException("Replay checkpoint cannot be decoded.");
            if (restored != entry.Frame) throw new InvalidDataException("Replay checkpoint frame mismatch.");
            int kinds = 0;
            foreach (DemoRecord record in records)
            {
                ValidateRecord(record);
                kinds |= 1 << record.Data[0];
            }
            int required = (1 << (int)DemoRecordKind.Match) | (1 << (int)DemoRecordKind.Snapshot)
                | (1 << (int)DemoRecordKind.World) | (1 << (int)DemoRecordKind.Roster)
                | (1 << (int)DemoRecordKind.Presentation) | (1 << (int)DemoRecordKind.Clock)
                | (1 << (int)DemoRecordKind.Perspective);
            if ((kinds & required) != required)
                throw new InvalidDataException("Replay checkpoint lacks required state records.");
            checkpoints++;
        }
        if (checkpoints == 0) throw new InvalidDataException("Replay has no checkpoint.");
        return new(count, checkpoints, first, last);
    }

    private static void ValidateRecord(DemoRecord record)
    {
        if (record.Data.Length < 2 || !Enum.IsDefined((DemoRecordKind)record.Data[0]))
            throw new InvalidDataException("Replay contains an invalid record envelope.");
    }
}
