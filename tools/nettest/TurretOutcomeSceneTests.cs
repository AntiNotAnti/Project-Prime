using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Fixture poses and owner requests are inputs. Only native turret Process/Spawn/Commit
// and incoming-history expiry may produce the outcome under test.
internal static class TurretOutcomeSceneTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); _checks++; }
    private static void Frame(uint value) => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);

    internal static int Run(string room)
    {
        _checks = 0;
        if (!ServerSim.Available(out string unavailable)) { Console.Error.WriteLine(unavailable); return 1; }
        bool previousScript = NetTestScript.Enabled, previousPlayback = NetFireEvents.AcceptedPlaybackEnabled;
        var previousSink = NetSession.TurretOutcomeSink;
        var sim = new ServerSim();
        try
        {
            NetTestScript.Enabled = false;
            if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte slot = 0; slot < 2; slot++)
            {
                roster.Slots[slot] = slot; roster.Generations[slot] = 1;
                roster.Hunters[slot] = (byte)(slot == 0 ? Hunter.Weavel : Hunter.Samus);
                roster.Names[slot] = $"OUTCOME{slot}";
            }
            NetSession.ApplyRoster(roster);
            for (int step = 0; step < 120; step++) sim.Step();
            var owner = PlayerEntity.Players[0]; var victim = PlayerEntity.Players[1];
            var scene = owner.OwningScene;
            owner.Health = 199; owner.ModStartFormSwitch(); owner.ModForceForm(true);
            owner.ModArmWeapon(BeamType.PowerBeam);
            var turret = owner.Halfturret;
            Check(NetSession.IsAuthority && owner.IsAltForm && turret.Health > 0,
                "authority fixture owns a real native Weavel turret");
            scene.GameState.EnhancedHunters = false; scene.GameState.BalancedMode = false;
            victim.ModPlaceAt(turret.Position + new Vector3(3, .4f, 0));
            foreach (var beam in owner.EquipInfo.Beams) beam.Destroy();
            NetFireEvents.Reset(); NetFireEvents.AcceptedPlaybackEnabled = true;
            var outcomes = new List<TurretFireOutcome>();
            NetSession.TurretOutcomeSink = payload =>
            {
                Check(TurretFireOutcome.TryRead(payload, out var outcome), "native publication passes production outcome decoder");
                outcomes.Add(outcome);
            };
            IntentPacket Carrier(uint sourceFrame, uint shot)
            {
                var packet = new IntentPacket { MatchId = NetSession.CurrentMatchId,
                    AuthorityEpoch = NetSession.AuthorityEpoch, SlotGeneration = NetPlayerLifecycle.Generation(0),
                    LifeId = NetPlayerLifecycle.Get(0), Frame = sourceFrame, AckFrame = sourceFrame,
                    HasFireEvents = true, FireEventCount = 1 };
                packet.FireEvents[0] = new FireEvent(shot, sourceFrame, sourceFrame, 0, FireEventKind.TurretFire,
                    (byte)BeamType.Battlehammer, 0, 0, FireEvent.FlagPose,
                    turret.Position + Vector3.UnitY * 2, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ,
                    SourceLife: packet.LifeId);
                return packet;
            }
            void Admit(IntentPacket packet)
            {
                NetSession.RemoteIntents[0] = packet; NetSession.RemoteIntentValid[0] = true;
                Check(NetFireEvents.Accept(0, packet), "owner turret request admitted by real incoming history");
                NetFireEvents.Prepare(owner, packet);
            }
            void NativeStep(IntentPacket packet)
            {
                Frame(NetSession.NetFrame + 1); NetFireEvents.Prepare(owner, packet);
                Check(turret.Process(), "native turret remains active during decision");
            }
            var first = Carrier(NetSession.NetFrame, 1); var original = first.FireEvents[0];
            Admit(first); victim.Health = 0; owner.TimeSinceShot = 1000;
            NativeStep(first);
            Check(turret.Target == null && outcomes.Count == 0 && NetFireEvents.IsPendingTurret(ShotKey.For(0, 1)),
                "missing native target keeps an offer pending without an emission");
            victim.Health = 99; owner.TimeSinceShot = 0;
            NativeStep(first);
            Check(ReferenceEquals(turret.Target, victim) && outcomes.Count == 0,
                "native target acquisition does not bypass cooldown");
            owner.TimeSinceShot = 1000; NativeStep(first);
            Check(outcomes.Count == 1 && outcomes[0].Status == TurretFireOutcomeStatus.Emitted,
                "native firing gate publishes one successful emission");
            var emitted = outcomes[0];
            Check(emitted.Key == ShotKey.For(0, 1) && emitted.Fire == original,
                "authority emission preserves the exact owner identity and original fire");
            Check(emitted.AuthorityFrame == NetSession.NetFrame && emitted.Fire.SourceFrame == first.Frame
                && emitted.AuthorityFrame > emitted.Fire.SourceFrame,
                "authority emission time does not replace the owner source clock");
            Check(emitted.EmissionOrigin == turret.Position.AddY(.4f)
                && emitted.EmissionOrigin != original.Origin && emitted.EmissionDirection != original.Direction,
                "outcome carries separate actual native muzzle and target direction");
            Check(owner.EquipInfo.Beams.Any(beam => beam.Lifespan > 0 && beam.ModLaunchKey == emitted.Key),
                "published emission has an actual native flight with its original key");
            Admit(first); owner.TimeSinceShot = 1000; NativeStep(first);
            Check(outcomes.Count == 1, "duplicate owner carrier cannot emit or publish twice");
            var second = Carrier(NetSession.NetFrame, 2); Admit(second);
            victim.Health = 0; owner.TimeSinceShot = 1000; NativeStep(second);
            Check(outcomes.Count == 1 && turret.Target == null, "second targetless request remains pending");
            uint expiry = second.Frame + NetIncomingFireHistory.RecoveryFrames + 1;
            Frame(expiry);
            var later = second; later.Frame = expiry; later.FireEventCount = 0; later.FireEvents = default;
            Check(NetFireEvents.Accept(0, later), "new owner carrier advances normal recovery expiry");
            Check(outcomes.Count == 2 && outcomes[1].Status == TurretFireOutcomeStatus.Refused
                && outcomes[1].Reason == TurretFireOutcomeReason.RecoveryExpired,
                "unemitted native request receives a real terminal recovery refusal");
            Check(outcomes[1].Key == ShotKey.For(0, 2) && outcomes[1].Fire == second.FireEvents[0]
                && outcomes[1].EmissionOrigin == Vector3.Zero && outcomes[1].EmissionDirection == Vector3.Zero,
                "refusal retains original source identity without inventing emission geometry");
            Check(!owner.EquipInfo.Beams.Any(beam => beam.Lifespan > 0 && beam.ModLaunchKey == outcomes[1].Key),
                "refused request never creates a native flight");
            Check(sim.StepFailures == 0, "fixture native warmup had no simulation failures");
            Console.WriteLine($"PASS: {_checks} native turret outcome scene assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            sim.Stop(); NetSession.TurretOutcomeSink = previousSink;
            NetTestScript.Enabled = previousScript; NetFireEvents.AcceptedPlaybackEnabled = previousPlayback;
        }
    }
}
