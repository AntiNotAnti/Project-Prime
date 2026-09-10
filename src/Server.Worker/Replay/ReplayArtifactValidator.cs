using MphRead.Mods.Network;

namespace MphRead.Replay;

public sealed record ReplayArtifactSummary(long RecordCount, int CheckpointCount, uint FirstFrame, uint LastFrame);

/// <summary>Offline structural validation of completed Worker archives using the playback decoder's size,
/// decompression and checksum bounds. This does not certify rendered playback or gameplay outcomes.</summary>
public static class ReplayArtifactValidator
{
    public static ReplayArtifactSummary Validate(string path)
    {
        using var reader = ReplayReader.Open(path) ?? throw new InvalidDataException("Invalid replay header or archive.");
        if (reader.FormatVersion != ReplayFile.IndexedFormatVersion
            || !ReplayFile.IsAuthoritativeProtocol(reader.ProtocolVersion)
            || reader.RecoveredTail)
            throw new InvalidDataException(
                $"Expected a complete indexed Worker replay using protocol 8 through {NetHeader.Version}.");
        long count = 0;
        uint first = 0, last = 0;
        while (reader.ReadNext() is ReplayRecord record)
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
            ReplayRecord[] records = reader.Seek(entry.Frame, out uint restored)
                ?? throw new InvalidDataException("Replay checkpoint cannot be decoded.");
            if (restored != entry.Frame) throw new InvalidDataException("Replay checkpoint frame mismatch.");
            int kinds = 0;
            foreach (ReplayRecord record in records)
            {
                ValidateRecord(record);
                kinds |= 1 << record.Data[0];
            }
            int required = (1 << (int)ReplayRecordKind.Match) | (1 << (int)ReplayRecordKind.Snapshot)
                | (1 << (int)ReplayRecordKind.World) | (1 << (int)ReplayRecordKind.Roster)
                | (1 << (int)ReplayRecordKind.Presentation) | (1 << (int)ReplayRecordKind.Clock)
                | (1 << (int)ReplayRecordKind.Perspective);
            if ((kinds & required) != required)
                throw new InvalidDataException("Replay checkpoint lacks required state records.");
            checkpoints++;
        }
        if (checkpoints == 0) throw new InvalidDataException("Replay has no checkpoint.");
        return new(count, checkpoints, first, last);
    }

    private static void ValidateRecord(ReplayRecord record)
    {
        if (record.Data.Length < 2 || !Enum.IsDefined((ReplayRecordKind)record.Data[0]))
            throw new InvalidDataException("Replay contains an invalid record envelope.");
    }
}
