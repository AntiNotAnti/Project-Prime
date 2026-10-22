using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Archive writer -> actual decoder/compatibility layer -> production JSON exporter.
internal static class ReplayFidelityExportTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("Replay fidelity export: " + label);
        Console.WriteLine("FIDELITY EXPORT PASS " + label);
    }

    internal static int Run(string? fixtureOutput = null)
    {
        _checks = 0;
        string directory = Path.Combine(Path.GetTempPath(), "prime-fidelity-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (byte protocol in new byte[] { 52, (byte)NetConfig.ProtocolVersion })
            {
                string path = Path.Combine(directory, "protocol-" + protocol + ".ppdemo");
                WriteFixture(path, protocol);
                string json = ReplayFidelityDiagnostics.Export(path);
                Check(json == ReplayFidelityDiagnostics.Export(path), "protocol " + protocol + " repeated export bytes stable");
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                Check(root.GetProperty("SourceSha256").GetString() == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), "source hash covers actual archive bytes");
                Check(root.GetProperty("Protocol").GetByte() == protocol && root.GetProperty("Decoder").GetString() == "Healthy", "actual protocol and healthy decoder disclosed");
                var rows = root.GetProperty("Rows").EnumerateArray().ToArray();
                var authored = rows.Where(r => r.GetProperty("Stage").GetString() == "authored-input").ToArray();
                var outcomes = rows.Where(r => r.GetProperty("Stage").GetString() == "authority-outcome").ToArray();
                Check(authored.Length == 6 && authored.Select(r => (r.GetProperty("Match").GetUInt16(), r.GetProperty("Epoch").GetUInt64(),
                    r.GetProperty("Slot").GetInt32(), r.GetProperty("Generation").GetUInt16(), r.GetProperty("Life").GetUInt16(),
                    r.GetProperty("Shot").GetUInt32())).Distinct().Count() == 6,
                    "same shot ID remains separate across match, epoch, slot, generation and life");
                Check(outcomes.Length == 3 && outcomes.Select(r => (r.GetProperty("Victim").GetByte(), r.GetProperty("VictimLife").GetUInt16())).Distinct().Count() == 3,
                    "same damage event preserves separate victims and victim lives");
                Check(authored.Count(r => r.GetProperty("Pose").ValueKind == JsonValueKind.Null) == 1
                    && authored.All(r => r.GetProperty("AuthorityFrame").ValueKind == JsonValueKind.Null
                        && r.GetProperty("Damage").ValueKind == JsonValueKind.Null), "absent authored pose and authority fields stay null");
                Check(outcomes.All(r => r.GetProperty("Pose").ValueKind == JsonValueKind.Null
                    && r.GetProperty("AuthorityFrame").GetUInt32() == 23 && r.GetProperty("RecordingFrame").GetUInt32() == 5),
                    "authority resolution and recording clocks remain distinct without invented pose");
                Check(root.GetProperty("Provenance").GetProperty("OwnerCamera").GetString()!.Contains("unavailable", StringComparison.Ordinal),
                    "missing owner camera explicitly disclosed");
                Check(!json.Contains("PRIVATE_PLAYER_NAME", StringComparison.Ordinal) && !json.Contains("PRIVATE_CHAT_TEXT", StringComparison.Ordinal)
                    && !json.Contains(path, StringComparison.Ordinal), "player names, chat and local source path omitted");
                if (protocol == NetConfig.ProtocolVersion)
                {
                    MalformedAndRecovered(path, directory, protocol);
                    if (fixtureOutput != null)
                    {
                        string destination = Path.GetFullPath(fixtureOutput);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(path, destination, overwrite: false);
                        Console.WriteLine("FIDELITY EXPORT fixture: " + destination);
                    }
                }
            }
            Console.WriteLine($"PASS: {_checks} replay fidelity export assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteFixture(string path, byte protocol, bool recovered = false)
    {
        var metadata = new ReplayMetadata
        {
            FormatVersion = 3, ProtocolVersion = protocol, TickRate = 60, RoomKey = "synthetic-fidelity-fixture",
            Mode = GameMode.Battle, RecordedAtUtc = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc),
            BuildId = "synthetic", BuildVersion = "synthetic", Recovered = recovered,
            Players = new[] { new ReplayPlayerInfo(0, 0, -1, "PRIVATE_PLAYER_NAME") }
        };
        using var writer = new ReplayWriterV3(path, metadata);
        for (int variant = 0; variant < 6; variant++)
        {
            var intent = new IntentPacket
            {
                MatchId = (ushort)(variant == 1 ? 2 : 1), AuthorityEpoch = variant == 2 ? 11UL : 10UL,
                SlotGeneration = (ushort)(variant == 4 ? 2 : 1), LifeId = (ushort)(variant == 5 ? 2 : 1),
                Frame = 20, Aim = Vector3.UnitZ, HasFireEvents = true, FireEventCount = 1
            };
            intent.FireEvents[0] = new FireEvent(7, 20, 19, 0, FireEventKind.PressFire, 0, 30, 0,
                PoseFlags: variant == 5 ? (byte)0 : FireEvent.FlagPose,
                Origin: new Vector3(1, 2, 3), Direction: Vector3.UnitZ, Aim: Vector3.UnitZ, View: Vector3.UnitZ);
            Check(NetFireEvents.Validate(intent), "fixture uses valid production fire-event intent");
            int width = protocol < 53 ? IntentPacket.FullSize : IntentPacket.TouchCanonicalSize;
            byte[] packet = new byte[2 + width]; packet[0] = (byte)PacketType.SlotIntent; packet[1] = (byte)(variant == 3 ? 1 : 0);
            intent.Write(packet.AsSpan(2));
            writer.WriteRecord(1, packet);
            if (variant == 0) writer.WriteRecord(1, packet); // exact retransmit collapses only itself
        }
        for (byte variant = 0; variant < 3; variant++)
        {
            var fact = new ReplayShotFact(1, 10, 23, 20, 7, 9, 0, 1, 1,
                (byte)(variant == 1 ? 2 : 1), 1, (ushort)(variant == 2 ? 2 : 1), 0,
                ReplayShotFactFlags.Direct, 10, 90, 0, 0, new Vector3(4, 5, 6));
            byte[] packet = new byte[1 + ReplayShotFactPacket.Size]; packet[0] = (byte)PacketType.ReplayShotFact;
            ReplayShotFactPacket.Write(fact, packet.AsSpan(1));
            Check(ReplayShotFactPacket.TryRead(packet.AsSpan(1), out var decoded) && decoded == fact, "fixture uses valid production authority fact");
            writer.WriteRecord(5, packet);
        }
        writer.WriteRecord(6, new[] { (byte)PacketType.Chat }.Concat(Encoding.UTF8.GetBytes("PRIVATE_CHAT_TEXT")).ToArray());
        ReplayTimelineArchive.EndFrame(writer, 7);
    }

    private static void MalformedAndRecovered(string healthy, string directory, byte protocol)
    {
        string recovered = Path.Combine(directory, "recovered.ppdemo");
        WriteFixture(recovered, protocol, recovered: true);
        using (var json = JsonDocument.Parse(ReplayFidelityDiagnostics.Export(recovered)))
            Check(json.RootElement.GetProperty("Decoder").GetString() == "Recovered", "recovered archive status disclosed");
        byte[] bytes = File.ReadAllBytes(healthy);
        // Archive header: magic[4], format[1], protocol[1], metadata length[4], CRC[4], metadata.
        int firstChunk = 14 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6));
        Check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(firstChunk)) == ReplayFormatV3.ChunkMagic, "fixture located actual first archive chunk");
        string truncated = Path.Combine(directory, "truncated.ppdemo");
        File.WriteAllBytes(truncated, bytes.AsSpan(0, firstChunk + ReplayFormatV3.ChunkHeaderSize + 1).ToArray());
        using (var json = JsonDocument.Parse(ReplayFidelityDiagnostics.Export(truncated)))
            Check(json.RootElement.GetProperty("Decoder").GetString() == "Truncated"
                && json.RootElement.GetProperty("Rows").GetArrayLength() == 0, "truncated chunk disclosed without trusting partial records");
        bytes[firstChunk + 24] ^= 1; // actual chunk CRC; retain valid footer/index to exercise read-time failure
        string corrupt = Path.Combine(directory, "corrupt.ppdemo"); File.WriteAllBytes(corrupt, bytes);
        using (var json = JsonDocument.Parse(ReplayFidelityDiagnostics.Export(corrupt)))
            Check(json.RootElement.GetProperty("Decoder").GetString() == "Corrupt"
                && json.RootElement.GetProperty("Rows").GetArrayLength() == 0, "corrupt chunk disclosed without trusting partial records");
    }
}
