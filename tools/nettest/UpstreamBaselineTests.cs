using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Sanitized constructed facts through the production recorder/codecs. This is
// not gameplay, hit validation, real UDP, a loaded replay or a GPU acceptance run.
internal static class UpstreamBaselineTests
{
    private sealed record Captured(uint Frame, uint Tick, ReplayFactKind Kind,
        byte[] Payload, ReplayMarker? Marker);
    private sealed record Result(int SchemaVersion, string Scope, int Protocol,
        int HunterIds, int Lives, int AcceptedShots, int WrongLifeRejected,
        int CrossMatchRejected, int CrossEpochRejected, int Records, int Events,
        string PacketSha256, string EventSha256, string ShotSha256);
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException("UPSTREAM BASELINE " + label); }

    internal static int Run(string? outputPath = null)
    {
        var first = Capture();
        var second = Capture();
        Check(first == second, "two independent recorder instances produce identical counters and SHA256 digests");
        Check(first.Records == 20 && first.Events == 4
            && first.PacketSha256 == "db1a6ea3b5dd4c1199f4fb04946a999da9f7998172c4f3d07586b6012f5055ed"
            && first.EventSha256 == "e73817c8a2bfc4f0d33e6c9bafd2b22f69ecbde422cdd69cb7d27110fa867e92"
            && first.ShotSha256 == "3c12bae80dd14d27e385dd0a505129c2e041a66e819a223629dfa98540bec5b2",
            "protocol61 sanitized fixture matches committed expected digests");
        string json = JsonSerializer.Serialize(first, new JsonSerializerOptions { WriteIndented = true });
        if (outputPath != null) File.WriteAllText(outputPath, json + Environment.NewLine);
        Console.WriteLine(json);
        Console.WriteLine("UPSTREAM BASELINE PASS two independent constructed multi-life recorder runs");
        return 0;
    }

    private static Result Capture()
    {
        const ushort matchId = 7, generation = 3;
        const ulong epoch = 9;
        var recorder = new ReplayRecorder();
        var records = new List<Captured>();
        recorder.Accepted += record => records.Add(new(record.RecordingFrame, record.ServerTick,
            record.Kind, record.Payload.ToArray(), record.Marker));
        int accepted = 0, wrongLife = 0, wrongMatch = 0, wrongEpoch = 0;
        var identities = new HashSet<(ushort Match, ulong Epoch, byte Slot, ushort Generation, ushort Life, uint Shot)>();
        try
        {
            recorder.AcceptMatch(new MatchStatePacket { MatchId = matchId, AuthorityEpoch = epoch,
                RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.Battle }, 0);
            var roster = RosterPacket.Create();
            roster.MatchId = matchId; roster.AuthorityEpoch = epoch; roster.Revision = 1; roster.Count = 7;
            for (byte slot = 0; slot < 7; slot++)
            {
                roster.Slots[slot] = slot; roster.Generations[slot] = generation;
                roster.Hunters[slot] = slot; roster.Names[slot] = "FIXTURE" + slot;
            }
            recorder.AcceptRoster(roster, 0);
            var victim = new NetLifecycleTracker(); victim.SetOccupant(generation);
            var shooter = new NetLifecycleTracker(); shooter.SetOccupant(generation);
            for (ushort life = 1; life <= 2; life++)
            {
                uint frame = (uint)(life * 100);
                Check(victim.Accept(generation, life, NetworkPlayerState.Alive, out bool fresh)
                    == LifecycleRejection.None && fresh, "authority victim begins distinct life");
                Check(shooter.Accept(generation, life, NetworkPlayerState.Alive, out fresh)
                    == LifecycleRejection.None && fresh, "authority shooter begins distinct life");
                recorder.Marker(frame, frame, new ReplayMarker(ReplayMarkerKind.Spawn, 1, 1, life));
                Snapshot(frame, life, 100);
                for (uint shot = 1; shot <= 2; shot++)
                {
                    uint launch = frame + shot;
                    var fire = new FireEvent(shot, launch, launch - 1, 0, FireEventKind.PressFire,
                        (byte)BeamType.PowerBeam, 0, 0, FireEvent.FlagPose | FireEvent.FlagReticle,
                        new Vector3(life, 2, shot), Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY,
                        new Vector2(.5f, .5f), SourceLife: life, Target: NetTargetIdentity.None);
                    var intent = new IntentPacket { MatchId = matchId, AuthorityEpoch = epoch,
                        SlotGeneration = generation, LifeId = life, Frame = launch, AckFrame = launch - 1,
                        HasFireEvents = true, FireEventCount = 1 };
                    intent.FireEvents[0] = fire;
                    recorder.AcceptIntent(0, intent, launch);
                    var saved = IntentPacket.Read(records[^1].Payload.AsSpan(2));
                    Check(saved.LifeId == life && saved.FireEvents[0].ShotId == shot
                        && saved.FireEvents[0].SourceLife == life && saved.FireEvents[0].Origin == fire.Origin
                        && saved.FireEvents[0].Direction == fire.Direction && saved.FireEvents[0].Aim == fire.Aim,
                        "canonical recorder preserves lifecycle and exact posed ray");
                    bool lethal = shot == 2;
                    var fact = new ReplayShotFact(matchId, epoch, launch + 1, launch, shot,
                        (ushort)(life * 10 + shot), 0, generation, life, 1, generation, life,
                        (byte)BeamType.PowerBeam, ReplayShotFactFlags.Direct
                            | (lethal ? ReplayShotFactFlags.Lethal : ReplayShotFactFlags.None),
                        lethal ? 75u : 25u, lethal ? (ushort)0 : (ushort)75, 0, 0, new Vector3(life, 2, 10));
                    byte[] bytes = new byte[ReplayShotFactPacket.Size]; ReplayShotFactPacket.Write(fact, bytes);
                    Check(recorder.AcceptShotFact(bytes, launch + 1), "constructed accepted result enters production recorder");
                    Check(ReplayShotFactPacket.TryRead(records[^1].Payload.AsSpan(1), out var decoded)
                        && decoded == fact && !decoded.Headshot && decoded.Lethal == lethal,
                        "accepted result retains exact fields and does not invent headshot");
                    Check(identities.Add((decoded.MatchId, decoded.AuthorityEpoch, decoded.ShooterSlot,
                        decoded.ShooterGeneration, decoded.ShooterLifeId, decoded.ShotId)),
                        "reused per-life shot IDs remain distinct under complete identity");
                    accepted++;
                    Snapshot(launch + 1, life, fact.HealthAfter);
                    if (lethal)
                    {
                        Check(victim.Accept(generation, life, NetworkPlayerState.Dead, out _)
                            == LifecycleRejection.None, "authority death accepted");
                        recorder.Marker(launch + 1, launch + 1, new ReplayMarker(ReplayMarkerKind.Kill, 0, 1,
                            Kill: new ReplayKillIdentity(matchId, epoch, launch + 1, fact.DamageEventId,
                                0, generation, 1, generation, life, life), Weapon: fact.Weapon));
                    }
                    int count = records.Count;
                    ReplayShotFactPacket.Write(fact with { MatchId = 8 }, bytes);
                    if (!recorder.AcceptShotFact(bytes, launch + 2)) wrongMatch++;
                    ReplayShotFactPacket.Write(fact with { AuthorityEpoch = 10 }, bytes);
                    if (!recorder.AcceptShotFact(bytes, launch + 2)) wrongEpoch++;
                    Check(records.Count == count, "wrong match/epoch publishes no result");
                }
                Check(victim.Accept(generation, life, NetworkPlayerState.Alive, out _)
                    == LifecycleRejection.InvalidResurrection, "late same-life alive refused");
                if (life == 2)
                {
                    int before = records.Count;
                    // Recorder consumes accepted input. The production lifecycle
                    // guard, not the recorder, owns this stale-life rejection.
                    if (victim.Accept(generation, 1, NetworkPlayerState.Dead, out _)
                        == LifecycleRejection.OldLife) wrongLife++;
                    Check(wrongLife == 1 && records.Count == before, "old-life death is not forwarded to recorder");
                }
            }
            Check(accepted == 4 && identities.Count == 4 && wrongLife == 1
                && wrongMatch == 4 && wrongEpoch == 4, "exact fixture acceptance/rejection counts");
            return new(1, "synthetic-production-recorder; no gameplay/UDP/GPU", NetConfig.ProtocolVersion,
                7, 2, accepted, wrongLife, wrongMatch, wrongEpoch, records.Count,
                records.Count(r => r.Kind == ReplayFactKind.Event),
                Digest(records), Digest(records.Where(r => r.Kind == ReplayFactKind.Event)),
                Digest(records.Where(r => r.Payload.Length > 0 && r.Payload[0] == (byte)PacketType.ReplayShotFact)));
        }
        finally { recorder.Reset(); }

        void Snapshot(uint frame, ushort life, ushort health)
        {
            var state = new PlayerState { SlotIndex = 1, SlotGeneration = generation, LifeId = life,
                Health = health, Flags = PlayerState.FlagSpawned, Position = new Vector3(life, 2, 10) };
            byte[] packet = new byte[1 + SnapshotHeader.Size + PlayerState.Size];
            packet[0] = (byte)PacketType.Snapshot;
            new SnapshotHeader { MatchId = matchId, AuthorityEpoch = epoch, Frame = frame, PlayerCount = 1 }
                .Write(packet.AsSpan(1));
            state.Write(packet.AsSpan(1 + SnapshotHeader.Size));
            recorder.AcceptSnapshot(packet, frame, frame);
        }
    }

    // Explicit little-endian, length-delimited envelope: no JSON ordering,
    // locale, wall-clock, runtime hash code or process-specific identifiers.
    private static string Digest(IEnumerable<Captured> records)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        foreach (var record in records)
        {
            writer.Write(record.Frame); writer.Write(record.Tick); writer.Write((int)record.Kind);
            writer.Write(record.Payload.Length); writer.Write(record.Payload);
            writer.Write(record.Marker.HasValue);
            if (record.Marker is not { } marker) continue;
            writer.Write((int)marker.Kind); writer.Write(marker.Actor); writer.Write(marker.Target);
            writer.Write(marker.Value); writer.Write(marker.Weapon); writer.Write(marker.DamageFlags);
            writer.Write(marker.Kill.HasValue);
            if (marker.Kill is not { } kill) continue;
            writer.Write(kill.MatchId); writer.Write(kill.AuthorityEpoch); writer.Write(kill.ServerTick);
            writer.Write(kill.EventId); writer.Write(kill.KillerSlot); writer.Write(kill.KillerGeneration);
            writer.Write(kill.VictimSlot); writer.Write(kill.VictimGeneration); writer.Write(kill.VictimLifeId);
            writer.Write(kill.KillerLifeId);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}
