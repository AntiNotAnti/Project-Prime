using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class MomentumReplayMetadataTests
{
    private const string Room = "MP3 PROVING GROUND";
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static byte[] Match(MovementModel model)
    {
        byte[] packet = new byte[1 + MatchStatePacket.Size]; packet[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { RoomKey = Room, MatchId = 7, AuthorityEpoch = 23,
            Mode = (byte)GameMode.Battle, RuleBits = (uint)MovementModelRules.Modifier(model) }.Write(packet.AsSpan(1));
        return packet;
    }
    private static byte[] Session(MovementModel model)
    {
        byte[] packet = new byte[1 + SessionStatePacket.Size]; packet[0] = (byte)PacketType.SessionState;
        new SessionStatePacket { MatchId = 7, AuthorityEpoch = 23, Phase = SessionPhase.InMatch,
            MaxPlayers = 8, WorldProfile = MatchWorldProfile.Resolve(8),
            Match = new MatchDefinition { RoomKey = Room, Mode = GameMode.Battle,
                TimeLimitSeconds = 300, MovementModel = model } }.Write(packet.AsSpan(1));
        return packet;
    }
    private static ReplayMetadata Metadata(byte protocol, params byte[][] packets) => new()
    { RoomKey = Room, ProtocolVersion = protocol, Bootstrap = new() { Packets = packets } };

    internal static int Run()
    {
        _checks = 0;
        foreach (MovementModel model in new[] { MovementModel.Native, MovementModel.Momentum, MovementModel.Quake3 })
        {
            var decoder = new ReplayReplicaState(); decoder.Accept(Match(model), 0); decoder.Accept(Session(model), 0);
            byte[] construction = ReplayTimelineArchive.Construction(decoder);
            Check(Metadata(60, construction).RecordedMovementModel == model, "production compressed construction resolves " + model);
            var opposite = model == MovementModel.Native ? MovementModel.Momentum : MovementModel.Native;
            Check(Metadata(60, construction, Match(opposite)).RecordedMovementModel == null, "construction/bootstrap contradiction unknown");
            Check(Metadata(60, new byte[] { 253, 255, 255 }).RecordedMovementModel == null, "malformed compressed construction unknown");
            foreach (byte[][] packets in new[] { new[] { Match(model) }, new[] { Session(model) }, new[] { Match(model), Session(model) } })
            {
                var metadata = Metadata(60, packets);
                Check(metadata.RecordedMovementModel == model, "actual recorded rules resolve " + model);
                string json = JsonSerializer.Serialize(metadata);
                Check(!json.Contains("MovementModel") && !json.Contains("MovementLabel"), "archive metadata JSON excludes presentation projection");
                var entry = new ReplayLibraryIndexEntry { HasMetadata = true, Room = Room,
                    ProtocolVersion = 60, RecordedUtcTicks = DateTime.UtcNow.Ticks,
                    RecordedMovementModel = metadata.RecordedMovementModel };
                var restored = JsonSerializer.Deserialize<ReplayLibraryIndexEntry>(JsonSerializer.Serialize(entry))!;
                Check(restored.ToRecording("unused.ppdemo").Metadata!.RecordedMovementModel == model,
                    "cache serialization retains movement without bootstrap " + model);
            }
        }
        Check(Metadata(68, Match(MovementModel.Traversal), Session(MovementModel.Traversal)).RecordedMovementModel == MovementModel.Traversal
            && Metadata(68, Match(MovementModel.Traversal)).RecordedMovementProfileVersion == 1,
            "protocol68 Traversal metadata retains its model and immutable profile version");
        Check(Metadata(69, Match(MovementModel.Traversal), Session(MovementModel.Traversal)).RecordedMovementModel == MovementModel.Traversal
            && Metadata(69, Match(MovementModel.Traversal)).RecordedMovementProfileVersion == 2,
            "protocol69 Traversal metadata selects accepted-stance profile v2");
        foreach (MovementModel model in new[] { MovementModel.Quake3, MovementModel.Traversal })
        foreach (byte protocol in new byte[] { 70, 71 })
        foreach (byte[][] packets in new[] { new[] { Match(model) }, new[] { Session(model) }, new[] { Match(model), Session(model) } })
        {
            int expectedProfile = protocol == 70 ? 2 : 3;
            var metadata = Metadata(protocol, packets);
            Check(metadata.RecordedMovementModel == model && metadata.RecordedMovementProfileVersion == expectedProfile,
                $"recorded {model} protocol{protocol} bootstrap selects immutable profile{expectedProfile}");
            var entry = new ReplayLibraryIndexEntry { HasMetadata = true, Room = Room, ProtocolVersion = protocol,
                RecordedUtcTicks = DateTime.UtcNow.Ticks, RecordedMovementModel = model };
            var cached = JsonSerializer.Deserialize<ReplayLibraryIndexEntry>(JsonSerializer.Serialize(entry))!;
            Check(cached.ToRecording("unused.ppdemo").Metadata!.RecordedMovementProfileVersion == expectedProfile,
                $"cached {model} protocol{protocol} attribution retains profile{expectedProfile} without synthesizing gameplay");
        }
        foreach (byte protocol in new byte[] { 60, 61, 62, 69, 70, 71 })
            Check(Metadata(protocol, Match(MovementModel.Quake3)).RecordedMovementProfileVersion
                == (protocol >= 71 ? 3 : protocol >= 62 ? 2 : 1),
                "Quake III historical profile boundaries are selected by the recorded protocol");
        Check(Metadata(67, Match(MovementModel.Traversal)).RecordedMovementModel == null
            && Metadata(67, Session(MovementModel.Traversal)).RecordedMovementModel == null,
            "historical reserved Traversal bits cannot label replay metadata");
        Check(Metadata(58).RecordedMovementModel == MovementModel.Native, "supported pre-movement absence is Classic");
        Check(Metadata(59).RecordedMovementModel == null, "current missing facts unknown");
        Check(Metadata((byte)(NetConfig.ProtocolVersion + 1), Match(MovementModel.Momentum)).RecordedMovementModel == null, "future protocol unknown");
        Check(Metadata(3).RecordedMovementModel == null, "unsupported historical protocol unknown");
        Check(Metadata(58, Match(MovementModel.Native), Session(MovementModel.Native)).RecordedMovementModel == MovementModel.Native, "historical 58 Classic");
        Check(Metadata(58, Match(MovementModel.Momentum)).RecordedMovementModel == null, "historical reserved match bit refused");
        Check(Metadata(58, Session(MovementModel.Momentum)).RecordedMovementModel == null, "historical reserved session bit refused");
        Check(Metadata(59, Match(MovementModel.Native), Session(MovementModel.Momentum)).RecordedMovementModel == null, "contradiction unknown");
        byte[] wrongRoom = Match(MovementModel.Momentum); var wrong = MatchStatePacket.Read(wrongRoom.AsSpan(1));
        wrong.RoomKey = "MP1 COMBAT HALL"; wrong.Write(wrongRoom.AsSpan(1));
        Check(Metadata(59, wrongRoom).RecordedMovementModel == null, "different room cannot label replay");
        foreach (bool sessionPacket in new[] { false, true })
        {
            foreach (bool zeroEpoch in new[] { false, true })
            {
                byte[] malformed = sessionPacket ? Session(MovementModel.Momentum) : Match(MovementModel.Momentum);
                if (sessionPacket)
                {
                    Check(SessionStatePacket.TryReadArchived(malformed.AsSpan(1), out var value), "valid session fixture");
                    if (zeroEpoch) value.AuthorityEpoch = 0; else value.MatchId = 0;
                    value.Write(malformed.AsSpan(1));
                }
                else
                {
                    var value = MatchStatePacket.Read(malformed.AsSpan(1));
                    if (zeroEpoch) value.AuthorityEpoch = 0; else value.MatchId = 0;
                    value.Write(malformed.AsSpan(1));
                }
                Check(Metadata(59, malformed).RecordedMovementModel == null, "current zero identity unknown");
                Check(Metadata(58, malformed).RecordedMovementModel == null, "historical Momentum reserved even with zero identity");
            }
        }
        foreach (MatchModifierFlags extra in new[] { MatchModifierFlags.BalancedMode, MatchModifierFlags.EnhancedHunters })
        {
            byte[] malformed = Match(MovementModel.Momentum);
            var value = MatchStatePacket.Read(malformed.AsSpan(1)); value.RuleBits |= (ushort)extra; value.Write(malformed.AsSpan(1));
            Check(Metadata(59, malformed).RecordedMovementModel == null, "invalid Momentum modifier combination unknown");
            byte[] invalidSession = Session(MovementModel.Momentum);
            BinaryPrimitives.WriteUInt32LittleEndian(invalidSession.AsSpan(1 + SessionStatePacket.Protocol28Size),
                (uint)(MatchModifierFlags.MomentumMovement | extra));
            Check(Metadata(59, invalidSession).RecordedMovementModel == null, "invalid session Momentum combination unknown");
        }
        byte[] historical = Match(MovementModel.Native);
        var historicalMatch = MatchStatePacket.Read(historical.AsSpan(1));
        historicalMatch.MatchId = 0; historicalMatch.AuthorityEpoch = 0; historicalMatch.Write(historical.AsSpan(1));
        Check(Metadata(58, historical).RecordedMovementModel == MovementModel.Native, "reviewed historical zero identity remains Classic");
        CacheRoundTrip();
        Console.WriteLine($"Momentum replay metadata: {_checks} assertions passed.");
        return 0;
    }
    private static void CacheRoundTrip()
    {
        // Isolate actual persistent cache I/O, then restore every process-global seam.
        var paths = (Dictionary<string, string>)typeof(Paths).GetField("_allPaths", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        bool hadExport = paths.TryGetValue("Export", out string? export);
        var document = typeof(ReplayLibraryIndex).GetField("_document", BindingFlags.Static | BindingFlags.NonPublic)!;
        var dirty = typeof(ReplayLibraryIndex).GetField("_dirty", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? oldDocument = document.GetValue(null); object? oldDirty = dirty.GetValue(null);
        string root = Path.Combine(Path.GetTempPath(), "movement-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            paths["Export"] = root; document.SetValue(null, null); dirty.SetValue(null, false);
            Directory.CreateDirectory(DemoLibrary.Directory);
            string path = Path.Combine(DemoLibrary.Directory, "fixture.ppdemo"); File.WriteAllBytes(path, new byte[] { 1 });
            var metadata = Metadata(60, Match(MovementModel.Quake3));
            var recording = new DemoRecording(path, Room, DateTime.Now, 1, metadata);
            Check(!ReplayLibraryIndex.TryGet(path, out _), "cold index misses");
            ReplayLibraryIndex.Note(path, recording, 3); ReplayLibraryIndex.Flush(); document.SetValue(null, null);
            Check(ReplayLibraryIndex.TryGet(path, out var warm) && warm.Metadata!.RecordedMovementModel == MovementModel.Quake3,
                "disk warm cache retains Quake 3");
            Check(warm.Metadata!.Bootstrap.Packets.Count == 0, "warm projection does not synthesize bootstrap authority");
            string index = Path.Combine(DemoLibrary.Directory, "library.index.json"); string valid = File.ReadAllText(index);
            File.WriteAllText(index, valid.Replace("\"RecordedMovementModel\": 2", "\"RecordedMovementModel\": 255")); document.SetValue(null, null);
            Check(!ReplayLibraryIndex.TryGet(path, out _), "invalid cache enum forces rescan");
            File.WriteAllText(index, valid.Replace("\"ProtocolVersion\": 60", "\"ProtocolVersion\": 59")); document.SetValue(null, null);
            Check(!ReplayLibraryIndex.TryGet(path, out _), "historical cache Momentum forces rescan");
            File.WriteAllText(index, valid.Replace("\"RecordedMovementModel\": 2", "\"RecordedMovementModel\": 3")
                .Replace("\"ProtocolVersion\": 60", "\"ProtocolVersion\": 67")); document.SetValue(null, null);
            Check(!ReplayLibraryIndex.TryGet(path, out _), "historical cache Traversal forces rescan");
            File.WriteAllText(index, valid.Replace("\"Version\": 6", "\"Version\": 5")); document.SetValue(null, null);
            Check(!ReplayLibraryIndex.TryGet(path, out _), "old index version forces rescan");
        }
        finally
        {
            if (hadExport) paths["Export"] = export!; else paths.Remove("Export");
            document.SetValue(null, oldDocument); dirty.SetValue(null, oldDirty);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
