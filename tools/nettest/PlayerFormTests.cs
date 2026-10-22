using System;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Real native Weavel models/entities; only the observer clock and identity policy
// are controlled. Every correction enters through PlayerReplicationBridge.
internal static class PlayerFormTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        _checks++; Console.WriteLine("FORM PASS " + name);
    }

    public static int Run(string room)
    {
        _checks = 0;
        if (!ServerSim.Available(out string reason)) { Console.Error.WriteLine(reason); return 1; }
        var sim = new ServerSim();
        if (!sim.Start(room, GameMode.Battle, 3, _ => { }, () => { })) return 1;
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 3;
            for (byte i = 0; i < 3; i++)
            {
                roster.Slots[i] = i; roster.Generations[i] = 1;
                roster.Hunters[i] = (byte)(i == 0 ? Hunter.Samus : Hunter.Weavel);
                roster.Names[i] = "FORM" + i;
            }
            NetSession.ApplyRoster(roster); NetSlotManager.Sync();
            for (int i = 0; i < 120; i++) sim.Step();
            GameState.PointGoal = 10000; GameState.MatchTime = 3600;
            var authority = PlayerEntity.Players[1];
            var observer = PlayerEntity.Players[2];
            Check(authority.Hunter == Hunter.Weavel && observer.Hunter == Hunter.Weavel
                && authority.ModIsInPlay && observer.ModIsInPlay, "asset-backed authority and observer Weavels spawned");

            authority.Health = 199;
            authority.ModStartFormSwitch();
            Check(authority.IsMorphing && authority.Health == 100 && authority.Halfturret.Health == 99,
                "native transition creates detached turret and splits odd health once");
            // Canonical post-damage health is deliberately different from a fresh split.
            authority.Halfturret.Health = 37;
            PlayerState start = Sample(authority);
            Check(start.Form.Phase == FormPhase.Morphing && start.Form.StartedFrame == start.Form.Frame,
                "native capture records the start frame at transition entry");
            for (int i = 0; i < 8; i++) sim.Step();
            PlayerState middle = Sample(authority);
            int nativeMiddle = authority.BipedModel2.AnimInfo.Frame[0];
            Check(authority.IsMorphing && nativeMiddle > 0, "native model supplies a real transition midpoint");
            for (int i = 0; i < 100 && authority.IsMorphing; i++) sim.Step();
            PlayerState complete = Sample(authority);
            Check(authority.IsAltForm && !authority.IsMorphing && complete.Form.Phase == FormPhase.Alt,
                "native transition reaches settled alt form");
            // Leave distinct authority frames for the upper-body movement sample
            // below and the subsequent native exit event.
            for (int i = 0; i < 2; i++) sim.Step();
            authority.ExitAltForm();
            PlayerState exit = Sample(authority);
            for (int i = 0; i < 8; i++) sim.Step();
            PlayerState exitMiddle = Sample(authority);
            int nativeExitMiddle = authority.BipedModel2.AnimInfo.Frame[0];
            for (int i = 0; i < 100 && authority.IsUnmorphing; i++) sim.Step();
            PlayerState biped = Sample(authority);
            Check(exit.Form.Phase == FormPhase.Unmorphing && biped.Form.Phase == FormPhase.Biped,
                "native exit capture distinguishes transition from settled biped");

            var host = new FormHost(observer.SlotIndex);
            var bridge = new PlayerReplicationBridge(host);
            PlayerState ForObserver(PlayerState state)
            {
                state.SlotIndex = (byte)observer.SlotIndex;
                state.SlotGeneration = host.Generation; state.LifeId = host.Life;
                return state;
            }
            void Apply(PlayerState state, bool local = false) => bridge.ApplyState(observer, ForObserver(state), local);
            // A late receipt must not age an authority midpoint to completion.
            host.Clock = middle.Form.Frame + 1000;
            Apply(start);
            var turret = observer.Halfturret;
            Check(observer.IsMorphing && observer.Health == start.Health && turret.Health == 37,
                "observer transition preserves canonical body and turret health without another split");
            Check(observer.Speed == start.Speed, "observer transition start preserves canonical speed without another native pop");
            Apply(start);
            Check(ReferenceEquals(turret, observer.Halfturret) && observer.Health == start.Health && turret.Health == 37,
                "duplicate transition sample neither recreates turret nor splits health");
            Apply(middle);
            Check(observer.IsMorphing && Math.Abs(observer.BipedModel2.AnimInfo.Frame[0] - nativeMiddle) <= 1,
                "delayed observer uses authority transition age and native animation midpoint");
            Check(turret.Position == middle.Form.TurretPosition, "midpoint places turret at canonical detached anchor");
            Apply(complete);
            Check(observer.IsAltForm && !observer.IsMorphing && !observer.IsUnmorphing
                && observer.Health == complete.Health && turret.Health == complete.Form.TurretHealth,
                "completed authority sample settles immediately with canonical health");
            Check(observer.Speed == complete.Speed, "completed form correction preserves canonical speed");

            var moved = complete; moved.Form.Frame += 1; moved.Position += new Vector3(25, 3, 0);
            Apply(moved);
            Check(observer.Position != complete.Position && turret.Position == complete.Form.TurretPosition,
                "moving upper body does not move detached turret anchor");
            Apply(exit);
            Check(observer.IsUnmorphing && observer.Health == exit.Health && !Active(observer) && turret.Health == 0,
                "unmorph reconciles merged canonical body health once and removes turret");
            Apply(exitMiddle);
            Check(observer.IsUnmorphing && Math.Abs(observer.BipedModel2.AnimInfo.Frame[0] - nativeExitMiddle) <= 1,
                "unmorph midpoint uses authority age");
            Apply(biped);
            Check(!observer.IsAltForm && !observer.IsUnmorphing && !observer.IsMorphing && observer.Health == biped.Health,
                "completed biped sample does not replay the exit animation or merge again");

            // New scene observer joining after completion, before any render/step.
            bridge.Reset(); Apply(complete);
            Check(observer.IsAltForm && !observer.IsMorphing && Active(observer)
                && observer.Health == complete.Health && turret.Position == complete.Form.TurretPosition,
                "late join materializes completed form and anchor before first draw");
            Check(observer.Speed == complete.Speed, "late join native form construction preserves canonical speed");
            var missedCycle = complete; missedCycle.Form.StartedFrame = complete.Form.Frame + 2;
            missedCycle.Form.Frame = complete.Form.Frame + 3;
            Apply(missedCycle);
            int ownedTurrets = 0;
            foreach (var entity in observer.OwningScene.Entities)
                if (entity is HalfturretEntity half && ReferenceEquals(half.Owner, observer)) ownedTurrets++;
            Check(ownedTurrets == 1 && ReferenceEquals(turret, observer.Halfturret)
                && Active(observer) && observer.Speed == missedCycle.Speed && observer.Health == missedCycle.Form.BodyHealth,
                "missed unmorph and remorph cycle leaves one turret entity and canonical body motion");

            // Destruction is monotonic within one form episode, including while morphing.
            bridge.Reset(); Apply(start);
            var destroyed = middle; destroyed.Form.Frame += 1;
            destroyed.Form.TurretActive = false; destroyed.Form.TurretHealth = 0;
            destroyed.HalfturretActive = false; destroyed.HalfturretHealth = 0;
            Apply(destroyed);
            Check(observer.IsMorphing && !Active(observer) && turret.Health == 0 && observer.Health == destroyed.Health,
                "canonical turret destruction takes effect during morph without changing body health");
            Check(!turret.Process(), "destroyed native turret cannot continue its target/fire process");
            Apply(start);
            Check(!Active(observer) && turret.Health == 0, "older transition cannot resurrect destroyed turret");
            var staleAlive = destroyed; staleAlive.Form.Frame += 1;
            staleAlive.Form.TurretActive = true; staleAlive.Form.TurretHealth = 37;
            staleAlive.HalfturretActive = true; staleAlive.HalfturretHealth = 37;
            Apply(staleAlive);
            Check(!Active(observer) && turret.Health == 0, "newer sample in destroyed episode cannot resurrect turret");
            var remorph = start; remorph.Form.Frame = biped.Form.Frame + 10;
            remorph.Form.StartedFrame = remorph.Form.Frame;
            var settled = biped; settled.Form.Frame = remorph.Form.Frame - 1;
            Apply(settled); Apply(remorph);
            Check(observer.IsMorphing && Active(observer) && turret.Health == 37 && observer.Health == remorph.Health,
                "newer remorph episode in same player life creates a valid new turret");
            Apply(destroyed);
            Check(Active(observer) && turret.Health == 37 && observer.IsMorphing,
                "old destroyed episode cannot erase newer remorph");

            var dead = remorph; dead.Form.Frame += 1; dead.Health = dead.Form.BodyHealth = 0;
            dead.Form.TurretActive = false; dead.Form.TurretHealth = 0;
            dead.HalfturretActive = false; dead.HalfturretHealth = 0; dead.Flags = 0;
            Apply(dead);
            Check(observer.Health == 0 && !Active(observer) && turret.Health == 0,
                "owner death during morph removes turret through native death path");
            var sameLifeAlive = remorph; sameLifeAlive.Form.Frame += 2; Apply(sameLifeAlive);
            Check(observer.Health == 0 && !Active(observer), "same-life alive snapshot cannot undo confirmed owner death");
            host.Life++; Apply(biped);
            Check(observer.Health == biped.Health && !observer.IsAltForm && !Active(observer),
                "new life respawns biped without old turret");
            var wrongLife = ForObserver(remorph); wrongLife.LifeId--;
            bridge.ApplyState(observer, wrongLife, false);
            var wrongGeneration = ForObserver(remorph); wrongGeneration.SlotGeneration++;
            bridge.ApplyState(observer, wrongGeneration, false);
            Check(!observer.IsAltForm && !observer.IsMorphing && !Active(observer) && observer.Health == biped.Health,
                "old life and wrong generation cannot restore prior form");

            observer.Health = biped.Health; observer.ModStartFormSwitch();
            int predictedBody = observer.Health;
            Apply(biped, local: true);
            Check(observer.IsMorphing && observer.Health == predictedBody,
                "local owner keeps immediate predicted morph and health split against delayed biped sample");
            var localCanonical = remorph; localCanonical.Form.Frame += 10;
            localCanonical.Form.StartedFrame = localCanonical.Form.Frame;
            localCanonical.Health = localCanonical.Form.BodyHealth = 100; localCanonical.Form.TurretHealth = 29;
            localCanonical.HalfturretHealth = 29;
            localCanonical.Form.TurretPosition += new Vector3(2, 0, 3);
            Apply(localCanonical, local: true);
            Check(observer.IsMorphing && observer.Health == 100 && turret.Health == 29
                && turret.Position == localCanonical.Form.TurretPosition,
                "local owner reconciles canonical turret health and anchor while preserving predicted form");
            observer.ModForceForm(true);
            typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, NetSession.NetFrame + 1);
            observer.ExitAltForm();
            int merged = observer.Health;
            Check(merged == 129 && observer.IsUnmorphing && !Active(observer),
                "local native exit merges body and turret health once");
            var delayedAlt = localCanonical; delayedAlt.Form.Frame += 1; delayedAlt.Form.Phase = FormPhase.Alt;
            Apply(delayedAlt, local: true); Apply(delayedAlt, local: true);
            Check(observer.IsUnmorphing && !observer.IsAltForm && !Active(observer) && observer.Health == merged,
                "delayed canonical alt samples cannot undo local unmorph or repeat its health merge");
            var acknowledgedBiped = delayedAlt; acknowledgedBiped.Form.Frame += 1;
            acknowledgedBiped.Form.StartedFrame += 1; acknowledgedBiped.Form.Phase = FormPhase.Biped;
            acknowledgedBiped.Form.TurretActive = false; acknowledgedBiped.Form.TurretHealth = 0;
            acknowledgedBiped.HalfturretActive = false; acknowledgedBiped.HalfturretHealth = 0;
            acknowledgedBiped.Flags = PlayerState.FlagSpawned;
            acknowledgedBiped.Health = acknowledgedBiped.Form.BodyHealth = (ushort)merged;
            observer.ModForceForm(false);
            Apply(acknowledgedBiped, local: true);
            Check(!observer.IsAltForm && !observer.IsUnmorphing && !Active(observer) && observer.Health == merged,
                "new canonical biped episode converges after native exit without another merge");

            LaneArrivalCases(observer, biped, start, exit);
            CombatAcknowledgementCases(observer, PlayerEntity.Players[0], complete);
            Check(sim.StepFailures == 0, "native form fixture has no engine step failures");
            Console.WriteLine($"PASS: {_checks} asset-backed form lifecycle assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FORM FAIL " + ex); return 1; }
        finally { NetHitPrediction.Reset(); sim.Stop(); }
    }

    private static bool Active(PlayerEntity player) => player.Flags2.TestFlag(PlayerFlags2.Halfturret);

    private static void LaneArrivalCases(PlayerEntity observer, PlayerState biped, PlayerState morph, PlayerState exit)
    {
        var host = new FormHost(observer.SlotIndex);
        var bridge = new PlayerReplicationBridge(host);
        biped.Health = biped.Form.BodyHealth = 199;
        biped.Form.Frame = biped.Form.StartedFrame = 1000;
        morph.Health = morph.Form.BodyHealth = 100;
        morph.HalfturretHealth = morph.Form.TurretHealth = 99;
        morph.Form.Frame = morph.Form.StartedFrame = 1001;
        exit.Health = exit.Form.BodyHealth = 199;
        exit.Form.Frame = exit.Form.StartedFrame = 1002;
        void Apply(PlayerState state)
        {
            state.SlotIndex = (byte)observer.SlotIndex;
            state.SlotGeneration = host.Generation; state.LifeId = host.Life;
            bridge.ApplyState(observer, state, isLocal: false);
        }
        foreach (bool fastFirst in new[] { true, false })
        {
            bridge.Reset(); Apply(biped);
            var first = fastFirst ? biped : morph;
            first.Health = fastFirst ? morph.Health : biped.Health;
            Apply(first);
            Check(fastFirst
                ? !observer.IsMorphing && observer.Health == 199 && !Active(observer)
                : observer.IsMorphing && observer.Health == 100 && observer.Halfturret.Health == 99,
                $"{(fastFirst ? "fast" : "slow")}-first morph arrival keeps body health coherent with accepted form");
            Apply(morph);
            Check(observer.IsMorphing && observer.Health == 100 && observer.Halfturret.Health == 99,
                $"{(fastFirst ? "fast" : "slow")}-first morph converges to one native split");
            first = fastFirst ? morph : exit;
            first.Health = fastFirst ? exit.Health : morph.Health;
            Apply(first);
            Check(fastFirst
                ? observer.IsMorphing && observer.Health == 100 && observer.Halfturret.Health == 99
                : observer.IsUnmorphing && observer.Health == 199 && !Active(observer),
                $"{(fastFirst ? "fast" : "slow")}-first unmorph arrival keeps merged health coherent with accepted form");
            Apply(exit);
            Check(observer.IsUnmorphing && observer.Health == 199 && !Active(observer),
                $"{(fastFirst ? "fast" : "slow")}-first unmorph converges without duplicate merge");
        }
        bridge.Reset();
        var initial = morph; initial.Health = 199;
        Apply(initial);
        Check(observer.IsMorphing && observer.Health == 100 && observer.Halfturret.Health == 99,
            "first accepted life materializes slow canonical form health despite different fast health");
        var fastDeath = initial; fastDeath.Health = 0; fastDeath.Flags = 0;
        Apply(fastDeath);
        Check(observer.Health == 0 && !Active(observer),
            "fast zero health replays native death immediately despite retained alive form sample");
        Apply(morph);
        Check(observer.Health == 0 && !Active(observer), "alive form sample cannot revive dead current life");
        host.Life++;
        Apply(biped);
        Check(observer.Health == 199 && !Active(observer) && !observer.IsMorphing,
            "new life resets accepted form frontier and materializes coherent biped health");

        bridge.Reset();
        biped.Form.Frame = biped.Form.StartedFrame = uint.MaxValue - 2;
        morph.Form.Frame = morph.Form.StartedFrame = uint.MaxValue - 1;
        exit.Form.Frame = 1; exit.Form.StartedFrame = 0;
        Apply(biped); Apply(morph); Apply(exit); Apply(morph);
        Check(observer.IsUnmorphing && observer.Health == 199 && !Active(observer),
            "wrapped form frontier accepts new exit and rejects pre-wrap morph health and turret");
    }

    private static void CombatAcknowledgementCases(PlayerEntity victim, PlayerEntity shooter, PlayerState alt)
    {
        var sceneBridge = victim.OwningScene.PlayerReplication;
        NetRole role = NetSession.Role; bool authority = NetSession.IsAuthority; int local = NetSession.LocalSlot;
        void Session(string property, object value) => typeof(NetSession).GetProperty(property)!.SetValue(null, value);
        try
        {
            NetHitPrediction.Reset(); NetHitClaims.Reset(); sceneBridge.Reset();
            alt.SlotIndex = (byte)victim.SlotIndex;
            alt.SlotGeneration = NetPlayerLifecycle.Generation(victim.SlotIndex);
            alt.LifeId = NetPlayerLifecycle.Get(victim.SlotIndex);
            alt.Health = alt.Form.BodyHealth = 100; alt.HalfturretActive = alt.Form.TurretActive = true;
            alt.HalfturretHealth = alt.Form.TurretHealth = 37;
            alt.DamageEventId = alt.Form.DamageEventId = 1;
            alt.Form.Frame = NetSession.NetFrame + 100;
            alt.Form.StartedFrame = alt.Form.Frame - 40;
            NetSession.RemoteStates[victim.SlotIndex] = alt;
            sceneBridge.ApplyState(victim, alt, isLocal: false);
            victim.ModReleaseSpawnProtection();
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(victim, (ushort)0);
            Session(nameof(NetSession.Role), NetRole.Client);
            Session(nameof(NetSession.IsAuthority), false); Session(nameof(NetSession.LocalSlot), shooter.SlotIndex);

            HitClaimPacket Predict()
            {
                long before = NetHitPrediction.Predicted;
                victim.TakeDamage(2, DamageFlags.Halfturret | DamageFlags.NoDmgInvuln, null, shooter);
                byte[] outgoing = new byte[2048];
                Check(NetHitPrediction.Predicted == before + 1 && NetHitClaims.Compose(outgoing) > 0,
                    "native turret hit creates a real pending claim for acknowledgement regression");
                return HitClaimPacket.Read(outgoing.AsSpan(1));
            }
            void Acknowledge(HitClaimPacket claim, ushort sequence, ushort turretHealth)
            {
                var ack = new CombatAckEntry { ClaimId = claim.ClaimId, ShotId = claim.ShotId,
                    VictimSlot = claim.VictimSlot, VictimGeneration = claim.VictimGeneration,
                    VictimLife = claim.VictimLifeId, Result = (byte)CombatAckResult.AlreadyResolved,
                    DamageApplied = 1, HealthAfter = 99, HalfturretHealthAfter = turretHealth,
                    DamageSequence = sequence, Flags = CombatAckFlags.OutcomePresent | CombatAckFlags.HalfturretAffected };
                byte[] verdict = new byte[HitVerdictPacket.HeaderSize + CombatAckEntry.Size];
                HitVerdictPacket.Write(verdict, new[] { ack }, NetSession.CurrentMatchId, NetSession.AuthorityEpoch,
                    NetPlayerLifecycle.Generation(shooter.SlotIndex), NetPlayerLifecycle.Get(shooter.SlotIndex));
                long before = NetHitPrediction.CombatAcks;
                NetHitClaims.ApplyVerdicts(verdict);
                Check(NetHitPrediction.CombatAcks == before + 1,
                    "production verdict consumes the exact pending claim rather than ignoring acknowledgement");
            }

            var claim = Predict(); Acknowledge(claim, 10, 29);
            Check(victim.Halfturret.Health == 29 && Active(victim), "newer real combat acknowledgement corrects turret health");
            var oldHealth = alt; oldHealth.Form.Frame += 1;
            oldHealth.DamageEventId = 10; // A newer fast lane must not give an old slow form lane its sequence.
            oldHealth.Form.DamageEventId = 1;
            NetSession.RemoteStates[victim.SlotIndex] = oldHealth;
            sceneBridge.ApplyState(victim, oldHealth, isLocal: false);
            Check(victim.Halfturret.Health == 29 && Active(victim),
                "new form frame with older form damage sequence preserves newer acknowledgement turret health");
            oldHealth.Form.Frame += 1;
            NetSession.RemoteStates[victim.SlotIndex] = oldHealth;
            sceneBridge.ApplyState(victim, oldHealth, isLocal: false);
            Check(victim.Halfturret.Health == 29 && Active(victim),
                "another delayed form sample preserves turret acknowledgement after fast damage lane catches up");

            var delayedClaim = Predict();
            var destroyed = oldHealth; destroyed.Form.Frame += 1;
            destroyed.Form.TurretActive = destroyed.HalfturretActive = false;
            destroyed.Form.TurretHealth = destroyed.HalfturretHealth = 0;
            destroyed.Form.DamageEventId = destroyed.DamageEventId = 11;
            NetSession.RemoteStates[victim.SlotIndex] = destroyed;
            sceneBridge.ApplyState(victim, destroyed, isLocal: false);
            Check(victim.Health > 0 && !Active(victim) && victim.Halfturret.Health == 0,
                "canonical turret destruction is installed in the production scene bridge");
            Acknowledge(delayedClaim, 12, 25);
            Check(!Active(victim) && victim.Halfturret.Health == 0,
                "late positive acknowledgement cannot resurrect canonically destroyed turret");
            var remorph = alt; remorph.Form.Frame = destroyed.Form.Frame + 2;
            remorph.Form.StartedFrame = destroyed.Form.Frame + 1;
            remorph.Form.Phase = FormPhase.Morphing;
            remorph.Flags = PlayerState.FlagSpawned;
            remorph.Form.DamageEventId = remorph.DamageEventId = 13;
            remorph.Form.TurretHealth = remorph.HalfturretHealth = 22;
            NetSession.RemoteStates[victim.SlotIndex] = remorph;
            sceneBridge.ApplyState(victim, remorph, isLocal: false);
            Check(victim.IsMorphing && Active(victim) && victim.Halfturret.Health == 22,
                "newer canonical remorph permits fresh turret after old positive acknowledgement and destruction");
        }
        finally
        {
            Session(nameof(NetSession.Role), role); Session(nameof(NetSession.IsAuthority), authority);
            Session(nameof(NetSession.LocalSlot), local); NetHitClaims.Reset(); NetHitPrediction.Reset();
        }
    }
    private static PlayerState Sample(PlayerEntity player)
    {
        var form = player.ModCaptureForm(); form.BodyHealth = (ushort)player.Health;
        return new()
        {
            SlotIndex = (byte)player.SlotIndex,
            SlotGeneration = NetPlayerLifecycle.Generation(player.SlotIndex), LifeId = NetPlayerLifecycle.Get(player.SlotIndex),
            Position = player.Position, Facing = Vector3.UnitZ, Speed = player.Speed,
            Health = (ushort)player.Health, CurrentWeapon = (byte)player.CurrentWeapon,
            Flags = (byte)(PlayerState.FlagSpawned | (player.IsAltForm ? PlayerState.FlagAltForm : 0)),
            HalfturretActive = Active(player), HalfturretHealth = (ushort)player.Halfturret.Health,
            Form = form
        };
    }

    private sealed class FormHost : IPlayerReplicationHost
    {
        public FormHost(int slot) => LocalSlot = slot;
        public ushort Generation = 41, Life = 7;
        public uint Clock;
        public bool IsReplica => true;
        public bool Active => true;
        public bool IsAuthority => false;
        public bool IsHost => false;
        public int LocalSlot { get; }
        public uint Frame => Clock;
        public bool Settling => false;
        public bool GameplayReady => true;
        public bool CanSpawn => true;
        public int Ping(int slot) => 100;
        public bool Matches(int slot, ushort generation, ushort life)
            => slot == LocalSlot && generation == Generation && life == Life;
        public bool TryGetIntent(int slot, out IntentPacket intent) { intent = default; return false; }
        public bool TryGetState(int slot, out PlayerState state) { state = default; return false; }
        public uint IntentAge(int slot) => 0;
        public void OnSpawn(PlayerEntity player) { }
        public void BeginLife(PlayerEntity player, in PlayerState state) => NetHitPrediction.NoteRespawn(player.SlotIndex);
        public void Spawn(PlayerEntity player, in PlayerState state)
            => player.Spawn(state.Position, state.Facing, Vector3.UnitY, player.NodeRef, respawn: true);
        public void ReplayDamage(PlayerEntity player, in PlayerState state) { }
        public void ReplayDeath(PlayerEntity player) => NetDamage.ReplayDeath(player);
        public void NoteDeath(int slot) => NetHitPrediction.NoteDeath(slot);
        public int HealthFor(PlayerEntity player, int health, bool local) => health;
        public bool SamplePosition(int slot, bool presentation, out Vector3 position, out bool alt)
        { position = default; alt = false; return false; }
        public void StampAcknowledgement(ref IntentPacket intent) { }
    }
}
