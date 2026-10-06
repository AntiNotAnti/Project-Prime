using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static class ReplayAcceptedFireChecks
{
    internal static void Run(Action<bool, string> check, string source, string folder)
    {
        string quiet = Path.Combine(folder, "quiet-full-carriers.ppdemo");
        using (var seed = new PassiveReplayScene(source, new(256, 192)))
        {
            if (!seed.Session.HasSimulatedFrame) seed.Step();
            if (!seed.State.TryGetPlayer(0, out var actor)) throw new InvalidDataException("Fire coverage needs an accepted actor.");
            ReplayWorldCoverageCheck.Write(quiet, seed.State.Match!.Value.RoomKey, GameMode.Battle, actor.Position);
        }
        Compare(quiet, authored: false);
        ReplayLegacyRangeChecks.Run(check, quiet, folder);
        string authoredPath = Path.Combine(folder, "authored-current-and-late.ppdemo");
        using (var reader = DemoReader.Open(quiet)!)
        using (var writer = new ReplayWriterV3(authoredPath, reader.Metadata!))
        {
            while (reader.ReadNext() is { } record)
            {
                byte[] packet = record.Data;
                if (packet[0] == (byte)PacketType.SlotIntent && packet[1] == 0)
                {
                    var intent = IntentPacket.Read(packet.AsSpan(2));
                    if (record.Frame == 1530)
                    {
                        intent.FireEventCount = 1;
                        intent.FireEvents[0] = new(1, 1530, 1530, 0, FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0, 0);
                    }
                    if (record.Frame is >= 1555 and <= 1560)
                    {
                        // Initial carrier at source frame 1540 was lost. A later
                        // full intent recovers the same authored event, then retries.
                        intent.FireEventCount = 1;
                        intent.FireEvents[0] = new(2, 1540, 1540, 0, FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0, 0);
                    }
                    intent.Write(packet.AsSpan(2));
                }
                writer.WriteRecord(record.Frame, packet);
            }
        }
        Compare(authoredPath, authored: true);

        void Compare(string path, bool authored)
        {
            var size = new Vector2i(256, 192);
            var recorder = new ReplayRecorder(); using var capture = new ReplayLiveWorld(recorder);
            var reference = new Dictionary<uint, (string Gameplay, string Presentation)>();
            using (var disk = new PassiveReplayPlayer(path, size, ReplayPlayerOptions.Linear))
            {
                int beforeCurrent = 0, beforeRecovered = 0;
                for (uint frame = 0; frame <= 1800; frame++)
                {
                    while (!disk.Current.Session.HasSimulatedFrame || disk.Current.Session.CurrentFrame < frame) disk.Update();
                    reference[frame] = (ReplayStateHash.Compute(disk.Current.Scene, frame), disk.Current.Scene.ReplayPresentationHash(frame));
                    int sinceShot = disk.Current.Scene.Players.Items[0].TimeSinceShot;
                    if (frame == 1529) beforeCurrent = sinceShot;
                    if (frame == 1530 && authored) check(sinceShot < beforeCurrent, "current authored event fires on its source frame");
                    if (frame == 1539) beforeRecovered = sinceShot;
                    if (frame == 1540 && authored) check(sinceShot < beforeRecovered, "late repeated carrier schedules its authored shot on the earlier source frame");
                }
            }
            using var reader = DemoReader.Open(path)!;
            foreach (var packet in reader.Metadata!.Bootstrap.Packets.OrderBy(p => p[0] == (byte)PacketType.MatchState ? 0 : 1))
                ReplayLiveCaptureCheck.Accept(recorder, packet, 0);
            DemoRecord? pending = reader.ReadNext(); bool corrected = false;
            for (uint frame = 0; frame <= 1800; frame++)
            {
                while (pending is { } record && record.Frame <= frame)
                { ReplayLiveCaptureCheck.Accept(recorder, record.Data, frame); pending = reader.ReadNext(); }
                capture.Advance(frame, size);
                if (capture.LastError != null) throw new InvalidDataException(capture.LastError);
                if (capture.World == null) throw new InvalidDataException("No accepted-fact world.");
                if (capture.Preparing || capture.World.Session.CurrentFrame < frame)
                {
                    corrected = true;
                    var timer = Stopwatch.StartNew();
                    while (capture.Preparing || capture.World.Session.CurrentFrame < frame)
                    {
                        if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("accepted fire correction warmup");
                        capture.Advance(frame, size); Thread.Sleep(1);
                        if (capture.LastError != null) throw new InvalidDataException(capture.LastError);
                    }
                }
                // Before a missing carrier is recovered, the live observer has
                // no fact proving the source-frame shot. After correction it
                // must converge without changing the immutable accepted history.
                if (!authored || frame < 1540 || frame >= 1555)
                {
                    var expected = reference[frame];
                    if (ReplayStateHash.Compute(capture.World.Scene, frame) != expected.Gameplay
                        || capture.World.Scene.ReplayPresentationHash(frame) != expected.Presentation)
                        throw new InvalidDataException($"{(authored ? "Authored" : "Quiet")} accepted fire live/file differs at {frame}.");
                }
            }
            check(!authored || corrected, authored ? "recovered carrier uses a private bounded correction before resuming live" : "quiet modern full carriers never synthesize held-button shots");
            if (!recorder.Timeline.TryFreeze(1520, 1580, out var clip) || clip == null) throw new InvalidDataException("No accepted fire frozen clip.");
            using (clip)
            {
                var shots = clip.Records.Where(record => record.Marker is { Kind: ReplayMarkerKind.WeaponFired, Actor: 0 }).ToArray();
                check(authored ? shots.Length == 2 && shots[0].RecordingFrame == 1530 && shots[1].RecordingFrame == 1540
                    : shots.Length == 0, authored ? "current and recovered shots each publish exactly one source-frame marker despite carrier retries"
                    : "quiet modern carriers publish no invented shot telemetry");
                foreach (bool staged in new[] { false, true })
                {
                    using var job = staged ? ReplayPreparationJob.Clip(clip) : null;
                    using var prepared = job?.WaitCompleted();
                    using var player = staged ? new PassiveReplayPlayer(prepared!, size, ReplayPlayerOptions.Linear)
                        : new PassiveReplayPlayer(clip, size, ReplayPlayerOptions.Linear);
                    var timer = Stopwatch.StartNew(); int compared = 0;
                    while (!player.Ready || player.Current.Session.CurrentFrame <= 1580)
                    {
                        if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("accepted fire frozen warmup");
                        if (!player.Ready) { player.Update(24, 1); continue; }
                        uint frame = player.Current.Session.CurrentFrame; var expected = reference[frame];
                        if (ReplayStateHash.Compute(player.Current.Scene, frame) != expected.Gameplay
                            || player.Current.Scene.ReplayPresentationHash(frame) != expected.Presentation)
                            throw new InvalidDataException($"{(authored ? "Authored" : "Quiet")} {(staged ? "staged" : "sync")} frozen fire differs at {frame}.");
                        compared++; if (frame == 1580) break; player.Update(1);
                    }
                    check(compared == 61, $"{(authored ? "authored current/recovered" : "quiet full carrier")} {(staged ? "staged" : "sync")} frozen continuation preserves all gameplay/presentation frames");
                }
            }
        }
    }
}
