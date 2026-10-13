using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.EnhancedHunters;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class EnhancedRetirementTests
{
    private static int _checks;
    private const string WorldContract = "A4512223EC0CC49516DB00DD3733168DA6969DEA059E9DDE84B31062ADEA675A";
    private static void Check(bool ok, string message)
    {
        _checks++;
        if (!ok) throw new InvalidOperationException(message);
    }

    private static ReplayReplicaCheckpoint ArchiveConstruction(List<byte[]>? packets = null)
    {
        var decoder = new ReplayReplicaState();
        void Accept(PacketType type, int size, Action<byte[]> write)
        {
            var packet = new byte[1 + size]; packet[0] = (byte)type;
            var body = new byte[size]; write(body); body.CopyTo(packet, 1);
            packets?.Add(packet);
            long before = decoder.AcceptedPackets; decoder.Accept(packet, 0);
            Check(decoder.AcceptedPackets == before + 1, "archive decoder accepts " + type);
        }
        var match = new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "",
            MatchId = 7, AuthorityEpoch = 9, Mode = (byte)GameMode.Battle, PlayerCount = 2,
            RuleBits = (ushort)MatchModifierFlags.EnhancedHunters, TimeRemaining = 60 };
        Accept(PacketType.MatchState, MatchStatePacket.Size, b => match.Write(b));
        var session = new SessionStatePacket { MatchId = 7, AuthorityEpoch = 9, MaxPlayers = 8,
            Phase = SessionPhase.InMatch, WorldProfile = MatchWorldProfile.Resolve(2),
            Match = new MatchDefinition { RoomKey = match.RoomKey, Mode = GameMode.Battle,
                EnhancedHunters = true, TimeLimitSeconds = 60, PointGoal = 999 } };
        Accept(PacketType.SessionState, SessionStatePacket.Size, b => session.Write(b));
        var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 9;
        roster.Count = 2; roster.Revision = 1;
        for (byte i = 0; i < 2; i++)
        {
            roster.Slots[i] = i; roster.Generations[i] = 2; roster.Hunters[i] = (byte)(i == 0 ? Hunter.Trace : Hunter.Samus);
            roster.Names[i] = "archive" + i;
        }
        Accept(PacketType.Roster, RosterPacket.Size, b => roster.Write(b));
        int healthOffset = SnapshotHeader.Size + 2 * PlayerState.Size + NetMatchTimeSync.Size;
        Accept(PacketType.Snapshot, healthOffset + NetHealthSync.HeaderSize, b =>
        {
            new SnapshotHeader { MatchId = 7, AuthorityEpoch = 9, PlayerCount = 2, Frame = 0 }.Write(b);
            for (byte i = 0; i < 2; i++)
                new PlayerState { SlotIndex = i, SlotGeneration = 2, LifeId = 1, Health = 99,
                    Flags = PlayerState.FlagActive | PlayerState.FlagSpawned, Position = new(2 + i * 4, 0, 2),
                    Facing = Vector3.UnitZ, Enhanced = new EnhancedHunterNetState { TargetSlot = (byte)(1 - i),
                        TargetLifeId = 1, TargetGeneration = 2, ValueA = 3, TimerA = 24, TimerB = 10 } }
                    .Write(b.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(healthOffset), 7);
        });
        return decoder.CaptureCheckpoint();
    }

    // Generated with the unchanged baseline before retiring the live feature.
    // This is a content-backed synthetic archive, not a recorded Enhanced match.
    internal static int ArchiveScene(string fixtureDirectory, bool record)
    {
        try
        {
            _checks = 0; Headless.Enter(); NetSession.Stop();
            Directory.CreateDirectory(fixtureDirectory);
            var packets = new List<byte[]>(); ArchiveConstruction(packets);
            string recording = Path.Combine(fixtureDirectory, "enhanced-synthetic.ppdemo");
            if (record)
            {
                if (File.Exists(recording)) File.Delete(recording);
                using var writer = new ReplayWriterV3(recording, new ReplayMetadata { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle,
                    MapHash = ReplayMapIdentity.Compute("MP1 SANCTORUS"), Bootstrap = new ReplayBootstrap { Packets = packets } });
                writer.WriteRecord(12, packets[^1]);
            }
            using var source = new PassiveReplayScene(recording, new(256, 192));
            Check(source.Step(), "source initializes frame zero before capture");
            var trace = source.Scene.Players.Items[0];
            trace.EnhancedState.ValueA = 3; trace.EnhancedState.TimerA = 96;
            trace.EnhancedState.GhostFrames = 40; trace.EnhancedState.TargetSlot = 1;
            trace.EnhancedState.FrostStacks[1] = 2; trace.EnhancedState.FrostLives[1] = 3;
            trace.EnhancedState.FrostGenerations[1] = 2; trace.EnhancedState.FrostExpiry[1] = 80;
            source.Scene.EnhancedWorld.Zones[0] = new EnhancedZone { Type = EnhancedZoneType.MagmaPool,
                OwnerSlot = 0, OwnerLifeId = 1, OwnerGeneration = 2, Position = new(1, 2, 3), Radius = 1.6f, RemainingFrames = 100 };
            using var captured = ReplayWorldCheckpoint.Capture(source);
            using var reflection = ReplayWorldCheckpoint.Capture(source, boundAccessors: false);
            Check(captured.Bytes.SequenceEqual(reflection.Bytes), "bound/reflection capture bytes agree");
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == WorldContract, "world contract retains baseline identities");
            Directory.CreateDirectory(fixtureDirectory);
            string capsule = Path.Combine(fixtureDirectory, "enhanced-world-v9.bin");
            string hashes = Path.Combine(fixtureDirectory, "enhanced-world-v9-hashes.json");
            if (record) File.WriteAllBytes(capsule, captured.Bytes.ToArray());
            using var historical = ReplayWorldCheckpoint.FromBytes(File.ReadAllBytes(capsule));
            using var restored = new PassiveReplayScene(recording, new(256, 192));
            historical.Restore(restored);
            Check(restored.Scene.GameState.EnhancedHunters, "historic world restores rule");
            Check(restored.Scene.Players.Items[0].EnhancedState.FrostStacks[1] == 2
                && restored.Scene.Players.Items[0].EnhancedState.GhostFrames == 40
                && restored.Scene.EnhancedWorld.Zones[0].RemainingFrames == 100, "historic capsule restores player and world extension");
            var observations = new List<string>();
            for (uint frame = 1; frame <= 12; frame++)
            {
                Check(source.Step() && restored.Step(), "archive steps continuation " + frame);
                string current = ReplayStateHash.Compute(restored.Scene, frame) + ":" + restored.Scene.ReplayPresentationHash(frame);
                if (current != ReplayStateHash.Compute(source.Scene, frame) + ":" + source.Scene.ReplayPresentationHash(frame))
                    throw new InvalidOperationException("archive forward/restore equivalence " + frame + ": " + ReplayReplicaCheck.DescribeDifferences(source, restored));
                Check(true, "archive forward/restore equivalence " + frame);
                observations.Add(current);
            }
            if (record) File.WriteAllText(hashes, JsonSerializer.Serialize(observations));
            var expected = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(hashes))!;
            Check(observations.Count == expected.Count, "baseline continuation length");
            for (int i = 0; i < expected.Count; i++) Check(observations[i] == expected[i], "baseline gameplay/presentation hashes " + i);
            Console.WriteLine($"ENHANCED ARCHIVE PASS {_checks} checks ({(record ? "baseline fixture recorded" : "baseline fixture replayed")}; headless synthetic archive)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("ENHANCED ARCHIVE FAIL " + ex); return 1; }
        finally { NetSession.Stop(); }
    }
    private static void PacketChecks()
    {
        byte[] canonical = new byte[NetConfig.MaxSnapshotSize];
        var header = new SnapshotHeader { Frame = 100, MatchId = 1, AuthorityEpoch = 1, PlayerCount = 8 };
        header.Write(canonical);
        int at = SnapshotHeader.Size;
        for (byte i = 0; i < 8; i++)
        {
            var state = new PlayerState { SlotIndex = i, SlotGeneration = 1, LifeId = 1,
                Enhanced = new EnhancedHunterNetState { TargetSlot = (byte)((i + 1) % 8), TargetLifeId = 1,
                    TargetGeneration = 1, ValueA = 3, ValueB = 3, TimerA = 255, TimerB = 255 } };
            state.Write(canonical.AsSpan(at)); at += PlayerState.Size;
        }
        NetMatchTimeSync.Write(canonical.AsSpan(at)); at += NetMatchTimeSync.Size;
        int healthAt = at;
        BinaryPrimitives.WriteUInt16LittleEndian(canonical.AsSpan(at), 1);
        canonical[at + 2] = NetHealthSync.MaxSpawns; at += NetHealthSync.HeaderSize;
        for (int i = 0; i < NetHealthSync.MaxSpawns; i++)
        { BinaryPrimitives.WriteInt16LittleEndian(canonical.AsSpan(at), (short)i); at += NetHealthSync.EntrySize; }
        var world = new EnhancedHunterWorld();
        for (int i = 0; i < 16; i++) world.Zones[i] = new EnhancedZone { Type = EnhancedZoneType.MagmaPool,
            OwnerSlot = (byte)(i / 2), OwnerLifeId = 1, OwnerGeneration = 1, RemainingFrames = 135,
            Position = new Vector3(i, 0, i), Radius = 1.6f };
        at += world.Write(canonical.AsSpan(at));
        Check(NetHealthSync.Validate(canonical.AsSpan(healthAt, at - healthAt)), "maximum world validates");
        var lanes = new NetReplicationLanes(); lanes.Prepare(canonical.AsSpan(0, at));
        Check(lanes.FastLength + NetHeader.Size <= 1200 && lanes.WorldLength + NetHeader.Size <= 1200
            && lanes.SlowLength + NetHeader.Size <= 1200, "all maximum lanes fit realtime budget");
        // Live fast snapshots carry a personalized continuous-payment receipt;
        // reliable WorldBootstrap strips it before adding its own envelope.
        Check(SnapshotFast.MaximumEncodedSize - SnapshotFast.ReceiptSize == SnapshotFast.BootstrapMaximumEncodedSize
            && SnapshotFast.BootstrapMaximumEncodedSize + WorldBootstrapIdentity.Size + 1 + 4 <= 1200,
            "receipt-free fast bootstrap fits its reliable envelope budget");
        var receiver = new NetReplicationReceiver();
        Check(receiver.Receive(PacketType.PlayerSlowState, lanes.Slow.AsSpan(0, lanes.SlowLength), 1, 1), "slow lane accepted");
        Check(receiver.Receive(PacketType.WorldState, lanes.World.AsSpan(0, lanes.WorldLength), 1, 1), "world lane accepted");
        Check(!receiver.Receive(PacketType.WorldState, lanes.World.AsSpan(0, lanes.WorldLength), 1, 1), "duplicate world rejected");
        byte[] restored = new byte[NetConfig.MaxSnapshotSize];
        int length = receiver.Assemble(lanes.Fast.AsSpan(0, lanes.FastLength), restored, 1, 1);
        Check(length == at && canonical.AsSpan(0, at).SequenceEqual(restored.AsSpan(0, length)), "all enhanced players and zones round-trip");
        world.Zones[0] = default;
        int zoneAt = healthAt + NetHealthSync.HeaderSize + NetHealthSync.MaxSpawns * NetHealthSync.EntrySize;
        int nextLength = zoneAt + world.Write(canonical.AsSpan(zoneAt));
        header.Frame++; header.Write(canonical); lanes.Prepare(canonical.AsSpan(0, nextLength));
        Check(lanes.SendWorld, "zone removal publishes immediately");
        byte[] legacy = new byte[104]; legacy[0] = (byte)PacketType.MatchState;
        Check(!MatchStatePacket.Read(ReplayIdentityCompatibility.Convert(legacy, 28)[1..]).EnhancedHunters, "legacy replay opts out");
        byte[] invalid = new byte[1 + EnhancedHunterWorld.ZoneSize]; invalid[0] = 1; invalid[1] = 255;
        Check(!EnhancedHunterWorld.Validate(invalid), "invalid zone rejected");
    }
    internal static int Run()
    {
        try
        {
            _checks = 0; NetSession.Stop();
            Check(!EnhancedHistoricalMatrixTests.CanRecordBaseline, "candidate cannot regenerate pinned historical fixtures");
            Check(NetConfig.ProtocolVersion == 55, "retirement does not bump protocol");
            Check((uint)MatchModifierFlags.EnhancedHunters == 1024 && (uint)MatchModifierFlags.BalancedMode == 8192,
                "historic modifier IDs remain reserved and distinct");
            Check(EnhancedHunterNetState.Size == 26 && EnhancedMovementEvent.Size == 8
                && EnhancedHunterWorld.ZoneSize == 24 && EnhancedHunterWorld.Capacity == 16,
                "fixed historic suffix and zone sizes");
            Check(ReplayWorldCheckpoint.ComputeContract(stable: true) == WorldContract
                && ReplayCheckpointAccessors.Contract == WorldContract, "canonical world types/fields retain baseline contract");
            foreach (int schema in new[] { 0, 10, SettingsMigration.CurrentSchema })
            {
                var settings = new MenuSettings { SettingsSchemaVersion = schema, EnhancedHunters = "on", Mode = "Battle",
                    BalancedMode = "on", AffinityWeapons = "on", PointGoal = "13" };
                Check(SettingsMigration.Apply(settings, out string summary) && settings.EnhancedHunters == "off"
                    && summary.Contains("Enhanced Hunters") && settings.Mode == "Battle" && settings.BalancedMode == "on"
                    && settings.AffinityWeapons == "on" && settings.PointGoal == "13", "saved retirement preserves independent choices at schema " + schema);
                Check(!SettingsMigration.Apply(settings, out _), "saved migration is idempotent " + schema);
                // Rule application must remain safe even when callers bypass LoadSettings.
                settings.EnhancedHunters = "on";
                var game = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false).GameState;
                game.Mode = GameMode.Battle;
                GameSettings.ApplyMatchRules(settings, game);
                Check(!game.EnhancedHunters && game.BalancedMode && game.AffinityWeapons, "direct saved rules cannot reactivate retired mode");
            }
            var match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle, EnhancedHunters = true };
            Check(!MatchModifierRules.Validate(match, out string reason) && reason.Contains("retired"), "online rejection explains retirement");
            Check(LobbyRules.ValidateDefinition(match, out reason) == LobbyResultCode.InvalidConfiguration && reason.Contains("retired"), "lobby edits reject retired modifier");
            var session = new SessionStatePacket { Match = match, MatchId = 7, AuthorityEpoch = 9, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, WorldProfile = MatchWorldProfile.Resolve(8) };
            var bytes = new byte[SessionStatePacket.Size]; session.Write(bytes);
            Check(!SessionStatePacket.TryRead(bytes, out _), "live session parser rejects retired modifier");
            Check(SessionStatePacket.TryReadArchived(bytes, out var archive) && archive.Match == match,
                "archive decoder retains retired modifier without live eligibility");
            foreach (var invalid in new[] {
                match with { RoomKey = "" }, match with { RoomKey = " " }, match with { Mode = GameMode.SinglePlayer },
                match with { InstaGib = true, NoImperialist = true }, match with { Fiesta = true, InstaGib = true },
                match with { BalancedMode = true } })
            {
                var malformed = session; malformed.Match = invalid; malformed.Write(bytes);
                Check(!SessionStatePacket.TryReadArchived(bytes, out _), "archive retains original rejection: " + invalid);
            }
            session.Write(bytes);
            foreach (int length in new[] { 10, 11, 12, SessionStatePacket.Size - 1 })
                Check(!SessionStatePacket.TryReadArchived(bytes.AsSpan(0, length), out _), "archive rejects truncated time/config fields " + length);
            var nonfinite = new byte[1 + MatchStatePacket.Size]; nonfinite[0] = (byte)PacketType.MatchState;
            new MatchStatePacket { MatchId = 7, AuthorityEpoch = 9, RoomKey = match.RoomKey, NextRoomKey = "",
                TimeRemaining = float.NaN }.Write(nonfinite.AsSpan(1));
            bool invalidTime = false;
            try { new ReplayReplicaState().Accept(nonfinite, 0); }
            catch (InvalidDataException) { invalidTime = true; }
            Check(invalidTime, "archive rejects nonfinite recorded time");
            NetSession.ApplySessionState(session);
            Check(NetSession.ServerSession == null && NetSession.LastError?.Contains("retired") == true,
                "direct online configuration rejects retirement with visible error");
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 7, AuthorityEpoch = 9, RoomKey = match.RoomKey,
                NextRoomKey = "", RuleBits = 1024 }, rotated: false);
            Check(NetSession.ServerMatch == null && NetSession.LastError?.Contains("retired") == true,
                "match control cannot bypass session rejection");
            Check(MatchModifierRules.Validate(match with { EnhancedHunters = false, BalancedMode = true }, out _), "Balanced Mode remains supported");
            Check(BalancedModeRules.HunterProfile(Hunter.Kanden).AltTractionMultiplier > 1
                && BalancedModeRules.HunterProfile(Hunter.Samus) == BalancedHunterProfile.Baseline,
                "independent Balanced Hunter profiles remain intact");
            foreach (int protocol in new[] { 29, 30 })
            {
                var historic = new byte[1 + SessionStatePacket.Protocol28Size]; historic[0] = (byte)PacketType.SessionState;
                bytes.AsSpan(0, SessionStatePacket.Protocol28Size).CopyTo(historic.AsSpan(1));
                BinaryPrimitives.WriteUInt16LittleEndian(historic.AsSpan(15), 8192);
                Check(SessionStatePacket.TryReadArchived(ReplayIdentityCompatibility.Convert(historic, protocol)[1..], out archive)
                    && archive.Match.EnhancedHunters && !archive.Match.BalancedMode, "historic Enhanced bit translation " + protocol);
            }
            var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(ArchiveConstruction());
            Check(restored.Configuration?.Match.EnhancedHunters == true && restored.TryGetPlayer(0, out var actor)
                && actor.Enhanced.ValueA == 3 && actor.Enhanced.TargetSlot == 1,
                "decoder checkpoint retains historic rule/player suffix");
            var wireState = new EnhancedHunterNetState { Flags = 5, TargetSlot = 3, TargetLifeId = 0x1234, TargetGeneration = 0x5678,
                ValueA = 2, ValueB = 1, TimerA = 17, TimerB = 23,
                Impulse0 = new EnhancedMovementEvent { Sequence = 0x1234, X = 0x0102, Y = -2, Z = -32767 } };
            byte[] suffix = new byte[26]; wireState.Write(suffix);
            byte[] expected = { 5, 3, 0x34, 0x12, 0x78, 0x56, 2, 1, 17, 23, 0x34, 0x12, 2, 1, 0xfe, 0xff, 1, 0x80, 0, 0, 0, 0, 0, 0, 0, 0 };
            Check(suffix.AsSpan().SequenceEqual(expected), "historic enhanced suffix golden bytes");
            byte[] playerBytes = new byte[PlayerState.Size]; new PlayerState { Enhanced = wireState }.Write(playerBytes);
            Check(playerBytes.AsSpan(PlayerState.LegacySize, 26).SequenceEqual(expected), "canonical PlayerState preserves suffix offset and bytes");
            PacketChecks();
            LiveHooks();
            int previousExit = Environment.ExitCode;
            try
            {
                Check(ModEntry.TryHandleHeadless(new[] { "-server", "-enhancedhunters" }) && Environment.ExitCode == 1,
                    "retired CLI fails before server or asset startup");
            }
            finally { Environment.ExitCode = previousExit; }
            Console.WriteLine($"ENHANCED RETIREMENT PASS {_checks} checks (live eligibility, settings, wire and archive compatibility)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("ENHANCED RETIREMENT FAIL " + ex); return 1; }
        finally { NetSession.Stop(); }
    }

    private sealed class ReplicaServices(ISceneServices inner) : ISceneServices
    {
        public bool IsReplica => true;
        public bool MayEndOnScore => inner.MayEndOnScore;
        public bool ShouldLeaveAfterMatch => inner.ShouldLeaveAfterMatch;
        public bool SuppressDamage => inner.SuppressDamage;
        public bool AllowsPresentationSideEffects => inner.AllowsPresentationSideEffects;
        public IPlayerReplicationHost PlayerReplication => inner.PlayerReplication;
        public bool DisablePowerups => inner.DisablePowerups;
        public MatchWorldProfile? NetworkWorldProfile => inner.NetworkWorldProfile;
        public bool ReplicatesHealthSpawns => inner.ReplicatesHealthSpawns;
        public bool TryGetHealthSpawn(short id, out HealthSpawnState state) => inner.TryGetHealthSpawn(id, out state);
    }

    private static void LiveHooks()
    {
        var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
        scene.GameState.Mode = GameMode.Battle; scene.GameState.EnhancedHunters = true;
        foreach (var player in scene.Players.Items)
        {
            player.Hunter = Hunter.Weavel; player.Health = 99; player.LoadFlags |= LoadFlags.Active;
            player.EnhancedState.ValueA = 3; player.EnhancedState.ValueB = 2; player.EnhancedState.TimerB = 91;
            Check(!EnhancedHunters.Enabled(player) && !EnhancedHunters.Authority(player), "forced live flag is inert slot " + player.SlotIndex);
            EnhancedHunters.OnPlayerSpawn(player); EnhancedHunters.OnPlayerFrame(player);
            EnhancedHunters.OnEnterAlt(player); EnhancedHunters.OnExitAlt(player);
            Check(player.EnhancedState.LocalLife == 0 && player.EnhancedState.ValueA == 3
                && player.EnhancedState.ValueB == 2 && player.EnhancedState.TimerB == 91, "live lifecycle/form hooks do not process state");
            player.Hunter = Hunter.Trace;
            Check(TraceEnhancement.LaunchScale(player) == 1 && TraceEnhancement.CloakIncrement(player) == 1,
                "live Trace retains native movement and cloak");
        }
        var replica = new Scene(default, null!, null!, _ => { }, () => { }, new ReplicaServices(scene.Services), initializeRuntime: false);
        replica.GameState.Mode = GameMode.Battle; replica.GameState.EnhancedHunters = true;
        Check(!EnhancedHunters.Enabled(replica.Players.Items[0]), "a generic replica cannot opt into legacy reconstruction");
        var owner = scene.Players.Items[0]; owner.Hunter = Hunter.Samus;
        var beam = new BeamProjectileEntity(scene) { Owner = owner, Beam = BeamType.Missile, Flags = BeamFlags.Charged };
        EnhancedHunterProjectiles.Spawned(beam, null);
        Check(!beam.EnhancedMicroSeeker && !beam.ModEnhancedSelfJump && owner.EnhancedState.ValueA == 3,
            "forced live rule cannot create enhanced projectile children or consume pips");
        var world = scene.EnhancedWorld;
        world.Zones[0] = new EnhancedZone { Type = EnhancedZoneType.MagmaPool, RemainingFrames = 100 };
        world.Tick(scene);
        Check(world.Zones[0].RemainingFrames == 100, "live world hook cannot tick archived zones");
        world.Draw(scene); world.DrawIndicators(scene);
        Check(EnhancedHunterTelemetry.Count(Hunter.Weavel, "enabled-frames") == 0, "live hooks emit no enhanced telemetry");
    }

    internal static int LiveScene()
    {
        try
        {
            _checks = 0; Headless.Enter(); NetSession.Stop();
            var session = new SessionStatePacket { MatchId = 7, AuthorityEpoch = 9, MaxPlayers = 8,
                Phase = SessionPhase.InMatch, WorldProfile = MatchWorldProfile.Resolve(8),
                Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle, EnhancedHunters = true } };
            var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 9; roster.Revision = 1; roster.Count = 8;
            for (byte i = 0; i < 8; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)(i % 7); roster.Names[i] = "retired" + i; }
            var sim = new ServerSim();
            try
            {
                Check(!sim.Start(session.Match.RoomKey, GameMode.Battle, 8, _ => { }, () => { }, roster, session), "authority rejects retired match before load");
                session.Match = session.Match with { EnhancedHunters = false, BalancedMode = true };
                Check(sim.Start(session.Match.RoomKey, GameMode.Battle, 8, _ => { }, () => { }, roster, session), "authority loads supported Balanced match");
                for (int frame = 0; frame < 180; frame++) sim.Step();
                var scene = PlayerEntity.Players[0].OwningScene;
                Check(sim.StepFailures == 0 && scene.GameState.BalancedMode && !scene.GameState.EnhancedHunters, "eight-player Balanced scene simulates independently");
                scene.GameState.BalancedMode = false; scene.GameState.EnhancedHunters = true;
                foreach (var player in scene.Players.Items)
                {
                    EnhancedHunters.OnPlayerSpawn(player);
                    Check(!EnhancedHunters.Enabled(player), "forced authority flag cannot enable Hunter " + player.Hunter);
                }
                for (int frame = 0; frame < 60; frame++) sim.Step();
                foreach (var player in scene.Players.Items)
                    Check(player.EnhancedState.LocalLife == 0 && player.EnhancedState.ValueA == 0
                        && player.EnhancedState.TimerA == 0 && player.EnhancedState.TimerB == 0, "live simulation leaves legacy state inert " + player.SlotIndex);
                Check(sim.StepFailures == 0, "forced retirement boundary has no simulation failure");
            }
            finally { sim.Stop(); }
            Console.WriteLine($"ENHANCED RETIREMENT SCENE PASS {_checks} checks (headless, eight-player Balanced and forced retired rules)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("ENHANCED RETIREMENT SCENE FAIL " + ex); return 1; }
        finally { NetSession.Stop(); }
    }
}
