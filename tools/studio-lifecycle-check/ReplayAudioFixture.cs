using MphRead.Mods.Network;

/// <summary>Owned canonical recorder fixture for explicit recorded event audio cues.
/// Packet, bootstrap, world checkpoints and existing events are retained; source bytes are never edited.</summary>
internal static class ReplayAudioFixture
{
    internal static void Create(string source, string destination)
    {
        using var reader = DemoReader.Open(source, out var result)
            ?? throw new InvalidDataException("Audio cue fixture source could not open: " + result);
        var metadata = reader.Metadata ?? throw new InvalidDataException("Audio cue fixture requires canonical replay metadata.");
        using var writer = new ReplayWriterV3(destination, metadata);
        while (reader.ReadNext() is { } record) writer.WriteRecord(record.Frame, record.Data);
        if (reader.LastResult != ReplayOpenResult.Success) throw new InvalidDataException("Audio cue fixture packet archive failed: " + reader.LastResult);
        foreach (var checkpoint in reader.Checkpoints) writer.WriteCheckpoint(checkpoint.Frame, reader.ReadCheckpoint(checkpoint));
        foreach (var hash in metadata.ExpectedHashes)
            writer.WriteExpectedHash(hash with { Frame = hash.Frame + metadata.LeadInFrames }, metadata.HashSchema, metadata.HashBuildId);
        foreach (var item in metadata.Events) writer.WriteEvent(item with { Frame = item.Frame + metadata.LeadInFrames });
        writer.WriteEvent(new(2 + metadata.LeadInFrames, ReplayEventType.WeaponFired, 0, byte.MaxValue, 7));
        writer.WriteEvent(new(3 + metadata.LeadInFrames, ReplayEventType.Damage, 0, 1, 27));
    }
}
