using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;
internal static class GrantReplayTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    { _checks++; if (!ok) throw new InvalidOperationException("Grant replay: " + message); }
    private static void Reject(Action action, string message)
    { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, message); }
    private static MatchStatePacket Match() => new() { MatchId = 2, AuthorityEpoch = 9, RoomKey = "unit-grant-replay", Mode = (byte)GameMode.Battle };
    private static PickupClaim Claim(uint sequence = 6, uint spawn = 8) => new(new(9, 2, 3, 4, 5, sequence),
        new(9, 2, 7, 10, spawn), TimedPowerUp.Cloak, 91, new Vector3(1, 2, 3));
    private static TimedGrantFact Fact(PickupClaim claim) => new(claim.Key.LifeKey, claim.Spawn.Room, 1,
        claim.Type, 12, claim.Spawn.SpawnId, claim.Spawn.Generation, 100, 1900, TimedGrantEnd.Active, 120);
    private static byte[] Packet(PickupVerdict verdict)
    { var bytes = new byte[1 + PickupVerdict.Size]; bytes[0] = (byte)PacketType.PickupVerdict; verdict.Write(bytes.AsSpan(1)); return bytes; }
    private static byte[] Packet(PickupItemFact item)
    { var bytes = new byte[1 + PickupItemFact.Size]; bytes[0] = (byte)PacketType.PickupItem; item.Write(bytes.AsSpan(1)); return bytes; }
    private static byte[] Packet(TimedGrantFact fact)
    { var bytes = new byte[1 + TimedGrantFact.Size]; bytes[0] = (byte)PacketType.TimedGrant; fact.Write(bytes.AsSpan(1)); return bytes; }
    private static ReplayReplicaState State()
    { var state = new ReplayReplicaState(); var bytes = new byte[1 + MatchStatePacket.Size]; bytes[0] = (byte)PacketType.MatchState; Match().Write(bytes.AsSpan(1)); state.Accept(bytes, 0); return state; }
    private static void RecorderAndProjection()
    {
        var recorder = new ReplayRecorder(); recorder.AcceptMatch(Match(), 0);
        var records = new List<(byte[] Bytes, uint Frame, uint Tick)>();
        recorder.Accepted += record => records.Add((record.Payload.ToArray(), record.RecordingFrame, record.ServerTick));
        var claim = Claim(); var fact = Fact(claim);
        recorder.AcceptPickupPrediction(claim, 5000, 5010); recorder.AcceptTimedGrant(fact, 5020);
        Check(records.Count == 2 && records[0].Bytes[0] == 78 && records[1].Bytes[0] == 77,
            "actual recorder publishes explicit prediction78 and grant77 events");
        Check(records[0].Frame == 5010 && records[1].Frame == 5020 && records[1].Tick == 120
            && ReplayPickupPrediction.TryRead(records[0].Bytes.AsSpan(1), out var readClaim, out uint producerFrame)
            && readClaim == claim && producerFrame == 5000, "recorded producer timing and complete claim identity preserved");
        var state = State(); state.Accept(records[0].Bytes, 10);
        Check(state.TimedGrants.PendingCount == 1 && state.TimedGrants.Hides(claim.Spawn, 10)
            && state.TimedGrants.View(claim.Key.LifeKey, claim.Type, 10).Remaining == 1800
            && state.TimedGrants.View(claim.Key.LifeKey, claim.Type, 10).ConfirmedRemaining == 0,
            "rebased clip uses actual consumer arrival, not producer local frame, for prediction");
        var checkpoint = state.CaptureCheckpoint(); var resumed = State(); resumed.RestoreCheckpoint(checkpoint);
        resumed.Advance(11);
        Check(resumed.TimedGrants.PendingCount == 1 && resumed.TimedGrants.View(claim.Key.LifeKey, claim.Type, 11).Remaining == 1799,
            "private checkpoint preserves pending prediction across clip frame offset");
        resumed.Accept(records[1].Bytes, 20);
        Check(resumed.TimedGrants.PendingCount == 0 && resumed.TimedGrants.View(claim.Key.LifeKey, claim.Type, 20).ConfirmedRemaining == 1780,
            "recorded delta confirms with authority clock before verdict");
        var consumedCheckpoint = resumed.CaptureCheckpoint(); var consumedRestore = State(); consumedRestore.RestoreCheckpoint(consumedCheckpoint);
        Check(resumed.PickupItems.Count == 0 && resumed.TimedGrants.Hides(claim.Spawn, 20)
            && consumedRestore.PickupItems.Count == 0 && consumedRestore.TimedGrants.Hides(claim.Spawn, 20),
            "grant-before-item delivery keeps consumed incarnation hidden across private checkpoint");
        var grant = new PickupVerdict(claim, PickupResult.Grant, 120, 100, 12, 100, 1900);
        recorder.AcceptPickupVerdict(grant, 5021); resumed.Accept(records[^1].Bytes, 21);
        Check(resumed.PickupVerdicts[claim.Key] == grant && resumed.TimedGrants.View(claim.Key.LifeKey, claim.Type, 21).GrantSequence == 12,
            "late verdict preserves existing confirmed sequence");
        var taken = new PickupItemFact(claim.Spawn, claim.Type, 120, false, claim.Position);
        recorder.AcceptPickupItem(taken, 5022); resumed.Accept(records[^1].Bytes, 22);
        var final = resumed.CaptureCheckpoint(); var restored = State(); restored.RestoreCheckpoint(final);
        Check(restored.PickupItems[claim.Spawn] == taken && restored.TimedGrants.View(claim.Key.LifeKey, claim.Type, 22)
            == resumed.TimedGrants.View(claim.Key.LifeKey, claim.Type, 22), "checkpoint preserves exact item incarnation and confirmed full life");
        Check(state.TimedGrants.PendingCount == 1 && state.PickupItems.Count == 0,
            "interleaved private continuation leaves original replica untouched");
        var renewal = Claim(7, 9); recorder.AcceptPickupPrediction(renewal, 5030, 5030);
        restored.Accept(records[^1].Bytes, 30);
        var denial = new PickupVerdict(renewal, PickupResult.Taken, 130, 120, 0, 0, 0);
        recorder.AcceptPickupVerdict(denial, 5031); restored.Accept(records[^1].Bytes, 31);
        Check(restored.TimedGrants.PendingCount == 0 && restored.TimedGrants.View(claim.Key.LifeKey, claim.Type, 31).ConfirmedRemaining > 0,
            "recorded denial removes renewal prediction without ending older grant");
        var ended = fact with { Revision = 2, EndFrame = 131, Reason = TimedGrantEnd.Death, AuthorityFrame = 131 };
        restored.Accept(Packet(ended), 32);
        Check(!restored.TimedGrants.View(claim.Key.LifeKey, claim.Type, 32).Active, "recorded death end overrides matching ACK and effect");
        var nextLife = ended with { Life = ended.Life with { LifeId = 6 }, Revision = 3, Sequence = 0,
            SpawnId = -1, SpawnGeneration = 0, ContactFrame = 0, EndFrame = 0, Reason = TimedGrantEnd.Retired, AuthorityFrame = 140 };
        restored.Accept(Packet(nextLife), 40);
        Check(!restored.TimedGrants.View(claim.Key.LifeKey, claim.Type, 40).Active
            && restored.TimedGrants.View(nextLife.Life, nextLife.Type, 40).Covered, "new-life explicit absence fences old ACK presentation");
        byte[] before = restored.CaptureCheckpoint().Bytes.ToArray();
        byte[] bad = restored.CaptureCheckpoint().Bytes[..^1].ToArray();
        Reject(() => restored.RestoreCheckpoint(new ReplayReplicaCheckpoint(bad)), "truncated private grant appendix rejected");
        Check(restored.CaptureCheckpoint().Bytes.SequenceEqual(before), "failed restore does not mutate private decoder");
        recorder.Reset();
    }
    private static void BootstrapAgeAndQuietResume()
    {
        var recorder = new ReplayRecorder(); recorder.AcceptMatch(Match(), 0);
        var older = Claim(); var pending = Claim(7, 9);
        var truth = Fact(older) with { ContactFrame = 50, EndFrame = 1850, AuthorityFrame = 100 };
        recorder.AcceptTimedGrant(truth, 100); recorder.AcceptPickupPrediction(pending, 100, 100);
        var roster = RosterPacket.Create(); roster.MatchId = 2; roster.AuthorityEpoch = 9; roster.Revision = 1;
        recorder.AcceptRoster(roster, 100);
        var snapshot = new byte[1 + SnapshotHeader.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        snapshot[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = 2, AuthorityEpoch = 9, Frame = 150, PlayerCount = 0 }.Write(snapshot.AsSpan(1));
        BinaryPrimitives.WriteUInt16LittleEndian(snapshot.AsSpan(1 + SnapshotHeader.Size + NetMatchTimeSync.Size), 2);
        recorder.AcceptSnapshot(snapshot, 150, 150);
        Check(recorder.Timeline.TryGetRestorePoint(150, out var baseline)
            && baseline!.Records.Any(r => r.Payload.Length > 0 && r.Payload[0] == 79), "actual network restore baseline includes grant bootstrap");
        var worldRecords = new List<byte[]>(); recorder.SeedWorld(record => worldRecords.Add(record.Payload.ToArray()));
        var seededWorld = new ReplayReplicaState(); foreach (var packet in worldRecords) seededWorld.Accept(packet, 10);
        Check(seededWorld.TimedGrants.View(pending.Key.LifeKey, pending.Type, 10).Remaining == 1750,
            "actual SeedWorld restores grant prediction with snapshot-relative age");
        byte[] bootstrap = Array.Empty<byte>();
        recorder.SeedGrantFacts(150, record => bootstrap = record.Payload.ToArray());
        Check(bootstrap.Length > 5 && bootstrap[0] == 79
            && ReplayGrantBootstrap.TryRead(bootstrap.AsSpan(1), out uint original, out var saved)
            && original == 150 && saved.PendingCount == 1, "actual recorder seeds full grant bootstrap79 at snapshot time");
        var state = State(); var occupant = state.Occupant(3);
        state.Accept(bootstrap, 10);
        var view = state.TimedGrants.View(pending.Key.LifeKey, pending.Type, 10);
        Check(view.Remaining == 1750 && view.ConfirmedRemaining == 1700 && state.TimedGrants.PendingCount == 1,
            "bootstrap rebase preserves fifty-frame prediction age and authority grant clock");
        Check(state.Occupant(3) == occupant && !state.TryGetPlayer(3, out _), "grant bootstrap leaves roster and player decoder unchanged");
        var restore = State(); restore.RestoreCheckpoint(state.CaptureCheckpoint());
        Check(restore.TimedGrants.View(pending.Key.LifeKey, pending.Type, 10) == view,
            "rebased bootstrap and pending age survive private checkpoint");
        restore.Advance(859); Check(restore.TimedGrants.PendingCount == 1, "rebased prediction retains last pre-deadline frame");
        restore.Advance(860); Check(restore.TimedGrants.PendingCount == 0, "rebased prediction deadline is consumer860 rather than fresh910");
        state.Accept(Packet(new PickupVerdict(pending, PickupResult.Taken, 151, 150, 0, 0, 0)), 11);
        var resumed = state.TimedGrants.View(older.Key.LifeKey, older.Type, 11);
        Check(resumed.Active && resumed.Resume && resumed.TransitionFrom(default, resumed.Resume) == TimedEffectTransition.Resume,
            "seeded underlying effect resumes quietly after pending overlay ends");
        Check(!state.TimedGrants.Accept(truth, 11), "byte-identical authoritative fact remains a duplicate after bootstrap");
        Check(state.TimedGrants.Facts.Single() == truth, "quiet resume marker leaves authoritative fact bytes unchanged");
        Check(ReplayIdentityCompatibility.Convert(bootstrap, 53).IsEmpty, "old protocol53 ignores bootstrap79");
        byte[] trailing = new byte[bootstrap.Length + 1]; bootstrap.CopyTo(trailing, 0);
        Check(!ReplayGrantBootstrap.TryRead(trailing.AsSpan(1), out _, out _), "bootstrap codec rejects trailing bytes");
        Reject(() => state.Accept(trailing, 12), "private decoder rejects malformed trailing bootstrap");
        byte[] invalidScope = (byte[])bootstrap.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(invalidScope.AsSpan(5), 10);
        Check(!ReplayGrantBootstrap.TryRead(invalidScope.AsSpan(1), out _, out _), "bootstrap rejects state/header fact scope disagreement");
        recorder.Reset();
    }

    private static void StationaryItemSeed()
    {
        var recorder = new ReplayRecorder(); recorder.AcceptMatch(Match(), 0);
        var claim = Claim();
        var available = new PickupItemFact(claim.Spawn, claim.Type, 1, true, claim.Position);
        var tombstone = available with { Spawn = available.Spawn with { SpawnId = 11, Generation = 9 },
            AuthorityFrame = 1400, Available = false };
        recorder.AcceptPickupItem(available, 1); recorder.AcceptPickupItem(tombstone, 1400);
        recorder.AcceptTimedGrant(Fact(claim) with { ContactFrame = 1300, EndFrame = 3100, AuthorityFrame = 1400 }, 1400);
        var roster = RosterPacket.Create(); roster.MatchId = 2; roster.AuthorityEpoch = 9; roster.Revision = 1;
        recorder.AcceptRoster(roster, 1400);
        var snapshot = new byte[1 + SnapshotHeader.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        snapshot[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = 2, AuthorityEpoch = 9, Frame = 1500, PlayerCount = 0 }.Write(snapshot.AsSpan(1));
        BinaryPrimitives.WriteUInt16LittleEndian(snapshot.AsSpan(1 + SnapshotHeader.Size + NetMatchTimeSync.Size), 2);
        recorder.AcceptSnapshot(snapshot, 1500, 1500);
        Check(recorder.Timeline.TryGetRestorePoint(1500, out var restore), "late network baseline exists after old stationary birth");
        var fromBaseline = new ReplayReplicaState();
        foreach (var record in restore!.Records) fromBaseline.Accept(record.Payload, 0);
        Check(fromBaseline.PickupItems[available.Spawn] == available && fromBaseline.PickupItems[tombstone.Spawn] == tombstone
            && fromBaseline.TimedGrants.HasCoverage, "network baseline preserves old available identity beyond newer tombstone retention clock");
        var fromSeed = new ReplayReplicaState(); recorder.SeedWorld(record => fromSeed.Accept(record.Payload, 0));
        Check(fromSeed.PickupItems[available.Spawn] == available && fromSeed.PickupItems[tombstone.Spawn] == tombstone,
            "actual late SeedWorld retains stationary discovery alongside grant bootstrap");
        var newerRoom = available with { Spawn = available.Spawn with { Room = 8 }, AuthorityFrame = 1501 };
        recorder.AcceptPickupItem(newerRoom, 1501); recorder.AcceptPickupItem(available, 1502);
        var roomState = new ReplayReplicaState(); recorder.SeedWorld(record => roomState.Accept(record.Payload, 0));
        Check(roomState.PickupItems.Count == 1 && roomState.PickupItems.ContainsKey(newerRoom.Spawn)
            && !roomState.TimedGrants.HasCoverage,
            "recorder discovery seed retires older room grants and ignores late old room facts");
        recorder.Reset(); recorder.AcceptMatch(Match(), 1600);
        var reset = new ReplayReplicaState(); recorder.SeedWorld(record => reset.Accept(record.Payload, 0));
        Check(reset.PickupItems.Count == 0 && !reset.TimedGrants.HasCoverage,
            "recorder reset removes item and grant bootstrap state together");
        recorder.Reset();
    }
    private static void HistoricalCompatibility()
    {
        var claim = Claim(); var grant = new PickupVerdict(claim, PickupResult.Grant, 120, 100, 12, 100, 1900);
        var historical = State(); historical.Accept(Packet(grant), 121);
        Check(historical.PickupVerdicts[claim.Key] == grant && !historical.TimedGrants.HasCoverage,
            "protocol53 terminal evidence alone retains historical presentation semantics");
        var empty = State().CaptureCheckpoint().Bytes.ToArray();
        // v11 adds length4 plus the canonical empty presentation83 bytes.
        byte[] ten = empty.AsSpan(0, empty.Length - 87).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(ten.AsSpan(4), 10); ten[6] = 53;
        var restored = State(); restored.RestoreCheckpoint(new ReplayReplicaCheckpoint(ten));
        Check(!restored.TimedGrants.HasCoverage && restored.TimedGrants.PendingCount == 0,
            "v10 checkpoint has unknown A3 coverage and no invented predictions");
        Check(ReplayIdentityCompatibility.Convert(Packet(Fact(claim)), 53).IsEmpty,
            "historical protocol excludes introduced grant event");
        var recorder = new ReplayRecorder(); recorder.AcceptMatch(Match(), 0); byte[] prediction = Array.Empty<byte>();
        recorder.Accepted += record => prediction = record.Payload.ToArray(); recorder.AcceptPickupPrediction(claim, 100, 100);
        Check(ReplayIdentityCompatibility.Convert(prediction, 53).IsEmpty
            && ReplayIdentityCompatibility.Convert(prediction, 54).SequenceEqual(prediction), "prediction protocol gate preserves native54 bytes");
        recorder.Reset();
    }
    internal static int Run()
    {
        try { _checks = 0; RecorderAndProjection(); BootstrapAgeAndQuietResume(); StationaryItemSeed(); HistoricalCompatibility(); Console.WriteLine($"PASS: {_checks} grant replay assertions"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
