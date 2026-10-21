using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class TraversalStanceNetworkTests
{
    private static int _checks;
    private const IntentButtons StanceBits = IntentButtons.Crouch | IntentButtons.CrouchedState;
    private static void Check(bool good, string label)
    { _checks++; if (!good) throw new InvalidOperationException(label); }
    private static byte[] Snapshot(PlayerState player, bool historical = false)
    {
        int width = historical ? 173 : 192;
        var bytes = new byte[1 + SnapshotHeader.Size + width + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        bytes[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 23, Frame = 20, PlayerCount = 1 }.Write(bytes.AsSpan(1));
        byte[] currentPlayer = new byte[192]; player.Write(currentPlayer);
        if (historical && currentPlayer.AsSpan(173, 19).IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidOperationException("Historical stance fixture cannot discard grapple state.");
        currentPlayer.AsSpan(0, width).CopyTo(bytes.AsSpan(1 + SnapshotHeader.Size));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - NetHealthSync.HeaderSize), 7);
        return bytes;
    }
    private static byte[] Intent(IntentPacket intent, bool slotted, bool historical = false)
    {
        if (historical && (intent.MantleVisualPhase != 0 || intent.GrappleRequestNonce != 0 || (intent.Buttons & IntentButtons.Grapple) != 0))
            throw new InvalidOperationException("Historical stance fixture cannot discard grapple request.");
        var bytes = new byte[(slotted ? 2 : 1) + (historical ? 1296 : 1299)];
        bytes[0] = (byte)(slotted ? PacketType.SlotIntent : PacketType.Intent);
        intent.Write(bytes.AsSpan(slotted ? 2 : 1)); return bytes;
    }
    internal static int Run()
    {
        try
        {
            _checks = 0;
            Check(NetConfig.ProtocolVersion == 84 && (uint)IntentButtons.Crouch == 1u << 22
                && (uint)IntentButtons.CrouchedState == 1u << 23, "protocol84 preserves distinct held and canonical stance bits");
            foreach (IntentButtons bits in new[] { IntentButtons.None, IntentButtons.Crouch, IntentButtons.CrouchedState, StanceBits })
            {
                var carrier = new IntentPacket { Buttons = IntentButtons.InPlayState | bits, Aim = Vector3.UnitZ };
                Check((IntentPacket.Read(Intent(carrier, false).AsSpan(1)).Buttons & StanceBits) == bits,
                    "held crouch and canonical stance independently survive all four wire combinations");
            }
            foreach (bool crouched in new[] { false, true }) WireAndCheckpoint(crouched);
            var lower = new HistoricalPlayerPose(new(1, 2, 3), false, default, false);
            var upper = lower with { Position = new(2, 2, 3), Crouched = true };
            foreach (float fraction in new[] { 0f, .01f, .5f, .99f })
            {
                Check(NetUnlagged.InterpolatePose(lower, upper, fraction) == lower,
                    "standing-to-crouch boundary retains complete earlier pose");
                Check(NetUnlagged.InterpolatePose(upper, lower, fraction) == upper,
                    "crouch-to-standing boundary retains complete earlier pose");
            }
            var same = lower with { Position = new(3, 2, 3) };
            Check(NetUnlagged.InterpolatePose(lower, same, .5f).Position == new Vector3(2, 2, 3),
                "same-stance historical positions interpolate normally");
            Console.WriteLine($"TRAVERSAL STANCE NETWORK PASS {_checks} byte/checkpoint/history assertions; no live UDP qualification");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void WireAndCheckpoint(bool crouched)
    {
        var player = new PlayerState { SlotIndex = 0, SlotGeneration = 4, LifeId = 5, Health = 77,
            Flags = PlayerState.FlagSpawned, Position = new(1, 2, 3), Facing = Vector3.UnitZ,
            Crouched = crouched, SpawnProtected = true, HalfturretActive = true, HalfturretHealth = 17 };
        byte[] full = Snapshot(player);
        var decoded = PlayerState.Read(full.AsSpan(1 + SnapshotHeader.Size));
        Check(decoded.Crouched == crouched && decoded.SpawnProtected && decoded.HalfturretActive
            && decoded.HalfturretHealth == 17, "full snapshot stance preserves adjacent auxiliary fields");
        byte[] fast = new byte[1 + SnapshotHeader.Size + SnapshotFast.PlayerSize + SnapshotFast.ReceiptSize];
        fast[0] = (byte)PacketType.SnapshotFast; SnapshotFast.Write(full.AsSpan(1), fast.AsSpan(1));
        var fastDecoded = PlayerState.ReadFast(fast.AsSpan(1 + SnapshotHeader.Size, SnapshotFast.PlayerSize),
            full.AsSpan(1 + SnapshotHeader.Size + 41, 7));
        Check(fastDecoded.Crouched == crouched && fastDecoded.SpawnProtected && fastDecoded.HalfturretActive,
            "fast snapshot stance survives split-lane packing with adjacent fields");
        var intent = new IntentPacket { MatchId = 7, AuthorityEpoch = 23, SlotGeneration = 4, LifeId = 5,
            Frame = 18, Aim = Vector3.UnitZ, Position = player.Position,
            Buttons = IntentButtons.InPlayState | IntentButtons.MoveUp | (crouched ? StanceBits : 0), MoveY = 91 };
        foreach (bool slotted in new[] { false, true })
        {
            byte[] currentPacket = Intent(intent, slotted), packet = Intent(intent, slotted, historical: true);
            int at = slotted ? 2 : 1;
            Check(IntentPacket.Read(currentPacket.AsSpan(at)).Buttons == intent.Buttons
                && ReplayIdentityCompatibility.Convert(currentPacket, 84).SequenceEqual(currentPacket),
                "current84 intent preserves exact held and canonical stance bytes");
            foreach (int protocol in new[] { 82, 83 })
            {
                byte[] oldGrapple = currentPacket.AsSpan(0, currentPacket.Length - 1).ToArray();
                Check(ReplayIdentityCompatibility.Convert(oldGrapple, protocol).SequenceEqual(currentPacket),
                    "historical1298-byte intent preserves stance and nonce while adding zero visual phase: " + protocol);
            }
            foreach (int protocol in new[] { 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81 })
                Check(ReplayIdentityCompatibility.Convert(packet, protocol).SequenceEqual(currentPacket),
                    "historical1296-byte intent preserves original stance bytes with zero nonce and visual phase: " + protocol);
            foreach (int protocol in new[] { 60, 61, 62, 63, 64, 65, 66, 67, 68 })
            {
                var old = IntentPacket.Read(ReplayIdentityCompatibility.Convert(packet, protocol)[at..]);
                var expected = intent; expected.Buttons &= ~StanceBits;
                Check((old.Buttons & StanceBits) == 0 && (old.Buttons & ~StanceBits) == (intent.Buttons & ~StanceBits)
                    && old.MoveY == intent.MoveY
                    && ReplayIdentityCompatibility.Convert(packet, protocol).SequenceEqual(Intent(expected, slotted)),
                    "historical intent clears only previously reserved stance bits and preserves every other byte");
            }
        }
        foreach (var fixture in new[] { (Packet: full, Historical: Snapshot(player, historical: true)), (Packet: fast, Historical: fast) })
        {
            byte[] packet = fixture.Packet, historical = fixture.Historical;
            Check(ReplayIdentityCompatibility.Convert(packet, 82).SequenceEqual(packet), "current82 full/fast stance packet remains exact");
            foreach (int protocol in new[] { 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81 })
                Check(ReplayIdentityCompatibility.Convert(historical, protocol).SequenceEqual(packet),
                    "historical173-byte full/unchanged fast stance preserves body and real tail: " + protocol);
            foreach (int protocol in new[] { 60, 61, 62, 63, 64, 65, 66, 67, 68 })
            {
                byte[] normalized = ReplayIdentityCompatibility.Convert(historical, protocol).ToArray();
                var expectedPlayer = player; expectedPlayer.Crouched = false;
                byte[] expected = Snapshot(expectedPlayer);
                if (packet == fast)
                {
                    byte[] packed = new byte[fast.Length]; packed[0] = (byte)PacketType.SnapshotFast;
                    SnapshotFast.Write(expected.AsSpan(1), packed.AsSpan(1)); expected = packed;
                }
                PlayerState old = packet == full ? PlayerState.Read(normalized.AsSpan(1 + SnapshotHeader.Size))
                    : PlayerState.ReadFast(normalized.AsSpan(1 + SnapshotHeader.Size, SnapshotFast.PlayerSize),
                        full.AsSpan(1 + SnapshotHeader.Size + 41, 7));
                Check(!old.Crouched && old.SpawnProtected && old.HalfturretActive && old.Health == 77
                    && normalized.AsSpan().SequenceEqual(expected),
                    "historical full/fast snapshots clear reserved stance while retaining every other byte");
            }
        }
        var source = new ReplayReplicaState();
        byte[] match = new byte[1 + MatchStatePacket.Size]; match[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 23, RoomKey = "stance-byte-fixture", Mode = (byte)GameMode.Battle }.Write(match.AsSpan(1));
        source.Accept(match, 0);
        var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 23; roster.Revision = 1;
        roster.Count = 1; roster.Slots[0] = 0; roster.Generations[0] = 4; roster.Names[0] = "stance";
        byte[] rosterPacket = new byte[1 + RosterPacket.Size]; rosterPacket[0] = (byte)PacketType.Roster;
        roster.Write(rosterPacket.AsSpan(1)); source.Accept(rosterPacket, 1);
        source.Accept(full, 20); source.Accept(Intent(intent, true), 21);
        byte[] checkpoint = source.CaptureCheckpoint().Bytes.ToArray();
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(new(checkpoint));
        Check(restored.TryGetPlayer(0, out var current) && current.Crouched == crouched
            && restored.TryGetIntent(0, out var currentIntent) && currentIntent.Buttons == intent.Buttons
            && restored.CaptureCheckpoint().Bytes.SequenceEqual(checkpoint), "current replica checkpoint retains exact stance and intent bytes");
        foreach (byte protocol in new byte[] { 60, 61, 62, 63, 64, 65, 66, 67, 68 })
        {
            byte[] oldCheckpoint = ReplicaCheckpointCompatibilityTests.Protocol80Rows(checkpoint); oldCheckpoint[6] = protocol;
            restored.RestoreCheckpoint(new(oldCheckpoint));
            Check(restored.TryGetPlayer(0, out var old) && !old.Crouched && old.Health == 77
                && restored.TryGetIntent(0, out var oldIntent) && (oldIntent.Buttons & StanceBits) == 0
                && (oldIntent.Buttons & ~StanceBits) == (intent.Buttons & ~StanceBits),
                "historical checkpoint decoder independently clears reserved stance fields");
        }
    }

    // Actual admitted ServerSim and its live replication bridge. Input carriers
    // and lane placements are explicit test seams; this is not live UDP proof.
    internal static int RunScene(string? output)
    {
        var rows = new List<object>();
        try
        {
            _checks = 0;
            Check(!NetSession.Active && !DemoPlayback.IsActive, "fresh authority stance process");
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
                AuthorityScene(hunter, rows);
            if (output != null)
            {
                string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    scope = "Admitted ServerSim, real live replication intake and authored collision; carrier/placement seams, no live UDP qualification", rows },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"TRAVERSAL STANCE AUTHORITY PASS {_checks} assertions"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void AuthorityScene(Hunter hunter, List<object> rows)
    {
        string room = TraversalSceneTests.Room;
        var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 23; roster.Revision = 1; roster.Count = 2;
        for (byte index = 0; index < 2; index++) { roster.Slots[index] = index; roster.Generations[index] = 1; roster.Hunters[index] = (byte)hunter; }
        var session = new SessionStatePacket { MatchId = 7, AuthorityEpoch = 23, Revision = 1,
            Phase = SessionPhase.InMatch, MaxPlayers = 2, WorldProfile = MatchWorldProfile.Resolve(2),
            Match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, MovementModel = MovementModel.Traversal,
                TimeLimitSeconds = 0, PointGoal = 10000 } };
        var sim = new ServerSim(); PlayerState published = default; int snapshots = 0;
        try
        {
            Check(sim.Start(room, GameMode.Battle, 2, bytes => {
                var header = SnapshotHeader.Read(bytes);
                for (int index = 0; index < header.PlayerCount; index++)
                {
                    var state = PlayerState.Read(bytes[(SnapshotHeader.Size + index * PlayerState.Size)..]);
                    if (state.SlotIndex == 0) { published = state; snapshots++; }
                }
            }, () => { }, roster, session), "actual admitted ServerSim start");
            for (int index = 0; index < 120; index++) sim.Step();
            var scene = sim.EvidenceScene!; var player = scene.Players.Items[0];
            Check(NetSession.IsAuthority && NetSession.Role == NetRole.Server && NetSession.LocalSlot == -1
                && NetRoomChange.GameplayReady && scene.GameState.MovementModel == MovementModel.Traversal
                && NetSession.ActiveMatchDefinition?.MovementModel == MovementModel.Traversal
                && player.ModIsInPlay && player.Hunter == hunter && !player.IsBot, "genuine admitted living remote authority body");
            uint source = NetSession.NetFrame;
            void Intake(bool held, bool reported)
            {
                var intent = new IntentPacket { MatchId = 7, AuthorityEpoch = 23,
                    SlotGeneration = NetPlayerLifecycle.Generation(0), LifeId = NetPlayerLifecycle.Get(0),
                    Frame = ++source, AckFrame = NetSession.NetFrame, Position = player.Position, Aim = Vector3.UnitZ,
                    WeaponSelect = (byte)BeamType.PowerBeam,
                    Buttons = IntentButtons.InPlayState | (held ? IntentButtons.Crouch : 0) | (reported ? IntentButtons.CrouchedState : 0) };
                byte[] wire = Intent(intent, false); intent = IntentPacket.Read(wire.AsSpan(1));
                Check(NetSession.AcceptSlotIntent(0, intent, out bool fresh) && fresh
                    && scene.PlayerReplication.TryApplyIntent(player, NetSession.RemoteIntents[0]), "life-fenced carrier enters real authority replication intake");
                NetSession.BroadcastSnapshot();
                Check(published.Crouched == player.ModCrouched && published.LifeId == NetPlayerLifecycle.Get(0),
                    "actual canonical authority snapshot publishes resolved stance and life");
            }
            player.ModPlaceAt(new(42, .5f, 20));
            Intake(false, true); Check(!player.ModCrouched, "unsupported released stance report cannot crouch in clear space");
            Intake(true, false); Check(player.ModCrouched && player.Controls.Crouch.IsDown,
                "held crouch derives canonical low stance despite absent claimant state bit");
            Geometry(player, rows, hunter + "/held");
            player.ModPlaceAt(new(30, .5f, 0));
            Intake(false, false); Check(player.ModCrouched && !player.Controls.Crouch.IsDown,
                "released Ctrl and false state report cannot grow body under real tunnel roof");
            Geometry(player, rows, hunter + "/blocked-release");
            player.ModPlaceAt(new(42, .5f, 20));
            Intake(false, true); Check(!player.ModCrouched, "earned roof clearance stands despite stale crouched report");
            Geometry(player, rows, hunter + "/standing");
            player.ModPlaceAt(new(30, .5f, 0));
            Intake(false, true); Check(player.ModCrouched,
                "lost held packet can recover released crouch only when real standing obstruction and low fit are independently proved");
            Geometry(player, rows, hunter + "/recovered-blocked-release");
            Check(snapshots > 0 && sim.StepFailures == 0, "actual authority snapshots and simulation without failures");
        }
        finally { sim.Stop(); NetSession.Stop(); }
    }
    private static void Geometry(PlayerEntity player, List<object> rows, string label)
    {
        var pose = new HistoricalPlayerPose(player.Position, false, player.ModCaptureAltPose(), player.ModCrouched);
        var live = NetHistoricalTrace.CurrentBody(player); var historical = NetHistoricalTrace.Body(player, pose);
        Check(live == historical && live.Top == player.ModBipedTopFor(player.ModCrouched),
            "live and independently built historical biped cylinder use identical canonical stance");
        foreach (float belowTop in new[] { .1f, .4f })
        {
            float height = live.Position.Y + live.Top - belowTop;
            var start = new Vector3(live.Position.X - 3, height, live.Position.Z);
            var end = start + Vector3.UnitX * 6;
            var a = NetHistoricalTrace.Trace(new[] { live }, start, end, 0);
            var b = NetHistoricalTrace.Trace(new[] { historical }, start, end, 0);
            Check(a == b && a.Slot == player.SlotIndex && a.Head == (belowTop <= .3f),
                "live and historical traces share actual cylinder hits and top-relative head band");
        }
        var before = player.ModCaptureCollisionState(); bool stance = player.ModCrouched;
        try
        {
            player.ModApplyHistoricalCollisionPose(pose with { Crouched = !stance });
            Check(player.ModCollisionCrouched == !stance && player.ModCrouched == stance,
                "historical scratch changes collision stance without mutating gameplay stance");
        }
        finally { player.ModRestoreCollisionState(before); }
        Check(player.ModCaptureCollisionState() == before && player.ModCollisionCrouched == stance,
            "historical scratch restoration preserves exact prior collision and gameplay stance");
        rows.Add(new { label, crouched = stance, live.Bottom, live.Top, live.Radius });
    }
}
