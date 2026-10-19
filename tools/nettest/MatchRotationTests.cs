using System;
using System.Buffers.Binary;
using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Production transitions and packet parsers, with explicit asset-free world/load
// fixtures. Virtual participants do not represent live 0/2/4/8-device acceptance.
internal static class MatchRotationTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static int _checks;

    public static int Run()
    {
        var current = typeof(GameState).GetProperty("Current", Static)!;
        object? previous = current.GetValue(null);
        try
        {
            var scene = new Scene(new Vector2i(256, 192),
                Mods.Input.SyntheticInput.CreateKeyboard(), Mods.Input.SyntheticInput.CreateMouse(),
                _ => { }, () => { }, initializeRuntime: false);
            current.SetValue(null, scene.GameState);
            ServerRotations();
            ClientRotations(scene);
            InitialRules(scene);
            PostMatchGenerationFences(scene);
            Console.WriteLine($"PASS: {_checks} match-rotation assertions; 6 transitions for each virtual 0/2/4/8-participant cohort, stale/reordered packets and epoch restart. No live-device or asset-world acceptance claimed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { NetSession.Stop(); current.SetValue(null, previous); }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException($"FAIL: {message}");
    }

    private static object? Call(object owner, string name, params object?[] args) =>
        owner.GetType().GetMethod(name, Instance)!.Invoke(owner, args);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Instance)!.GetValue(owner)!;
    private static void Set(object owner, string name, object? value) => owner.GetType().GetField(name, Instance)!.SetValue(owner, value);
    private static void SetPeer(object owner, string name, object value) => owner.GetType().GetField(name)!.SetValue(owner, value);
    private static T Peer<T>(object owner, string name) => (T)owner.GetType().GetField(name)!.GetValue(owner)!;

    private static void ServerRotations()
    {
        foreach (int humans in new[] { 0, 2, 4, 8 })
        {
            var rotation = MapRotation.FromList(new[] {
                ("MP1 SANCTORUS", GameMode.Battle), ("MP1 SANCTORUS", GameMode.Survival),
                ("MP2 HARVESTER", GameMode.Battle) }, 300, 5);
            var server = new DedicatedServer(0, 8, rotation);
            Set(server, "_controlPlaneOnlyForTests", true);
            var peers = Field<IList>(server, "_peers");
            Type peerType = typeof(DedicatedServer).GetNestedType("Peer", BindingFlags.NonPublic)!;
            for (int slot = 0; slot < humans; slot++)
            {
                object peer = Activator.CreateInstance(peerType, nonPublic: true)!;
                SetPeer(peer, "SlotIndex", slot); SetPeer(peer, "ClientId", (uint)(slot + 1));
                SetPeer(peer, "EndPoint", new IPEndPoint(IPAddress.Loopback, 20000 + slot));
                SetPeer(peer, "Spectating", slot == humans - 1);
                peers.Add(peer);
                Field<ushort[]>(server, "_slotGenerations")[slot] = 1;
            }
            if (humans is 2 or 4)
            {
                var command = new LobbyCommandPacket { Hunter = (byte)Hunter.Trace, BotLevel = 2, TeamIndex = -1 };
                object?[] configure = { command, false, null };
                Check((LobbyResultCode)Call(server, "ConfigureBot", configure)! == LobbyResultCode.Ok,
                    "virtual rotation includes an ordinary server bot slot");
            }
            var first = new MatchDefinition { RoomKey = rotation.Current.RoomKey, Mode = GameMode.Battle,
                PointGoal = 5, TimeLimitSeconds = 300, SpawnProtection = true };
            object?[] begin = { first, 1d, null };
            Check((bool)Call(server, "BeginLobbyMatch", begin)!, "initial generation begins through server owner");
            var start = Field<NetMatchStart>(server, "_start");
            MatchStartIdentity old = default;
            for (int round = 0; round < 6; round++)
            {
                var state = (SessionStatePacket)Call(server, "BuildSessionState")!;
                Check(start.Expected == (byte)((1 << humans) - 1), "only frozen human participants join load barrier");
                Check(start.Identity.MatchId == state.MatchId && start.Identity.AuthorityEpoch == state.AuthorityEpoch,
                    "session and start barrier publish the same authoritative identity");
                Check(round == 0 || !start.MarkLoaded(0, old), "preceding round load acknowledgement is fenced");
                foreach (object peer in peers)
                {
                    Check(!Peer<bool>(peer, "HasIntentFrame") && Peer<uint>(peer, "LastIntentFrame") == 0
                        && !Peer<bool>(peer, "MatchReady") && !Peer<bool>(peer, "SceneLoaded")
                        && Peer<int>(peer, "BootstrapLength") == 0
                        && Peer<WorldBootstrapIdentity>(peer, "BootstrapIdentity") == default
                        && !Peer<bool>(peer, "PostMatchReady") && Peer<ushort>(peer, "TimingMatch") == 0
                        && Peer<bool>(peer, "Spectating") == (Peer<int>(peer, "SlotIndex") == humans - 1),
                        "new generation invalidates prior intent, admission, bootstrap, results and timing ownership");
                }
                server.FriendlyFire = !state.Match.FriendlyFire;
                server.SpawnProtection = !state.Match.SpawnProtection;
                var frozen = (SessionStatePacket)Call(server, "BuildSessionState")!;
                var control = (MatchStatePacket)Call(server, "BuildState", round * 100d)!;
                Check(frozen.Match == state.Match && frozen.WorldProfile == state.WorldProfile
                    && control.FriendlyFire == state.Match.FriendlyFire
                    && control.SpawnProtection == state.Match.SpawnProtection,
                    "continuous authority advertises frozen rules despite mutable next-round settings");

                if (humans > 0)
                {
                    for (int slot = humans - 1; slot >= 0; slot--) Check(start.MarkLoaded(slot, start.Identity), "reordered scene readiness accepted");
                    Check(start.Advance(round * 100 + 2) && start.Stage == StartStage.Synchronizing,
                        "scene completion cannot bypass world synchronization");
                    for (int slot = 0; slot < humans; slot++) Check(start.MarkWorldReady(slot, start.Identity), "matching world readiness accepted");
                    Check(start.Advance(round * 100 + 3) && start.Stage == StartStage.Countdown,
                        "world completion commits the shared countdown");
                }
                Call(server, "CheckLoadBarrier", round * 100 + 5d);
                Check(((SessionStatePacket)Call(server, "BuildSessionState")!).Phase == SessionPhase.InMatch,
                    "all virtual cohorts can release the start barrier through the server owner");
                old = start.Identity;
                Call(server, "EndMatch", round * 100 + 10d, "test sudden end");
                Check(((SessionStatePacket)Call(server, "BuildSessionState")!).Phase == SessionPhase.PostMatch,
                    "sudden end enters the server-owned intermission before rotation");

                // Dirty the state that previously survived a consecutive round.
                foreach (object peer in peers)
                {
                    SetPeer(peer, "HasIntentFrame", true); SetPeer(peer, "LastIntentFrame", uint.MaxValue - 5);
                    SetPeer(peer, "MatchReady", true); SetPeer(peer, "SceneLoaded", true);
                    SetPeer(peer, "BootstrapLength", 3); SetPeer(peer, "BootstrapIdentity", new WorldBootstrapIdentity(old, 9, 1, 900));
                    SetPeer(peer, "PostMatchReady", true); SetPeer(peer, "TimingMatch", old.MatchId);
                }
                // One borrowed rotation models a resolved map vote; the owner
                // still performs exactly the same transition as a sudden end.
                if (round == 2) rotation.PlayNext("MP3 PROVING GROUND", GameMode.Battle);
                string wanted = rotation.Next.RoomKey;
                Call(server, "AdvanceMap", (round + 1) * 100d);
                Check(start.Identity.MatchId == NetLifecycleTracker.Next(old.MatchId)
                    && start.Identity.StartGeneration != old.StartGeneration
                    && ((SessionStatePacket)Call(server, "BuildSessionState")!).Match.RoomKey == wanted,
                    "rotation/voted map advances one identity and freezes the selected next map");
            }
            // Disconnect removes a frozen participant; a late join cannot
            // enlarge this attempt or acknowledge on behalf of an old start.
            if (humans > 0)
            {
                int slot = humans - 1;
                start.Remove(slot);
                Check(!start.MarkLoaded(slot, start.Identity) && (start.Expected & (1 << slot)) == 0,
                    "disconnected participant cannot return to the frozen barrier");
            }
        }
    }

    private static SessionStatePacket Session(ushort match, ulong epoch, ushort revision, MatchDefinition definition) => new()
    {
        MatchId = match, AuthorityEpoch = epoch, Revision = revision, Phase = SessionPhase.InMatch,
        Policy = ServerSessionPolicy.Continuous, MaxPlayers = 8, Match = definition,
        StartGeneration = revision, StartStage = StartStage.InMatch,
        WorldProfile = Mods.Multiplayer.MatchWorldProfile.Resolve(8)
    };

    private static MatchStatePacket Control(SessionStatePacket state) => new()
    {
        MatchId = state.MatchId, AuthorityEpoch = state.AuthorityEpoch, RoomKey = state.Match.RoomKey,
        Mode = (byte)state.Match.Mode, PointGoal = state.Match.PointGoal, TimeRemaining = 300,
        Flags = MatchStatePacket.FlagInProgress
    };

    private static void ClientRotations(Scene scene)
    {
        NetSession.StartPlayback();
        SpectatorMode.SetSessionPreference(true);
        var definition = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Survival,
            PointGoal = 5, TimeLimitSeconds = 300, SpawnProtection = true };
        var previous = Session(40, 700, 1, definition);
        NetSession.ApplySessionState(previous);
        var replay = new ReplayReplicaState();
        ushort generation = 2;
        for (int round = 0; round < 7; round++)
        {
            // The last transition restarts the same numeric match in a newer
            // epoch. Counters and the loaded world must still be invalidated.
            var next = Session(round == 6 ? previous.MatchId : NetLifecycleTracker.Next(previous.MatchId),
                round == 6 ? previous.AuthorityEpoch + 1 : previous.AuthorityEpoch,
                (ushort)(round + 2), definition);
            SetRoom("_loadedMatch", previous.MatchId); SetRoom("_loadedEpoch", previous.AuthorityEpoch);
            NetSession.RemoteIntents[1] = new IntentPacket { Frame = 9000 };
            NetSession.RemoteIntentValid[1] = true;
            NetSession.RemoteStates[1] = new PlayerState { Health = 99 };
            NetSession.RemoteStateValid[1] = true;
            NetPlayerLifecycle.SetOccupant(1, generation);
            NetSession.SlotOccupied[1] = true;
            var result = PostMatchReportPacket.Create(); result.MatchId = previous.MatchId;
            result.Count = 1; result.Slots[0] = 1; result.Generations[0] = generation; result.Kills[0] = 9;
            typeof(NetSession).GetMethod("ApplyPostMatchReport", Static)!.Invoke(null, new object[] { result });
            Check(NetSession.HasPostMatchReport, "preceding round has a result fixture to invalidate");
            var baseline = new WorldBootstrapIdentity(new(previous.MatchId, previous.AuthorityEpoch, previous.StartGeneration), 9, generation, 900);
            typeof(NetSession).GetField("_receivingBootstrap", Static)!.SetValue(null, baseline);
            typeof(NetSession).GetField("_confirmedBootstrap", Static)!.SetValue(null, baseline);
            var lanes = (NetReplicationReceiver)typeof(NetSession).GetField("_laneReceiver", Static)!.GetValue(null)!;
            byte[] oldSlow = new byte[15 + NetMatchTimeSync.Size];
            BinaryPrimitives.WriteUInt16LittleEndian(oldSlow, previous.MatchId);
            BinaryPrimitives.WriteUInt64LittleEndian(oldSlow.AsSpan(2), previous.AuthorityEpoch);
            BinaryPrimitives.WriteUInt32LittleEndian(oldSlow.AsSpan(10), 9000);
            Check(lanes.Receive(PacketType.PlayerSlowState, oldSlow, previous.MatchId, previous.AuthorityEpoch),
                "preceding stream has high-revision slow state to invalidate");
            var sequence = (ushort[])typeof(NetDamage).GetField("_sequence", Static)!.GetValue(null)!;
            sequence[1] = 100;
            int resets = NetSession.SnapshotStreamResets;
            if ((round & 1) == 0)
            {
                NetSession.ApplyMatchState(Control(next), false);
                NetSession.ApplySessionState(next);
            }
            else
            {
                NetSession.ApplySessionState(next);
                NetSession.ApplyMatchState(Control(next), false);
            }
            Check(NetSession.SnapshotStreamResets == resets + 1 && NetSession.LastSnapshotFrame == 0
                && !NetSession.RemoteIntentValid[1] && !NetSession.RemoteStateValid[1]
                && NetSession.RemoteIntents[1].Frame == 0 && NetDamage.Sequence(1) == 0
                && !NetSession.HasPostMatchReport && lanes.SlowRevision == 0
                && typeof(NetSession).GetField("_receivingBootstrap", Static)!.GetValue(null) == null
                && typeof(NetSession).GetField("_confirmedBootstrap", Static)!.GetValue(null) == null
                && SpectatorMode.PreferSpectator,
                "either control arrival order resets all match counters and ownership once");
            Check(!NetRoomChange.GameplayReady && !NetRoomChange.CanApplyConfiguration(next.MatchId, next.AuthorityEpoch),
                "previous loaded world cannot admit gameplay or new rules, including a same-id epoch restart");
            SetRoom("_loadPending", true); SetRoom("_requestedDefinition", definition);
            NetRoomChange.PrepareRoom(scene);
            SetRoom("_loadPending", false); SetRoom("_loadedMatch", next.MatchId); SetRoom("_loadedEpoch", next.AuthorityEpoch);
            Check(NetRoomChange.GameplayReady && scene.GameState.PointGoal == 5 && scene.GameState.SpawnProtection,
                "load boundary installs the frozen round configuration");

            var roster = RosterPacket.Create(); roster.MatchId = next.MatchId; roster.AuthorityEpoch = next.AuthorityEpoch;
            roster.Revision = 1; roster.Count = 1; roster.Slots[0] = 1; roster.Generations[0] = generation;
            roster.Names[0] = "Current"; roster.Teams[0] = -1;
            NetSession.ApplyRoster(roster);
            var alive = new PlayerState { SlotIndex = 1, SlotGeneration = generation, LifeId = 1, Health = 75,
                Flags = PlayerState.FlagActive | PlayerState.FlagSpawned, Facing = Vector3.UnitZ };
            Deliver(Snapshot(next, 1, alive));
            Check(NetSession.LastSnapshotFrame == 1 && NetSession.RemoteStateValid[1], "new stream accepts frame one and life one");
            NetSession.ApplySessionState(previous);
            NetSession.ApplyMatchState(Control(previous), false);
            Deliver(Snapshot(previous, 50000, alive));
            Check(NetSession.CurrentMatchId == next.MatchId && NetSession.AuthorityEpoch == next.AuthorityEpoch
                && NetSession.LastSnapshotFrame == 1, "late prior session/control/snapshot cannot poison the new frame origin");
            Check(!lanes.Receive(PacketType.PlayerSlowState, oldSlow, next.MatchId, next.AuthorityEpoch),
                "delayed old slow-lane revision cannot repopulate the new stream");
            if (previous.MatchId != next.MatchId)
            {
                typeof(NetSession).GetMethod("ApplyPostMatchReport", Static)!.Invoke(null, new object[] { result });
                Check(!NetSession.HasPostMatchReport && scene.GameState.Kills[1] == 0,
                    "delayed prior-round report cannot repopulate new results or score counters");
            }
            Deliver(Snapshot(next, 3, alive with { Health = 0, Flags = PlayerState.FlagActive }));
            Deliver(Snapshot(next, 2, alive));
            Check(NetSession.RemoteStates[1].Health == 0, "reordered living state cannot undo a current-life death");

            ushort retired = generation; generation++;
            roster.Revision = 2; roster.Generations[0] = generation; NetSession.ApplyRoster(roster);
            var intent = new IntentPacket { MatchId = next.MatchId, AuthorityEpoch = next.AuthorityEpoch,
                SlotGeneration = retired, LifeId = 1, Frame = 50000 };
            Check(!NetSession.AcceptSlotIntent(1, intent), "same-round reconnect fences prior occupant intent");
            alive.SlotGeneration = generation;
            Deliver(Snapshot(next, 4, alive));
            intent.SlotGeneration = generation; intent.Frame = 1;
            Check(NetSession.AcceptSlotIntent(1, intent), "replacement admission accepts a fresh intent origin");
            Deliver(Snapshot(next, 5, alive with { SlotGeneration = retired, Health = 0 }));
            Check(NetPlayerLifecycle.Generation(1) == generation && NetPlayerLifecycle.Get(1) == 1,
                "late retired occupant cannot kill the replacement life");

            roster.Revision = 3; roster.Count = 2; roster.Slots[1] = 2;
            roster.Generations[1] = 1; roster.Names[1] = "Late spectator"; roster.Teams[1] = -1;
            NetSession.ApplyRoster(roster);
            var spectator = new PlayerState { SlotIndex = 2, SlotGeneration = 1,
                Flags = PlayerState.FlagActive | PlayerState.FlagSpectating, Facing = Vector3.UnitZ };
            Deliver(Snapshot(next, 6, spectator));
            Check(NetSession.SlotOccupied[2] && NetPlayerLifecycle.Get(2) == 0
                && NetPlayerLifecycle.StateOf(NetSession.RemoteStates[2]) == NetworkPlayerState.Spectating,
                "join-in-progress spectator admits without allocating a phantom life");
            spectator.LifeId = 1; spectator.Health = 75; spectator.Flags = PlayerState.FlagActive | PlayerState.FlagSpawned;
            Deliver(Snapshot(next, 7, spectator));
            Check(NetSession.RemoteStateValid[2] && NetPlayerLifecycle.Get(2) == 1
                && NetPlayerLifecycle.StateOf(NetSession.RemoteStates[2]) == NetworkPlayerState.Alive,
                "matching first life can leave spectator admission during an existing round");

            byte[] control = new byte[1 + MatchStatePacket.Size]; control[0] = (byte)PacketType.MatchState;
            Control(next).Write(control.AsSpan(1)); replay.Accept(control, (uint)round * 10);
            byte[] members = new byte[1 + RosterPacket.Size]; members[0] = (byte)PacketType.Roster;
            roster.Write(members.AsSpan(1)); replay.Accept(members, (uint)round * 10 + 1);
            Check(replay.Match?.MatchId == next.MatchId && replay.Match?.AuthorityEpoch == next.AuthorityEpoch
                && replay.Occupant(1).Generation == generation, "archival replica agrees on consecutive match and occupant identities");
            previous = next;
        }
    }

    // Exercise the real wire parser and client receive/apply path. Same-match
    // reconnects retain MatchId, so slot incarnation must fence report rows.
    private static void PostMatchGenerationFences(Scene scene)
    {
        foreach (GameMode mode in new[] { GameMode.Capture, GameMode.Nodes, GameMode.Defender,
            GameMode.PrimeHunter, GameMode.Relic, GameMode.GunGame, GameMode.KillConfirmed, GameMode.Headhunter })
        {
            NetSession.StartPlayback();
            var session = Session((ushort)(500 + (int)mode), 1900, 1,
                new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = mode, PointGoal = 5, TimeLimitSeconds = 300 });
            NetSession.ApplySessionState(session);
            NetSession.ApplyMatchState(Control(session), false);
            scene.GameState.ConfigureMatchMode(mode, applyDefaults: true);
            var roster = RosterPacket.Create();
            roster.MatchId = session.MatchId; roster.AuthorityEpoch = session.AuthorityEpoch;
            roster.Revision = 1; roster.Count = 4;
            for (int i = 0; i < 4; i++)
            {
                roster.Slots[i] = (byte)i; roster.Generations[i] = (ushort)(20 + i);
                roster.Names[i] = $"Original {i}"; roster.Teams[i] = -1;
            }
            NetSession.ApplyRoster(roster);
            var retained = PostMatchReportPacket.Create(); retained.MatchId = session.MatchId;
            retained.Count = 1; ReportRow(ref retained, 0, 0, 20, 30, "Retained current result");
            DeliverReport(retained);
            Check(NetSession.HasPostMatchReport && NetSession.PostMatchReportCount == 1
                && NetSession.PostMatchReportSlot(0) == 0, "current generation seeds a real parsed result row");

            // The actual roster owner retires slot1's old occupant within this match.
            roster.Revision = 2; roster.Generations[1] = 50; roster.Names[1] = "Replacement";
            NetSession.ApplyRoster(roster);
            Check(NetPlayerLifecycle.Generation(1) == 50, "same-match roster advances the replacement generation");
            for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++) SeedResultState(scene, slot);
            string[] before = Enumerable.Range(0, PlayerEntity.SlotCapacity).Select(slot => ResultState(scene, slot)).ToArray();
            string retainedUi = ResultUi();

            var stale = PostMatchReportPacket.Create(); stale.MatchId = session.MatchId; stale.Count = 1;
            ReportRow(ref stale, 0, 1, 21, 300, "Retired occupant must not return");
            DeliverReport(stale);
            Check(Enumerable.Range(0, before.Length).All(slot => ResultState(scene, slot) == before[slot]),
                $"{mode}: delayed same-match retired-generation report preserves every counter, objective and nickname");
            Check(ResultUi() == retainedUi, $"{mode}: all-stale report preserves retained result UI rows");

            var mixed = PostMatchReportPacket.Create(); mixed.MatchId = session.MatchId; mixed.Count = 5;
            ReportRow(ref mixed, 0, 1, 21, 301, "Stale row");
            ReportRow(ref mixed, 1, 2, 0, 302, "Zero generation row");
            ReportRow(ref mixed, 2, 3, 999, 303, "Unknown generation row");
            ReportRow(ref mixed, 3, 4, 1, 304, "Unoccupied unknown-generation row");
            ReportRow(ref mixed, 4, 0, 20, 400, "Accepted current row");
            DeliverReport(mixed);
            Check(Enumerable.Range(1, before.Length - 1).All(slot => ResultState(scene, slot) == before[slot]),
                $"{mode}: mixed report cannot mutate stale, zero-generation or unknown-generation occupants");
            Check(scene.GameState.Kills[0] == 400 && scene.GameState.Deaths[0] == 401
                && scene.GameState.HeadshotKills[0] == 402 && scene.GameState.LongestKillStreak[0] == 403
                && scene.GameState.ShotsFired[0] == 404 && scene.GameState.ShotsHit[0] == 405
                && scene.GameState.MatchDamageDealt[0] == 406 && scene.GameState.MatchDamageTaken[0] == 407
                && GameState.Nicknames[0] == "Accepted current row", $"{mode}: valid mixed row still applies its complete current result");
            var objective = Mods.Multiplayer.MatchObjectiveReport.Values(scene.GameState, 0);
            Check(mode switch
            {
                GameMode.Capture => objective == (408, 409, 410, 411),
                GameMode.Nodes => objective == (408, 409, 410, 0),
                GameMode.Defender => objective == (408, 409, 0, 0),
                GameMode.PrimeHunter or GameMode.Relic or GameMode.Headhunter => objective == (408, 409, 410, 0),
                GameMode.GunGame => scene.GameState.FastestStageSeconds[0] == 409 && scene.GameState.Points[0] == 35,
                GameMode.KillConfirmed => objective == (408, 409, 0, 0),
                _ => false
            }, $"{mode}: valid mixed row applies its native objective fields");
            Check(NetSession.PostMatchReportCount == 1 && NetSession.PostMatchReportSlot(0) == 0
                && NetSession.PostMatchReportTeam(0) == 1 && NetSession.PostMatchReportName(0) == "Accepted current row",
                $"{mode}: filtered rows compact result UI without default-slot ghost rows");
            string[] afterMixed = Enumerable.Range(0, before.Length).Select(slot => ResultState(scene, slot)).ToArray();
            string mixedUi = ResultUi();

            var unknown = PostMatchReportPacket.Create(); unknown.MatchId = session.MatchId; unknown.Count = 2;
            ReportRow(ref unknown, 0, 2, 0, 500, "Zero cannot replace results");
            ReportRow(ref unknown, 1, 3, 999, 501, "Unknown cannot replace results");
            DeliverReport(unknown);
            Check(Enumerable.Range(0, before.Length).All(slot => ResultState(scene, slot) == afterMixed[slot])
                && ResultUi() == mixedUi, $"{mode}: entirely invalid generations preserve stats and retained UI");

            var invalidSlot = PostMatchReportPacket.Create(); invalidSlot.MatchId = session.MatchId; invalidSlot.Count = 2;
            ReportRow(ref invalidSlot, 0, 0, 20, 600, "Must reject whole malformed packet");
            ReportRow(ref invalidSlot, 1, 255, 20, 601, "Invalid slot");
            DeliverReport(invalidSlot, parserAccepts: false);
            Check(Enumerable.Range(0, before.Length).All(slot => ResultState(scene, slot) == afterMixed[slot])
                && ResultUi() == mixedUi, $"{mode}: invalid-slot parser rejection preserves current results without slot-zero ghosts");
        }
    }

    private static void ReportRow(ref PostMatchReportPacket report, int row, byte slot, ushort generation,
        ushort value, string name)
    {
        report.Slots[row] = slot; report.Generations[row] = generation; report.Teams[row] = 1;
        report.Names[row] = name; report.Kills[row] = value; report.Deaths[row] = (ushort)(value + 1);
        report.Headshots[row] = (ushort)(value + 2); report.LongestKillStreaks[row] = (ushort)(value + 3);
        report.ShotsFired[row] = (uint)value + 4; report.ShotsHit[row] = (uint)value + 5;
        report.DamageDealt[row] = (uint)value + 6; report.DamageTaken[row] = (uint)value + 7;
        report.ObjectiveA[row] = value + 8; report.ObjectiveB[row] = value + 9;
        report.ObjectiveC[row] = value + 10; report.ObjectiveD[row] = value + 11;
    }

    private static void DeliverReport(PostMatchReportPacket report, bool parserAccepts = true)
    {
        byte[] packet = new byte[1 + PostMatchReportPacket.Size]; packet[0] = (byte)PacketType.PostMatchReport;
        report.Write(packet.AsSpan(1));
        Check(PostMatchReportPacket.TryRead(packet.AsSpan(1), out _) == parserAccepts,
            "post-match wire parser accepts only the intended structurally valid report fixture");
        Deliver(packet);
    }

    private static void SeedResultState(Scene scene, int slot)
    {
        var state = scene.GameState; int value = 10 + slot;
        GameState.Nicknames[slot] = $"Current occupant {slot}";
        state.Kills[slot] = value; state.Deaths[slot] = value + 1; state.HeadshotKills[slot] = value + 2;
        state.LongestKillStreak[slot] = value + 3; state.ShotsFired[slot] = value + 4; state.ShotsHit[slot] = value + 5;
        state.MatchDamageDealt[slot] = value + 6; state.MatchDamageTaken[slot] = value + 7;
        state.OctolithScores[slot] = value + 8; state.OctolithDrops[slot] = value + 9;
        state.OctolithStops[slot] = value + 10; state.ObjectiveSeconds[slot] = value + 11;
        state.NodesCaptured[slot] = value + 12; state.NodesLost[slot] = value + 13;
        state.ObjectiveContests[slot] = value + 14; state.Time[slot] = value + 15;
        state.KillsAsPrime[slot] = value + 16; state.PrimesKilled[slot] = value + 17;
        state.ObjectivePickups[slot] = value + 18; state.FastestStageSeconds[slot] = value + 19;
        state.TokenConfirms[slot] = value + 20; state.TokenDenies[slot] = value + 21;
        state.TokensCollected[slot] = value + 22; state.TokensBanked[slot] = value + 23;
        state.LargestBank[slot] = value + 24; state.Points[slot] = value + 25;
    }

    private static string ResultState(Scene scene, int slot)
    {
        var s = scene.GameState;
        return string.Join("|", new object[] { GameState.Nicknames[slot], s.Kills[slot], s.Deaths[slot],
            s.HeadshotKills[slot], s.LongestKillStreak[slot], s.ShotsFired[slot], s.ShotsHit[slot],
            s.MatchDamageDealt[slot], s.MatchDamageTaken[slot], s.OctolithScores[slot], s.OctolithDrops[slot],
            s.OctolithStops[slot], s.ObjectiveSeconds[slot], s.NodesCaptured[slot], s.NodesLost[slot],
            s.ObjectiveContests[slot], s.Time[slot], s.KillsAsPrime[slot], s.PrimesKilled[slot], s.ObjectivePickups[slot],
            s.FastestStageSeconds[slot], s.TokenConfirms[slot], s.TokenDenies[slot], s.TokensCollected[slot],
            s.TokensBanked[slot], s.LargestBank[slot], s.Points[slot] });
    }

    private static string ResultUi() => NetSession.HasPostMatchReport + ":" + NetSession.PostMatchReportCount + ":"
        + string.Join("|", Enumerable.Range(0, NetSession.PostMatchReportCount).Select(row =>
            $"{NetSession.PostMatchReportSlot(row)}/{NetSession.PostMatchReportTeam(row)}/{NetSession.PostMatchReportName(row)}"));

    private static void InitialRules(Scene scene)
    {
        var match = NetSession.ActiveMatchDefinition!.Value;
        scene.GameState.ConfigureMatchMode(GameMode.Battle, applyDefaults: true);
        match.ApplyInitialConfiguration(scene.GameState);
        Check(scene.GameState.Mode == GameMode.Survival && scene.GameState.PointGoal == 5,
            "pre-player configuration replaces prior-round mode and spare lives");
        scene.GameState.ConfigureMatchMode(GameMode.Survival, applyDefaults: true);
        GameSettings.ApplyMatchRules(new MenuSettings(), scene.GameState);
        Check(scene.GameState.PointGoal == 5 && scene.GameState.MatchTime == 300 && scene.GameState.SpawnProtection,
            "online post-Setup restore keeps authoritative first-life goal, timer and modifiers");
        (match with { Mode = GameMode.Defender, PointGoal = 137, TimeLimitSeconds = 0 }).ApplyInitialConfiguration(scene.GameState);
        Check(scene.GameState.PointGoal == 0 && scene.GameState.TimeGoal == 137 && scene.GameState.MatchTime == -1,
            "time-target mode clears old point target and preserves untimed sentinel");
    }

    private static void SetRoom(string field, object value) => typeof(NetRoomChange).GetField(field, Static)!.SetValue(null, value);
    private static void Deliver(byte[] packet)
    {
        NetSession.InjectPlaybackPacket(packet, packet.Length);
        NetSession.Update(0);
    }
    private static byte[] Snapshot(SessionStatePacket session, uint frame, PlayerState state)
    {
        byte[] packet = new byte[1 + SnapshotHeader.Size + PlayerState.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        packet[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = session.MatchId, AuthorityEpoch = session.AuthorityEpoch, Frame = frame, PlayerCount = 1 }.Write(packet.AsSpan(1));
        state.Write(packet.AsSpan(1 + SnapshotHeader.Size));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(packet.Length - NetHealthSync.HeaderSize), session.MatchId);
        return packet;
    }
}
