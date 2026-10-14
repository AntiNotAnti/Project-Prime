using System;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Asset-backed authority snapshot history, not gameplay-damage or claim-cap
// coverage. Susceptibility, handicap and transition flags are declared fixture
// mutations of actual native actors; no paid hit, grant or collision is injected.
internal static class TimedDamageProfileSceneTests
{
    private static int _checks;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Native victim profile history: " + message);
        _checks++;
    }
    private static void Frame(uint value)
        => typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, value);
    private static void Flags(PlayerEntity player, PlayerFlags1 value)
        => typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1))!.SetValue(player, value);

    private sealed class Fixture : IDisposable
    {
        private readonly ServerSim _sim = new();
        internal readonly Scene Scene;
        internal readonly PlayerEntity Victim;
        internal Fixture(string room)
        {
            NetTimedShotProof.Mode = TimedGrantProofMode.Shadow;
            NetShotProof.Mode = ShotProofMode.Off;
            NetTestScript.Enabled = false;
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 1;
            roster.Slots[0] = 0; roster.Generations[0] = 1;
            roster.Hunters[0] = (byte)Hunter.Weavel; roster.Names[0] = "Victim history fixture";
            try
            {
                Check(_sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }, roster: roster), "real server room starts");
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, Mode = (byte)GameMode.Battle }, false);
                NetSession.ApplyRoster(roster);
                Scene = (Scene)typeof(ServerSim).GetField("_scene", Hidden)!.GetValue(_sim)!;
                Scene.GameState.MatchTime = -1; Scene.GameState.PointGoal = 10000;
                Scene.GameState.EnhancedHunters = false; Scene.GameState.BalancedMode = false;
                Scene.GameState.PrimeHunter = -1;
                NetSlotManager.Sync(); Victim = Scene.Players.Items[0];
                foreach (var item in Scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
                for (int i = 0; i < 120; i++) Step();
                Check(NetSession.Role == NetRole.Server && NetSession.IsAuthority && NetRoomChange.GameplayReady
                    && !Scene.Services.IsReplica && Victim.ModIsInPlay && Victim.Health > 0
                    && Victim.Hunter == Hunter.Weavel && !Victim.IsBot,
                    "real native authority actor spawned from the declared roster without a local/helper shot lane");
                NetTimedDamageProfile.Reset();
            }
            catch { _sim.Stop(); NetSession.Stop(); throw; }
        }
        internal uint Step()
        {
            uint next = unchecked(NetSession.NetFrame + 1);
            long failures = _sim.StepFailures;
            Frame(next); _sim.Step();
            Check(_sim.StepFailures == failures && failures == 0 && NetSession.NetFrame == next,
                "one complete declared authority simulation tick without a swallowed failure");
            return next;
        }
        internal uint Snapshot()
        {
            // Capture fixture-mutated native flags without pretending that an
            // animation/death or gameplay input created those states.
            uint next = unchecked(NetSession.NetFrame + 1); Frame(next);
            long sent = NetSession.SnapshotsSent; NetSession.BroadcastSnapshot();
            Check(NetSession.SnapshotsSent == sent + 1, "actual authority snapshot publication reaches the recording seam");
            return next;
        }
        internal TimedVictimDamageProfile Read(uint at, ShotKey key)
        {
            Check(NetTimedDamageProfile.TryGet(key.ShooterSlot, key.Generation, key.LifeId, at, out var profile),
                "actual published frame has exact retained victim facts");
            Check(profile.Key == key, "retained native profile owns the complete exact life and stream");
            return profile;
        }
        public void Dispose() { _sim.Stop(); NetSession.Stop(); }
    }

    public static int Run(string room)
    {
        var oldTimed = NetTimedShotProof.Mode; var oldProof = NetShotProof.Mode;
        bool oldUnlagged = NetUnlagged.Enabled, oldScript = NetTestScript.Enabled;
        try
        {
            _checks = 0; Check(ServerSim.Available(out string reason), reason);
            using var fixture = new Fixture(room);
            var victim = fixture.Victim; var key = ShotKey.For(0, 0);
            NetUnlagged.Enabled = false;
            for (int beam = 0; beam < 9; beam++) victim.BeamEffectiveness[beam] = (Effectiveness)(beam % 4);
            NetSession.SlotDamageReduction[0] = 30; fixture.Scene.GameState.BalancedMode = true;
            long snapshots = NetSession.SnapshotsSent;
            uint first = fixture.Step();
            Check(NetSession.SnapshotsSent > snapshots, "native AfterSimulation publishes the observed authority tick");
            var retained = fixture.Read(first, key);
            Check(NetTimedDamageProfile.TryPackEffectiveness(victim.BeamEffectiveness, out uint firstPacked)
                && retained.PackedEffectiveness == firstPacked && retained.Handicap == 30
                && retained.Hunter == Hunter.Weavel && retained.Balanced
                && retained.Transitioning == (victim.IsMorphing || victim.IsUnmorphing),
                "disabled unlagged history still captures actual susceptibility, roster reduction, hunter and rules");
            Check(retained.DamageLevel == 1 && fixture.Scene.GameState.DamageLevel == 1,
                "native match getter fixes damage level1; no fixture setter claims a fictitious1.25 mode");

            var originalFlags = victim.Flags1;
            Array.Fill(victim.BeamEffectiveness, Effectiveness.Half);
            NetSession.SlotDamageReduction[0] = 50; fixture.Scene.GameState.BalancedMode = false;
            Flags(victim, (originalFlags & ~(PlayerFlags1.Morphing | PlayerFlags1.Unmorphing)) | PlayerFlags1.Morphing);
            // Explicitly re-enter the real publication seam at the same frame;
            // the first snapshot facts must win despite mutable native inputs.
            NetSession.BroadcastSnapshot();
            Check(fixture.Read(first, key) == retained, "later same-frame native mutation cannot rewrite first captured facts");
            uint morph = fixture.Snapshot(); var morphProfile = fixture.Read(morph, key);
            Check(morphProfile.Transitioning && morphProfile.Handicap == 50 && !morphProfile.Balanced
                && morphProfile.PackedEffectiveness != retained.PackedEffectiveness,
                "next actual snapshot captures changed fixture facts while old frame stays immutable");
            Check(fixture.Read(first, key) == retained, "prior frame keeps original full profile after subsequent capture");
            Flags(victim, (originalFlags & ~(PlayerFlags1.Morphing | PlayerFlags1.Unmorphing)) | PlayerFlags1.Unmorphing);
            uint unmorph = fixture.Snapshot();
            Check(fixture.Read(unmorph, key).Transitioning, "actual IsUnmorphing property participates in the recorded transition bit");
            Flags(victim, originalFlags & ~(PlayerFlags1.Morphing | PlayerFlags1.Unmorphing));
            uint settled = fixture.Snapshot();
            Check(!fixture.Read(settled, key).Transitioning, "subsequent snapshot observes settled native flags");
            Check(!NetTimedDamageProfile.TryGet(0, unchecked((ushort)(key.Generation + 1)), key.LifeId, settled, out _)
                && !NetTimedDamageProfile.TryGet(0, key.Generation, unchecked((ushort)(key.LifeId + 1)), settled, out _)
                && !NetTimedDamageProfile.TryGet(0, key.Generation, key.LifeId, unchecked(settled + 1), out _),
                "wrong occupant, life and unobserved exact frame cannot borrow profile facts");

            // Declared lifecycle-only stimulus: it invokes the real authority
            // life transition method, without claiming a native death/respawn.
            NetPlayerLifecycle.OnSpawn(victim); var nextLife = ShotKey.For(0, 0);
            Check(nextLife.LifeId != key.LifeId && nextLife.Generation == key.Generation,
                "authority lifecycle stimulus advances real full-key life");
            uint spawned = fixture.Snapshot(); fixture.Read(spawned, nextLife);
            Check(fixture.Read(settled, key).Key == key
                && !NetTimedDamageProfile.TryGet(0, nextLife.Generation, nextLife.LifeId, settled, out _)
                && !NetTimedDamageProfile.TryGet(0, key.Generation, key.LifeId, spawned, out _),
                "old-life historical facts remain exact and cannot answer the new life");

            victim.BeamEffectiveness[8] = (Effectiveness)4;
            uint unsupported = fixture.Snapshot();
            Check(!NetTimedDamageProfile.TryGet(0, nextLife.Generation, nextLife.LifeId, unsupported, out var missing)
                && missing == default, "unsupported actual native susceptibility refuses the entire captured profile");
            Array.Fill(victim.BeamEffectiveness, Effectiveness.Normal);
            uint fresh = fixture.Snapshot(); var latest = fixture.Read(fresh, nextLife);
            while (unchecked(NetSession.NetFrame - fresh) < 127) fixture.Snapshot();
            Check(fixture.Read(fresh, nextLife) == latest, "actual retained profile survives age127");
            fixture.Snapshot();
            Check(!NetTimedDamageProfile.TryGet(0, nextLife.Generation, nextLife.LifeId, fresh, out _),
                "age128 and actual modulo-ring overwrite retire the old exact frame");
            fixture.Read(NetSession.NetFrame, nextLife);
            Console.WriteLine($"PASS: {_checks} asset-backed authority victim profile history assertions; no gameplay damage or cap proof");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            NetTimedDamageProfile.Reset(); NetTimedShotProof.Mode = oldTimed; NetShotProof.Mode = oldProof;
            NetUnlagged.Enabled = oldUnlagged; NetTestScript.Enabled = oldScript;
        }
    }
}
