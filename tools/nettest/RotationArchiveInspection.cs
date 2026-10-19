using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.NetTest;

// Read-only evidence from the production archive reader and detached decoder.
// A caller must independently bind the producer; this reports the reader DLL only.
internal static class RotationArchiveInspection
{
    internal sealed class Identity
    {
        public ulong Epoch { get; set; }
        public ushort Match { get; set; }
        public uint StartGeneration { get; set; }
        public bool ConfigurationRecorded { get; set; }
        public string? Policy { get; set; }
        public string? StartStage { get; set; }
        public string Room { get; set; } = "";
        public uint FirstFrame { get; set; }
        public uint LastFrame { get; set; }
        public bool InProgressSeen { get; set; }
        public int AcceptedEndingRecords { get; set; }
        public uint? FirstAcceptedEndingFrame { get; set; }
        public bool Completed => ConfigurationRecorded && AcceptedEndingRecords > 0;
    }

    internal sealed class Report
    {
        public string Schema { get; set; } = "RotationArchiveInspectionV1";
        public string Status { get; set; } = "FAIL";
        public string Scope { get; set; } = "Production reader/decoder archive inspection; no native playback, producer provenance or device acceptance";
        public string Archive { get; set; } = "";
        public long? BytesBefore { get; set; }
        public long? BytesAfter { get; set; }
        public string? Sha256Before { get; set; }
        public string? Sha256After { get; set; }
        public string ReaderDll { get; set; } = "";
        public string? ReaderDllSha256 { get; set; }
        public string? ReaderBuildId { get; set; }
        public string? RecordedBuildId { get; set; }
        public byte FormatVersion { get; set; }
        public byte ProtocolVersion { get; set; }
        public string? Integrity { get; set; }
        public string? ReaderResult { get; set; }
        public string? MetadataRoom { get; set; }
        public uint OriginRecordingFrame { get; set; }
        public uint DurationFrames { get; set; }
        public long RecordsRead { get; set; }
        public long AcceptedControlRecords { get; set; }
        public long IgnoredControlRecords { get; set; }
        public uint? LastPacketDecoderRecordingFrame { get; set; }
        public long DetachedDrawPacketsDiscarded { get; set; }
        public int MaxPendingBombPacketsInFrame { get; set; }
        public int PendingDrawPacketsAfterEof { get; set; }
        public int CheckpointsValidated { get; set; }
        public bool FullRecordsValidated { get; set; }
        public bool AllCheckpointPayloadsValidated { get; set; }
        public string Completion { get; set; } = "Unvalidated";
        public List<Identity> Identities { get; set; } = new();
        public List<Identity> CompletedIdentities => Identities.Where(x => x.Completed).ToList();
        public List<string> Failures { get; set; } = new();
    }

    internal static int Run(string archive, string output)
    {
        // Exclusive output is part of the CLI contract; never overwrite a receipt.
        if (!Path.IsPathFullyQualified(archive) || !Path.IsPathFullyQualified(output)
            || Path.GetFullPath(archive) == Path.GetFullPath(output))
        {
            Console.Error.WriteLine("Archive and new output must be distinct absolute paths.");
            return 1;
        }
        try
        {
            using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            Report report = Inspect(archive);
            JsonSerializer.Serialize(destination, report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"{report.Status}: {report.Completion}; {report.RecordsRead} records, "
                + $"{report.CompletedIdentities.Count} completed identities; {output}");
            return report.Status == "PASS" ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static (long Bytes, string Sha256) Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    internal static Report Inspect(string archive)
    {
        var report = new Report { Archive = Path.GetFullPath(archive), ReaderDll = typeof(DemoReader).Assembly.Location,
            ReaderBuildId = typeof(DemoReader).Assembly.GetName().Version?.ToString() };
        try
        {
            var before = Hash(report.Archive);
            report.BytesBefore = before.Bytes; report.Sha256Before = before.Sha256;
            report.ReaderDllSha256 = Hash(report.ReaderDll).Sha256;
            Read(report);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { report.Failures.Add(ex.GetType().Name + ": " + ex.Message); }
        finally
        {
            try
            {
                var after = Hash(report.Archive);
                report.BytesAfter = after.Bytes; report.Sha256After = after.Sha256;
                if (report.BytesBefore != after.Bytes || report.Sha256Before != after.Sha256)
                    report.Failures.Add("Archive bytes changed during inspection.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { report.Failures.Add("After inspection: " + ex.Message); }
        }
        if (report.Failures.Count == 0)
        {
            report.Status = "PASS";
            report.Completion = report.CompletedIdentities.Count == 0 ? "HealthyActiveTailNoCompletedIdentities"
                : report.Identities.Any(x => !x.Completed) ? "CompletedIdentitiesAndHealthyActiveTail" : "CompletedIdentities";
        }
        return report;
    }

    // Content-free real containers: these exercise production writer/reader and
    // decoder boundaries, without implying native world/playback acceptance.
    internal static int RunTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rotation-archive-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int checks = 0;
        void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }
        byte[] MatchBytes(MatchStatePacket value)
        {
            byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
            value.Write(packet.AsSpan(1)); return packet;
        }
        byte[] SessionBytes(SessionStatePacket value)
        {
            byte[] packet = new byte[1 + SessionStatePacket.Size]; packet[0] = (byte)PacketType.SessionState;
            value.Write(packet.AsSpan(1)); return packet;
        }
        try
        {
            var match = new MatchStatePacket { AuthorityEpoch = 41, MatchId = 7, RoomKey = "MP1 SANCTORUS",
                NextRoomKey = "", Mode = (byte)GameMode.Battle, TimeRemaining = 300, Flags = MatchStatePacket.FlagInProgress };
            var session = new SessionStatePacket { AuthorityEpoch = 41, MatchId = 7, StartGeneration = 9,
                StartStage = StartStage.InMatch, Phase = SessionPhase.InMatch, Policy = ServerSessionPolicy.Continuous,
                OwnerSlot = byte.MaxValue, MaxPlayers = 8, WorldProfile = MatchWorldProfile.Resolve(8),
                Match = new MatchDefinition { RoomKey = match.RoomKey, Mode = GameMode.Battle, Format = MatchFormat.Auto } };
            string Fixture(string name, MatchStatePacket baseline, Action<ReplayWriterV3>? body = null, uint origin = 0, uint endFrame = 10)
            {
                string path = Path.Combine(directory, name + DemoFile.Extension);
                var metadata = new ReplayMetadata { RoomKey = match.RoomKey, Mode = GameMode.Battle,
                    FormatVersion = origin == 0 ? (byte)3 : (byte)4, OriginRecordingFrame = origin,
                    Bootstrap = new ReplayBootstrap { Packets = new[] { SessionBytes(session), MatchBytes(baseline) } } };
                using (var writer = new ReplayWriterV3(path, metadata))
                { writer.WriteRecord(0, new byte[] { 255 }); body?.Invoke(writer); writer.WriteRecord(endFrame, new byte[] { 255 }); }
                return path;
            }
            string activePath = Fixture("active", match);
            Report active = Inspect(activePath);
            Check(active.Status == "PASS" && active.CompletedIdentities.Count == 0
                && active.Completion == "HealthyActiveTailNoCompletedIdentities", "Healthy active tail must not imply a completed rotation.");
            Check(active.Identities.Count == 1 && active.Identities[0].Epoch == 41 && active.Identities[0].Match == 7
                && active.Identities[0].StartGeneration == 9 && active.Identities[0].Room == match.RoomKey,
                "Complete identity must come from exact recorded Session/Match fields.");
            Check(active.Sha256Before == active.Sha256After && active.BytesBefore == active.BytesAfter
                && active.ReaderDllSha256?.Length == 64 && active.FullRecordsValidated && active.AllCheckpointPayloadsValidated,
                "Production full read and unchanged source/reader hashes are explicit.");
            var ending = match; ending.Flags = MatchStatePacket.FlagEnding;
            Report complete = Inspect(Fixture("complete", match, writer => writer.WriteRecord(5, MatchBytes(ending))));
            Check(complete.Status == "PASS" && complete.CompletedIdentities.Count == 1
                && complete.CompletedIdentities[0].AcceptedEndingRecords == 1
                && complete.CompletedIdentities[0].FirstAcceptedEndingFrame == 5,
                "Actual accepted Ending record completes exactly the recorded world.");
            Report offset = Inspect(Fixture("nonzero-origin", match,
                writer => writer.WriteRecord(5, MatchBytes(ending)), origin: 100));
            Check(offset.Status == "PASS" && offset.LastPacketDecoderRecordingFrame == 105
                && offset.CompletedIdentities[0].FirstAcceptedEndingFrame == 5
                && offset.OriginRecordingFrame == 100 && offset.DurationFrames == 10,
                "Actual decoder receives absolute recording frame while output archive witnesses remain relative.");
            byte[][] EmptyBombPages(uint frame)
            {
                return BombStatePage.PackSnapshot(match.MatchId, match.AuthorityEpoch, frame, 0,
                    Array.Empty<BombState>()).Select(page =>
                    {
                        byte[] packet = new byte[page.Length + 1]; packet[0] = (byte)PacketType.BombState;
                        page.CopyTo(packet, 1); return packet;
                    }).ToArray();
            }
            Report manyFrames = Inspect(Fixture("many-bomb-frames", match, writer =>
            {
                for (uint frame = 1; frame <= 700; frame++)
                    foreach (byte[] packet in EmptyBombPages(frame)) writer.WriteRecord(frame, packet);
                writer.WriteRecord(701, MatchBytes(ending));
            }, endFrame: 701));
            Check(manyFrames.Status == "PASS" && manyFrames.CompletedIdentities.Count == 1
                && manyFrames.DetachedDrawPacketsDiscarded == 2100 && manyFrames.MaxPendingBombPacketsInFrame == 3
                && manyFrames.PendingDrawPacketsAfterEof == 0,
                "Detached analysis drains at frame boundaries instead of retaining thousands of valid prior-frame draw packets.");
            Report frameBudget = Inspect(Fixture("same-frame-bomb-budget", match, writer =>
            {
                byte[] packet = EmptyBombPages(5)[0];
                for (int index = 0; index < 2048; index++) writer.WriteRecord(5, packet);
                writer.WriteRecord(5, MatchBytes(ending));
            }, endFrame: 5));
            Check(frameBudget.Status == "PASS" && frameBudget.MaxPendingBombPacketsInFrame == 2048
                && frameBudget.DetachedDrawPacketsDiscarded == 2048 && frameBudget.PendingDrawPacketsAfterEof == 0,
                "The full existing same-frame bomb queue budget is retained.");
            Report frameOverflow = Inspect(Fixture("same-frame-bomb-overflow", match, writer =>
            {
                byte[] packet = EmptyBombPages(5)[0];
                for (int index = 0; index < 2049; index++) writer.WriteRecord(5, packet);
            }));
            Check(frameOverflow.Status == "FAIL" && frameOverflow.Failures.Any(x => x.Contains("Replay bomb packet queue exceeded its budget")),
                "No per-packet drain may hide a real same-frame queue overflow.");
            Report footer = Inspect(Fixture("footer-only", match,
                writer => writer.WriteEvent(new ReplayEvent(5, ReplayEventType.MatchEnded))));
            Check(footer.Status == "PASS" && footer.CompletedIdentities.Count == 0, "Footer MatchEnded cannot substitute for accepted Ending.");
            var staleEnding = ending; staleEnding.MatchId--;
            Report stale = Inspect(Fixture("stale-ending", match, writer => writer.WriteRecord(5, MatchBytes(staleEnding))));
            Check(stale.Status == "PASS" && stale.CompletedIdentities.Count == 0 && stale.IgnoredControlRecords == 1,
                "A stale Ending rejected by the production decoder cannot complete the current identity.");
            Report endedBaseline = Inspect(Fixture("ended-baseline", ending));
            Check(endedBaseline.Status == "PASS" && endedBaseline.CompletedIdentities.Count == 0,
                "A baseline captured after Ending cannot invent a body Ending record.");
            Report malformed = Inspect(Fixture("malformed-body", match,
                writer => writer.WriteRecord(5, new byte[] { (byte)PacketType.MatchState })));
            Check(malformed.Status == "FAIL" && malformed.Failures.Count > 0, "CRC-valid malformed control bodies fail.");
            var wrongRoom = match; wrongRoom.RoomKey = "MP2 HARVESTER";
            Report room = Inspect(Fixture("wrong-room", match, writer => writer.WriteRecord(5, MatchBytes(wrongRoom))));
            Check(room.Status == "FAIL", "Same identity with a changed room fails.");
            var wrongGeneration = session; wrongGeneration.StartGeneration++;
            Report generation = Inspect(Fixture("wrong-start", match,
                writer => writer.WriteRecord(5, SessionBytes(wrongGeneration))));
            Check(generation.Status == "FAIL", "Conflicting nonzero start generations fail.");
            var wrongSession = session; wrongSession.Match = session.Match with { RoomKey = "MP2 HARVESTER" };
            Report sessionRoom = Inspect(Fixture("wrong-session-room", match,
                writer => writer.WriteRecord(5, SessionBytes(wrongSession))));
            Check(sessionRoom.Status == "FAIL", "Accepted Session and Match rooms must agree.");
            session.StartGeneration = 0;
            Report missingStart = Inspect(Fixture("zero-start", match, writer => writer.WriteRecord(5, MatchBytes(ending))));
            Check(missingStart.Status == "FAIL" && missingStart.CompletedIdentities.Count == 0,
                "Zero start generation outside the continuous initial None stage cannot become completed proof.");
            session.StartStage = StartStage.None;
            Report initialContinuous = Inspect(Fixture("initial-continuous", match,
                writer => writer.WriteRecord(5, MatchBytes(ending))));
            Check(initialContinuous.Status == "PASS" && initialContinuous.CompletedIdentities.Count == 1
                && initialContinuous.CompletedIdentities[0].ConfigurationRecorded
                && initialContinuous.CompletedIdentities[0].StartGeneration == 0,
                "Recorded Continuous/None configuration permits the real initial start generation zero.");
            session.Policy = ServerSessionPolicy.Lobby;
            Report zeroLobby = Inspect(Fixture("zero-lobby", match, writer => writer.WriteRecord(5, MatchBytes(ending))));
            Check(zeroLobby.Status == "FAIL", "Lobby generation zero remains invalid.");
            byte[] truncated = File.ReadAllBytes(activePath);
            string partialPath = Path.Combine(directory, "partial" + DemoFile.Extension);
            File.WriteAllBytes(partialPath, truncated.AsSpan(0, truncated.Length - 1).ToArray());
            Check(Inspect(partialPath).Status == "FAIL", "A partial footer fails finalized archive inspection.");
            string output = Path.Combine(directory, "receipt.json");
            Check(Run(activePath, output) == 0 && File.Exists(output), "CLI writes an exclusive structured receipt.");
            byte[] receipt = File.ReadAllBytes(output);
            Check(Run(activePath, output) != 0 && receipt.AsSpan().SequenceEqual(File.ReadAllBytes(output)),
                "Existing receipt cannot be overwritten.");
            Console.WriteLine($"PASS: {checks} production-linked rotation archive inspection assertions; no native playback claim.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Read(Report report)
    {
        using DemoReader reader = DemoReader.Open(report.Archive, out var opened)
            ?? throw new InvalidDataException("Cannot open archive: " + opened);
        ReplayMetadata metadata = reader.Metadata ?? throw new InvalidDataException("Indexed archive metadata required.");
        report.FormatVersion = reader.FormatVersion; report.ProtocolVersion = reader.ProtocolVersion;
        report.RecordedBuildId = metadata.BuildId; report.MetadataRoom = metadata.RoomKey;
        report.OriginRecordingFrame = metadata.OriginRecordingFrame;
        if (!ReplayIdentityCompatibility.Supports(reader.ProtocolVersion) || metadata.Type != ReplayType.FullMatch
            || metadata.LeadInFrames != 0 || metadata.Recovered || string.IsNullOrEmpty(metadata.RoomKey))
            throw new InvalidDataException("Supported unrecovered full-match archive with exact room and no hidden clip lead-in required.");

        var state = new ReplayReplicaState();
        var worlds = new Dictionary<(ulong Epoch, ushort Match), Identity>();
        Identity World(MatchStatePacket match, uint frame)
        {
            if (match.AuthorityEpoch == 0 || match.MatchId == 0 || string.IsNullOrEmpty(match.RoomKey))
                throw new InvalidDataException("Recorded match has missing lifecycle identity/room.");
            var key = (match.AuthorityEpoch, match.MatchId);
            if (!worlds.TryGetValue(key, out var world))
            {
                if (worlds.Count == 0 && match.RoomKey != metadata.RoomKey)
                    throw new InvalidDataException("Initial recorded match room differs from archive metadata.");
                world = new Identity { Epoch = key.AuthorityEpoch, Match = key.MatchId,
                    Room = match.RoomKey, FirstFrame = frame, LastFrame = frame };
                worlds.Add(key, world); report.Identities.Add(world);
            }
            if (world.Room != match.RoomKey) throw new InvalidDataException("One match identity changed rooms.");
            world.LastFrame = Math.Max(world.LastFrame, frame);
            world.InProgressSeen |= (match.Flags & MatchStatePacket.FlagInProgress) != 0 && !match.Ending;
            return world;
        }
        void Observe(uint frame, bool acceptedEnding)
        {
            if (state.Match is not { } match) return;
            Identity world = World(match, frame);
            if (state.Configuration is { } configuration)
            {
                if (configuration.AuthorityEpoch != match.AuthorityEpoch || configuration.MatchId != match.MatchId
                    || configuration.Match.RoomKey != match.RoomKey)
                    throw new InvalidDataException("Accepted SessionState and MatchState identities/rooms differ.");
                if (configuration.StartGeneration == 0 && (configuration.Policy != ServerSessionPolicy.Continuous
                    || configuration.StartStage != StartStage.None))
                    throw new InvalidDataException("Zero start generation requires recorded Continuous policy and None start stage.");
                if (world.ConfigurationRecorded && (world.StartGeneration != configuration.StartGeneration
                    || world.Policy != configuration.Policy.ToString()))
                    throw new InvalidDataException("One recorded world has conflicting start generations/policies.");
                world.ConfigurationRecorded = true;
                world.StartGeneration = configuration.StartGeneration;
                world.Policy = configuration.Policy.ToString(); world.StartStage = configuration.StartStage.ToString();
            }
            if (acceptedEnding)
            {
                world.AcceptedEndingRecords++;
                world.FirstAcceptedEndingFrame ??= frame;
            }
        }
        void Packet(ReadOnlySpan<byte> original, uint frame, bool actualRecord)
        {
            if (original.IsEmpty) throw new InvalidDataException("Empty archive body record.");
            if (original[0] == 255)
            { if (original.Length != 1) throw new InvalidDataException("Malformed quiet-frame marker."); return; }
            if (original[0] == 254)
            { _ = ReplayTimelineArchive.DecodeMarker(frame, original.ToArray()); return; }
            ReadOnlySpan<byte> packet = ReplayIdentityCompatibility.Convert(original, reader.ProtocolVersion);
            if (packet.IsEmpty) return;
            bool control = packet[0] is (byte)PacketType.MatchState or (byte)PacketType.SessionState;
            MatchStatePacket? incoming = null;
            if (packet[0] == (byte)PacketType.MatchState)
            {
                if (packet.Length != 1 + MatchStatePacket.Size) throw new InvalidDataException("Malformed MatchState record.");
                incoming = MatchStatePacket.Read(packet[1..]);
                // Even an ignored control record must not smuggle a different room for the same world.
                if (worlds.TryGetValue((incoming.Value.AuthorityEpoch, incoming.Value.MatchId), out var known)
                    && known.Room != incoming.Value.RoomKey) throw new InvalidDataException("Conflicting recorded match room.");
            }
            if (packet[0] == (byte)PacketType.SessionState
                && !SessionStatePacket.TryReadArchived(packet[1..], out _))
                throw new InvalidDataException("Malformed SessionState record.");
            long acceptedBefore = state.AcceptedPackets;
            var previousMatch = state.Match;
            state.Accept(packet, checked(metadata.OriginRecordingFrame + frame));
            report.LastPacketDecoderRecordingFrame = state.RecordingFrame;
            report.MaxPendingBombPacketsInFrame = Math.Max(report.MaxPendingBombPacketsInFrame, state.PendingBombPacketCount);
            bool resetMatchAccepted = incoming is { } next && state.Match is { } now
                && next.AuthorityEpoch == now.AuthorityEpoch && next.MatchId == now.MatchId
                && (!previousMatch.HasValue || previousMatch.Value.AuthorityEpoch != now.AuthorityEpoch || previousMatch.Value.MatchId != now.MatchId);
            bool accepted = state.AcceptedPackets == acceptedBefore + 1 || resetMatchAccepted;
            if (control && accepted)
            {
                if (actualRecord) report.AcceptedControlRecords++;
                Observe(frame, actualRecord && incoming?.Ending == true);
            }
            else if (control && actualRecord) report.IgnoredControlRecords++;
            else if (packet[0] == 253) Observe(frame, false);
        }

        if (metadata.WorldCheckpoint.Length != 0)
        {
            using var checkpoint = ReplayWorldCheckpoint.FromBytes(metadata.WorldCheckpoint, metadata.BuildId);
            if (checkpoint.Frame != metadata.OriginRecordingFrame)
                throw new InvalidDataException("Initial world origin differs from archive metadata.");
            state.RestoreCheckpoint(checkpoint.ConstructionState());
            Observe(0, false); // A saved Ending baseline is never a completed-record witness.
        }
        else foreach (byte[] packet in metadata.Bootstrap.Packets) Packet(packet, 0, false);

        void DrainDetachedFrame()
        {
            report.DetachedDrawPacketsDiscarded += state.PendingBombPacketCount;
            state.DiscardDetachedDrawPackets();
        }
        uint archiveFrame = 0;
        while (reader.ReadNext() is { } record)
        {
            // Match the production detached-analysis owner: preserve every
            // same-frame packet and its existing budget until the frame ends.
            if (record.Frame != archiveFrame)
            {
                DrainDetachedFrame();
                archiveFrame = record.Frame;
            }
            report.RecordsRead++;
            Packet(record.Data, record.Frame, true);
        }
        DrainDetachedFrame();
        report.PendingDrawPacketsAfterEof = state.PendingBombPacketCount;
        report.ReaderResult = reader.LastResult.ToString(); report.Integrity = metadata.Integrity.ToString();
        report.DurationFrames = reader.DurationFrames;
        if (reader.LastResult != ReplayOpenResult.Success || metadata.Integrity != ReplayIntegrity.Healthy || report.RecordsRead == 0)
            throw new InvalidDataException("Archive lacks nonempty healthy finalized records: " + reader.LastResult + "/" + metadata.Integrity);
        report.FullRecordsValidated = true;
        foreach (var checkpoint in reader.Checkpoints)
        { _ = reader.ReadCheckpoint(checkpoint); report.CheckpointsValidated++; }
        report.AllCheckpointPayloadsValidated = true;
        if (worlds.Count == 0 || worlds.Values.Any(x => !x.ConfigurationRecorded))
            throw new InvalidDataException("Recorded world lacks a matching SessionState configuration/start identity.");
    }
}
