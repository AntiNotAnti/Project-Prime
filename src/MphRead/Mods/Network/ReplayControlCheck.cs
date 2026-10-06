using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Input;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    internal static class ReplayControlCheck
    {
        public static int Run()
        {
            try
            {
                foreach (float rate in ReplayController.Rates)
                {
                    ReplayController.Begin();
                    ReplayController.SetPlaybackRate(rate);
                    int frames = 0;
                    for (int i = 0; i < 240; i++) frames += ReplayController.FramesDue();
                    Require(frames == 240 * rate, $"{rate}x: expected {240 * rate}, got {frames}");
                    ReplayController.Pause();
                    for (int i = 0; i < 60; i++) Require(ReplayController.FramesDue() == 0, "pause advanced");
                    ReplayController.StepForward();
                    Require(ReplayController.FramesDue() == 1, "step did not advance exactly once");
                    Require(ReplayController.FramesDue() == 0, "step repeated");
                    ReplayController.Play();
                    int resumed = 0;
                    for (int i = 0; i < 240; i++) resumed += ReplayController.FramesDue();
                    Require(resumed == 240 * rate, "resume changed rate");
                }
                // Different presentation rates all owe 60 fixed intervals per second.
                foreach (int fps in new[] { 30, 60, 120, 144, 240 })
                {
                    ReplayController.Begin();
                    ReplayController.SetPlaybackRate(4);
                    double accumulator = 0;
                    int frames = 0;
                    for (int draw = 0; draw < fps * 4; draw++)
                    {
                        accumulator += 60.0 / fps;
                        while (accumulator + 1e-9 >= 1)
                        { accumulator -= 1; frames += ReplayController.FramesDue(); }
                    }
                    Require(frames == 960, $"display rate {fps} changed replay timing");
                }
                using var clockSession = new ReplayPlaybackSession(new PassiveReplaySessionHost());
                var sessionFrame = typeof(ReplayPlaybackSession).GetField("_frame",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                typeof(ReplayPlaybackSession).GetProperty(nameof(ReplayPlaybackSession.LastFrame))!
                    .SetValue(clockSession, 1000u);
                foreach (float rate in ReplayTransport.Rates)
                {
                    sessionFrame.SetValue(clockSession, 0u);
                    var transport = clockSession.Transport;
                    transport.Begin(); transport.SetPlaybackRate(rate);
                    double expectedFrame = 0;
                    uint simulatedFrame = 0;
                    for (int update = 0; update < 3; update++)
                    {
                        int due = transport.FramesDue();
                        Require(Math.Abs(transport.PresentationFrame(0) - expectedFrame) < 1e-9,
                            $"{rate}x presentation clock did not retain fractional progress");
                        Require(Math.Abs(transport.PresentationFrame(.5) - (expectedFrame + rate * .5)) < 1e-9,
                            $"{rate}x midpoint was not sampled in continuous replay time");
                        expectedFrame += rate;
                        simulatedFrame += (uint)due;
                        sessionFrame.SetValue(clockSession, simulatedFrame);
                    }
                    transport.Pause();
                    Require(Math.Abs(transport.PresentationFrame(0) - expectedFrame) < 1e-9,
                        $"{rate}x pause did not freeze the presentation clock");
                }
                long arrival100 = DemoPlayback.PlaybackArrivalTicks(100);
                long arrival101 = DemoPlayback.PlaybackArrivalTicks(101);
                double replayStep = arrival101 - arrival100;
                double expectedReplayStep = System.Diagnostics.Stopwatch.Frequency / 60.0;
                Require(arrival101 > arrival100
                    && Math.Abs(replayStep - expectedReplayStep) <= 1.1,
                    "replay receive clock is not deterministic 60 Hz");

                using (var transport = new NetTransport(0, playbackOnly: true))
                {
                    byte[] ping = { (byte)PacketType.Ping };
                    transport.EnqueueForPlayback(ping, ping.Length, arrival101);
                    ReceivedPacket delivered = transport.Drain().Single();
                    Require(delivered.ArrivedAt == arrival101,
                        "playback packet lost its recorded receive timestamp");
                }

                Console.WriteLine("[replaycheck] timing: rates, presentation independence and deterministic receive clock passed");
                Replay.ReplayCameraTrackCheck.Run();
                Diagnostics.CollisionCandidatePoolCheck.Run(Require);
                Replay.ReplayReviewCheck.Run(Require);
                Replay.ReplayHardeningCheck.Run(Require);
                var poseA = new PlayerState { SlotGeneration = 1, LifeId = 1, Health = 99,
                    Flags = PlayerState.FlagActive | PlayerState.FlagSpawned, Position = OpenTK.Mathematics.Vector3.Zero };
                var poseB = poseA; poseB.Position = OpenTK.Mathematics.Vector3.UnitX;
                Require(ReplayPoseStream.CanBlend(poseA, poseB), "continuous recorded poses cannot interpolate");
                poseB.SlotGeneration++; Require(!ReplayPoseStream.CanBlend(poseA, poseB), "interpolated across slot reuse");
                poseB = poseA; poseB.LifeId++; Require(!ReplayPoseStream.CanBlend(poseA, poseB), "interpolated across respawn");
                poseB = poseA; poseB.Health = 0; Require(!ReplayPoseStream.CanBlend(poseA, poseB), "interpolated across death");
                poseB = poseA; poseB.Flags |= PlayerState.FlagAltForm; Require(!ReplayPoseStream.CanBlend(poseA, poseB), "interpolated across form change");
                poseB = poseA; poseB.Position = OpenTK.Mathematics.Vector3.UnitX * 20;
                Require(!ReplayPoseStream.CanBlend(poseA, poseB), "interpolated across teleport");
                var ackIntent = new IntentPacket { AckFrame = 240, AckSubFrame = 128 };
                Require(Math.Abs(ReplayPoseStream.AcknowledgedServerFrame(ackIntent) - 240.5) < 1e-9,
                    "POV ACK clock lost its fractional server frame");
                ackIntent.AckFrame = 0;
                Require(double.IsNaN(ReplayPoseStream.AcknowledgedServerFrame(ackIntent)),
                    "POV ACK clock treated an unavailable acknowledgement as server frame zero");
                Require(ReplayPoseStream.FireSourceRecordingFrame(500, 1000, 1000) == 500,
                    "same-frame FireEvent moved away from its carrier frame");
                Require(ReplayPoseStream.FireSourceRecordingFrame(500, 1004, 1000) == 496,
                    "recovered FireEvent did not regain its four-frame source age");
                Require(ReplayPoseStream.FireSourceRecordingFrame(500, 1100, 1000) == 500,
                    "out-of-retention FireEvent was allowed to invent an old replay frame");
                Require(ReplayPoseStream.TryMapServerTick(500, 1000, 1004, out uint impactAfter)
                    && impactAfter == 504,
                    "resolved-shot server tick did not map forward on the recording clock");
                Require(ReplayPoseStream.TryMapServerTick(500, 1000, 996, out uint impactBefore)
                    && impactBefore == 496,
                    "resolved-shot server tick did not map backward on the recording clock");
                Require(!ReplayPoseStream.TryMapServerTick(2, 1000, 990, out _),
                    "resolved-shot clock mapping underflowed before replay frame zero");
                var impactA = new ReplayShotFact(1, 1, 100, 90, 7, 10,
                    0, 1, 1, 1, 1, 1, (byte)BeamType.Missile,
                    ReplayShotFactFlags.Direct, 20, 79, 0, 0,
                    new OpenTK.Mathematics.Vector3(1, 2, 3));
                var impactNear = impactA with
                {
                    DamageEventId = 11, VictimSlot = 2,
                    ImpactPoint = impactA.ImpactPoint + new OpenTK.Mathematics.Vector3(.05f, 0, 0)
                };
                var impactFar = impactNear with
                {
                    DamageEventId = 12,
                    ImpactPoint = impactA.ImpactPoint + OpenTK.Mathematics.Vector3.UnitX
                };
                Require(ReplayPoseStream.SameImpactVisual(impactA, impactNear)
                    && !ReplayPoseStream.SameImpactVisual(impactA, impactFar)
                    && !ReplayPoseStream.SameImpactVisual(impactA,
                        impactNear with { ShotId = impactA.ShotId + 1 }),
                    "authoritative replay impacts did not dedupe nearby direct/splash facts by ShotId");
                var impactKey = new ShotKey(impactA.AuthorityEpoch, impactA.MatchId,
                    impactA.ShooterSlot, impactA.ShooterGeneration,
                    impactA.ShooterLifeId, impactA.ShotId);
                Require(BeamProjectileEntity.ModReplayIdentityMatches(
                        impactKey, impactA.ShotId, (BeamType)impactA.Weapon, impactA)
                    && !BeamProjectileEntity.ModReplayIdentityMatches(
                        impactKey with { LifeId = (ushort)(impactKey.LifeId + 1) },
                        impactA.ShotId, (BeamType)impactA.Weapon, impactA)
                    && !BeamProjectileEntity.ModReplayIdentityMatches(
                        impactKey, impactA.ShotId + 1, (BeamType)impactA.Weapon, impactA),
                    "authoritative replay impact matched the wrong shot or shooter life");
                var lethalHeadshot = impactA with
                {
                    Flags = ReplayShotFactFlags.Direct | ReplayShotFactFlags.Headshot
                        | ReplayShotFactFlags.Lethal | ReplayShotFactFlags.HalfturretTarget,
                    HealthAfter = 0
                };
                ReplayHitMarkerFlags markerFlags = ReplayPoseStream.MarkerFlags(lethalHeadshot);
                Require((markerFlags & ReplayHitMarkerFlags.Headshot) != 0
                    && (markerFlags & ReplayHitMarkerFlags.Lethal) != 0
                    && (markerFlags & ReplayHitMarkerFlags.Halfturret) != 0,
                    "authoritative replay marker lost headshot/lethal/halfturret classification");
                markerFlags = ReplayPoseStream.MarkerFlags(impactA);
                Require(markerFlags == ReplayHitMarkerFlags.None,
                    "ordinary authoritative hit gained a special marker classification");
                Require(Replay.ReplayKillMessagePresenter.MessageId(friendly: false, headshot: false) == 238
                    && Replay.ReplayKillMessagePresenter.MessageId(friendly: false, headshot: true) == 239
                    && Replay.ReplayKillMessagePresenter.MessageId(friendly: true, headshot: true) == 240,
                    "replay kill notification IDs no longer match native kill/headshot/teamkill HUD strings");
                Require(ReplayPoseStream.DiagnosticFrameMatches(
                        anchorFrame: 90, weapon: (int)BeamType.Missile, direction: 0,
                        hasFire: true, fireFrame: 90, resolveFrame: 102)
                    && !ReplayPoseStream.DiagnosticFrameMatches(
                        anchorFrame: 90, weapon: (int)BeamType.Missile, direction: 0,
                        hasFire: true, fireFrame: 91, resolveFrame: 102)
                    && ReplayPoseStream.DiagnosticFrameMatches(
                        anchorFrame: 100, weapon: -1, direction: 0,
                        hasFire: true, fireFrame: 90, resolveFrame: 100)
                    && ReplayPoseStream.DiagnosticFrameMatches(
                        anchorFrame: 100, weapon: -1, direction: -1,
                        hasFire: true, fireFrame: 90, resolveFrame: 110)
                    && ReplayPoseStream.DiagnosticFrameMatches(
                        anchorFrame: 100, weapon: -1, direction: 1,
                        hasFire: true, fireFrame: 110, resolveFrame: 120),
                    "combat inspector did not require exact fire/resolve association");
                var diagnostic = new ReplayCombatDiagnostic(
                    RecordingFrame: 102, FireRecordingFrame: 90,
                    Fact: lethalHeadshot, HasFire: true,
                    Fire: new FireEvent(lethalHeadshot.ShotId, 900, 850, 128,
                        FireEventKind.PressFire, lethalHeadshot.Weapon, 0, 0,
                        FireEvent.FlagPose,
                        new OpenTK.Mathematics.Vector3(0, 1, 2),
                        OpenTK.Mathematics.Vector3.UnitZ,
                        OpenTK.Mathematics.Vector3.UnitZ,
                        OpenTK.Mathematics.Vector3.UnitZ),
                    AckServerFrame: 850.5, HasAckTarget: true,
                    AckTargetPosition: new OpenTK.Mathematics.Vector3(1, 2, 3),
                    AckImpactDistance: .25f);
                Replay.ReplayCombatDiagnostics.Select(diagnostic);
                Require(Replay.ReplayCombatDiagnostics.Selected is { } selectedDiagnostic
                    && selectedDiagnostic.Matches(lethalHeadshot)
                    && Replay.ReplayCombatDiagnostics.Describe(diagnostic).Contains(
                        $"SHOT #{lethalHeadshot.ShotId}", StringComparison.Ordinal)
                    && Replay.ReplayCombatDiagnostics.Explain(lethalHeadshot).Contains(
                        "headshot", StringComparison.OrdinalIgnoreCase),
                    "combat diagnostic identity/why-hit description lost authority evidence");
                Replay.ReplayCombatDiagnostics.ClearSelection();
                var fractional = new Replay.ReplayCameraTrack();
                fractional.Put(new(0, OpenTK.Mathematics.Vector3.Zero, OpenTK.Mathematics.Quaternion.Identity, 1,
                    Interpolation: Replay.ReplayCameraInterpolation.Linear, Ease: Replay.ReplayCameraEase.None));
                fractional.Put(new(10, OpenTK.Mathematics.Vector3.UnitX * 10, OpenTK.Mathematics.Quaternion.Identity, 1));
                Require(fractional.Sample(.5, out var half) && Math.Abs(half.Position.X - .5f) < .00001f,
                    "camera track rounded a fractional presentation frame");

                var events = new[]
                {
                    new ReplayEvent(60, ReplayEventType.Kill, 0, 1),
                    new ReplayEvent(120, ReplayEventType.Kill, 0, 1),
                    new ReplayEvent(180, ReplayEventType.Objective, 1),
                    new ReplayEvent(200, ReplayEventType.Damage, 1, 0, 200),
                    new ReplayEvent(210, ReplayEventType.WeaponFired, 0,
                        Value: 7),
                    new ReplayEvent(300, ReplayEventType.MatchEnded)
                };
                var analytics = Replay.ReplayStudio.Analytics(events, 300);
                Require(analytics.TotalKills == 2 && analytics.TotalDamage == 200
                    && analytics.ObjectiveEvents == 1, "studio aggregate analytics");
                var player0 = System.Linq.Enumerable.Single(analytics.Players, p => p.Slot == 0);
                var player1 = System.Linq.Enumerable.Single(analytics.Players, p => p.Slot == 1);
                Require(player0.Kills == 2 && player1.Deaths == 2
                    && player1.ObjectiveEvents == 1 && player1.Damage == 200,
                    "studio per-player analytics");
                Require(analytics.DamageTimeline.Count > 0
                    && analytics.DamageTimeline.Sum(bucket => bucket.Damage) == 200,
                    "studio damage timeline");
                Require(analytics.WeaponUsage.Count == 1
                    && analytics.WeaponUsage[0].Weapon == 7
                    && analytics.WeaponUsage[0].Shots == 1,
                    "studio weapon usage");
                Require(Replay.ReplayStudio.TryBeamType((int)BeamType.Imperialist, out BeamType replayBeam)
                    && replayBeam == BeamType.Imperialist
                    && !Replay.ReplayStudio.TryBeamType(999, out _),
                    "studio weapon labels handle sbyte-backed BeamType without Enum.IsDefined type mismatch");
                var paired = Replay.ReplayStudio.Analytics(new[]
                {
                    new ReplayEvent(60, ReplayEventType.PlayerDeath, 1, 0),
                    new ReplayEvent(60, ReplayEventType.Kill, 0, 1)
                }, 60);
                Require(paired.Players.Single(player => player.Slot == 1).Deaths == 1,
                    "paired kill/death analytics double-counted a death");
                var highlights = Replay.ReplayStudio.Highlights(events, 300);
                Require(System.Linq.Enumerable.Any(highlights,
                    h => h.Kind == Replay.ReplayHighlightKind.MultiKill && h.ActorSlot == 0),
                    "multi-kill highlight");
                Require(System.Linq.Enumerable.Any(highlights,
                    h => h.Kind == Replay.ReplayHighlightKind.Objective && h.ActorSlot == 1),
                    "objective highlight");
                Console.WriteLine("[replaycheck] studio: analytics and highlight derivation passed");

                Require(MphRead.Mods.KillCam.IsRecentFinalKill(120, 120),
                    "same-frame final kill was not eligible");
                Require(MphRead.Mods.KillCam.IsRecentFinalKill(120, 120 + 8 * 60),
                    "final kill window excluded its boundary");
                Require(!MphRead.Mods.KillCam.IsRecentFinalKill(120, 120 + 8 * 60 + 1),
                    "stale kill was accepted as final");
                Require(!MphRead.Mods.KillCam.IsRecentFinalKill(121, 120),
                    "future kill frame was accepted as final");
                Require(Replay.KillcamController.FinalEligible(1208, 1214, timedEnd: false, causalEnd: true)
                    && !Replay.KillcamController.FinalEligible(1215, 1214, timedEnd: false, causalEnd: true),
                    "final kill selection did not use the authoritative match-end frame");
                Require(Replay.ReplayExportRates.IsSupported(30) && Replay.ReplayExportRates.IsSupported(60)
                    && Replay.ReplayExportRates.IsSupported(120) && !Replay.ReplayExportRates.IsSupported(45),
                    "export FPS contract accepted an unsupported rate");
                bool invalidExportRejected = false;
                try { Replay.ReplayVideoExport.CreateManifest("unused.ppdemo", 0, 1, fps: 45); }
                catch (ArgumentOutOfRangeException) { invalidExportRejected = true; }
                Require(invalidExportRejected, "manifest creation clamped unsupported export FPS");
                var legacyClean = new Replay.ReplayVideoExportManifest(2, "fixture", 0, 1, 1280, 720, 60,
                    true, false, false, "frames", "output", "ffmpeg");
                var legacyHud = legacyClean with { CleanHud = false };
                var gameHudOnly = legacyClean with { Version = 3, GameHud = true, ReplayOverlay = false };
                var overlayOnly = legacyClean with { Version = 3, GameHud = false, ReplayOverlay = true };
                Require(!legacyClean.IncludeGameHud && !legacyClean.IncludeReplayOverlay
                    && legacyHud.IncludeGameHud && !legacyHud.IncludeReplayOverlay
                    && gameHudOnly.IncludeGameHud && !gameHudOnly.IncludeReplayOverlay
                    && !overlayOnly.IncludeGameHud && overlayOnly.IncludeReplayOverlay,
                    "export HUD visibility did not preserve v2 CleanHud compatibility or independent v3 layers");
                if (OperatingSystem.IsLinux())
                    Require(!ReplayPathComparer.Same("/tmp/ReplayCase.ppdemo", "/tmp/replaycase.ppdemo"),
                        "Linux replay paths were compared case-insensitively");
                Require(MphRead.Mods.KillCam.WeaponName((int)BeamType.Imperialist)
                        == "IMPERIALIST"
                    && MphRead.Mods.KillCam.WeaponName(999) == "",
                    "kill cam weapon label enum conversion");
                Console.WriteLine("[replaycheck] kill cam: eligibility and weapon labels passed");

                var bindings = new PadBindingState();
                bindings.SetSlot(PadAction.ReplayPlayPause, 0, GamepadButtons.A,
                    GamepadButtons.LeftBumper);
                ulong liveButtons = bindings.Evaluate(
                    GamepadButtons.LeftBumper | GamepadButtons.A, replayContext: false);
                Require((liveButtons & (1UL << (int)PadAction.ReplayPlayPause)) == 0,
                    "replay controller chord leaked into live gameplay");
                Require((liveButtons & (1UL << (int)PadAction.Jump)) != 0,
                    "replay controller chord suppressed live jump");
                ulong replayButtons = bindings.Evaluate(
                    GamepadButtons.LeftBumper | GamepadButtons.A, replayContext: true);
                Require((replayButtons & (1UL << (int)PadAction.ReplayPlayPause)) != 0,
                    "replay controller chord did not activate in replay context");
                Console.WriteLine("[replaycheck] input: replay controller context isolation passed");

                string annotationReplay = Path.Combine(Path.GetTempPath(),
                    "replay-annotations-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
                try
                {
                    File.WriteAllBytes(annotationReplay, new byte[] { 1 });
                    var named = Replay.ReplayAnnotations.AddHighlight(
                        annotationReplay, 60, 180, "Final push");
                    var stored = Replay.ReplayAnnotations.Highlights(annotationReplay);
                    Require(stored.Count == 1 && stored[0].Id == named.Id
                        && stored[0].Name == "Final push"
                        && stored[0].StartFrame == 60 && stored[0].EndFrame == 180,
                        "named replay highlight did not round-trip");
                    Replay.ReplayAnnotations.RemoveHighlight(annotationReplay, named.Id);
                    Require(Replay.ReplayAnnotations.Highlights(annotationReplay).Count == 0,
                        "named replay highlight did not remove");

                    Replay.ReplayAnnotations.SetOrganization(annotationReplay,
                        new[] { "scrim", "Sylux" }, new[] { "Tournament A" });
                    Require(Replay.ReplayAnnotations.Tags(annotationReplay).Count == 2
                        && Replay.ReplayAnnotations.Collections(annotationReplay).Single()
                            == "Tournament A",
                        "replay organization did not round-trip");

                    var segmentA = Replay.ReplayReels.Add(annotationReplay,
                        60, 120, "Opening", Replay.ReplaySegmentCamera.Chase);
                    var segmentB = Replay.ReplayReels.Add(annotationReplay,
                        180, 240, "Finish", Replay.ReplaySegmentCamera.Director);
                    Replay.ReplayReels.Move(annotationReplay, segmentB.Id, -1);
                    var reel = Replay.ReplayReels.Segments(annotationReplay);
                    Require(reel.Count == 2 && reel[0].Id == segmentB.Id,
                        "reel ordering did not persist");
                    Replay.ReplayReels.Trim(annotationReplay, segmentA.Id, 70, 130);
                    reel = Replay.ReplayReels.Segments(annotationReplay);
                    Require(reel.Single(segment => segment.Id == segmentA.Id).StartFrame == 70,
                        "reel trim did not persist");
                }
                finally
                {
                    Replay.ReplayAnnotations.DeleteFor(annotationReplay);
                    Replay.ReplayReels.DeleteFor(annotationReplay);
                    File.Delete(annotationReplay);
                }
                Console.WriteLine("[replaycheck] studio: annotation sidecar round-trip passed");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine($"[replaycheck] FAIL: {ex.Message}"); return 1; }
            finally { ReplayController.Stop(); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
