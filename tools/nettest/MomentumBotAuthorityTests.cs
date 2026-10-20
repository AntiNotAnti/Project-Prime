using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual ServerSim authority and native AI. No artificial role/readiness/playback state.
internal static class MomentumBotAuthorityTests
{
    private static int _checks;
    private static void Require(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    internal static int Run(string? output, MovementModel? onlyModel = null)
    {
        try
        {
            _checks = 0;
            Require(!NetSession.Active && !DemoPlayback.IsActive, "fresh non-playback process");
            Require(ServerSim.Available(out string reason), reason);
            var summaries = new List<object>();
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Spire, Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel })
                foreach (int level in new[] { 0, 3 })
                    foreach (MovementModel model in onlyModel is { } selected ? new[] { selected }
                        : new[] { MovementModel.Native, MovementModel.Momentum })
                        summaries.Add(Capture(hunter, level, model));
            if (output != null)
            {
                string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new { checks = _checks,
                    scope = "actual ServerSim.Start session/roster and native AI; one authority bot per scene; seven Hunters, difficulties 0 and 3; actual intent sink/wire and snapshot poses; no client-puppet, rendered-camera or physical-device proof", summaries },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine($"MOMENTUM BOT AUTHORITY PASS {_checks} assertions, {(onlyModel.HasValue ? 14 : 28)} actual authority scenes");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM BOT AUTHORITY FAIL " + ex); return 1; }
    }

    private static object Capture(Hunter hunter, int level, MovementModel model)
    {
        const string room = "MP3 PROVING GROUND";
        Rng.SetRng1(8128); Rng.SetRng2(0x8128a5a5);
        var roster = RosterPacket.Create();
        roster.MatchId = 7; roster.AuthorityEpoch = 23; roster.Revision = 1; roster.Count = 1;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Hunters[0] = (byte)hunter;
        roster.Flags[0] = 1; roster.BotLevels[0] = (byte)level; roster.ContainsBots = true;
        roster.Names[0] = "Authority bot " + hunter;
        var session = new SessionStatePacket { MatchId = roster.MatchId, AuthorityEpoch = roster.AuthorityEpoch,
            Revision = 1, Phase = SessionPhase.InMatch, MaxPlayers = 2, WorldProfile = MatchWorldProfile.Resolve(2),
            Match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, TimeLimitSeconds = 0,
                PointGoal = 10000, MovementModel = model } };
        var sim = new ServerSim(); Scene? scene = null;
        int snapshots = 0, intents = 0, movingIntents = 0, negativeAxes = 0, positiveAxes = 0;
        int partialAxes = 0, solverSamples = 0, exactIntentSolverComparisons = 0, liveTicks = 0, movingTicks = 0;
        int activeSceneTicks = 0, ownedBotTicks = 0, eligibleBotTicks = 0;
        int acceptedIntentMatches = 0, capturePoseDiffersFromFrameEntry = 0, snapshotPoseMatches = 0, solverDuringCameraSequence = 0;
        IntentPacket? acceptedIntent = null;
        void ObserveAccepted(ReplayTimelineRecord record)
        {
            if (record.Kind == ReplayFactKind.Intent && record.Payload.Length >= 2
                && record.Payload[0] == (byte)PacketType.SlotIntent && record.Payload[1] == 0)
                acceptedIntent = IntentPacket.Read(record.Payload[2..]);
        }
        IntentPacket? capturedIntent = null;
        uint lastIntent = 0, lastSnapshot = 0;
        var packetBytes = new byte[IntentPacket.TouchCanonicalSize];
        Vector3 priorPosition = Vector3.Zero; bool priorLive = false;
        var samples = new List<object>();
        try
        {
            Require(sim.Start(room, GameMode.Battle, 2, payload =>
            {
                if (scene == null) return;
                var header = SnapshotHeader.Read(payload);
                Require(header.Frame > lastSnapshot, "one increasing authority snapshot per frame");
                lastSnapshot = header.Frame; snapshots++;
                Require(header.MatchId == roster.MatchId && header.AuthorityEpoch == roster.AuthorityEpoch,
                    "actual snapshot match/epoch identity");
                for (int i = 0; i < header.PlayerCount; i++)
                {
                    var state = PlayerState.Read(payload.Slice(SnapshotHeader.Size + i * PlayerState.Size, PlayerState.Size));
                    if (state.SlotIndex != 0) continue;
                    Require(state.Position == scene.Players.Items[0].Position, "published bot pose equals actual native body");
                    snapshotPoseMatches++;
                    Require(state.SlotGeneration == NetPlayerLifecycle.Generation(0) && state.LifeId == NetPlayerLifecycle.Get(0), "snapshot lifecycle matches authority");
                }
            }, () => { }, roster, session), "actual ServerSim.Start");
            scene = sim.EvidenceScene ?? throw new InvalidOperationException("actual authority scene missing");
            Require(scene.GameState.MovementModel == model && NetSession.ActiveMatchDefinition?.MovementModel == model,
                "ServerSim.Start applies selected session before loaded first-life simulation");
            Require(NetSession.Role == NetRole.Server && NetSession.IsAuthority && NetSession.LocalSlot == -1,
                "real dedicated authority without local human slot");
            Require(NetRoomChange.GameplayReady && !NetSession.FreezeGameplay,
                $"actual authority readiness blocked: ready={NetRoomChange.GameplayReady}, freeze={NetSession.FreezeGameplay}");
            typeof(Scene).GetField("_developerMomentumDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, true);
            var player = scene.Players.Items[0];
            ReplayCapture.Recorder.Accepted += ObserveAccepted;
            NetBotInput.Sink = (slot, intent) =>
            {
                Require(slot == 0 && intent.Frame == NetSession.NetFrame && intent.Frame > lastIntent,
                    "one actual actor intent per eligible authority frame");
                lastIntent = intent.Frame; intents++;
                Require(intent.MatchId == roster.MatchId && intent.AuthorityEpoch == roster.AuthorityEpoch
                    && intent.SlotGeneration == NetPlayerLifecycle.Generation(slot) && intent.LifeId == NetPlayerLifecycle.Get(slot)
                    && intent.SlotGeneration != 0 && intent.LifeId != 0, "bot intent lifecycle identity");
                Require(intent.AckFrame == 0 && intent.AckSubFrame == 0, "authority bot never authors client rewind ACK");
                Require(Finite(intent.Position), "finite actual bot capture pose");
                // The canonical accepted fact is published immediately before Sink.
                // Its pose belongs to actual input/shot capture, which can differ from
                // both whole-frame entry and final collision-body position.
                Require(acceptedIntent.HasValue, "actual bot sink has a canonical accepted intent");
                var accepted = acceptedIntent!.Value;
                Require(accepted.Frame == intent.Frame && accepted.MatchId == intent.MatchId
                    && accepted.AuthorityEpoch == intent.AuthorityEpoch && accepted.SlotGeneration == intent.SlotGeneration
                    && accepted.LifeId == intent.LifeId && accepted.Position == intent.Position && accepted.Aim == intent.Aim
                    && accepted.MoveX == intent.MoveX && accepted.MoveY == intent.MoveY && accepted.Buttons == intent.Buttons
                    && accepted.HasAnalogMove == intent.HasAnalogMove && accepted.ContinuousFireTick == intent.ContinuousFireTick,
                    "actual accepted replay fact and emitted bot intent preserve capture pose, axes, timing and lifecycle");
                acceptedIntentMatches++;
                if (priorLive && (intent.Position - priorPosition).Length >= .0001f) capturePoseDiffersFromFrameEntry++;
                // HasAnalogMove advertises the presence of the axis fields, including
                // neutral/digital input; it is not Controls.AnalogMoveActive.
                Require(intent.HasAnalogMove, "actual bot packet carries the analog fields");
                capturedIntent = intent;
                intent.Write(packetBytes); var read = IntentPacket.Read(packetBytes);
                Require(read.MoveX == intent.MoveX && read.MoveY == intent.MoveY && read.HasAnalogMove == intent.HasAnalogMove
                    && read.MatchId == intent.MatchId && read.AuthorityEpoch == intent.AuthorityEpoch
                    && read.SlotGeneration == intent.SlotGeneration && read.LifeId == intent.LifeId && read.Position == intent.Position,
                    "actual bot packet serializes axes, pose and lifecycle without drift");
                if (intent.MoveX != 0 || intent.MoveY != 0) movingIntents++;
                if (intent.MoveX < 0 || intent.MoveY < 0) negativeAxes++;
                if (intent.MoveX > 0 || intent.MoveY > 0) positiveAxes++;
                if (Math.Abs((int)intent.MoveX) is > 0 and < 127 || Math.Abs((int)intent.MoveY) is > 0 and < 127) partialAxes++;
            };
            for (int tick = 0; tick < 900; tick++)
            {
                long before = scene.DeveloperMomentumSteps; ulong frame = scene.FrameCount;
                uint networkFrame = NetSession.NetFrame;
                priorPosition = player.Position; priorLive = player.ModIsInPlay && player.Health > 0;
                capturedIntent = null; acceptedIntent = null;
                sim.Step();
                Require(sim.StepFailures == 0 && scene.FrameCount == frame + 1 && NetSession.NetFrame == networkFrame + 1,
                    "one actual successful simulation and authority frame per Step");
                Require(scene.GameState.MovementModel == model && NetSession.Role == NetRole.Server
                    && NetSession.IsAuthority && NetSession.LocalSlot == -1 && NetRoomChange.GameplayReady,
                    "actual authority model and readiness persist");
                if (player.ModIsInPlay && player.Health > 0)
                {
                    liveTicks++;
                    Require(player.IsBot && player.BotLevel == level && player.Hunter == hunter && !NetHooks.IsPuppet(player), "roster-selected real authority AI identity");
                    Require(Finite(player.Position) && Finite(player.Speed), "finite real bot state");
                    if ((player.Position - priorPosition).Length > .0001f) movingTicks++;
                }
                if (scene.MomentumMovementActive) activeSceneTicks++;
                if (scene.MomentumOwnsPlayer(player)) ownedBotTicks++;
                if (player.ModMomentumEligible) eligibleBotTicks++;
                long delta = scene.DeveloperMomentumSteps - before;
                Require(delta is >= 0 and <= 1, "one bot solver application at most per simulation frame");
                if (model == MovementModel.Native) Require(delta == 0 && !scene.MomentumMovementActive, "Native bot bypasses solver");
                if (delta == 1)
                {
                    Require(!player.IsMainPlayer, "real dedicated Roam authority bot is not a presentation main player");
                    if (scene.CameraSequences.Current != null) solverDuringCameraSequence++;
                    var sample = scene.LastMomentumStep!.Value;
                    Require(sample.Slot == 0 && sample.SimulationFrame == frame, "solver diagnostic identifies actual bot/frame");
                    Require(BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y), "solver preserves native vertical bits");
                    Vector3 incoming = new(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30);
                    var result = model == MovementModel.Quake3 ? Quake3MovementProfileV1.Step(incoming, sample.Input)
                        : MomentumProfileV1.Step(incoming, sample.Input);
                    Require(sample.NativeVelocityAfter.X == result.Velocity.X / 30 && sample.NativeVelocityAfter.Z == result.Velocity.Z / 30,
                        "actual authority bot velocity equals its immutable match profile");
                    if (capturedIntent is { } packet)
                    {
                        Require(packet.Frame == NetSession.NetFrame, "intent/solver comparison uses the same actual authority frame");
                        // Buttons select the consumed axis. A tiny analog tail may be
                        // quantized in the packet after its movement button has released;
                        // digital directions have zero packed axes and full magnitude.
                        sbyte ConsumedAxis(sbyte packed, IntentButtons positive, IntentButtons negative)
                        {
                            if ((packet.Buttons & positive) != 0) return packed != 0 ? packed : (sbyte)127;
                            if ((packet.Buttons & negative) != 0) return packed != 0 ? packed : (sbyte)-127;
                            return 0;
                        }
                        Require(IntentPacket.PackMoveAxis(sample.Input.MoveAxes.X) == ConsumedAxis(packet.MoveX, IntentButtons.MoveRight, IntentButtons.MoveLeft)
                            && IntentPacket.PackMoveAxis(sample.Input.MoveAxes.Y) == ConsumedAxis(packet.MoveY, IntentButtons.MoveUp, IntentButtons.MoveDown),
                            "capture-phase signed packet axes equal same-frame consumed Momentum input after existing button/digital semantics");
                        exactIntentSolverComparisons++;
                    }
                    solverSamples++;
                }
                if (tick % 120 == 0) samples.Add(new { tick, networkFrame = NetSession.NetFrame, live = player.ModIsInPlay,
                    position = new[] { player.Position.X, player.Position.Y, player.Position.Z }, solverSamples, intents });
            }
            Require(liveTicks > 0 && movingTicks > 0 && movingIntents > 0 && partialAxes > 0 && snapshots > 0 && snapshotPoseMatches > 0 && acceptedIntentMatches == intents, "native AI produces live movement, actual intents and snapshots");
            if (MovementModelRules.UsesMomentumSolver(model) && solverSamples == 0)
            {
                // Failure-only readback; never changes a gate, role, control or physics field.
                var privateValues = new Dictionary<string, object?>();
                foreach (string name in new[] { "_frozenTimer", "_field6D0", "_jumpPadControlLock", "_jumpPadControlLockMin",
                    "_q3LaunchGravity", "_q3LaunchFrames", "_accelerationTimer", "_slipperiness", "_standingEntCol", "_spireLedgeCrestTimer" })
                    privateValues[name] = typeof(PlayerEntity).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(player)?.ToString();
                foreach (string name in new[] { "_developerMomentumDiagnostics", "_cleanupComplete", "_visualReleaseComplete" })
                    privateValues[name] = typeof(Scene).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(scene)?.ToString();
                Console.Error.WriteLine("MOMENTUM BOT ELIGIBILITY " + JsonSerializer.Serialize(new {
                    activeSceneTicks, ownedBotTicks, eligibleBotTicks, sceneActive = scene.MomentumMovementActive,
                    owned = scene.MomentumOwnsPlayer(player), eligible = player.ModMomentumEligible,
                    stateModel = scene.GameState.MovementModel.ToString(), sessionModel = NetSession.ActiveMatchDefinition?.MovementModel.ToString(),
                    scene.GameState.Multiplayer, scene.GameState.BalancedMode, scene.GameState.EnhancedHunters,
                    foregroundThread = scene.ForegroundLease.ThreadId, currentThread = Environment.CurrentManagedThreadId,
                    foregroundGameState = ReferenceEquals(GameState.Current, scene.GameState),
                    foregroundPlayers = ReferenceEquals(PlayerEntity.LegacyRegistry, scene.Players),
                    replica = scene.Services.IsReplica, scene.SideScene, scene.Exiting, scene.IsReplayLab,
                    demoPlayback = DemoPlayback.IsActive, cameraSequence = scene.CameraSequences.Current?.GetType().Name,
                    characterVariant = player.ModCharacterGameplay.Variant.ToString(), player.IsBot, player.IsAltForm,
                    player.IsMorphing, player.IsUnmorphing, player.IsPrimeHunter, player.Health,
                    flags1 = player.Flags1.ToString(), flags2 = player.Flags2.ToString(), loadFlags = player.LoadFlags.ToString(),
                    morphPressed = player.Controls.Morph.IsPressed, attachedEnemy = player.AttachedEnemy != null, privateValues }));
            }
            Require(model == MovementModel.Native ? solverSamples == 0 : solverSamples > 0 && exactIntentSolverComparisons > 0, "selected model governs actual authority bot solver");
            if (MovementModelRules.UsesMomentumSolver(model))
                Require(solverDuringCameraSequence > 0, "Proving Ground presentation intro remains active while actual authority bot receives Momentum");
            if (MovementModelRules.UsesMomentumSolver(model))
                Require(solverDuringCameraSequence > 0, "Proving Ground presentation intro remains active while actual authority bot receives Momentum");
            Console.WriteLine($"MOMENTUM BOT AUTHORITY PASS {hunter} level={level} model={model} solver={solverSamples} intents={intents} moving={movingIntents} partial={partialAxes}");
            return new { hunter = hunter.ToString(), level, model = model.ToString(), liveTicks, movingTicks, snapshots,
                intents, movingIntents, negativeAxes, positiveAxes, partialAxes, solverSamples, exactIntentSolverComparisons, acceptedIntentMatches, capturePoseDiffersFromFrameEntry, snapshotPoseMatches, solverDuringCameraSequence, activeSceneTicks, ownedBotTicks, eligibleBotTicks, samples };
        }
        finally { ReplayCapture.Recorder.Accepted -= ObserveAccepted; NetBotInput.Sink = null; sim.Stop(); }
    }
}
