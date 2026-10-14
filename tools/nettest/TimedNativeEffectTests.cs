using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Pure policy and real-asset common-applier/profile coverage. These fixtures do
// not prove native charged-shot admission, collision, payment, or rescue intake.
internal static class TimedNativeEffectTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Native effect: " + label);
        _checks++;
    }
    internal static int Run()
    {
        try
        {
            _checks = 0;
            var unknown = new TimedVictimDamageProfile(default, 0, 1, 0, Hunter.Samus, true, false);
            Check(!unknown.AllowsBalancedDisruption, "omitted effect facts fail closed");
            var eligible = unknown with { EffectStateKnown = true };
            Check(eligible.AllowsBalancedDisruption, "explicit balanced zero-immunity profile permits disruption");
            Check(!(eligible with { BalancedDisruptImmunity = 1 }).AllowsBalancedDisruption,
                "retained immunity refuses balanced disruption");
            Check(!(eligible with { Balanced = false }).AllowsBalancedDisruption,
                "ordinary victim rules cannot supply balanced disruption eligibility");
            Check(!(eligible with { EffectStateKnown = false }).AllowsBalancedDisruption,
                "unknown effect facts cannot borrow known damage facts");
            var exactClock = eligible with { EffectFrame = 100 };
            Check(NetTimedShotEffects.SameEffectClock(exactClock, 100, 100),
                "known exact authority collision clock is eligible");
            Check(!NetTimedShotEffects.SameEffectClock(exactClock, 100, 101)
                && !NetTimedShotEffects.SameEffectClock(exactClock, 100, 165)
                && !NetTimedShotEffects.SameEffectClock(exactClock, 100, 295),
                "late zero-current and zero-historical timers never infer an uninterrupted effect interval");
            byte disrupted = HitClaimPacket.FlagDisrupted;
            byte effects = TimedShotDamageTemplate.FilterEffects(Hunter.Kanden, BeamType.VoltDriver,
                true, true, disrupted, out byte directOnly);
            Check(effects == disrupted && directOnly == disrupted, "charged balanced Kanden source restricts disruption to direct hits");
            effects = TimedShotDamageTemplate.FilterEffects(Hunter.Kanden, BeamType.VoltDriver,
                true, false, disrupted, out directOnly);
            Check(effects == 0 && directOnly == 0, "uncharged balanced Kanden source removes disruption");
            effects = TimedShotDamageTemplate.FilterEffects(Hunter.Kanden, BeamType.VoltDriver,
                true, true, 0, out directOnly);
            Check(effects == 0 && directOnly == 0, "source mask cannot invent an absent native affliction");
            effects = TimedShotDamageTemplate.FilterEffects(Hunter.Samus, BeamType.VoltDriver,
                true, false, disrupted, out directOnly);
            Check(effects == disrupted && directOnly == 0, "ordinary source retains native disruption mask");
            Check(TimedShotDamageTemplate.DisruptionFor(Hunter.Kanden, BeamType.VoltDriver, true, true, disrupted)
                == TimedDisruptionPolicy.BalancedKanden, "charged balanced Kanden freezes affinity policy");
            Check(TimedShotDamageTemplate.DisruptionFor(Hunter.Samus, BeamType.VoltDriver, true, true, disrupted)
                == TimedDisruptionPolicy.Ordinary, "nonaffinity owner freezes ordinary 120-frame policy");
            Check(TimedShotDamageTemplate.DisruptionFor(Hunter.Kanden, BeamType.VoltDriver, false, true, disrupted)
                == TimedDisruptionPolicy.Ordinary, "ordinary rules freeze ordinary policy for Kanden");
            Check(TimedShotDamageTemplate.DisruptionFor(Hunter.Kanden, BeamType.VoltDriver, true, false, disrupted)
                == TimedDisruptionPolicy.None
                && TimedShotDamageTemplate.DisruptionFor(Hunter.Kanden, BeamType.VoltDriver, true, true, 0)
                == TimedDisruptionPolicy.None, "uncharged or absent source affliction freezes no disruption");
            var ordinary = new TimedShotDamageTemplate(10, 10, 10, Vector3.Zero, Vector3.UnitX,
                1, 1, 1, 0, true, disrupted, true, DisruptionPolicy: TimedDisruptionPolicy.Ordinary);
            var affinity = ordinary with { DisruptionPolicy = TimedDisruptionPolicy.BalancedKanden };
            Check(ordinary.SameChannels(ordinary) && !ordinary.SameChannels(affinity)
                && ordinary.Effects == affinity.Effects, "equal masks cannot hide a different frozen disruption policy");
            Console.WriteLine($"PASS: {_checks} pure disruption profile/source-mask assertions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static int RunScene(string profile, string room)
    {
        var oldTimed = NetTimedShotProof.Mode; var oldProof = NetShotProof.Mode;
        bool oldScript = NetTestScript.Enabled;
        var sim = new SceneOwner();
        try
        {
            _checks = 0;
            Check(ServerSim.Available(out string reason), reason);
            NetTimedShotProof.Mode = TimedGrantProofMode.Shadow;
            NetShotProof.Mode = ShotProofMode.Off; NetTestScript.Enabled = false;
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1;
            roster.Hunters[0] = (byte)Hunter.Samus; roster.Names[0] = "Native effect fixture";
            Check(sim.Server.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "real asset room starts");
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            NetSession.ApplyRoster(roster);
            var scene = (Scene)typeof(ServerSim).GetField("_scene", Hidden)!.GetValue(sim.Server)!;
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            scene.GameState.EnhancedHunters = false; scene.GameState.BalancedMode = true;
            scene.GameState.PrimeHunter = -1;
            foreach (var item in scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
            void Step()
            {
                long failures = sim.Server.StepFailures; uint before = NetSession.NetFrame;
                sim.Server.Step();
                Check(sim.Server.StepFailures == failures && failures == 0
                    && NetSession.NetFrame == unchecked(before + 1), "complete native simulation step advances without failure");
            }
            for (int i = 0; i < 120; i++) Step();
            var victim = scene.Players.Items[0];
            Check(victim.ModIsInPlay && !victim.IsBot && !scene.Services.IsReplica,
                "real live authority actor supplies native state");
            victim.Health = 999; victim.Controls.ClearAll(); victim.ModReleaseSpawnProtection();
            victim.ModResetBalancedAffinityState();
            Check(victim.ModApplyNativeDisruption(true) && victim.ModDisruptedTimer == 75
                && victim.ModBalancedDisruptImmunityTimer == 195,
                "shared native balanced applier starts 75 control and 195 total immunity frames");
            NetTimedDamageProfile.Reset(); NetSession.BroadcastSnapshot();
            uint captured = NetSession.NetFrame; var key = ShotKey.For(0, 0);
            Check(NetTimedDamageProfile.TryGet(0, key.Generation, key.LifeId, captured, out var retained)
                && retained.EffectStateKnown && retained.BalancedDisruptImmunity == 195
                && !retained.AllowsBalancedDisruption, "authority publication retains actual native immunity");
            Step();
            Check(victim.ModDisruptedTimer == 74 && victim.ModBalancedDisruptImmunityTimer == 194,
                "native player process decrements both timers exactly once");
            Check(!victim.ModApplyNativeDisruption(true) && victim.ModDisruptedTimer == 74
                && victim.ModBalancedDisruptImmunityTimer == 194, "current immunity retry cannot refresh either timer");
            Check(NetTimedDamageProfile.TryGet(0, key.Generation, key.LifeId, captured, out var reread)
                && reread == retained, "native timer mutation cannot rewrite retained effect facts");
            Check(!NetTimedDamageProfile.TryGet(0, unchecked((ushort)(key.Generation + 1)), key.LifeId, captured, out _)
                && !NetTimedDamageProfile.TryGet(0, key.Generation, unchecked((ushort)(key.LifeId + 1)), captured, out _),
                "other occupants and lives cannot borrow retained immunity");
            for (int i = 0; i < 74; i++) Step();
            Check(!victim.ModDisrupted && victim.ModDisruptedTimer == 0
                && victim.ModBalancedDisruptImmunityTimer == 120, "real control expiry retains subsequent immunity window");
            for (int i = 0; i < 52; i++) Step();
            Check(NetTimedDamageProfile.TryGet(0, key.Generation, key.LifeId, captured, out reread)
                && reread == retained, "retained immunity remains immutable through ring age127");
            Step();
            Check(!NetTimedDamageProfile.TryGet(0, key.Generation, key.LifeId, captured, out _),
                "ring age128 retires exact old immunity facts");
            for (int i = 0; i < 67; i++) Step();
            Check(victim.ModBalancedDisruptImmunityTimer == 0 && victim.ModApplyNativeDisruption(true),
                "real immunity expiry permits a subsequent balanced application");
            victim.ModResetBalancedAffinityState();
            Check(victim.ModApplyNativeDisruption(false) && victim.ModDisruptedTimer == 120
                && victim.ModBalancedDisruptImmunityTimer == 0, "ordinary common applier retains native 120-frame control");
            for (int i = 0; i < 120; i++) Step();
            Check(victim.ModDisruptedTimer == 0 && !victim.ModDisrupted, "ordinary native control expires through actual process ticks");
            Console.WriteLine($"PASS: {_checks} asset-backed common-applier/profile assertions ({profile}).");
            Console.WriteLine("NOT_RUN: native charged Kanden projectile admission/collision, rescued claim intake, authenticated UDP, GL/audio. Helper invocation and health are declared fixture inputs.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            sim.Dispose(); NetTimedDamageProfile.Reset(); NetTimedShotProof.Mode = oldTimed;
            NetShotProof.Mode = oldProof; NetTestScript.Enabled = oldScript;
        }
    }
    private sealed class SceneOwner : IDisposable
    {
        internal readonly ServerSim Server = new();
        public void Dispose() { Server.Stop(); NetSession.Stop(); }
    }
}
