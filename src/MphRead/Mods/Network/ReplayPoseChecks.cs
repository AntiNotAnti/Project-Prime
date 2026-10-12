using System;
using System.IO;
using System.Reflection;
using OpenTK.Mathematics;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Network;

internal static class ReplayPoseChecks
{
    internal static void Run(string path, Vector2i size)
    {
        byte[]? bomb = null;
        using (var reader = DemoReader.Open(path)!)
            while (reader.ReadNext() is { } record)
                if (record.Data.Length > 0 && record.Data[0] is (byte)PacketType.BombState or (byte)PacketType.BombEvent)
                { bomb = ReplayIdentityCompatibility.Convert(record.Data, reader.ProtocolVersion).ToArray(); break; }
        if (bomb == null)
        { Console.WriteLine("[replayposes] NOT_RUN: source contains no bomb fact for the sustained lookahead regression."); return; }
        using var source = new PassiveReplayScene(path, size);
        using var reference = new PassiveReplayScene(path, size);
        using var consumer = new PassiveReplayScene(path, size);
        var poses = source.Scene.ReplayPoses!;
        if (!poses.Prepare()) throw new InvalidDataException(poses.LastError);
        using (var indexed = ReplayWorldCheckpoint.Capture(source))
        using (var indexedRoundtrip = ReplayWorldCheckpoint.FromBytes(indexed.Bytes))
        using (var live = new PassiveReplayScene(source.InitialState, source.Session.RecordingFrame, source.MapHash, size))
        using (var liveCapsule = ReplayWorldCheckpoint.Capture(live))
        using (var liveRoundtrip = ReplayWorldCheckpoint.FromBytes(liveCapsule.Bytes))
        {
            if (!indexed.IndexedFirePresentation || !indexedRoundtrip.IndexedFirePresentation
                || liveCapsule.IndexedFirePresentation || liveRoundtrip.IndexedFirePresentation)
                throw new InvalidDataException("World capsule lost its indexed/live firing reconstruction contract.");
            foreach (bool? policy in new bool?[] { null, false, true })
            {
                var metadata = new ReplayMetadata { FormatVersion = policy.HasValue ? (byte)5 : (byte)4, Type = ReplayType.Clip,
                    WorldCheckpoint = liveCapsule.Bytes.ToArray(), IndexedFireContinuation = policy };
                var decoded = ReplayFormatV3.DecodeMetadata(metadata.ProtocolVersion, ReplayFormatV3.EncodeMetadata(metadata), metadata.FormatVersion);
                using var producer = ReplayWorldCheckpoint.FromBytes(decoded.WorldCheckpoint);
                if (decoded.IndexedFireContinuation != policy || producer.IndexedFirePresentation)
                    throw new InvalidDataException("Consumer metadata changed the capsule producer contract.");
            }
        }
        for (int i = 0; i < 4096; i++)
        {
            poses.Accept(source.Session.RecordingFrame, bomb);
            consumer.State.Accept(bomb, consumer.Session.RecordingFrame);
            if (consumer.State.PendingBombPacketCount != 1)
                throw new InvalidDataException("Simulation decoder discarded the real bomb fact.");
            consumer.State.PrepareBombWorld(consumer.Scene);
            consumer.State.ApplyBombPackets(consumer.Scene);
            if (consumer.State.PendingBombPacketCount != 0)
                throw new InvalidDataException("Simulation decoder failed to consume the real bomb fact.");
        }
        var decoder = (ReplayReplicaState)typeof(ReplayPoseStream).GetField("_decoder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(poses)!;
        if (decoder.PendingBombPacketCount != 0 || !poses.Prepare())
            throw new InvalidDataException("Presentation lookahead accumulated bomb facts or failed after a sustained stream.");
        int continued = 0;
        for (; continued < 180 && source.Step(); continued++)
        {
            if (!reference.Step() || !poses.Prepare()
                || !ReplayDeterminism.Projection(source.Scene, source.Session.CurrentFrame).AsSpan()
                    .SequenceEqual(ReplayDeterminism.Projection(reference.Scene, reference.Session.CurrentFrame)))
                throw new InvalidDataException("Bomb lookahead changed subsequent gameplay or presentation.");
        }
        if (continued == 0) throw new InvalidDataException("Bomb lookahead regression has no continuation frames.");
        CheckFullMatchExtraction(path);
        Console.WriteLine($"[replayposes] PASS: 4096 real bomb facts bypass pose lookahead, remain consumed by simulation, and preserve {continued} continuation frames.");
    }
    private static void CheckFullMatchExtraction(string path)
    {
        using var reader = DemoReader.Open(path)!;
        ReplayMetadata metadata = reader.Metadata!;
        if (metadata.Type != ReplayType.FullMatch || metadata.WorldCheckpoint.Length == 0)
        { Console.WriteLine("[replayposes] NOT_RUN: full-match world baseline needed for the export policy regression."); return; }
        byte[] baseline = metadata.WorldCheckpoint;
        ushort version = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(baseline.AsSpan(4));
        if (version is not (5 or 6))
        { Console.WriteLine("[replayposes] NOT_RUN: version 5/6 baseline needed for the export policy regression."); return; }
        // Preserve every value of the recorded baseline. An unindexed producer
        // is conservative provenance; full-match consumers still use indexing.
        baseline = baseline.AsSpan().ToArray();
        if (version == 5) Array.Resize(ref baseline, baseline.Length + 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(baseline.AsSpan(4), 6);
        baseline[^1] = 0;
        string directory = Path.Combine(Path.GetTempPath(), "prime-export-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string full = Path.Combine(directory, "unindexed-producer.ppdemo");
            using (var writer = new ReplayWriterV3(full, new ReplayMetadata
            {
                FormatVersion = 4, ProtocolVersion = metadata.ProtocolVersion, Type = ReplayType.FullMatch,
                RoomKey = metadata.RoomKey, Mode = metadata.Mode, MapHash = metadata.MapHash,
                Players = metadata.Players, Bootstrap = metadata.Bootstrap, BuildId = metadata.BuildId,
                BuildVersion = metadata.BuildVersion, OriginRecordingFrame = metadata.OriginRecordingFrame,
                LeadInFrames = metadata.LeadInFrames, WorldCheckpoint = baseline
            }))
                while (reader.ReadNext() is { } record) writer.WriteRecord(record.Frame, record.Data);
            uint start = Math.Min(300u, reader.DurationFrames / 3);
            uint end = Math.Min(reader.DurationFrames, start + 120);
            string clip = Path.Combine(directory, "indexed-consumer.ppdemo");
            if (ReplayArchive.Extract(full, start, end, clip) != ReplayOpenResult.Success)
                throw new InvalidDataException("Could not extract the full-match consumer policy regression.");
            using (var exported = DemoReader.Open(clip)!)
            using (var producer = ReplayWorldCheckpoint.FromBytes(exported.Metadata!.WorldCheckpoint))
                if (exported.Metadata.IndexedFireContinuation != true || producer.IndexedFirePresentation)
                    throw new InvalidDataException("Full-match extraction lost its consumer policy or relabeled its producer capsule.");
            if (ReplayClipFidelity.Run(full, clip, start) != 0)
                throw new InvalidDataException("Full-match extraction changed indexed gameplay/presentation.");
            Console.WriteLine("[replayposes] PASS: full-match extraction preserves indexed continuation and unindexed producer provenance.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

}
