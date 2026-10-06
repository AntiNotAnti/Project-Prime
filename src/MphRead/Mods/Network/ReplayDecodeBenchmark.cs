using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Asset-free replay reader workload. Reports decode/I/O/allocation cost,
/// not simulation/frame pacing; three independent cursors preserve lookahead isolation.</summary>
public static class ReplayDecodeBenchmark
{
    public static int Run(int minutes = 30, string? fixtureOutput = null)
    {
        minutes = Math.Clamp(minutes, 1, 120);
        string directory = Path.Combine(Path.GetTempPath(), "prime-replay-decode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "snapshots.ppdemo");
            uint frames = (uint)(minutes * 60 * 60);
            int tail = 1 + SnapshotHeader.Size + 8 * PlayerState.Size;
            byte[] packet = new byte[tail + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
            packet[0] = (byte)PacketType.Snapshot;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(tail + NetMatchTimeSync.Size), 1);
            var match = new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "", Mode = (byte)GameMode.Battle,
                MatchId = 1, AuthorityEpoch = 1, PlayerCount = 8, Flags = MatchStatePacket.FlagInProgress };
            var configuration = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 8,
                OwnerSlot = byte.MaxValue, Phase = SessionPhase.InMatch, Policy = ServerSessionPolicy.Lobby,
                WorldProfile = MatchWorldProfile.Resolve(8), Match = new MatchDefinition { RoomKey = match.RoomKey, Mode = GameMode.Battle } };
            byte[] matchBytes = new byte[1 + MatchStatePacket.Size], sessionBytes = new byte[1 + SessionStatePacket.Size];
            matchBytes[0] = (byte)PacketType.MatchState; match.Write(matchBytes.AsSpan(1));
            sessionBytes[0] = (byte)PacketType.SessionState; configuration.Write(sessionBytes.AsSpan(1));
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 8;
            for (int slot = 0; slot < 8; slot++)
            {
                roster.Slots[slot] = (byte)slot; roster.Generations[slot] = 1;
                roster.Hunters[slot] = (byte)Hunter.Samus; roster.Teams[slot] = -1;
                roster.Names[slot] = "Synthetic " + slot;
            }
            byte[] rosterBytes = new byte[1 + RosterPacket.Size]; rosterBytes[0] = (byte)PacketType.Roster;
            roster.Write(rosterBytes.AsSpan(1));
            using (var writer = new ReplayWriterV3(path, new ReplayMetadata { RoomKey = match.RoomKey, Mode = GameMode.Battle,
                Bootstrap = new ReplayBootstrap { Packets = new[] { sessionBytes, matchBytes, rosterBytes } } }))
                for (uint frame = 0; frame < frames; frame++)
                {
                    new SnapshotHeader { MatchId = 1, AuthorityEpoch = 1, Frame = frame, PlayerCount = 8 }.Write(packet.AsSpan(1));
                    for (int slot = 0; slot < 8; slot++)
                        new PlayerState { SlotIndex = (byte)slot, SlotGeneration = 1, LifeId = 1, Health = 99,
                            Flags = PlayerState.FlagActive | PlayerState.FlagSpawned,
                            Position = new Vector3(MathF.Sin(frame * .013f + slot) * 20, slot, MathF.Cos(frame * .009f + slot) * 20),
                            Facing = Vector3.UnitZ }.Write(packet.AsSpan(1 + SnapshotHeader.Size + slot * PlayerState.Size));
                    writer.WriteRecord(frame, packet);
                }
            object Stream(int cursors)
            {
                var readers = new List<DemoReader>();
                long allocated = GC.GetAllocatedBytesForCurrentThread(); long started = Stopwatch.GetTimestamp();
                long packets = 0, payloadBytes = 0;
                try
                {
                    for (int i = 0; i < cursors; i++) readers.Add(DemoReader.Open(path, out var result) ?? throw new InvalidDataException(result.ToString()));
                    bool more;
                    do
                    {
                        more = false;
                        foreach (var reader in readers)
                            if (reader.ReadNext() is { } record) { packets++; payloadBytes += record.Data.Length; more = true; }
                    } while (more);
                    if (packets != frames * (long)cursors) throw new InvalidDataException("Reader lost fixture records.");
                    return new { cursors, packets, payloadBytes, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated };
                }
                finally { foreach (var reader in readers) reader.Dispose(); }
            }
            object Seek(int cursors)
            {
                var readers = new List<DemoReader>(); var times = new List<double>(); long allocated = GC.GetAllocatedBytesForCurrentThread();
                try
                {
                    for (int i = 0; i < cursors; i++) readers.Add(DemoReader.Open(path, out var result) ?? throw new InvalidDataException(result.ToString()));
                    var random = new Random(10205);
                    for (int i = 0; i < 100; i++)
                    {
                        uint frame = (uint)random.Next((int)frames - 2); long started = Stopwatch.GetTimestamp();
                        foreach (var reader in readers)
                            if (reader.SeekAfter(frame) is not { } record || record.Frame != frame + 1) throw new InvalidDataException("Seek disagrees with linear frame index.");
                        times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    }
                    var sorted = times.Order().ToArray();
                    return new { cursors, samples = 100, p50Ms = sorted[49], p99Ms = sorted[98], maximumMs = sorted[^1],
                        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated };
                }
                finally { foreach (var reader in readers) reader.Dispose(); }
            }
            // The generated file has just been written. These are warm filesystem
            // observations, not an invented cold-cache or real-player benchmark.
            Console.WriteLine("REPLAYDECODE " + JsonSerializer.Serialize(new { minutes, frames, archiveBytes = new FileInfo(path).Length,
                cache = "warm filesystem; generated moving eight-player snapshots", streamOne = Stream(1), streamThree = Stream(3), seekOne = Seek(1), seekThree = Seek(3) }));
            // Optional synthetic input for the separate asset-backed private-world
            // benchmark. Never replace an existing recording.
            if (fixtureOutput != null) File.Copy(path, Path.GetFullPath(fixtureOutput), overwrite: false);
            return 0;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
