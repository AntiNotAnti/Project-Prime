using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Diagnostic over actual archived packets and existing replay implementations.
// An absent launch-life fence is reported, never inferred from a later actor.
internal static class RecordedKillcamTests
{
    internal static int Run(string path, byte victimSlot, string output)
    {
        if (File.Exists(output))
        {
            Console.Error.WriteLine("[recorded-killcam] Refusing to overwrite an existing diagnostic attempt: " + output);
            return 1;
        }
        using var recordingInput = File.OpenRead(path);
        var report = new Dictionary<string, object?>
        {
            ["status"] = "NOT_QUALIFIED", ["fileSha256"] = Convert.ToHexString(SHA256.HashData(recordingInput)),
            ["victimSlot"] = victimSlot, ["scope"] = "Actual recorded marker; existing linear file and bounded frozen playback; no GL"
        };
        var checks = new List<object>(); report["comparisons"] = checks;
        Scene? foreground = null;
        try
        {
            Headless.Enter();
            var size = new Vector2i(640, 480);
            foreground = new Scene(size, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
            foreground.AddPlayer(Hunter.Samus); foreground.Random.SetRng1(8943); foreground.GameState.Points[0] = 572;
            string sentinel = ReplayStateHash.Compute(foreground, 0);
            uint rng1 = foreground.Random.Rng1, rng2 = foreground.Random.Rng2;
            using var reader = DemoReader.Open(path, out var opened) ?? throw new InvalidDataException(opened.ToString());
            uint origin = reader.Metadata?.OriginRecordingFrame ?? 0;
            var markers = new List<ReplayTimelineRecord>();
            var semanticKills = new List<MatchSemanticEventPacket>(); uint last = 0; int records = 0;
            while (reader.ReadNext() is DemoRecord record)
            {
                if (++records > 2_000_000) throw new InvalidDataException("Recording exceeds diagnostic record bound.");
                uint frame = checked(record.Frame + origin); last = Math.Max(last, frame);
                if (record.Data.Length > 0 && record.Data[0] == 254)
                {
                    var marker = ReplayTimelineArchive.DecodeMarker(frame, record.Data);
                    if (marker.Marker is { Kind: ReplayMarkerKind.Kill, Kill: { } kill }
                        && kill.VictimSlot == victimSlot) markers.Add(marker);
                }
                else if (record.Data.Length > 0 && record.Data[0] == (byte)PacketType.MatchSemanticEvent
                    && MatchSemanticEventPacket.TryRead(record.Data, out var fact)
                    && !fact.IsBaseline && fact.Type == MatchSemanticEventType.PlayerKilled && fact.TargetSlot == victimSlot)
                    semanticKills.Add(fact);
            }
            if (reader.LastResult != ReplayOpenResult.Success) throw new InvalidDataException(reader.LastResult.ToString());
            report["protocol"] = reader.ProtocolVersion; report["origin"] = origin;
            report["records"] = records; report["lastRecordingFrame"] = last; report["victimKillMarkers"] = markers.Count;
            // Prefer a genuinely fenced marker, then the first actual marker with a
            // complete range. Never replace a selected death after a mismatch.
            var eligible = markers.Where(r => r.Marker?.Kill is { } k && k.MatchId != 0 && k.AuthorityEpoch != 0
                && k.EventId != 0 && k.VictimGeneration != 0 && k.VictimLifeId != 0
                && k.KillerSlot != k.VictimSlot && k.KillerGeneration != 0
                && r.RecordingFrame >= origin + KillcamController.PreRollFrames + 120
                && (ulong)r.RecordingFrame + KillcamController.PostRollFrames <= last)
                .OrderBy(r => r.Marker!.Value.Kill!.Value.KillerLifeId == 0).ThenBy(r => r.RecordingFrame).ToArray();
            if (eligible.Length == 0)
            {
                report["reason"] = "No actual authority kill marker with exact victim identity and full pre/post-roll.";
                return Finish(2);
            }
            var selected = eligible[0]; var markerValue = selected.Marker!.Value; var identity = markerValue.Kill!.Value;
            uint death = selected.RecordingFrame, start = death - KillcamController.PreRollFrames;
            uint end = checked(death + KillcamController.PostRollFrames), baseline = start - 120;
            uint retainedEnd = (uint)Math.Min(last, (ulong)end + ReplayPoseStream.ShotFactLookaheadFrames);
            report["marker"] = markerValue; report["deathRecordingFrame"] = death;
            // Show existing authority facts without guessing a join or changing
            // the actual marker. Semantic event IDs and victim damage IDs have
            // different domains; a nearby tick alone is not an identity proof.
            report["recordedSemanticDeathsSameVictimLife"] = semanticKills.Where(f => f.MatchId == identity.MatchId
                && f.AuthorityEpoch == identity.AuthorityEpoch && f.TargetGeneration == identity.VictimGeneration
                && f.TargetLife == identity.VictimLifeId).Select(f => new { f.EventId, f.Tick, f.ActorSlot,
                    f.ActorGeneration, f.ActorLife, f.TargetSlot, f.TargetGeneration, f.TargetLife,
                    exactMarkerTick = f.Tick == identity.ServerTick }).ToArray();
            report["baseline"] = baseline; report["start"] = start; report["end"] = end; report["retainedEnd"] = retainedEnd;
            report["lifecycleStatus"] = identity.KillerLifeId == 0 ? "NOT_QUALIFIED: recorded marker has no attacker launch-life fence" : "PENDING";

            using var source = new PassiveReplayScene(path, size);
            Advance(source, baseline);
            using var checkpoint = ReplayWorldCheckpoint.Capture(source, baseline);
            report["checkpointSha256"] = Convert.ToHexString(SHA256.HashData(checkpoint.Bytes));
            var timeline = new RollingReplayTimeline(presentationProtocolVersion: reader.ProtocolVersion);
            try
            {
                var worldRecord = new ReplayTimelineRecord(baseline, baseline, ReplayFactKind.World, checkpoint.Bytes);
                var restore = new ReplayRestorePoint(baseline, baseline, ReplayRestoreKind.ReplicaCheckpoint, [worldRecord]);
                worldRecord.Release();
                Require(timeline.AppendRestorePoint(restore), "Actual linear checkpoint was rejected.");
                using (var input = DemoReader.Open(path, out var status) ?? throw new InvalidDataException(status.ToString()))
                {
                    while (input.ReadNext() is DemoRecord record)
                    {
                        uint frame = checked(record.Frame + origin);
                        if (frame <= baseline) continue;
                        if (frame > retainedEnd) break;
                        if (record.Data.Length == 0) continue;
                        if (record.Data[0] == 255) { Require(timeline.AdvanceFrame(frame, frame), "Quiet frame regressed."); continue; }
                        if (record.Data[0] == 253) throw new InvalidDataException("Unexpected legacy construction record inside bounded current source interval.");
                        ReplayTimelineRecord fact;
                        if (record.Data[0] == 254) fact = ReplayTimelineArchive.DecodeMarker(frame, record.Data);
                        else
                        {
                            var canonical = ReplayIdentityCompatibility.Convert(record.Data, input.ProtocolVersion);
                            if (canonical.IsEmpty) continue;
                            fact = new(frame, frame, ReplayFactKind.Snapshot, canonical);
                        }
                        try { Require(timeline.Append(fact), "A bounded actual packet was rejected."); }
                        finally { fact.Release(); }
                    }
                }
                Require(timeline.AdvanceFrame(retainedEnd, retainedEnd), "Bounded frontier regressed.");
                Require(timeline.TryFreeze(start, end, out var exact) && exact != null, "Actual killcam range could not freeze.");
                using var exactClip = exact!;
                Require(timeline.TryFreeze(start, retainedEnd, out var retained) && retained != null, "Presentation lookahead could not freeze.");
                using var retainedClip = retained!;
                using var lookaheadClip = new ReplayTimelineClip(retainedClip.RestorePoint, retainedClip.Records.ToArray(), start, end, reader.ProtocolVersion);
                report["retainedRecords"] = timeline.RecordCount; report["retainedBytes"] = timeline.PayloadBytes;
                using var frozen = new PassiveReplayPlayer(exactClip, size, ReplayPlayerOptions.Linear);
                using var lookahead = new PassiveReplayPlayer(lookaheadClip, size, ReplayPlayerOptions.Linear);
                Warm(frozen, start); Warm(lookahead, start); Advance(source, start);
                bool mismatch = false, markerObserved = false; int compared = 0;
                bool exactFailed = false, lookaheadFailed = false;
                for (uint frame = start; frame <= end; frame++)
                {
                    Require(source.Session.RecordingFrame == frame && frozen.Current.Session.CurrentFrame == frame
                        && lookahead.Current.Session.CurrentFrame == frame, "Playback clocks differ.");
                    string gameplay = ReplayStateHash.Compute(source.Scene, frame), presentation = source.Scene.ReplayPresentationHash(frame);
                    Compare("actual-killcam-range", frozen.Current, ref exactFailed);
                    Compare("bounded-file-lookahead-control", lookahead.Current, ref lookaheadFailed);
                    void Compare(string scope, PassiveReplayScene actual, ref bool alreadyFailed)
                    {
                        bool gameplayEqual = gameplay == ReplayStateHash.Compute(actual.Scene, frame);
                        bool presentationEqual = presentation == actual.Scene.ReplayPresentationHash(frame);
                        if (gameplayEqual && presentationEqual || alreadyFailed) return;
                        alreadyFailed = true; mismatch = true;
                        using var expectedCapsule = ReplayWorldCheckpoint.Capture(source, frame);
                        using var actualCapsule = ReplayWorldCheckpoint.Capture(actual, frame);
                        checks.Add(new { scope, firstRecordingFrame = frame, gameplayEqual, presentationEqual,
                            graphDifferences = expectedCapsule.GraphDifferences(actualCapsule),
                            canonicalDifferences = expectedCapsule.CanonicalGraphDifferences(actualCapsule) });
                    }
                    if (frame == death)
                    {
                        markerObserved = source.State.Match is { } match && match.MatchId == identity.MatchId
                            && match.AuthorityEpoch == identity.AuthorityEpoch && source.State.TryGetPlayer(victimSlot, out var victim)
                            && victim.SlotGeneration == identity.VictimGeneration && victim.LifeId == identity.VictimLifeId && victim.Health == 0;
                        report["sourceAtMarkerMatchesVictimDeath"] = markerObserved;
                        if (identity.KillerLifeId != 0)
                        {
                            int resolved = KillcamController.ResolveAttackerSlot(identity, source.State);
                            // Mutate away from the actual source occupant, since a
                            // legitimate post-launch respawn could already equal
                            // Next(recorded launch life) at this death frame.
                            ushort currentLife = source.State.TryGetPlayer(identity.KillerSlot, out var actor) ? actor.LifeId : identity.KillerLifeId;
                            int wrongLife = KillcamController.ResolveAttackerSlot(identity with { KillerLifeId = NetLifecycleTracker.Next(currentLife) }, source.State);
                            ushort currentGeneration = source.State.Occupant(identity.KillerSlot).Generation;
                            int wrongGeneration = KillcamController.ResolveAttackerSlot(identity with { KillerGeneration = NetLifecycleTracker.Next(currentGeneration) }, source.State);
                            report["attackerAtDeath"] = new { resolved, wrongLife, wrongGeneration };
                            Require(wrongLife == -1 && wrongGeneration == -1, "Wrong recorded attacker lifecycle resolved.");
                            // A dead/absent attacker may legitimately fail exact resolution; no victim fallback.
                            Require(resolved == -1 || resolved == identity.KillerSlot, "Attacker resolution fell back to another actor.");
                        }
                    }
                    compared++;
                    if (frame < end)
                    {
                        Require(source.Step() && frozen.Current.Step() && lookahead.Current.Step(), "Playback ended before actual post-roll.");
                    }
                }
                report["comparedFrames"] = compared;
                report["actualKillcamFidelity"] = exactFailed ? "FAIL" : "PASS";
                report["boundedLookaheadControl"] = lookaheadFailed ? "FAIL" : "PASS";
                using (var controller = new KillcamController(timeline))
                {
                    var context = new KillcamContext(identity.MatchId, identity.AuthorityEpoch, end, victimSlot,
                        identity.VictimGeneration, identity.VictimLifeId, false, true, true, true);
                    Require(!KillcamController.IsValidIdentity(identity with { VictimLifeId = 0 }, context), "Unfenced victim admitted.");
                    controller.NoteKill(markerValue, death, context);
                    for (int warm = 0; warm < 10 && !controller.Visible; warm++) controller.Update(foreground, context);
                    Require(controller.Visible && controller.Playing == markerValue && controller.PlayingFrame == death,
                        "Actual recorded marker failed controller startup: " + controller.LastError);
                    Require(controller.CheckpointCount == 0 && controller.CheckpointBytes == 0, "Linear killcam retained seek checkpoints.");
                    controller.Update(foreground, context with { LocalLife = NetLifecycleTracker.Next(identity.VictimLifeId), LocalAlive = true });
                    Require(!controller.Active && controller.EndReason == KillcamEndReason.Respawn, "Victim respawn retained recorded killcam.");
                    report["controllerMarkerAndRespawn"] = "PASS";
                }
                Require(ReplayStateHash.Compute(foreground, 0) == sentinel && foreground.Random.Rng1 == rng1
                    && foreground.Random.Rng2 == rng2, "Private replay changed foreground gameplay/RNG.");
                report["foregroundGameplayAndRng"] = "PASS";
                if (mismatch) { report["status"] = "FAIL"; return Finish(1); }
                if (!markerObserved || identity.KillerLifeId == 0)
                { report["reason"] = !markerObserved ? "Recorded marker does not expose the exact dead victim at its source frame." : "Actual client marker lacks attacker launch-life fence; no enrichment performed."; return Finish(2); }
                report["lifecycleStatus"] = "PASS"; report["status"] = "PASS"; return Finish(0);
            }
            finally { timeline.Reset(); }
        }
        catch (Exception ex) { report["status"] = "FAIL"; report["exception"] = ex.ToString(); return Finish(1); }
        finally { foreground?.DoCleanup(); foreground?.UnloadGl(); }

        int Finish(int code)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(destination, report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
            Console.WriteLine($"[recorded-killcam] {report["status"]}: {output}"); return code;
        }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Advance(PassiveReplayScene source, uint frame)
    {
        int steps = 0;
        while (!source.HasStepped || source.Session.RecordingFrame < frame)
        {
            Require(++steps <= 100_000 && source.Step(), "Source cannot reach requested actual recording frame.");
        }
        Require(source.Session.RecordingFrame == frame, "Source recording clock skipped target.");
    }
    private static void Warm(PassiveReplayPlayer player, uint start)
    {
        int steps = 0;
        while (!player.Ready) { steps += player.Update(); Require(steps <= 600, "Actual frozen clip exceeded bounded warmup."); }
        Require(player.Current.Session.CurrentFrame == start, "Frozen clip start changed.");
    }
}
