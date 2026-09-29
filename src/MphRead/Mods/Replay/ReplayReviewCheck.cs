using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal static class ReplayReviewCheck
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (int fps in ReplayExportRates.Supported)
        {
            var samples = new ReplayExportSampler(10000, 10060, fps);
            check(samples.Count == fps + 1, "one-second inclusive export count");
            double previous = -1;
            for (long i = 0; i < samples.Count; i++)
            {
                var sample = samples.At(i);
                check(Math.Abs(sample.Frame - (10000 + i * 60d / fps)) < 1e-9,
                    "export sample drift");
                check(sample.Frame > previous && sample.SimulationFrame == Math.Ceiling(sample.Frame)
                    && sample.Alpha > 0 && sample.Alpha <= 1,
                    "export simulation bracket");
                check(Math.Abs(sample.SimulationFrame - 1d + sample.Alpha - sample.Frame) < 1e-6,
                    "export pose and entity interpolation disagree");
                previous = sample.Frame;
            }
            var late = new ReplayExportSampler(uint.MaxValue - 60, uint.MaxValue, fps);
            check(late.At(late.Count - 1).SimulationFrame == uint.MaxValue,
                "export sampler overflow near maximum frame");
        }
        check(new ReplayExportSampler(0, 6, 120).Count == 13
            && new ReplayExportSampler(0, 6, 60).Count == 7
            && new ReplayExportSampler(0, 6, 30).Count == 4,
            "legacy inclusive export counts changed");
        check(new ReplayExportSampler(10, 16, 24).At(1).Frame == 12.5,
            "24 FPS must interpolate half a simulation frame");

        // Receipt/recording frame is later than the authoritative end. This
        // candidate is eligible, but the empty fixture has no world to freeze.
        var timeline = new RollingReplayTimeline();
        using var killcam = new KillcamController(timeline);
        var context = new KillcamContext(1, 2, 1230, 0, 1, 1, false, true, true, true);
        var kill = new ReplayKillIdentity(1, 2, 1208, 1, 1, 1, 0, 1, 1, 2);
        killcam.NoteKill(new(ReplayMarkerKind.Kill, 1, 0, Kill: kill), 1230, context);
        check(!killcam.BeginFinal(null!, context, 1214, false, true)
            && killcam.EndReason == KillcamEndReason.Unavailable,
            "final kill eligibility compared receipt time with server time");
        killcam.NoteKill(new(ReplayMarkerKind.Kill, 1, 0, Kill: kill with { ServerTick = 1215 }), 1200, context);
        check(!killcam.BeginFinal(null!, context, 1214, false, true)
            && killcam.EndReason == KillcamEndReason.None,
            "future authoritative kill was accepted using an earlier receipt frame");

        string directory = Path.Combine(Path.GetTempPath(), "prime-replay-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string replay = Path.Combine(directory, "source.ppdemo");
            File.WriteAllText(replay, "fixture bytes");
            var track = new ReplayCameraTrack();
            var key = new ReplayCameraKeyframe(10, Vector3.Zero, Quaternion.Identity, 1);
            check(track.Put(key) && track.Save(replay), "review track fixture");
            byte[] saved = File.ReadAllBytes(replay + ".camera");
            check(!track.EditAndSave(Path.Combine(directory, "missing.ppdemo"), edit =>
                    edit.Remove(10) && edit.Put(key with { Frame = 20 }))
                && track.Keys.Single() == key && track.LastError != null
                && File.ReadAllBytes(replay + ".camera").SequenceEqual(saved),
                "failed edit lost authored keys or reported success");
            string destination = Path.Combine(directory, "moved.ppdemo");
            File.WriteAllText(destination + ".camera", "occupied");
            bool rejected = false;
            try { ReplayArtifacts.Move(replay, destination); }
            catch (IOException) { rejected = true; }
            check(rejected && File.Exists(replay) && !File.Exists(destination)
                && File.ReadAllText(destination + ".camera") == "occupied",
                "artifact move collision partially moved the replay");
            File.Delete(destination + ".camera");
            ReplayArtifacts.Move(replay, destination);
            check(!File.Exists(replay) && track.Load(destination) && track.Keys.Single() == key,
                "artifact move did not rebind camera identity");

            string clip = Path.Combine(directory, "selection.ppclip");
            File.WriteAllText(clip, JsonSerializer.Serialize(new ReplayVirtualClipDocument(
                1, destination, 0, 20, DateTime.UtcNow, "Fixture")));
            string otherClip = Path.Combine(directory, "other", "selection.ppclip");
            Directory.CreateDirectory(Path.GetDirectoryName(otherClip)!);
            File.Copy(clip, otherClip);
            check(!ReplayPathComparer.Same(ReplayVirtualClips.CachePath(clip), ReplayVirtualClips.CachePath(otherClip))
                && ReplayPathComparer.Same(ReplayVirtualClips.LogicalPath(ReplayVirtualClips.CachePath(otherClip)), otherClip),
                "same-named clips in different directories share cache ownership");
            string cache = ReplayVirtualClips.CachePath(clip);
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllText(cache, "disposable");
            check(track.Save(cache), "legacy cache-owned camera fixture");
            ReplayArtifacts.DeleteCache(cache);
            check(!File.Exists(cache) && track.Load(clip) && track.Keys.Single() == key,
                "cache eviction lost unmigrated camera work");
            foreach (string suffix in new[] { ".camera", ".studio.json", ".favorite", ".reel.json", ".thumb0.png" })
                File.WriteAllText(clip + suffix, "fixture");
            File.WriteAllText(cache, "disposable");
            File.WriteAllText(cache + ".camera.old.tmp", "temporary");
            ReplayVirtualClips.Delete(clip);
            check(ReplayArtifacts.Enumerate(clip).Length == 0
                && !File.Exists(cache + ".camera.old.tmp") && File.Exists(destination),
                "virtual clip deletion stranded artifacts or deleted its source");
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("[replayreview] rational exports, delayed kill clock, edit rollback and artifact ownership passed");
    }
}
