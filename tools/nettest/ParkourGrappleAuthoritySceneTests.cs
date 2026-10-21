using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// A genuine ServerSim authority and native joint pose/control consumption.
// Serialized carrier intake and initial placement are explicit fixture seams.
// No role/lifecycle/controller fields are set; this is not live UDP evidence.
internal static class ParkourGrappleAuthoritySceneTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException(label); }

    internal static int Run(string? output = null)
    {
        _checks = 0; var rows = new List<object>(); string? failure = null; int result = 0, grappleChecks = 0;
        var sim = new ServerSim();
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive, "fresh authority process");
            var roster = RosterPacket.Create();
            roster.MatchId = 7; roster.AuthorityEpoch = 23; roster.Revision = 1; roster.Count = 2;
            for (byte slot = 0; slot < 2; slot++)
            { roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Hunters[slot] = (byte)Hunter.Samus; }
            var session = new SessionStatePacket { MatchId = 7, AuthorityEpoch = 23, Revision = 1,
                Phase = SessionPhase.InMatch, MaxPlayers = 2, WorldProfile = MatchWorldProfile.Resolve(2),
                Match = new MatchDefinition { RoomKey = TraversalSceneTests.Room, Mode = GameMode.Battle,
                    MovementModel = MovementModel.Parkour, TimeLimitSeconds = 0, PointGoal = 10000 } };
            PlayerState published = default; int snapshots = 0;
            Check(sim.Start(TraversalSceneTests.Room, GameMode.Battle, 2, bytes =>
            {
                var header = SnapshotHeader.Read(bytes);
                for (int index = 0; index < header.PlayerCount; index++)
                {
                    PlayerState state = PlayerState.Read(bytes.Slice(SnapshotHeader.Size + index * PlayerState.Size, PlayerState.Size));
                    if (state.SlotIndex == 0) { published = state; snapshots++; }
                }
            }, () => { }, roster, session), "real admitted ServerSim.Start");
            for (int i = 0; i < 120; i++) sim.Step();
            Scene scene = sim.EvidenceScene!; PlayerEntity player = scene.Players.Items[0];
            Check(NetSession.Role == NetRole.Server && NetSession.IsAuthority && NetSession.LocalSlot == -1
                && NetRoomChange.GameplayReady && player.ModIsInPlay && player.Health > 0 && !player.IsBot,
                "genuine ready dedicated authority with a living human puppet");
            Vector3 seed = new(-42, .5f, 25);
            player.Reposition(seed, Vector3.UnitZ, NodeRef.None); player.PrevPosition = seed;
            scene.Players.Items[1].Reposition(new(42, .5f, -25), Vector3.UnitZ, NodeRef.None);
            scene.Players.Items[1].PrevPosition = scene.Players.Items[1].Position;
            uint source = NetSession.NetFrame;
            IntentPacket Packet(ushort nonce, bool held) => new()
            {
                MatchId = 7, AuthorityEpoch = 23, SlotGeneration = NetPlayerLifecycle.Generation(0),
                LifeId = NetPlayerLifecycle.Get(0), Frame = ++source, AckFrame = NetSession.NetFrame,
                Position = seed, Aim = Vector3.UnitZ, WeaponSelect = (byte)BeamType.PowerBeam,
                Buttons = IntentButtons.InPlayState | (held ? IntentButtons.Grapple : 0), GrappleRequestNonce = nonce
            };
            void Intake(string label, IntentPacket packet, bool expectedFresh)
            {
                byte[] bytes = new byte[IntentPacket.TouchCanonicalSize]; packet.Write(bytes);
                IntentPacket decoded = IntentPacket.Read(bytes);
                bool admitted = NetSession.AcceptSlotIntent(0, decoded, out bool fresh);
                if (expectedFresh) Check(admitted && fresh, label + ": current carrier enters normal intake");
                else Check(!fresh, label + ": stale or wrong-identity carrier cannot become current input");
                sim.Step(); NetSession.BroadcastSnapshot();
                Check(sim.StepFailures == 0 && player.ModGrappleForceCount == 0,
                    label + ": authority never applies a human owner's grapple force");
                Check(published.Grapple == player.ModGrappleState && published.LifeId == NetPlayerLifecycle.Get(0),
                    label + ": actual canonical snapshot publishes the accepted lifecycle state");
                var state = player.ModGrappleState;
                rows.Add(new { label, packet.Frame, packet.MatchId, packet.AuthorityEpoch, packet.SlotGeneration,
                    packet.LifeId, packet.GrappleRequestNonce, admitted, fresh, state.Active,
                    packet.MantleVisualPhase, publishedMantlePhase = published.MantleVisualPhase,
                    nativeMantle = player.ModParkourMantleActive,
                    acceptedNonce = state.RequestNonce, anchorX = state.Anchor.X, anchorY = state.Anchor.Y,
                    anchorZ = state.Anchor.Z, state.RopeLength, selections = player.ModGrappleSelectionCount,
                    forces = player.ModGrappleForceCount });
            }
            void State(ushort nonce, bool active, int selections, string label)
                => Check(player.ModGrappleState.RequestNonce == nonce && player.ModGrappleState.Active == active
                    && player.ModGrappleSelectionCount == selections, label);

            int baseline = player.ModGrappleSelectionCount;
            Intake("release-before-press", Packet(1, false), true); State(1, false, baseline, "release establishes nonce tombstone");
            Intake("late-press-for-tombstone", Packet(1, true), true); State(1, false, baseline, "same nonce cannot undo release");
            var first = Packet(2, true);
            Intake("new-physical-hold", first, true); State(2, true, baseline + 1, "fresh request selects one real static anchor");
            Check(MathF.Abs(player.ModGrappleState.Anchor.Z - 40) < .001f,
                "authority uses the accepted source aim to select the actual native wall");
            Intake("held-carrier", Packet(2, true), true); State(2, true, baseline + 1, "same held nonce cannot raycast again");
            var duplicate = first; duplicate.GrappleRequestNonce = 3;
            Intake("old-source-new-nonce", duplicate, false); State(2, true, baseline + 1, "recovered old source cannot create a request");
            Intake("half-range-nonce", Packet(32770, true), true); State(2, true, baseline + 1, "ambiguous nonce cannot replace active request");
            Intake("new-hold-with-lost-release", Packet(3, true), true); State(3, true, baseline + 2, "new nonce replaces hold despite lost release carrier");
            Intake("older-nonce-new-source", Packet(2, true), true); State(3, true, baseline + 2, "source ordering cannot promote an older nonce");
            Intake("newer-release-before-press", Packet(4, false), true); State(4, false, baseline + 2, "newer release consumes unseen hold");
            Intake("late-newer-press", Packet(4, true), true); State(4, false, baseline + 2, "late press cannot undo newer tombstone");
            Intake("next-eligible-request", Packet(5, true), true); State(5, true, baseline + 3, "subsequent physical request can attach");
            var wrongLife = Packet(6, true); wrongLife.LifeId ^= 0x4000;
            Intake("wrong-life", wrongLife, false); State(5, true, baseline + 3, "wrong lifecycle cannot create or replace state");
            var wrongEpoch = Packet(6, true); wrongEpoch.AuthorityEpoch++;
            Intake("wrong-epoch", wrongEpoch, false); State(5, true, baseline + 3, "wrong epoch cannot create state");
            var wrongMatch = Packet(6, true); wrongMatch.MatchId++;
            Intake("wrong-match", wrongMatch, false); State(5, true, baseline + 3, "wrong match cannot create state");
            for (int i = 0; i < 35; i++) sim.Step();
            State(5, false, baseline + 3, "no new carrier expires active authority capability without a release packet");
            Intake("stale-timeout-same-hold", Packet(5, true), true); State(5, false, baseline + 3, "fresh carrier cannot revive a consumed expired hold");
            Intake("physical-repress-after-timeout", Packet(6, true), true); State(6, true, baseline + 4, "new request after timeout may attach");
            var heldJump = Packet(6, true); heldJump.Buttons |= IntentButtons.Jump;
            Intake("held-grapple-native-jump", heldJump, true); State(6, false, baseline + 4,
                "native Jump tombstones an existing same-nonce held grapple");
            Intake("release-jump-keep-grapple", Packet(6, true), true); State(6, false, baseline + 4,
                "releasing Jump cannot revive the still-held consumed grapple request");
            Intake("new-grapple-after-native-jump", Packet(7, true), true); State(7, true, baseline + 5,
                "new physical grapple nonce may attach after native Jump cancellation");
            Vector3 rejectedPosition = new(1000000, 1000000, 1000000);
            for (int i = 0; i < 35; i++)
            {
                var rejectedPose = Packet(8, true); rejectedPose.Position = rejectedPosition;
                Intake("fresh-carrier-rejected-pose-" + i, rejectedPose, true);
                Check(NetSession.RemoteIntents[0].Position == rejectedPosition
                    && NetSession.RemoteIntentAge(0) <= 1,
                    "finite rejected pose still refreshes actual admitted transport carrier age");
                Check(player.Position != rejectedPosition && player.ModGrappleSelectionCount == baseline + 5,
                    "joint pose rejection cannot select an anchor or relocate the native body");
            }
            State(7, false, baseline + 5,
                "fresh transport without jointly accepted pose cannot keep an old grapple alive beyond 30 scene ticks");
            Intake("valid-joint-pose-after-rejected-carriers", Packet(8, true), true);
            State(8, true, baseline + 6,
                "only a subsequent jointly accepted pose and new request can grant the next capability");
            Check(snapshots > 0 && player.ModGrappleForceCount == 0, "authority emits accepted states while owner-only forces remain absent");
            grappleChecks = _checks;

            // This extra sequence uses the same serialized normal intake and
            // jointly accepted pose/control seam. A cosmetic hint is not a
            // mantle capability: the authority puppet has no W/Jump/contact
            // route, and its body remains on the reported native flat floor.
            int visualSelections = player.ModGrappleSelectionCount;
            IntentPacket VisualPacket(byte phase)
            {
                var packet = Packet(9, false); packet.MantleVisualPhase = phase; return packet;
            }
            var receivedField = typeof(PlayerEntity).GetField("_modMantleVisualReceived", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var sourceField = typeof(PlayerEntity).GetField("_modMantleVisualSource", BindingFlags.NonPublic | BindingFlags.Instance)!;
            (ulong Received, uint Source) VisualStamp() => ((ulong)receivedField.GetValue(player)!, (uint)sourceField.GetValue(player)!);
            void Phase(byte expected, string label)
            {
                Check(published.MantleVisualPhase == expected && player.ModPublishedMantleVisualPhase == expected,
                    label + ": actual authority snapshot publishes exactly the accepted current cosmetic phase");
                Check(!player.ModParkourMantleActive && player.ModParkourMantleTicks == 0
                    && !player.ModParkourMantleApplied(scene.FrameCount - 1)
                    && player.ModLocalMantleVisualPhase == 0,
                    label + ": remote visual hint never activates or advances the native mantle controller");
                Check(!player.ModGrappleState.Active && player.ModGrappleSelectionCount == visualSelections
                    && player.ModGrappleForceCount == 0,
                    label + ": cosmetic phase never selects an anchor or grants owner forces");
            }
            var clearVisual = VisualPacket(0);
            Intake("mantle-visual-release-and-none", clearVisual, true);
            Phase(0, "no phase");
            // Two native zero-control ticks establish the matched steady body
            // before comparing phase-only carriers. No body or velocity writes.
            Intake("mantle-visual-native-baseline", VisualPacket(0), true);
            var nativePhysics = (player.Position, player.Speed, player.ModBipedBottom,
                player.ModBipedTop, player.ModBipedEyeOffset, player.ModCrouched, player.ModSliding, player.ModTucked,
                player.IsAltForm, player.IsMorphing, player.IsUnmorphing);
            for (byte phase = 1; phase <= 15; phase++)
            {
                var visual = VisualPacket(phase);
                Intake("mantle-visual-current-phase-" + phase, visual, true);
                Phase(phase, "current phase " + phase);
                Check(VisualStamp().Source == visual.Frame,
                    "jointly accepted fresh phase is tied to its actual current source frame");
                Check(nativePhysics == (player.Position, player.Speed, player.ModBipedBottom,
                    player.ModBipedTop, player.ModBipedEyeOffset, player.ModCrouched, player.ModSliding, player.ModTucked,
                    player.IsAltForm, player.IsMorphing, player.IsUnmorphing),
                    "changing only a cosmetic phase leaves matched actual native body, velocity and stance exact");
            }
            var acceptedVisual = VisualPacket(6);
            Intake("mantle-visual-established-source", acceptedVisual, true); Phase(6, "established phase");
            var acceptedStamp = VisualStamp();
            var repeatVisual = acceptedVisual; repeatVisual.MantleVisualPhase = 12;
            Intake("mantle-visual-repeat-source-cannot-rewrite", repeatVisual, false); Phase(6, "repeat source");
            Check(VisualStamp() == acceptedStamp, "same-frame carrier cannot rewrite or refresh visual acceptance age");
            var olderVisual = acceptedVisual; olderVisual.Frame--; olderVisual.MantleVisualPhase = 13;
            Intake("mantle-visual-old-source-cannot-rewrite", olderVisual, false); Phase(6, "old source");
            Check(VisualStamp() == acceptedStamp, "older frame cannot refresh the accepted visual source or age");
            var wrongVisualLife = VisualPacket(14); wrongVisualLife.LifeId ^= 0x4000;
            Intake("mantle-visual-wrong-life", wrongVisualLife, false); Phase(6, "wrong life");
            Check(VisualStamp() == acceptedStamp, "wrong lifecycle cannot replace or refresh the current visual hint");
            var wrongVisualEpoch = VisualPacket(15); wrongVisualEpoch.AuthorityEpoch++;
            Intake("mantle-visual-wrong-epoch", wrongVisualEpoch, false); Phase(6, "wrong epoch");
            Check(VisualStamp() == acceptedStamp, "wrong authority epoch cannot refresh visual age");
            for (int tick = 0; tick < 35; tick++)
            {
                var badVisualPose = VisualPacket(11); badVisualPose.Position = rejectedPosition;
                Intake("mantle-visual-fresh-transport-rejected-pose-" + tick, badVisualPose, true);
                Check(NetSession.RemoteIntents[0].MantleVisualPhase == 11
                    && NetSession.RemoteIntents[0].Position == rejectedPosition && NetSession.RemoteIntentAge(0) <= 1,
                    "phase-bearing finite bad pose really updates normal transport carrier age");
                Check(VisualStamp() == acceptedStamp && player.Position != rejectedPosition,
                    "joint pose rejection cannot install a phase or refresh its accepted source/scene age");
                byte expected = scene.FrameCount - acceptedStamp.Received <= 30 ? (byte)6 : (byte)0;
                Phase(expected, "rejected pose visual tick " + tick);
                Check(nativePhysics == (player.Position, player.Speed, player.ModBipedBottom,
                    player.ModBipedTop, player.ModBipedEyeOffset, player.ModCrouched, player.ModSliding, player.ModTucked,
                    player.IsAltForm, player.IsMorphing, player.IsUnmorphing),
                    "bad phase-bearing carriers preserve the matched native body without cosmetic physics");
            }
            Phase(0, "accepted visual age expires despite fresh bad-pose transport");
            var renewedVisual = VisualPacket(9);
            Intake("mantle-visual-valid-joint-pose-resumes", renewedVisual, true); Phase(9, "renewed valid phase");
            Check(VisualStamp().Source == renewedVisual.Frame && VisualStamp() != acceptedStamp,
                "only a subsequent current-life jointly accepted pose renews cosmetic visual publication");
            Intake("mantle-visual-explicit-release-zero", VisualPacket(0), true); Phase(0, "explicit release");
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Intake("mantle-visual-repeat-none-" + repeat, VisualPacket(0), true);
                Phase(0, "repeated none");
            }
        }
        catch (Exception error) { failure = error.ToString(); Console.Error.WriteLine("PARKOUR GRAPPLE AUTHORITY FAIL " + error); result = 1; }
        finally { sim.Stop(); NetSession.Stop(); }
        if (output != null)
        {
            string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { status = result == 0 ? "PASS" : "FAIL",
                checks = _checks, grappleChecks, mantleVisualChecks = _checks - grappleChecks,
                failure, scope = "Actual ServerSim authority and native accepted pose/control seam; serialized in-process carrier intake and initial placement, including cosmetic mantle phase source/age/life isolation; no live UDP qualification", rows },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"PARKOUR GRAPPLE AUTHORITY {(result == 0 ? "PASS" : "FAIL")} {_checks} assertions"); return result;
    }
}
