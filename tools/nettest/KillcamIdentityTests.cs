using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class KillcamIdentityTests
{
    private static readonly ReplayKillIdentity Raw = new(7, 9, 333, 15, 0, 2, 1, 3, 4);
    private static readonly ReplayMarker Marker = new(ReplayMarkerKind.Kill, 0, 1, Kill: Raw);
    private static readonly MatchSemanticEventPacket Fact = new(7, 9, 100, 332, MatchSemanticEventType.PlayerKilled,
        0, 2, 5, 1, 3, 4, 0, 0, -1, 0);
    private const uint Death = 300;

    private sealed class Timeline : IReplayTimeline, IDisposable
    {
        internal uint Frontier = Death + 90;
        internal int Freezes;
        internal int ProtocolVersion = 79;
        internal uint? FrozenStart;
        public uint? FirstRecordingFrame => 0;
        public uint? LastRecordingFrame => Frontier;
        public bool TryMapKillToRecordingFrame(ReplayKillIdentity kill, out uint frame) { frame = Death; return kill == Raw; }
        public bool TryMapServerTickToRecordingFrame(uint tick, out uint frame) { frame = Death; return false; }
        public bool TryGetRestorePoint(uint frame, out ReplayRestorePoint? point) { point = null; return false; }
        public bool TryFreeze(uint start, uint end, out ReplayTimelineClip? clip)
        {
            Freezes++; FrozenStart = start;
            using var restore = new ReplayRestorePoint(0, 0, ReplayRestoreKind.ReplicaCheckpoint,
                [new(0, 0, ReplayFactKind.World, ReadOnlySpan<byte>.Empty)]);
            clip = new(restore, [], start, end, ProtocolVersion); return true;
        }
        public void Dispose() { }
    }

    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); checks++; }
        Headless.Enter();
        var live = new Scene(new Vector2i(640, 480), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
        try
        {
            ReplayKillLifeResult Resolve(params MatchSemanticEventPacket[] events) => ReplayKillLifeResolver.Resolve(Raw, events);
            Check(Resolve(Fact).Identity.KillerLifeId == 5 && Resolve(Fact).FactEventId == 100,
                "Exact death key did not resolve distinct semantic and damage ID domains/different ticks.");
            Check(Resolve(Fact, Fact).Known, "Identical canonical fact copies became conflicting.");
            foreach (var foreign in new[] { Fact with { MatchId = 8 }, Fact with { AuthorityEpoch = 10 },
                Fact with { TargetSlot = 2 }, Fact with { TargetGeneration = 4 }, Fact with { TargetLife = 5 } })
                Check(Resolve(foreign).Status == ReplayKillLifeStatus.Unknown, "Foreign victim identity enriched marker.");
            foreach (var conflict in new[] { Fact with { EventId = 101 }, Fact with { ActorSlot = 2 },
                Fact with { ActorGeneration = 3 }, Fact with { ActorLife = 6 }, Fact with { ActorLife = 0 } })
                Check(Resolve(Fact, conflict).Status == ReplayKillLifeStatus.Conflict, "Conflicting exact death was filtered away.");
            Check(ReplayKillLifeResolver.Resolve(Raw with { KillerLifeId = 5 }, Array.Empty<MatchSemanticEventPacket>()).Status
                == ReplayKillLifeStatus.FencedMarker, "Fully fenced authority marker required another fact.");
            Check(ReplayKillLifeResolver.Resolve(Raw with { KillerLifeId = 5, VictimLifeId = 0 }, Array.Empty<MatchSemanticEventPacket>()).Status
                == ReplayKillLifeStatus.Unknown, "Malformed fenced marker bypassed exact identity validation.");
            Check(ReplayKillLifeResolver.Resolve(Raw with { KillerLifeId = 5, EventId = 0 }, Array.Empty<MatchSemanticEventPacket>()).Status
                == ReplayKillLifeStatus.Unknown, "Fenced marker accepted the absent damage-event sentinel.");

            var context = new KillcamContext(7, 9, Death + 90, 1, 3, 4, false, true, true, true);
            Check(!KillcamController.IsValidIdentity(Raw with { EventId = 0 }, context),
                "Controller admitted an absent damage-event identity rejected by the resolver.");
            var receiver = new MatchSemanticReceiver(); receiver.Begin(7, 9, requireBaseline: true);
            var baseline = new MatchSemanticEventPacket(7, 9, 99, 0, MatchSemanticEventType.MatchStarted,
                255, 0, 0, 255, 0, 0, -1, 0, -1, 0, true);
            Check(receiver.Accept(baseline) && receiver.Accept(Fact), "Accepted authority fixture refused baseline/death.");
            int drained = 0; receiver.DrainPresentation(_ => drained++, _ => { });
            Check(drained == 1 && ReplayKillLifeResolver.Resolve(Raw, receiver.Events).Known,
                "Consuming presentation discarded canonical life proof.");

            // Controlled opener observes admission without loading assets. Its
            // intentional exception also exercises startup disposal; actual-file
            // diagnostics separately verify successful private-world playback.
            foreach (bool factFirst in new[] { true, false })
            {
                var accepted = new List<MatchSemanticEventPacket>(); if (factFirst) accepted.Add(Fact);
                using var timeline = new Timeline(); int opens = 0;
                using var controller = new KillcamController(timeline, (_, _) => { opens++; throw new InvalidDataException("admission probe"); },
                    () => accepted, 79);
                controller.NoteKill(Marker, Death + 25, context);
                if (!factFirst)
                {
                    controller.Update(live, context);
                    Check(opens == 0 && timeline.Freezes == 0 && controller.Candidate == Marker && controller.CandidateFrame == Death,
                        "Missing proof froze, shifted or duplicated the original pending marker.");
                    accepted.Add(Fact);
                }
                controller.Update(live, context);
                Check(opens == 1 && timeline.Freezes == 1 && timeline.FrozenStart == Death - 210
                    && controller.Candidate?.Kill?.KillerLifeId == 5 && controller.CandidateFrame == Death
                    && controller.IdentityResolution == ReplayKillLifeStatus.VerifiedSemanticFact,
                    "Packet delivery order changed canonical identity or original range.");
                Check(!controller.Active && controller.EndReason == KillcamEndReason.Failed,
                    "Controlled opener failure leaked a private player.");
            }
            foreach (bool conflict in new[] { false, true })
            {
                var accepted = new List<MatchSemanticEventPacket>();
                if (conflict) { accepted.Add(Fact); accepted.Add(Fact with { EventId = 101, ActorLife = 6 }); }
                using var timeline = new Timeline(); int opens = 0;
                using var controller = new KillcamController(timeline, (_, _) => { opens++; throw new InvalidDataException("must not open"); },
                    () => accepted, 79);
                controller.NoteKill(Marker, Death, context);
                controller.Update(live, context with { Frame = Death + 150 });
                Check(!controller.Active && opens == 0 && timeline.Freezes == 0 && controller.CandidateFrame == Death,
                    "Missing/conflicting proof started before the existing deadline.");
                controller.Update(live, context with { Frame = Death + 151 });
                Check(controller.EndReason == KillcamEndReason.Unavailable && !controller.Active,
                    "Unresolved current marker did not expire at the existing bounded timeout.");
                Check(!controller.BeginFinal(live, context, Raw.ServerTick, false, true) && opens == 0 && timeline.Freezes == 0,
                    "Final fallback froze an unresolved current-protocol marker.");
                Check(controller.IdentityResolution == (conflict ? ReplayKillLifeStatus.Conflict : ReplayKillLifeStatus.Unknown),
                    "Unavailable identity lost its explicit unknown/conflict reason.");
                controller.Reset(KillcamEndReason.MatchChanged);
                Check(controller.Candidate == null && controller.IdentityFactEventId == 0, "Reset retained a previous selected kill proof.");
            }
            foreach (uint gap in new uint[] { 150, 151 })
            {
                var accepted = new List<MatchSemanticEventPacket>(); int opens = 0;
                using var timeline = new Timeline();
                using var controller = new KillcamController(timeline, (_, _) => { opens++; throw new InvalidDataException("personal deadline probe"); },
                    () => accepted, 79);
                controller.NoteKill(Marker, Death, context);
                controller.Update(live, context with { Frame = Death + 150 });
                Check(opens == 0 && timeline.Freezes == 0 && controller.CandidateFrame == Death,
                    "Missing personal proof opened or shifted at the exact pending deadline.");
                accepted.Add(Fact);
                controller.Update(live, context with { Frame = Death + gap });
                Check(gap == 150 ? opens == 1 && timeline.Freezes == 1
                    : opens == 0 && timeline.Freezes == 0 && controller.EndReason == KillcamEndReason.Unavailable,
                    "Personal admission failed the inclusive150/exclusive151 original pending deadline.");
            }
            foreach (bool factFirst in new[] { true, false })
            {
                var accepted = new List<MatchSemanticEventPacket>(); if (factFirst) accepted.Add(Fact);
                using var timeline = new Timeline(); int opens = 0;
                using var controller = new KillcamController(timeline, (_, _) => { opens++; throw new InvalidDataException("final admission probe"); },
                    () => accepted, 79);
                controller.NoteKill(Marker, Death, context);
                if (!factFirst)
                {
                    Check(!KillCam.CompleteFinalRequest(controller, live, context with { Frame = 391 }, Raw.ServerTick,
                        false, true, 390) && opens == 0 && timeline.Freezes == 0 && controller.CandidateFrame == Death,
                        "Foreground final-request boundary discarded a request before late canonical life proof.");
                    accepted.Add(Fact);
                }
                Check(KillCam.CompleteFinalRequest(controller, live, context with { Frame = 392 }, Raw.ServerTick,
                    false, true, 390) && opens == 1 && timeline.FrozenStart == Death - 210
                    && controller.Candidate?.Kill?.KillerLifeId == 5 && controller.CandidateFrame == Death,
                    "Foreground final-request delivery order changed exact identity/range.");
                Check(!controller.Active, "Controlled final opener failure retained a snapshot/player.");
            }
            foreach (bool conflict in new[] { false, true })
            {
                var accepted = new List<MatchSemanticEventPacket>();
                if (conflict) { accepted.Add(Fact); accepted.Add(Fact with { EventId = 101, ActorSlot = 2 }); }
                using var timeline = new Timeline();
                using var controller = new KillcamController(timeline, (_, _) => throw new InvalidDataException("must not open final"), () => accepted, 79);
                controller.NoteKill(Marker, Death, context);
                Check(!KillCam.CompleteFinalRequest(controller, live, context with { Frame = 419 }, Raw.ServerTick,
                    false, true, 390), "Proof-specific final retry ended before its existing 30-frame boundary.");
                Check(KillCam.CompleteFinalRequest(controller, live, context with { Frame = 420 }, Raw.ServerTick,
                    false, true, 390) && !controller.Active && timeline.Freezes == 0
                    && controller.EndReason == KillcamEndReason.Unavailable,
                    "Missing/conflicting final proof escaped bounded expiry or froze a snapshot.");
            }
            using (var timeline = new Timeline())
            {
                var accepted = new List<MatchSemanticEventPacket>(); int opens = 0;
                using var controller = new KillcamController(timeline, (_, _) => { opens++; throw new InvalidDataException("late final must not open"); }, () => accepted, 79);
                controller.NoteKill(Marker, Death, context);
                Check(!KillCam.CompleteFinalRequest(controller, live, context with { Frame = 419 }, Raw.ServerTick,
                    false, true, 390), "Final retry did not remain pending before its boundary.");
                accepted.Add(Fact);
                Check(KillCam.CompleteFinalRequest(controller, live, context with { Frame = 421 }, Raw.ServerTick,
                    false, true, 390) && opens == 0 && timeline.Freezes == 0,
                    "A skipped host frame admitted a late final identity after its bounded window.");
            }
            using (var timeline = new Timeline())
            {
                int opens = 0;
                using var modernClip = new KillcamController(timeline, (_, _) => { opens++; throw new InvalidDataException("modern clip must not open"); });
                modernClip.NoteKill(Marker, Death, context); modernClip.Update(live, context);
                Check(opens == 0 && modernClip.IdentityResolution == ReplayKillLifeStatus.Unknown,
                    "An unlabelled caller bypassed the modern frozen clip's own protocol contract.");
            }
            using (var timeline = new Timeline { ProtocolVersion = 78 })
            {
                int opens = 0;
                using var legacy = new KillcamController(timeline, (_, _) => { opens++; throw new InvalidDataException("legacy admission probe"); },
                    sourceProtocolVersion: 78);
                legacy.NoteKill(Marker, Death, context); legacy.Update(live, context);
                Check(opens == 1 && legacy.IdentityResolution == ReplayKillLifeStatus.LegacyUnqualified,
                    "Legacy marker contract was silently promoted or disabled.");
            }
            receiver.Begin(7, 10);
            Check(receiver.Events.Count == 0 && !ReplayKillLifeResolver.Resolve(Raw, receiver.Events).Known,
                "Authority reset retained canonical death history.");

            // A posthumous projectile retains launch life5 even when the live
            // snapshot already describes the same attacker's respawned life6.
            var replica = new ReplayReplicaState();
            var match = new MatchStatePacket { RoomKey = "MP1 SANCTORUS", NextRoomKey = "", MatchId = 7,
                AuthorityEpoch = 9, Mode = (byte)GameMode.Battle };
            var matchBytes = new byte[MatchStatePacket.Size + 1]; matchBytes[0] = (byte)PacketType.MatchState; match.Write(matchBytes.AsSpan(1)); replica.Accept(matchBytes, 0);
            var roster = RosterPacket.Create(); roster.MatchId = 7; roster.AuthorityEpoch = 9; roster.Count = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 2; roster.Names[0] = "actor";
            var rosterBytes = new byte[RosterPacket.Size + 1]; rosterBytes[0] = (byte)PacketType.Roster; roster.Write(rosterBytes.AsSpan(1)); replica.Accept(rosterBytes, 0);
            int tail = 1 + SnapshotHeader.Size + PlayerState.Size;
            var snapshot = new byte[tail + NetMatchTimeSync.Size + NetHealthSync.HeaderSize]; snapshot[0] = (byte)PacketType.Snapshot;
            new SnapshotHeader { MatchId = 7, AuthorityEpoch = 9, Frame = 400, PlayerCount = 1 }.Write(snapshot.AsSpan(1));
            new PlayerState { SlotIndex = 0, SlotGeneration = 2, LifeId = 6, Health = 99,
                Flags = PlayerState.FlagActive | PlayerState.FlagSpawned }.Write(snapshot.AsSpan(1 + SnapshotHeader.Size));
            BinaryPrimitives.WriteUInt16LittleEndian(snapshot.AsSpan(tail + NetMatchTimeSync.Size), 7); replica.Accept(snapshot, 400);
            var verified = Resolve(Fact).Identity;
            Check(verified.KillerLifeId == 5 && KillcamController.ResolveAttackerSlot(verified, replica) == -1,
                "Posthumous launch was enriched from the respawned current actor.");
            Check(KillcamController.ResolveAttackerSlot(verified with { KillerLifeId = 6 }, replica) == 0,
                "Current actor negative control did not expose the wrong-life alias.");
            Console.WriteLine($"[killcam-identity] PASS: {checks} exact authority, packet order, conflict, consumption, timeout/final, reset, legacy and posthumous checks.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[killcam-identity] FAIL: " + ex); return 1; }
        finally { live.DoCleanup(); live.UnloadGl(); }
    }
}
