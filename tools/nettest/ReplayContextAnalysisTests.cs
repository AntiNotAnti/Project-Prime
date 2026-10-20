using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ReplayContextAnalysisTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidDataException(message); _checks++; }
    private static readonly MatchSemanticActor A = new(0, 1, 1), B = new(1, 2, 1);
    private static MatchSemanticEvent Kill(uint id, MatchSemanticActor? actor = null, MatchSemanticActor? target = null,
        MatchSemanticEventFlags flags = 0) => new(id, 7, 99, id, MatchSemanticEventType.PlayerKilled, actor ?? A, target ?? B, Flags: flags);
    private static ReplayShotFact Shot(uint id = 1) => new(7, 99, 101, 100, id, (ushort)id,
        0, 1, 1, 1, 2, (ushort)id, (byte)BeamType.PowerBeam, ReplayShotFactFlags.Direct | ReplayShotFactFlags.Lethal,
        100, 0, 0, 0, new Vector3(0, 0, 40));
    private static ReplayTimedShotFact Launch(uint id = 1) => new(new(99, 7, 0, 1, 1, id),
        new FireEvent(id, 100, 99, 0, FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0, 0,
            FireEvent.FlagPose, Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY,
            new Vector2(.5f), SourceLife: 1, Target: NetTargetIdentity.None),
        1, 100, TimedShotSource.NativeInput, TimedGrantProofMode.Shadow,
        new(ShotProofReason.Verified, ShotProofReason.Verified, null, 0, 0, 0),
        new(TimedGrantKnowledge.NotGranted), TimedGrantKnowledge.NotGranted, null, TimedShotUnavailable.None,
        1, new(100, 150, 0, false, 0, 0, true));
    // Real detached decoder packets, including canonical player/time/health tails.
    private static ReplayReplicaState State(GameMode mode = GameMode.Battle, bool world = true, bool complete = true,
        Vector3? speed = null, uint tick = 101, float time = 20, ushort health = 20)
    {
        var state = new ReplayReplicaState();
        byte[] match = new byte[1 + MatchStatePacket.Size]; match[0] = (byte)PacketType.MatchState;
        new MatchStatePacket { MatchId = 7, AuthorityEpoch = 99, RoomKey = "context-fixture", Mode = (byte)mode }.Write(match.AsSpan(1));
        state.Accept(match, 0); Roster(state, 1);
        Snapshot(state, tick, [0, 0, 0, 0], complete ? 4 : 3, speed ?? Vector3.Zero, health);
        if (world) World(state, tick, time);
        return state;
    }
    private static void Roster(ReplayReplicaState state, uint revision, ushort secondGeneration = 2)
    {
        var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 99; roster.Revision = revision; roster.Count = 4;
        for (byte i = 0; i < 4; i++)
        { roster.Slots[i] = i; roster.Generations[i] = i == 1 ? secondGeneration : (ushort)(i + 1); roster.Names[i] = "Fixture" + i; roster.Teams[i] = (sbyte)i; }
        byte[] bytes = new byte[1 + RosterPacket.Size]; bytes[0] = (byte)PacketType.Roster; roster.Write(bytes.AsSpan(1)); state.Accept(bytes, revision);
    }
    private static void Snapshot(ReplayReplicaState state, uint tick, short[] points, int count = 4,
        Vector3 speed = default, ushort health = 20, ushort secondGeneration = 2)
    {
        int tail = 1 + SnapshotHeader.Size + count * PlayerState.Size;
        byte[] bytes = new byte[tail + NetMatchTimeSync.Size + NetHealthSync.HeaderSize]; bytes[0] = (byte)PacketType.Snapshot;
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 99, Frame = tick, PlayerCount = (byte)count }.Write(bytes.AsSpan(1));
        for (byte i = 0; i < count; i++)
        {
            var player = new PlayerState { SlotIndex = i, SlotGeneration = i == 1 ? secondGeneration : (ushort)(i + 1), LifeId = 1,
                Health = i == 0 ? health : i == 1 ? (ushort)0 : (ushort)100, Points = points[i], Team = i,
                Flags = PlayerState.FlagActive | PlayerState.FlagSpawned, Facing = Vector3.UnitZ, Speed = i == 0 ? speed : Vector3.Zero };
            player.Write(bytes.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Size));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(tail + NetMatchTimeSync.Size), 7); state.Accept(bytes, tick);
    }
    private static void World(ReplayReplicaState state, uint tick, float time, MatchState phase = MatchState.InProgress)
    {
        var world = new ReplayAuthorityWorld { MatchId = 7, Epoch = 99, Tick = tick, Phase = phase, MatchTime = time };
        foreach (byte[] packet in ReplayAuthorityWire.Packets(world)) state.Accept(packet, tick);
    }
    private static void SnapshotContexts()
    {
        var exact = State(speed: new Vector3(.25f, 0, 0));
        Check(exact.ServerTick == 101 && exact.AuthorityWorld?.Tick == 101 && exact.TryGetPlayer(0, out var decoded) && decoded.Health == 20,
            "production decoder accepts exact authority snapshot fixture");
        var proven = new ReplayContextAnalysis.Index(); proven.Shot(900, Shot(), exact);
        Check(proven.Events.Any(e => e.Kind == "FastMovement") && proven.Events.Any(e => e.Kind == "ClutchContext"),
            "exact speed plus complete late low-health outnumbered battle snapshot proves bounded contexts");
        Check(proven.Events.All(e => !e.Kind.Contains("Airborne", StringComparison.OrdinalIgnoreCase)
            && !e.Label.Contains("victory", StringComparison.OrdinalIgnoreCase)), "snapshot context never infers airborne or victory");
        foreach (var missing in new[] { State(world: false), State(complete: false), State(time: 31), State(time: 0),
            State(health: 26), State(mode: GameMode.Survival), State(tick: 100) })
        {
            var omitted = new ReplayContextAnalysis.Index(); omitted.Shot(900, Shot(), missing);
            Check(!omitted.Events.Any(e => e.Kind == "ClutchContext"), "missing/excessive/unknown-clock snapshot context omitted");
        }
        var mismatchedWorld = State(world: false); World(mismatchedWorld, 100, 20);
        var staleWorld = new ReplayContextAnalysis.Index(); staleWorld.Shot(900, Shot(), mismatchedWorld);
        Check(!staleWorld.Events.Any(e => e.Kind == "ClutchContext"), "authority world must have exact resolve clock");
        var ended = State(world: false); World(ended, 101, 20, MatchState.GameOver);
        var endedIndex = new ReplayContextAnalysis.Index(); endedIndex.Shot(900, Shot(), ended);
        Check(!endedIndex.Events.Any(e => e.Kind == "ClutchContext"), "ended match cannot prove late in-progress context");
        var slow = new ReplayContextAnalysis.Index(); slow.Shot(900, Shot(), State(speed: new Vector3(.19f, 0, 0)));
        Check(!slow.Events.Any(e => e.Kind == "FastMovement"), "slow resolve speed does not infer fast movement");
        var unknownTick = new ReplayContextAnalysis.Index(); unknownTick.Shot(900, Shot(), State(tick: 100, speed: Vector3.UnitX));
        Check(!unknownTick.Events.Any(e => e.Kind == "FastMovement"), "fast speed at unrelated authority clock omitted");
        var scores = State(world: false); var scoreIndex = new ReplayContextAnalysis.Index();
        Snapshot(scores, 102, [5, 1, 0, 0]); scoreIndex.Observe(1000, scores);
        Snapshot(scores, 103, [5, 5, 0, 0]); scoreIndex.Observe(1001, scores);
        Check(scoreIndex.Events.Count == 0, "score baseline and tie alone create no lead context");
        Snapshot(scores, 104, [5, 6, 0, 0]); scoreIndex.Observe(1002, scores);
        Check(scoreIndex.Events.Count(e => e.Kind == "LeadChange") == 1 && scoreIndex.Events.Count(e => e.Kind == "Comeback") == 1
            && scoreIndex.Events.All(e => e.Actor == 1 && e.Frame == 1002), "stable roster retains prior leader through tie and proves comeback from four-point deficit");
        Roster(scores, 2, 7); Snapshot(scores, 105, [9, 6, 0, 0], secondGeneration: 7); scoreIndex.Observe(1003, scores);
        Check(scoreIndex.Events.Count == 2, "occupant roster change resets lead and comeback baseline");
        var incomplete = State(world: false, complete: false); var incompleteIndex = new ReplayContextAnalysis.Index();
        incompleteIndex.Observe(1, incomplete); Snapshot(incomplete, 102, [0, 6, 0, 0], count: 3); incompleteIndex.Observe(2, incomplete);
        Check(incompleteIndex.Events.Count == 0, "incomplete occupied roster cannot prove score lead changes");
    }
    public static int Run()
    {
        try
        {
            _checks = 0;
            SnapshotContexts();
            var partial = new ReplayContextAnalysis.Index(); partial.Semantic(400, Kill(1));
            Check(partial.Events.Count == 0, "partial clip does not infer first blood");
            var index = new ReplayContextAnalysis.Index();
            index.Semantic(300, new(1, 7, 99, 1, MatchSemanticEventType.MatchStarted, MatchSemanticActor.None, MatchSemanticActor.None));
            index.Semantic(401, Kill(2)); index.Semantic(999, Kill(2));
            Check(index.Events.Count == 1 && index.Events[0].Kind == "FirstBlood" && index.Events[0].Frame == 401,
                "observed start verifies first blood at visible delivery clock and duplicate delivery is ignored");
            index.Semantic(402, Kill(3, B with { Life = 2 }, A));
            Check(index.Events.Any(e => e.Kind == "Revenge" && e.Actor == B.Slot && e.Life == 2),
                "revenge links occupants across distinct lives");
            var reused = new ReplayContextAnalysis.Index(); reused.Semantic(1, Kill(1));
            reused.Semantic(2, Kill(2, B with { SlotGeneration = 3 }, A));
            Check(!reused.Events.Any(e => e.Kind == "Revenge"), "slot reuse cannot inherit revenge");
            var streak = new ReplayContextAnalysis.Index();
            streak.Semantic(1, Kill(1, target: B with { Life = 1 })); streak.Semantic(2, Kill(2, target: B with { Life = 2 })); streak.Semantic(3, Kill(3, target: B with { Life = 3 }));
            Check(streak.Events.Count(e => e.Kind == "Streak") == 1, "three same-life kills prove streak");
            streak.Semantic(4, new(4, 7, 99, 4, MatchSemanticEventType.PlayerSuicide, A, A));
            streak.Semantic(5, Kill(5, target: B with { Life = 5 })); streak.Semantic(6, Kill(6, target: B with { Life = 6 }));
            Check(streak.Events.Count(e => e.Kind == "Streak") == 1, "suicide resets life streak");
            streak.Semantic(7, Kill(7, flags: MatchSemanticEventFlags.FriendlyFire));
            streak.Semantic(8, Kill(8, A, A));
            Check(streak.Events.Count(e => e.Kind == "Streak") == 1, "friendly fire and self-kill do not increment streak");
            streak.Semantic(9, Kill(9, B, A)); streak.Semantic(10, Kill(10, target: B with { Life = 10 }));
            Check(streak.Events.Count(e => e.Kind == "Streak") == 1, "being killed resets actor life streak");
            var scope = new ReplayContextAnalysis.Index(); scope.Semantic(1, Kill(1, target: B with { Life = 1 })); scope.Semantic(2, Kill(2, target: B with { Life = 2 }));
            scope.Semantic(3, Kill(3, target: B with { Life = 3 }) with { AuthorityEpoch = 100 });
            Check(scope.Events.Count == 0, "epoch switch resets streak proof");
            var accepted = new ReplayContextAnalysis.Index();
            accepted.Shot(600, Shot() with { Flags = ReplayShotFactFlags.Direct });
            accepted.Shot(601, Shot(2));
            Check(accepted.Events.Count == 0, "nonlethal and unflagged lethal impacts imply no headshot");
            accepted.Shot(602, Shot(3) with { Flags = ReplayShotFactFlags.Headshot | ReplayShotFactFlags.Lethal });
            Check(accepted.Events.Single().Kind == "Headshot" && accepted.Events[0].Frame == 602,
                "explicit accepted lethal headshot flag proves headshot at delivery clock");
            accepted.Shot(700, Shot(3) with { Flags = ReplayShotFactFlags.Headshot | ReplayShotFactFlags.Lethal });
            Check(accepted.Events.Count == 1, "duplicate shot result does not duplicate context");
            var pairedHeadshot = new ReplayContextAnalysis.Index();
            pairedHeadshot.Semantic(600, Kill(1, flags: MatchSemanticEventFlags.Headshot));
            pairedHeadshot.Shot(605, Shot() with { Flags = ReplayShotFactFlags.Headshot | ReplayShotFactFlags.Lethal });
            Check(pairedHeadshot.Events.Count(e => e.Kind == "Headshot") == 1,
                "semantic and accepted-shot evidence for one occupant life produce one headshot despite distinct delivery frames");
            var reversedHeadshot = new ReplayContextAnalysis.Index();
            reversedHeadshot.Shot(600, Shot() with { Flags = ReplayShotFactFlags.Headshot | ReplayShotFactFlags.Lethal });
            reversedHeadshot.Semantic(605, Kill(1, flags: MatchSemanticEventFlags.Headshot));
            Check(reversedHeadshot.Events.Count(e => e.Kind == "Headshot") == 1,
                "cross-channel headshot deduplication is independent of evidence arrival order");
            var launch = Launch(); Check(launch.IsValid, "synthetic verified launch certificate valid");
            var distance = new ReplayContextAnalysis.Index(); distance.Launch(launch); distance.Shot(800, Shot());
            Check(distance.Events.Single().Kind == "LongShot", "verified launch and accepted full-key result prove long shot");
            foreach (var mismatched in new[] { Shot() with { AuthorityEpoch = 100 }, Shot() with { MatchId = 8 },
                Shot() with { ShooterSlot = 2 }, Shot() with { ShooterGeneration = 2 },
                Shot() with { ShooterLifeId = 2 }, Shot() with { ShotId = 2 } })
            {
                var isolated = new ReplayContextAnalysis.Index(); isolated.Launch(launch); isolated.Shot(800, mismatched);
                Check(isolated.Events.Count == 0, "all six launch identity fields required");
            }
            foreach (var reason in new[] { ShotProofReason.SourceUnavailable, ShotProofReason.Unexamined })
            {
                var authored = new ReplayContextAnalysis.Index();
                authored.Launch(launch with { Source = launch.Source with { Source = reason } }); authored.Shot(800, Shot());
                Check(authored.Events.Count == 0, "authored-only or unavailable source never proves distance");
            }
            var unposed = new ReplayContextAnalysis.Index();
            unposed.Launch(launch with { Source = launch.Source with { Pose = ShotProofReason.SourcePoseUnavailable } });
            unposed.Shot(800, Shot()); Check(unposed.Events.Count == 0, "unverified launch pose never proves distance");
            var conflict = new ReplayContextAnalysis.Index(); conflict.Launch(launch);
            conflict.Launch(launch with { Fire = launch.Fire with { Origin = Vector3.UnitX } }); conflict.Shot(800, Shot());
            Check(conflict.Events.Count == 0, "conflicting launch origins invalidate distance proof");
            var close = new ReplayContextAnalysis.Index(); close.Launch(launch);
            close.Shot(800, Shot() with { ImpactPoint = new Vector3(0, 0, 29.99f) });
            Check(close.Events.Count == 0, "distance threshold does not round up");
            var retained = new ReplayContextAnalysis.Index();
            for (uint i = 1; i <= 4097; i++) retained.Launch(Launch(i));
            retained.Shot(800, Shot(1));
            Check(retained.Events.Count == 0, "bounded launch cache evicts oldest proof without fabricating distance");
            retained.Shot(801, Shot(4097));
            Check(retained.Events.Single().Kind == "LongShot", "launch cache retains newest certificate after eviction");
            var independent = new ReplayContextAnalysis.Index(); independent.Shot(800, Shot());
            Check(independent.Events.Count == 0, "one source index cannot leak launch proof to another");
            var candidates = new[] { new ReplayContextEvent(500, "Headshot", 1, 2, 80, "B", "proof"),
                new ReplayContextEvent(100, "LongShot", 0, 2, 85, "A", "proof") };
            var combined = ReplayContextAnalysis.Combine([], new(candidates, "abc"), 1000);
            var reversed = ReplayContextAnalysis.Combine([], new(candidates.Reverse().ToArray(), "abc"), 1000);
            Check(JsonSerializer.Serialize(combined) == JsonSerializer.Serialize(reversed), "combined suggestions deterministic across context input order");
            Check(combined[0].StartFrame == 0 && combined[0].EndFrame == 280 && combined[0].FocusFrame == 100,
                "context windows clamp at clip start and retain focus");
            Check(ReplayContextAnalysis.Combine([], new(candidates, "abc"), 1000, new(CombatPercent: 0)).Count == 0,
                "zero combat weight suppresses contextual suggestions");
            var budget = new ReplayContextAnalysis.Index(); bool rejected = false;
            try { for (uint i = 1; i <= ReplayContextAnalysis.MaximumContexts + 1; i++)
                budget.Shot(i, Shot(i) with { Flags = ReplayShotFactFlags.Headshot | ReplayShotFactFlags.Lethal }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && budget.Events.Count == ReplayContextAnalysis.MaximumContexts, "context event budget fails boundedly without growth");
            Console.WriteLine($"PASS: {_checks} context proof assertions; synthetic semantics/certificates/accepted facts only");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
