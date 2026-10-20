using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Actual private CPU reconstruction/checkpoint/transport APIs; no GL, Studio UI or live networking.
internal static class MomentumReplaySceneTests
{
    private static int _checks;
    private static void Require(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidDataException(message); }
    private readonly record struct Hashes(string Gameplay, string Presentation);

    internal static int Run(string replayPath, MovementModel expected, string outputPath, string? durablePath = null)
    {
        PassiveReplayScene? restored = null;
        try
        {
            _checks = 0;
            Require(!NetSession.Active && !DemoPlayback.IsActive, "private replay test begins without a live/global playback session");
            Require(ServerSim.Available(out string reason), reason); Headless.Enter();
            string source = Path.GetFullPath(replayPath), output = Path.GetFullPath(outputPath);
            var size = new Vector2i(256, 192);
            using var first = new PassiveReplayScene(source, size);
            using var second = new PassiveReplayScene(source, size);
            CheckMode(first, expected, "first construction before gameplay");
            CheckMode(second, expected, "second construction before gameplay");
            Require(first.Session.LastFrame >= 1800, "recorded asset-backed fixture covers at least 1800 visible frames");
            var references = new Dictionary<uint, Hashes>();
            bool stanceArchive = expected == MovementModel.Traversal && first.Session.Metadata!.ProtocolVersion >= 69;
            var previousStance = new Dictionary<int, bool>();
            var stanceSeekTargets = new HashSet<uint>(); var stanceBoundaryFrames = new List<uint>();
            int snapshotStanceChecks = 0, crouchedFrames = 0, standingFrames = 0;
            bool capturedCrouch = false, capturedStandingAfterCrouch = false;
            var checkpointFrames = new List<uint>(); var checkpointSizes = new List<int>();
            using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int frames = 0, continued = 0, snapshotPoseChecks = 0, morphFrames = 0, midairMorphFrames = 0, altFrames = 0, airborneFrames = 0;
            int minimumPlayers = Int32.MaxValue, maximumPlayers = 0;
            bool capturedMorph = false;
            if (first.Session.HasSimulatedFrame)
            {
                references[first.Session.CurrentFrame] = Hash(first);
                Require(references[first.Session.CurrentFrame] == Hash(second), "initial interleaved worlds agree");
            }
            while (first.Step())
            {
                Require(second.Step(), "second interleaved replica advances with first");
                CheckMode(first, expected, "linear first"); CheckMode(second, expected, "linear second");
                uint frame = first.Session.CurrentFrame;
                Hashes hash = Hash(first);
                Require(second.Session.CurrentFrame == frame && hash == Hash(second), "actual interleaved gameplay/presentation hashes agree at " + frame);
                references[frame] = hash;
                aggregate.AppendData(Encoding.UTF8.GetBytes($"{frame}:{(int)expected}:{hash.Gameplay}:{hash.Presentation}\n"));
                if (restored != null)
                {
                    Require(restored.Step(), "restored world advances through real continuation");
                    CheckMode(restored, expected, "world checkpoint continuation");
                    Require(restored.Session.CurrentFrame == frame && hash == Hash(restored), "restored actual world continuation matches at " + frame);
                    continued++;
                }
                int players = 0; bool morph = false, midairMorph = false, alt = false, air = false, crouched = false;
                foreach (PlayerEntity player in first.Scene.Players.Items)
                {
                    Require(!first.Scene.MomentumOwnsPlayer(player), "replica player is excluded from native/Momentum ownership");
                    if (!first.State.TryGetPlayer(player.SlotIndex, out var recorded)
                        || (recorded.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned)) != (PlayerState.FlagActive | PlayerState.FlagSpawned)
                        || recorded.Health == 0) continue;
                    players++;
                    Require((player.Position - recorded.Position).Length <= .00001f,
                        $"canonical recorded snapshot owns collision body at frame{frame}/slot{player.SlotIndex}");
                    snapshotPoseChecks++;
                    if (stanceArchive)
                    {
                        Require(player.ModCrouched == recorded.Crouched && player.ModCollisionCrouched == recorded.Crouched,
                            $"canonical recorded stance owns live and collision shape at frame{frame}/slot{player.SlotIndex}");
                        snapshotStanceChecks++;
                        crouched |= recorded.Crouched;
                        if (previousStance.TryGetValue(player.SlotIndex, out bool previous) && previous != recorded.Crouched)
                        {
                            stanceBoundaryFrames.Add(frame);
                            if (stanceSeekTargets.Count < 18)
                            {
                                if (frame > 0) stanceSeekTargets.Add(frame - 1);
                                stanceSeekTargets.Add(frame); stanceSeekTargets.Add(frame + 1);
                            }
                        }
                        previousStance[player.SlotIndex] = recorded.Crouched;
                    }
                    morph |= player.IsMorphing || player.IsUnmorphing;
                    midairMorph |= (player.IsMorphing || player.IsUnmorphing) && MathF.Abs(player.Speed.Y) > .001f;
                    alt |= player.IsAltForm; air |= !player.Flags1.TestFlag(PlayerFlags1.Grounded);
                    if (first.Scene.ReplayPoses?.SamplePresented(player.SlotIndex, 1, out Vector3 presented, out Vector3 facing) == true)
                        Require(Finite(presented) && Finite(facing), "indexed presentation pose remains finite");
                }
                minimumPlayers = Math.Min(minimumPlayers, players); maximumPlayers = Math.Max(maximumPlayers, players);
                if (morph) morphFrames++; if (midairMorph) midairMorphFrames++; if (alt) altFrames++; if (air) airborneFrames++;
                if (stanceArchive && players > 0) { if (crouched) crouchedFrames++; else standingFrames++; }
                bool captureCrouch = stanceArchive && crouched && !capturedCrouch;
                bool captureStanding = stanceArchive && capturedCrouch && !crouched && players > 0 && !capturedStandingAfterCrouch;
                if (frame > 0 && frame % 300 == 0 || morph && !capturedMorph || captureCrouch || captureStanding)
                {
                    capturedMorph |= morph;
                    capturedCrouch |= captureCrouch; capturedStandingAfterCrouch |= captureStanding;
                    using var checkpoint = ReplayWorldCheckpoint.Capture(first);
                    checkpointFrames.Add(checkpoint.Frame); checkpointSizes.Add(checkpoint.Bytes.Length);
                    restored?.Dispose(); restored = new PassiveReplayScene(source, size);
                    CheckMode(restored, expected, "restore target construction before gameplay");
                    checkpoint.Restore(restored);
                    CheckMode(restored, expected, "actual world checkpoint restored");
                    Require(restored.Session.CurrentFrame == frame && hash == Hash(restored), "actual captured world restores identical gameplay/presentation at " + frame);
                }
                frames++;
                Require(frames <= first.Session.LastFrame + 2, "linear replay frame loop bounded by actual archive");
            }
            Require(!second.Step(), "both replicas reach actual EOF together");
            Require(frames >= 1800 && continued > 0 && checkpointFrames.Count >= 6, "all-frame traversal and actual periodic checkpoint continuation activated");
            Require(morphFrames > 0 && midairMorphFrames > 0 && altFrames > 0 && airborneFrames > 0, "actual recorded midair form transitions and airborne interval covered");
            Require(maximumPlayers >= 2, "actual multiplayer recorded actors reconstructed");
            if (stanceArchive)
                Require(snapshotStanceChecks > 0 && crouchedFrames > 0 && standingFrames > 0 && stanceBoundaryFrames.Count >= 2
                    && capturedCrouch && capturedStandingAfterCrouch,
                    "protocol69 recording contains real stance transitions and captures actual crouched and subsequent standing checkpoints");
            string digest = Convert.ToHexString(aggregate.GetHashAndReset());
            restored?.Dispose(); restored = null;

            var seeks = new List<object>();
            using var playerTransport = new PassiveReplayPlayer(source, size,
                new ReplayPlayerOptions(EnableSeeking: true, EnableMemoryCheckpoints: true, EnableDurableCheckpoints: false));
            CheckMode(playerTransport.Current, expected, "seek player initial construction");
            // Forward seeks build real memory capsules; backward seeks restore them.
            uint last = playerTransport.Current.Session.LastFrame;
            foreach (uint requested in new uint[] { 1500, 35, 1100, 615, 0, 1799, 1201, 610, last }
                .Concat(stanceSeekTargets.Where(references.ContainsKey).OrderByDescending(frame => frame)).Distinct())
            {
                uint target = Math.Min(last, requested);
                playerTransport.Seek(target);
                int updates = 0, steps = 0;
                while (playerTransport.Transport.IsSeeking)
                {
                    int advanced = playerTransport.Update();
                    Require(advanced <= PassiveReplayPlayer.MaximumStepsPerUpdate, "actual seek update remains within 120-step budget");
                    steps += advanced; updates++;
                    CheckMode(playerTransport.Current, expected, "seek update including memory restore");
                    Require(updates <= last + 2, "actual seek completes within bounded updates");
                }
                Require(playerTransport.Current.Session.CurrentFrame == target, "real seek reaches exact target");
                Require(references.TryGetValue(target, out Hashes expectedHash) && Hash(playerTransport.Current) == expectedHash,
                    "actual forward/backward memory seek preserves gameplay/presentation at " + target);
                Require(playerTransport.RejectedCheckpoints == 0, "new memory checkpoints never fall back after a failed restore");
                seeks.Add(new { requested, target, updates, steps, restoreFrame = playerTransport.SeekRestoreFrame, source = playerTransport.CheckpointSource });
            }
            Require(playerTransport.CheckpointCount > 0 && playerTransport.CheckpointCount <= 128
                && playerTransport.CheckpointBytes <= 64L * 1024 * 1024, "actual memory checkpoint cache activated within bounds");
            // Exercise the real media transport, without claiming a Studio/theatre UI test.
            uint pausedTarget = Math.Min(last - 2, 620u);
            playerTransport.Seek(pausedTarget);
            while (playerTransport.Transport.IsSeeking) { playerTransport.Update(); CheckMode(playerTransport.Current, expected, "pause target seek"); }
            playerTransport.Transport.Pause();
            Hashes paused = Hash(playerTransport.Current);
            for (int i = 0; i < 10; i++)
            {
                Require(playerTransport.Update() == 0 && Hash(playerTransport.Current) == paused, "actual paused transport does not advance replica");
                CheckMode(playerTransport.Current, expected, "paused media transport");
            }
            playerTransport.Transport.StepForward();
            Require(playerTransport.Update() == 1 && playerTransport.Current.Session.CurrentFrame == pausedTarget + 1,
                "actual paused single-step advances exactly one fixed frame");
            CheckMode(playerTransport.Current, expected, "paused single step");
            Require(Hash(playerTransport.Current) == references[pausedTarget + 1], "paused single-step equals actual linear reference");
            playerTransport.Transport.Play(); Require(playerTransport.Update() == 1, "actual transport resumes one normal fixed step");
            CheckMode(playerTransport.Current, expected, "resumed media transport");
            Require(Hash(playerTransport.Current) == references[pausedTarget + 2], "resumed transport equals actual linear reference");

            var durableSeeks = new List<object>();
            if (durablePath != null)
            {
                // A fresh player starts cold with the normal durable-enabled policy.
                // These targets jump past a complete capsule interval; the final
                // backwards seek must also select a real file capsule.
                using var durablePlayer = new PassiveReplayPlayer(Path.GetFullPath(durablePath), size);
                CheckMode(durablePlayer.Current, expected, "cold durable player construction");
                Require(durablePlayer.Current.Session.DurableCheckpoints.Count > 0,
                    "provided real archive contains durable world capsules");
                foreach (uint requested in new uint[] { 301, 799, 1250, 1800, 2500, 301 })
                {
                    uint target = Math.Min(durablePlayer.Current.Session.LastFrame, requested);
                    Require(references.ContainsKey(target), "durable target belongs to the original all-frame reference");
                    durablePlayer.Seek(target);
                    int updates = 0, steps = 0;
                    while (durablePlayer.Transport.IsSeeking)
                    {
                        int advanced = durablePlayer.Update();
                        Require(advanced <= PassiveReplayPlayer.MaximumStepsPerUpdate,
                            "durable seek uses the actual bounded update budget");
                        updates++; steps += advanced;
                        CheckMode(durablePlayer.Current, expected, "durable file restore/seek update");
                        Require(updates <= durablePlayer.Current.Session.LastFrame + 2,
                            "actual durable seek completes within bounded updates");
                    }
                    Require(durablePlayer.Current.Session.CurrentFrame == target
                        && Hash(durablePlayer.Current) == references[target],
                        "durable file capsule preserves original gameplay/presentation at " + target);
                    Require(durablePlayer.CheckpointSource == "file" && durablePlayer.SeekSimulationSteps < 300,
                        "actual file capsule selected with fewer than 300 continuation steps");
                    Require(durablePlayer.RejectedCheckpoints == 0 && durablePlayer.RejectedDurableCheckpoints == 0,
                        "current-build durable capsule restores without fallback");
                    CheckMode(durablePlayer.Current, expected, "completed durable target");
                    durableSeeks.Add(new { requested, target, updates, steps,
                        simulationSteps = durablePlayer.SeekSimulationSteps,
                        restoreFrame = durablePlayer.SeekRestoreFrame, source = durablePlayer.CheckpointSource });
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { checks = _checks, expectedModel = expected.ToString(), frames,
                recordedProtocol = first.Session.Metadata!.ProtocolVersion,
                recordedProfileVersion = first.Session.Metadata.RecordedMovementProfileVersion,
                firstFrame = references.Keys.Min(), lastFrame = references.Keys.Max(), gameplayPresentationDigest = digest,
                snapshotPoseChecks, morphFrames, midairMorphFrames, altFrames, airborneFrames,
                stanceArchive, snapshotStanceChecks, crouchedFrames, standingFrames, stanceBoundaryFrames,
                stanceSeekTargets = stanceSeekTargets.OrderBy(frame => frame).ToArray(), capturedCrouch, capturedStandingAfterCrouch,
                gameplayHashIncludesStance = stanceArchive,
                minimumRecordedPlayers = minimumPlayers, maximumRecordedPlayers = maximumPlayers,
                checkpointFrames, checkpointSizes, continuedFrames = continued,
                memoryCheckpointCount = playerTransport.CheckpointCount, memoryCheckpointBytes = playerTransport.CheckpointBytes, seeks, durableEnabled = durablePath != null, durableSeeks,
                scope = "actual asset-backed private CPU replay worlds; within-environment interleaved gameplay/presentation hashes; actual world checkpoint capture/restore/continuation, memory seek and media pause/step APIs; optional real durable-file seeks when provided; no GL raster, physical device, Studio/theatre UI, killcam or export acceptance" }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"MOMENTUM REPLAY SCENE PASS {_checks} assertions model={expected} frames={frames} checkpoints={checkpointFrames.Count} continuation={continued}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("MOMENTUM REPLAY SCENE FAIL " + ex); return 1; }
        finally { restored?.Dispose(); }
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static Hashes Hash(PassiveReplayScene replay)
    {
        string gameplay = ReplayStateHash.Compute(replay.Scene, replay.Session.CurrentFrame);
        if (replay.Session.Metadata is { ProtocolVersion: >= 69, RecordedMovementModel: MovementModel.Traversal })
        {
            // Test-only extension: archived schemas and historical expected
            // hashes retain their original meaning. Current stance must also
            // survive world capsules, continuation, and every transport seek.
            var stance = new StringBuilder(gameplay);
            foreach (PlayerEntity player in replay.Scene.Players.Items)
                stance.Append('|').Append(player.SlotIndex).Append(':').Append(player.ModCrouched ? '1' : '0')
                    .Append(player.ModCollisionCrouched ? '1' : '0')
                    .Append(':').Append(BitConverter.SingleToInt32Bits(player.ModBipedTop));
            gameplay = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stance.ToString())));
        }
        return new(gameplay, replay.Scene.ReplayPresentationHash(replay.Session.CurrentFrame));
    }
    private static void CheckMode(PassiveReplayScene replay, MovementModel expected, string operation)
    {
        Require(replay.Scene.GameState.MovementModel == expected, operation + ": explicit recorded movement model preserved");
        Require(replay.Session.Metadata?.RecordedMovementModel == expected
            && replay.Session.Metadata.MovementLabel == MovementModelRules.DisplayName(expected),
            operation + ": metadata label follows recorded rules across playback and seeks");
        int recordedProfile = expected switch
        {
            MovementModel.Native => 0,
            MovementModel.Momentum => 1,
            MovementModel.Quake3 => replay.Session.Metadata!.ProtocolVersion >= 62 ? 2 : 1,
            MovementModel.Traversal => replay.Session.Metadata!.ProtocolVersion >= 69 ? 2 : 1,
            _ => throw new InvalidDataException("Unknown expected replay movement model.")
        };
        if (expected == MovementModel.Traversal)
            Require(replay.Session.Metadata!.ProtocolVersion >= 68, operation + ": Traversal requires an explicitly recorded supporting protocol");
        Require(replay.Session.Metadata!.RecordedMovementProfileVersion == recordedProfile,
            operation + ": profile version follows recorded protocol, never current live policy");
        Require(replay.Scene.Services.IsReplica && !replay.Scene.MomentumMovementActive && replay.Scene.DeveloperMomentumSteps == 0,
            operation + ": private replica never reruns an active horizontal adapter");
        if (expected == MovementModel.Traversal && replay.Session.Metadata!.ProtocolVersion >= 69)
            foreach (PlayerEntity player in replay.Scene.Players.Items)
                if (replay.State.TryGetPlayer(player.SlotIndex, out var recorded) && recorded.Health > 0
                    && (recorded.Flags & (PlayerState.FlagActive | PlayerState.FlagSpawned)) == (PlayerState.FlagActive | PlayerState.FlagSpawned))
                    Require(player.ModCrouched == recorded.Crouched && player.ModCollisionCrouched == recorded.Crouched,
                        operation + ": canonical recorded stance restored exactly for slot" + player.SlotIndex);
        Require(!NetSession.Active && !DemoPlayback.IsActive, operation + ": private reader never opens live/global playback ownership");
    }
}
