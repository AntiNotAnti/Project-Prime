using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

internal static class ReplayLegacyRangeChecks
{
    internal static void Run(Action<bool, string> check, string v3, string folder)
    {
        string v2 = Path.Combine(folder, "legacy-v2-source.ppdemo");
        using (var reader = DemoReader.Open(v3)!)
        using (var writer = new DemoWriter(v2))
        {
            foreach (var packet in reader.Metadata!.Bootstrap.Packets) writer.WriteRecord(0, packet);
            while (reader.ReadNext() is { } record) writer.WriteRecord(record.Frame, record.Data);
        }
        foreach (string original in new[] { v3, v2 })
        {
            string label = original == v3 ? "v3" : "v2";
            var size = new Vector2i(256, 192);
            var reference = new Dictionary<uint, (string Gameplay, string Presentation)>();
            using (var baseline = new PassiveReplayPlayer(original, size))
            {
                baseline.Seek(400, resume: true); Complete(baseline);
                for (uint frame = 400; frame <= 500; frame++)
                {
                    while (baseline.Current.Session.CurrentFrame < frame)
                    {
                        if (baseline.Transport.IsPaused || baseline.Current.Session.AtEnd)
                            throw new InvalidDataException("Legacy reference driver stopped before its requested frame.");
                        baseline.Update(1);
                    }
                    reference[frame] = (ReplayStateHash.Compute(baseline.Current.Scene, frame), baseline.Current.Scene.ReplayPresentationHash(frame));
                }
            }
            string extracted = Path.Combine(folder, label + "-packet-range.ppdemo");
            check(ReplayArchive.Extract(original, 400, 500, extracted) == ReplayOpenResult.Success, label + " packet-origin range extraction retains hidden lead-in");
            Validate(extracted, 400, 100, label + " extracted");
            string nested = Path.Combine(folder, label + "-nested-packet-range.ppdemo");
            check(ReplayArchive.Extract(extracted, 20, 80, nested) == ReplayOpenResult.Success, label + " nested packet-origin range preserves its original warmup");
            Validate(nested, 420, 60, label + " nested");

            void Validate(string path, uint sourceStart, uint duration, string description)
            {
                using (var reader = DemoReader.Open(path)!)
                    check(reader.Metadata?.WorldCheckpoint.Length == 0 && ReplayMapIdentity.PacketOrigin(reader.Metadata!) != null,
                        description + " declares the narrow validated packet-origin contract");
                foreach (bool staged in new[] { false, true })
                {
                    using var job = staged ? ReplayPreparationJob.File(path) : null;
                    using var prepared = job?.WaitCompleted();
                    using var player = staged ? new PassiveReplayPlayer(prepared!, size, new(EnableAsyncPreparation: true))
                        : new PassiveReplayPlayer(path, size);
                    Complete(player); uint compared = 0;
                    do
                    {
                        uint frame = player.Current.Session.CurrentFrame, recorded = sourceStart + frame;
                        var expected = reference[recorded];
                        if (ReplayStateHash.Compute(player.Current.Scene, recorded) != expected.Gameplay
                            || player.Current.Scene.ReplayPresentationHash(recorded) != expected.Presentation)
                            throw new InvalidDataException(description + (staged ? " staged" : " synchronous") + " differs at " + frame);
                        compared++; if (frame == duration) break;
                        if (player.Transport.IsPaused || player.Current.Session.AtEnd)
                            throw new InvalidDataException("Legacy range driver stopped before its advertised end.");
                        player.Update(1);
                    } while (true);
                    check(compared == duration + 1 && player.Current.Session.LastFrame == duration,
                        description + (staged ? " staged" : " synchronous") + " preserves every normalized gameplay/presentation frame");
                    player.Seek(0); Complete(player);
                    check(ReplayStateHash.Compute(player.Current.Scene, sourceStart) == reference[sourceStart].Gameplay,
                        description + (staged ? " staged" : " synchronous") + " backward seek preserves the first visible world");
                }
            }
        }
    }
    private static void Complete(PassiveReplayPlayer player)
    {
        var timer = Stopwatch.StartNew();
        while (!player.Ready)
        { if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException("legacy range preparation"); player.Update(24, 1); Thread.Sleep(1); }
    }
}
