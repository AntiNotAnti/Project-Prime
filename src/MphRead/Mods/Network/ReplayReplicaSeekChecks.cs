using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal static class ReplayReplicaSeekChecks
{
    internal static void Run(string path, Vector2i size, IReadOnlyDictionary<uint, (string Gameplay, string Presentation)> frames,
        ReplayWorldCheckpoint? checkpoint, bool comparePresentation)
    {
        void Compare(PassiveReplayScene scene, uint frameOffset = 0)
        {
            uint frame = checked(scene.Session.CurrentFrame - frameOffset);
            var expected = frames[frame];
            if (ReplayStateHash.Compute(scene.Scene, frame) != expected.Gameplay
                || comparePresentation && scene.Scene.ReplayPresentationHash(frame) != expected.Presentation)
                throw new InvalidDataException($"Checkpoint seek differs from linear playback at {scene.Session.CurrentFrame}.");
        }
        using var player = new PassiveReplayPlayer(path, size);
        uint last = player.Current.Session.LastFrame;
        int comparisons = 0;
        foreach (uint requested in new uint[] { 1500, 35, 1100, 615, 0, 1799, 1201, 610, last })
        {
            uint target = Math.Min(last, requested);
            player.Seek(target);
            int updates = 0;
            while (player.Transport.IsSeeking)
            {
                if (player.Update() > PassiveReplayPlayer.MaximumStepsPerUpdate || ++updates > last + 2)
                    throw new InvalidDataException("Replica seek failed its bounded scheduling contract.");
            }
            if (player.Current.Session.CurrentFrame != target)
                throw new InvalidDataException($"Replica seek target mismatch: requested={requested}, target={target}, actual={player.Current.Session.CurrentFrame}, restore={player.SeekRestoreFrame}, steps={player.SeekSimulationSteps}, rejectedCheckpoints={player.RejectedCheckpoints}.");
            // File capsules may use an older world layout. Their documented
            // fallback must still reach the exact target with identical state.
            // A newly captured memory capsule must never require that fallback.
            if (player.RejectedCheckpoints != player.RejectedDurableCheckpoints)
                throw new InvalidDataException($"Replica seek rejected a memory checkpoint at target {target}; total={player.RejectedCheckpoints}, durable={player.RejectedDurableCheckpoints}, error={player.LastCheckpointError}.");
            Compare(player.Current); comparisons++;
            Console.WriteLine($"[replayseek] {target} from {player.SeekRestoreFrame}: {player.SeekSimulationSteps} steps, {updates} updates, {player.SeekMilliseconds:F2} ms");
            if (player.RejectedDurableCheckpoints != 0)
                Console.WriteLine($"[replayseek] durable fallbacks={player.RejectedDurableCheckpoints}; last error={player.LastCheckpointError}");
        }
        if (player.CheckpointBytes > 64L * 1024 * 1024 || player.CheckpointCount > 128)
            throw new InvalidDataException("Replica checkpoint cache exceeded its bound.");
        string eof = ReplayStateHash.Compute(player.Current.Scene, last);
        for (int i = 0; i < 10; i++) player.Update();
        if (ReplayStateHash.Compute(player.Current.Scene, last) != eof) throw new InvalidDataException("Replica EOF advanced the world.");

        if (checkpoint != null && last >= 1200)
        {
            // File playback exposes visible frames; frozen clips use the
            // original recording clock, including a nonzero archive origin.
            uint frameOffset = checked(checkpoint.Frame - 900);
            uint start = checked(1150 + frameOffset), end = checked(1200 + frameOffset);
            uint retainedEnd = (uint)Math.Min((ulong)last + frameOffset,
                (ulong)end + ReplayPoseStream.ShotFactLookaheadFrames);
            var timeline = new RollingReplayTimeline();
            timeline.AppendRestorePoint(new(checkpoint.Frame, checkpoint.Frame, ReplayRestoreKind.ReplicaCheckpoint,
                [new(checkpoint.Frame, checkpoint.Frame, ReplayFactKind.World, checkpoint.Bytes)]));
            using var reader = DemoReader.Open(path, out var result) ?? throw new InvalidDataException(result.ToString());
            while (reader.ReadNext() is DemoRecord record)
            {
                uint recordingFrame = checked(record.Frame + (reader.Metadata?.OriginRecordingFrame ?? 0));
                if (recordingFrame <= checkpoint.Frame) continue;
                if (recordingFrame > retainedEnd) break;
                // A frozen timeline contains current canonical facts, unlike a
                // file reader whose payloads still use its advertised protocol.
                ReadOnlySpan<byte> canonical = ReplayIdentityCompatibility.Convert(record.Data, reader.ProtocolVersion);
                if (!canonical.IsEmpty)
                    timeline.Append(new(recordingFrame, recordingFrame, ReplayFactKind.Snapshot, canonical));
            }
            timeline.Append(new(retainedEnd, retainedEnd, ReplayFactKind.Event, ReadOnlySpan<byte>.Empty));
            if (!timeline.TryFreeze(start, retainedEnd, out var retained) || retained == null)
                throw new InvalidDataException("Could not freeze the replica clip.");
            using var retainedClip = retained;
            // Match the file's bounded presentation lookahead while keeping the
            // visible end fixed. Future carriers are never fed to simulation.
            using var clip = new ReplayTimelineClip(retained.RestorePoint, retained.Records.ToArray(),
                start, end, reader.ProtocolVersion);
            timeline.Reset(); // frozen bytes and references must survive eviction/reset
            using var frozen = new PassiveReplayPlayer(clip, size);
            int warmup = 0;
            while (!frozen.Ready)
            {
                warmup += frozen.Update();
                if (warmup > 300) throw new InvalidDataException("Frozen clip warmup did not finish.");
            }
            if (frozen.Current.Session.CurrentFrame != start) throw new InvalidDataException("Frozen clip start differs.");
            Compare(frozen.Current, frameOffset); comparisons++;
            while (!frozen.Current.Session.AtEnd) { frozen.Update(); Compare(frozen.Current, frameOffset); comparisons++; }
            if (frozen.Current.Session.CurrentFrame != end) throw new InvalidDataException("Frozen clip end differs.");
            long acceptedAtEnd = frozen.Current.State.AcceptedPackets;
            for (int i = 0; i < 10; i++) frozen.Update();
            if (frozen.Current.Session.CurrentFrame != end || frozen.Current.State.AcceptedPackets != acceptedAtEnd)
                throw new InvalidDataException("Frozen clip consumed presentation lookahead past its visible end.");
            Compare(frozen.Current, frameOffset); comparisons++;
            frozen.Seek(checked(1160 + frameOffset));
            while (!frozen.Ready) frozen.Update();
            Compare(frozen.Current, frameOffset); comparisons++;
            Console.WriteLine($"[replayseek] frozen clip: {warmup} bounded warmup steps, frames 1150–1200, backward seek, source timeline reset");
        }
        Console.WriteLine($"[replayseek] PASS: {comparisons} file/clip comparisons, {player.CheckpointCount} checkpoints, {player.CheckpointBytes} retained bytes, frozen EOF.");
    }
}
