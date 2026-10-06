using System.Linq;
using System;
using System.Reflection;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    // Asset-backed checks: actual controls, weapon spawn, damage and respawn in the headless engine.
    public static class NetCombatCheck
    {
        private static int _checks;
        private static void Check(bool ok, string name)
        {
            _checks++;
            if (!ok) throw new InvalidOperationException(name);
            Console.WriteLine($"COMBAT PASS {name}");
        }
        public static int Run(string room)
        {
            if (!ServerSim.Available(out string reason)) { Console.WriteLine(reason); return 1; }
            var sim = new ServerSim();
            if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
            try
            {
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, Mode = (byte)GameMode.Battle }, false);
                var roster = RosterPacket.Create();
                roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
                for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Names[i] = $"CHECK{i}"; }
                NetSession.ApplyRoster(roster);
                for (int i = 0; i < 120; i++) sim.Step();
                var shooter = PlayerEntity.Players[0]; var victim = PlayerEntity.Players[1];
                Check(shooter.ModIsInPlay && victim.ModIsInPlay && sim.StepFailures == 0, "spawn both players");
                var scene = (Scene)typeof(ServerSim).GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sim)!;
                var fire = typeof(PlayerEntity).GetMethod("TryFireWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!;
                InvalidHitClaimIsRefused();
                InvulnerableClaimIsRefused();
                MutualKillOrdering();
                ClaimArbitrationHasDeadline();
                ContinuousPhaseAgreesAcrossPeers();
                RecoveredShotIdentity();
                HistoricalCacheEquivalence();
                GameState.PointGoal = 1000; GameState.MatchTime = 3600;
                Array.Clear(GameState.Points);
                shooter.Health = victim.Health = 99;
                uint frame = 200;
                foreach (BeamType weapon in new[] { BeamType.PowerBeam, BeamType.Missile, BeamType.Imperialist,
                    BeamType.Magmaul, BeamType.ShockCoil, BeamType.Judicator, BeamType.Battlehammer, BeamType.VoltDriver, BeamType.OmegaCannon })
                {
                    shooter.ModArmWeapon(weapon);
                    int before = NetDamage.Fired[0];
                    for (int i = 0; i < 30; i++)
                    {
                        NetSession.AcceptSlotIntent(0, Intent(shooter, ++frame, playing: false, shoot: true));
                        sim.Step();
                    }
                    Check(shooter.ModIsInPlay && NetDamage.Fired[0] == before, $"DeadHeldFireDoesNotSpawnGhostShot/{weapon} hp={shooter.Health} load={shooter.LoadFlags} fired={before}->{NetDamage.Fired[0]} match={GameState.MatchState}");
                    NetSession.AcceptSlotIntent(0, Intent(shooter, ++frame, playing: true, shoot: false));
                    sim.Step();
                }
                shooter.ModArmWeapon(BeamType.Missile);
                for (int i = 0; i < 40; i++) sim.Step();
                Array.Clear((int[])typeof(PlayerEntity).GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shooter)!);
                typeof(PlayerEntity).GetField("_timeSinceShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shooter, (ushort)1000);
                shooter.Controls.Shoot.IsDown = shooter.Controls.Shoot.IsPressed = true;
                int fired = NetDamage.Fired[0];
                Check(!(bool)fire.Invoke(shooter, null)! && NetDamage.Fired[0] == fired
                    && NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo] > 0,
                    "FiredCounterRequiresActualSpawn/empty missile");
                shooter.ModArmWeapon(BeamType.PowerBeam);
                var fixtureAmmo = (int[])typeof(PlayerEntity).GetField("_ammo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shooter)!;
                fixtureAmmo[0] = 400; fixtureAmmo[1] = 50;
                typeof(PlayerEntity).GetField("_timeSinceShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shooter, (ushort)1000);
                int reportShots = scene.GameState.ShotsFired[0];
                int semanticShots = scene.MatchEvents.Bus.Events.Count(e => e.Type == Mods.MatchEvents.MatchSemanticEventType.WeaponFired);
                Check((bool)fire.Invoke(shooter, null)! && scene.GameState.ShotsFired[0] == reportShots + 1
                    && scene.MatchEvents.Bus.Events.Count(e => e.Type == Mods.MatchEvents.MatchSemanticEventType.WeaponFired) == semanticShots + 1,
                    "post-match fired count consumes exactly one canonical successful trigger");
                // Spawn with the production weapon table, then cross the actual Spawn method.
                foreach (var shot in new[] { (BeamType.Missile, false), (BeamType.Missile, true), (BeamType.Magmaul, false), (BeamType.Judicator, false) })
                {
                    BeamType weapon = shot.Item1;
                    shooter.ModArmWeapon(weapon);
                    shooter.EquipInfo.ChargeLevel = shot.Item2 ? (ushort)(shooter.EquipInfo.Weapon.FullCharge * 2) : (ushort)0;
                    NetFireEvents.Begin(shooter);
                    NetUnlagged.BeginShot(shooter);
                    var result = BeamProjectileEntity.Spawn(shooter, shooter.EquipInfo, shooter.Position + Vector3.UnitY,
                        Vector3.UnitY, BeamSpawnFlags.NoMuzzle, shooter.NodeRef, scene);
                    NetUnlagged.EndShot(shooter);
                    BeamProjectileEntity? launched = null;
                    foreach (var beam in shooter.EquipInfo.Beams)
                        if (beam.Owner == shooter && beam.Lifespan > 0 && beam.Beam == weapon) launched = beam;
                    Check(result != BeamResultFlags.NoSpawn && launched != null, $"production projectile/{weapon}");
                    typeof(NetHitClaims).GetMethod("NoteRescued", BindingFlags.NonPublic | BindingFlags.Static)!
                        .Invoke(null, new object[] { 0, 1, launched!.ModShotId });
                    if (shot.Item2) Check(launched!.Flags.TestFlag(BeamFlags.Homing), "charged missile uses homing flight");
                    shooter.Health = 0;
                    Check(NetPlayerLifecycle.CurrentProjectile(launched!), $"ProjectileLifecycleAcrossShooterDeath/{weapon}");
                    ushort life = NetPlayerLifecycle.Get(0);
                    shooter.Spawn(shooter.Position, Vector3.UnitZ, Vector3.UnitY, shooter.NodeRef, respawn: true);
                    Check(NetPlayerLifecycle.Get(0) != life, "actual spawn allocates new life");
                    Console.WriteLine($"COMBAT projectile {weapon} lifespan={launched!.Lifespan:F2}s survivesSpawn={launched.Lifespan > 0} valid={NetPlayerLifecycle.CurrentProjectile(launched)}");
                    Check(launched.Lifespan > 0 && NetPlayerLifecycle.CurrentProjectile(launched), $"ProjectileLifecycleAcrossShooterRespawn/{weapon}");
                    Check(NetHitClaims.AlreadyRescued(0, 1, launched.ModShotId, launched.ModLaunchKey)
                        && !NetHitClaims.AlreadyRescued(0, 1, launched.ModShotId, launched.ModLaunchKey),
                        $"rescued flight cannot pay twice after respawn/{weapon}");
                    victim.Health = 99;
                    victim.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, launched);
                    Check(victim.Health < 99, $"old launch still damages target/{weapon}");
                    ushort savedLife = launched.ModLaunchLife;
                    launched.ModLaunchLife++;
                    Check(!NetPlayerLifecycle.CurrentProjectile(launched), "forged launch life rejected");
                    launched.ModLaunchLife = savedLife;
                    var childKey = launched.ModLaunchKey;
                    var child = BeamProjectileEntity.Spawn(shooter, shooter.EquipInfo, shooter.Position + Vector3.UnitY,
                        Vector3.UnitY, BeamSpawnFlags.NoMuzzle, shooter.NodeRef, scene, parent: launched);
                    bool inherited = false;
                    foreach (var beam in shooter.EquipInfo.Beams)
                        if (beam != launched && beam.Lifespan > 0 && beam.ModLaunchKey == childKey) inherited = true;
                    Check(child != BeamResultFlags.NoSpawn && inherited, $"ricochet child keeps original fire event/{weapon}");
                }
                Check(sim.StepFailures == 0, "no simulation failures");
                Console.WriteLine($"COMBAT PASS {_checks} assertions");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine($"COMBAT FAIL {ex}"); return 1; }
            finally { sim.Stop(); }
        }
        private static IntentPacket Intent(PlayerEntity player, uint frame, bool playing, bool shoot) => new()
        {
            MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            SlotGeneration = NetPlayerLifecycle.Generation(player.SlotIndex), LifeId = NetPlayerLifecycle.Get(player.SlotIndex),
            Frame = frame, AckFrame = NetSession.NetFrame, Aim = Vector3.UnitZ, Position = player.Position,
            WeaponSelect = 255, AmmoUa = 400, AmmoMissiles = 50,
            Buttons = (playing ? IntentButtons.InPlayState : 0) | (shoot ? IntentButtons.Shoot : 0),
            Presses = new InputEdgeHistory()
        };

        private static void PrepareClaims()
        {
            NetHitClaims.Reset();
            foreach (var player in PlayerEntity.Players)
            {
                if (player.SlotIndex > 1) continue;
                player.Spawn(player.Position, Vector3.UnitZ, Vector3.UnitY, player.NodeRef, respawn: true);
                typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(player, (ushort)0);
                player.Health = 1;
            }
            NetHitClaims.Tick();
            NetUnlagged.Record(NetSession.NetFrame - 2);
            NetUnlagged.Record(NetSession.NetFrame - 1);
            NetUnlagged.Record(NetSession.NetFrame);
        }
        private static HitClaimPacket Claim(int shooter, uint world) => new()
        {
            MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
            ShooterGeneration = NetPlayerLifecycle.Generation(shooter), ShooterLifeId = NetPlayerLifecycle.Get(shooter),
            VictimSlot = (byte)(1 - shooter), VictimGeneration = NetPlayerLifecycle.Generation(1 - shooter),
            VictimLifeId = NetPlayerLifecycle.Get(1 - shooter), ClaimId = 1, AckFrame = world, LaunchFrame = world,
            Damage = 1, Beam = (byte)BeamType.Imperialist, HitPoint = PlayerEntity.Players[1 - shooter].Position
        };
        private static void Receive(int shooter, in HitClaimPacket claim)
        {
            byte[] bytes = new byte[1 + HitClaimPacket.Size]; bytes[0] = 1; claim.Write(bytes.AsSpan(1));
            NetHitClaims.Receive(shooter, bytes, requireAttackEvidence: false);
        }
        private static void InvalidHitClaimIsRefused()
        {
            PrepareClaims();
            var judge = typeof(NetHitClaims).GetMethod("Judge", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (float offset in new[] { .5f, 1f, 1.5f, 2f, 2.5f, 4f })
            {
                var claim = Claim(0, NetSession.NetFrame - 1); claim.HitPoint += Vector3.UnitX * offset;
                byte result = (byte)judge.Invoke(null, new object[] { 0, claim, false })!;
                Check(offset <= 2 ? result == HitVerdictPacket.ResultApplied : result != HitVerdictPacket.ResultApplied,
                    $"InvalidHitClaimIsRefused/offset={offset} result={HitVerdictPacket.Describe(result)}");
            }
            var impulse = Claim(0, NetSession.NetFrame - 1);
            impulse.Beam = (byte)BeamType.Missile;
            impulse.Direction = new Vector3(.3f, .03f, 0);
            Check((byte)judge.Invoke(null, new object[] { 0, impulse, false })! == HitVerdictPacket.ResultApplied,
                "InvalidHitClaimIsRefused/valid missile impulse");
            impulse.Direction = new Vector3(2f, 0, 0);
            Check((byte)judge.Invoke(null, new object[] { 0, impulse, false })! == HitVerdictPacket.ResultImpulseLimit,
                "InvalidHitClaimIsRefused/forged impulse rejected");

            MethodInfo validImpulse = typeof(NetHitClaims).GetMethod("ValidClaimImpulse",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Check((bool)validImpulse.Invoke(null, new object[]
                { (byte)BeamType.Battlehammer, Hunter.Weavel, new Vector3(.49f, 0, 0) })!,
                "InvalidHitClaimIsRefused/affinity Battlehammer impulse accepted");
            Check(!(bool)validImpulse.Invoke(null, new object[]
                { (byte)BeamType.Battlehammer, Hunter.Samus, new Vector3(.53f, 0, 0) })!,
                "InvalidHitClaimIsRefused/Battlehammer impulse above native airburst admission ceiling rejected");
        }
        private static void MutualKillOrdering()
        {
            foreach (int earlier in new[] { -1, 0, 1 })
            foreach (bool reverse in new[] { false, true })
            foreach (int arrivalGap in new[] { 0, 1, 8 })
            {
                PrepareClaims();
                var semantic = PlayerEntity.Players[0].OwningScene.MatchEvents.Bus;
                int killsBefore = semantic.Events.Count(e => e.Type == Mods.MatchEvents.MatchSemanticEventType.PlayerKilled);
                uint world = NetSession.NetFrame - 1;
                var a = Claim(0, earlier == 0 ? world - 1 : world);
                var b = Claim(1, earlier == 1 ? world - 1 : world);
                if (reverse) Receive(1, b); else Receive(0, a);
                for (int i = 0; i < arrivalGap; i++) { NetSession.Update(NetSession.NetFrame / 60.0); NetHitClaims.Tick(); }
                if (reverse) Receive(0, a); else Receive(1, b);
                for (int i = 0; i < NetHitClaims.MaxGraceFrames + 2; i++)
                { NetSession.Update(NetSession.NetFrame / 60.0); NetHitClaims.Tick(); }
                bool aDead = PlayerEntity.Players[0].Health == 0, bDead = PlayerEntity.Players[1].Health == 0;
                Check(earlier == -1 ? aDead && bDead : earlier == 0 ? !aDead && bDead : aDead && !bDead,
                    $"MutualKillOrdering/earlier={earlier} reversed={reverse} arrivalGap={arrivalGap} ADead={aDead} BDead={bDead}");
                int canonicalDeaths = semantic.Events.Count(e => e.Type == Mods.MatchEvents.MatchSemanticEventType.PlayerKilled) - killsBefore;
                Check(canonicalDeaths == (aDead ? 1 : 0) + (bDead ? 1 : 0),
                    $"CanonicalClaimDeaths/earlier={earlier} reversed={reverse} arrivalGap={arrivalGap}");
                Receive(0, a); Receive(1, b); NetHitClaims.Tick();
                Check(semantic.Events.Count(e => e.Type == Mods.MatchEvents.MatchSemanticEventType.PlayerKilled) - killsBefore == canonicalDeaths,
                    "duplicate claims do not duplicate canonical deaths");
            }
            PrepareClaims();
        }

        private static void InvulnerableClaimIsRefused()
        {
            PrepareClaims();
            var victim = PlayerEntity.Players[1];
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(victim, (ushort)1000);
            var claim = Claim(0, NetSession.NetFrame - 1);
            Receive(0, claim);
            for (int i = 0; i < NetHitClaims.MaxGraceFrames + 2; i++)
            { NetSession.Update(NetSession.NetFrame / 60.0); NetHitClaims.Tick(); }
            Check(victim.Health == 1 && NetHitClaims.AppliedHere == 0
                && !NetHitClaims.AlreadyRescued(0, 1, claim.LaunchFrame),
                "invulnerable target cannot be reported or prepaid as rescued damage");
            int bucket = NetShotDiagnostics.Bucket(BeamType.Imperialist);
            long refused = NetShotDiagnostics.Refusals[bucket], declared = NetShotDiagnostics.Claims[bucket];
            Receive(0, claim); NetHitClaims.Tick();
            Check(NetShotDiagnostics.Refusals[bucket] == refused && NetShotDiagnostics.Claims[bucket] == declared,
                "repeated claim does not duplicate per-weapon outcome counters");
            PrepareClaims();
        }

        private static void ClaimArbitrationHasDeadline()
        {
            PrepareClaims();
            var victim = PlayerEntity.Players[1];
            typeof(PlayerEntity).GetField("_spawnInvulnTimer", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(victim, (ushort)1000);
            uint firstWorld = NetSession.NetFrame - 1;
            byte verdict = 255;
            var previousSink = NetHitClaims.VerdictSink;
            NetHitClaims.VerdictSink = (int slot, ReadOnlySpan<(ushort Id, byte Result)> entries) =>
            {
                foreach (var entry in entries) if (slot == 0 && entry.Id == 1) verdict = entry.Result;
            };
            try
            {
                Receive(0, Claim(0, firstWorld));
                // Keep delivering earlier shots before the preceding one has
                // finished grace. None can kill; they must not starve id 1.
                for (uint tick = 1; tick <= 2 * NetHitClaims.MaxGraceFrames + 1; tick++)
                {
                    NetSession.Update(NetSession.NetFrame / 60.0);
                    if (tick % 8 == 0)
                    {
                        NetUnlagged.Record(NetSession.NetFrame);
                        var claim = Claim(0, NetSession.NetFrame);
                        claim.ClaimId = (ushort)(tick + 1); claim.LaunchFrame = firstWorld - tick;
                        Receive(0, claim);
                    }
                    NetHitClaims.Tick();
                }
                Check(verdict == HitVerdictPacket.ResultNoDamage,
                    "earlier claim stream cannot starve a verdict past the arbitration deadline");
            }
            finally { NetHitClaims.VerdictSink = previousSink; PrepareClaims(); }
        }


        private static void HistoricalCacheEquivalence()
        {
            var random = new Random(771);
            var player = PlayerEntity.Players[1];
            Vector3 original = player.Position;
            var begin = typeof(NetUnlagged).GetMethod("BeginCollisionCache", BindingFlags.Static | BindingFlags.NonPublic)!;
            var raw = typeof(NetUnlagged).GetMethod("BuildHistoricalPose", BindingFlags.Static | BindingFlags.NonPublic)!;
            var body = typeof(NetHistoricalTrace).GetMethod("BuildBody", BindingFlags.Static | BindingFlags.NonPublic)!;
            try
            {
                for (uint frame = 1; frame <= 300; frame++)
                {
                    player.Position = original + new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle());
                    NetUnlagged.Record(frame);
                }
                begin.Invoke(null, null);
                for (int i = 0; i < 1000; i++)
                {
                    double target = 175 + random.Next(124) + random.NextDouble();
                    object[] args = { player, target, default(HistoricalPlayerPose) };
                    bool expected = (bool)raw.Invoke(null, args)!;
                    bool valid = NetUnlagged.TryHistoricalPose(player, target, out var pose);
                    if (valid != expected || pose != (HistoricalPlayerPose)args[2])
                        throw new InvalidOperationException("cached pose differs from uncached history");
                    if (valid)
                    {
                        var expectedBody = (HistoricalBody)body.Invoke(null, new object[] { player, pose })!;
                        if (NetHistoricalTrace.Body(player, pose) != expectedBody
                            || NetHistoricalTrace.Body(player, pose) != expectedBody)
                            throw new InvalidOperationException("cached collision body differs from uncached body");
                    }
                }
                Check(true, "randomized fractional historical collision cache equals uncached history after ring wrap");
            }
            finally { NetUnlagged.AbortShot(); player.Position = original; PrepareClaims(); }
        }

        private static void RecoveredShotIdentity()
        {
            PrepareClaims(); NetFireEvents.Reset();
            var shooter = PlayerEntity.Players[0]; var victim = PlayerEntity.Players[1];
            shooter.Health = victim.Health = 999; shooter.ModArmWeapon(BeamType.Missile);
            uint now = NetSession.NetFrame, sourceFrame = 100;
            var carrier = new IntentPacket { Frame = 103, AckFrame = now, HasFireEvents = true,
                Position = shooter.Position, Aim = Vector3.UnitY, WeaponSelect = (byte)BeamType.Missile };
            var fire = typeof(PlayerEntity).GetMethod("TryFireWeapon", BindingFlags.Instance | BindingFlags.NonPublic)!;
            FireEvent Event(uint id, FireEventKind kind, BeamType beam, byte charge = 0, uint scope = 0,
                uint? launch = null) => new(id, sourceFrame, launch ?? NetSession.NetFrame - 2, 128, kind,
                    (byte)beam, charge, scope, FireEvent.FlagPose, shooter.Position + Vector3.UnitY,
                    Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, default,
                    shooter.Position, Vector3.UnitY, FireEvent.FlagSourcePose);
            void Advance(int frames)
            {
                sourceFrame += (uint)frames; now += (uint)frames;
                typeof(NetSession).GetProperty(nameof(NetSession.NetFrame))!.SetValue(null, now);
                for (uint at = now - 2; at <= now; at++) NetUnlagged.Record(at);
                carrier.Frame = sourceFrame + 3; carrier.AckFrame = now;
            }
            void Admit(FireEvent authored)
            {
                carrier.WeaponSelect = authored.Weapon; carrier.FireEventCount = 1; carrier.FireEvents[0] = authored;
                NetSession.RemoteIntents[0] = carrier; NetSession.RemoteIntentValid[0] = true;
                NetAcceptedAttacks.AcceptIntent(0, carrier);
                Check(NetAcceptedAttacks.Authorized(0, authored.ShotId), "retained source event has legal authority resource/cadence evidence");
                NetFireEvents.Prepare(shooter, carrier);
            }
            BeamProjectileEntity? Launched(uint id)
            { foreach (var candidate in shooter.EquipInfo.Beams) if (candidate.ModShotId == id) return candidate; return null; }
            void Spawn(string label)
            {
                typeof(PlayerEntity).GetField("_timeSinceShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shooter, (ushort)1000);
                Check((bool)fire.Invoke(shooter, null)!, label);
            }
            try
            {
                FireEvent source = Event(41, FireEventKind.PressFire, BeamType.Missile);
                Admit(source);
                object[] timing = { shooter.SlotIndex, true, 0, 0.0 };
                typeof(NetUnlagged).GetMethod("RewindFor", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, timing);
                Check((double)timing[3] > 0, "recovered event ACK is selected before validating a newer carrier ACK");
                Spawn("recovered press spawns from retained fire event");
                var beam = Launched(41);
                Check(beam != null && beam.ModLaunchFrame == source.AckFrame && beam.ModLaunchKey.ShotId == 41,
                    "non-1:1 carrier ACK retains original ShotId and launch clock");
                int before = victim.Health;
                victim.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
                Check(victim.Health == before - 1, "authority applies recovered physical hit once");
                var claim = Claim(0, now); claim.Beam = (byte)BeamType.Missile; claim.ShotId = 41; claim.LaunchFrame = source.AckFrame;
                Receive(0, claim); NetHitClaims.Tick();
                Check(victim.Health == before - 1 && NetHitClaims.DuplicateHere == 1 && NetHitClaims.AppliedHere == 0,
                    "recovered shot double-hit regression: later claim never rescues duplicate damage");
                NetFireEvents.Prepare(shooter, carrier);
                Check(!NetFireEvents.CanFire(shooter), "repeated or reordered carrier cannot fire twice");
                Advance(shooter.EquipInfo.Weapon.ShotCooldown * 2);
                Admit(Event(42, FireEventKind.PressFire, BeamType.Missile, launch: source.AckFrame));
                Spawn("second legally spaced shot with same launch clock spawns independently");
                beam = Launched(42); victim.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
                claim.ClaimId = 2; claim.ShotId = 42;
                Receive(0, claim); NetHitClaims.Tick();
                Check(victim.Health == before - 2 && NetHitClaims.DuplicateHere == 2 && NetHitClaims.AppliedHere == 0,
                    "distinct ShotIds at the same launch frame both pay exactly once");
                NetHitClaims.ValidateLedgerCounters();
                shooter.ModArmWeapon(BeamType.Imperialist); shooter.ModSetZoom(false);
                Advance(shooter.EquipInfo.Weapon.ShotCooldown * 2);
                carrier.Buttons &= ~IntentButtons.ZoomedState;
                Admit(Event(43, FireEventKind.PressFire, BeamType.Imperialist, scope: FireEvent.ScopedStateBit));
                Check(!shooter.EquipInfo.Zoomed, "recovered Imperialist quick-scope leaves newer carrier zoom state intact");
                Spawn("recovered scoped Imperialist quick-scope actually spawns");
                var scopedBeam = Launched(43);
                Check(scopedBeam != null && scopedBeam.HeadshotDamage == shooter.EquipInfo.HeadshotDamage,
                    "recovered scoped Imperialist keeps full headshot damage");
                NetFireEvents.Prepare(shooter, carrier);
                Check(!NetFireEvents.CanFire(shooter), "repeated scoped Imperialist carrier cannot fire twice");
                uint id = 44;
                foreach (var kind in new[] { FireEventKind.ReleaseFire, FireEventKind.AutomaticFire })
                {
                    shooter.ModArmWeapon(BeamType.PowerBeam);
                    int delay = Math.Max(shooter.EquipInfo.Weapon.ShotCooldown, shooter.EquipInfo.Weapon.AutofireCooldown) * 2;
                    Advance(delay);
                    byte charge = 0;
                    if (kind == FireEventKind.ReleaseFire)
                    {
                        carrier.WeaponSelect = (byte)BeamType.PowerBeam; carrier.Buttons |= IntentButtons.Shoot;
                        carrier.FireEventCount = 0; NetAcceptedAttacks.AcceptIntent(0, carrier);
                        charge = (byte)(shooter.EquipInfo.Weapon.FullCharge * 2); Advance(charge);
                    }
                    Admit(Event(id, kind, BeamType.PowerBeam, charge));
                    Check(shooter.EquipInfo.ChargeLevel == charge && (kind != FireEventKind.ReleaseFire || shooter.Controls.Shoot.IsReleased),
                        $"lost {kind} retains observed charge and control edge");
                    Spawn($"retained {kind} actually spawns");
                    Check(Launched(id) != null, $"retained {kind} carries independent shot identity");
                    NetFireEvents.Prepare(shooter, carrier);
                    Check(!NetFireEvents.CanFire(shooter), $"repeated {kind} does not spawn twice"); id++;
                }
            }
            finally { NetSession.RemoteIntentValid[0] = false; NetFireEvents.Reset(); PrepareClaims(); }
        }

        private static void ContinuousPhaseAgreesAcrossPeers()
        {
            var dedicatedPhase = new ContinuousWeaponPhase(1);
            Check(dedicatedPhase.Resolve(0, 1, true, false, 9000, true, 100, 1, 0,
                out _, out _, receivedBeforeStep: true) == 100,
                "dedicated input arriving before step retains source phase");
            // Independent golden phases preserve the original 30 Hz fractional
            // damage cadence, including the exact-boundary rounding difference.
            foreach (var golden in new[] {
                (10, new[] { 6, 12, 18, 24, 30, 38, 44, 50, 56, 62 }),
                (15, new[] { 4, 8, 12, 16, 20, 24, 28, 34, 38, 42, 46, 50, 54, 58, 62 }) })
            {
                bool exact = true;
                for (int phase = 0; phase < 64; phase++)
                    exact &= ContinuousWeaponPhase.Amount(golden.Item1, (ulong)phase, true)
                        == (Array.IndexOf(golden.Item2, phase) >= 0 ? 1 : 0);
                Check(exact, $"continuous golden damage cadence/{golden.Item1}");
            }
            foreach (int damage in new[] { 10, 15, 32, 47, 64 })
            foreach (int phaseOffset in new[] { 0, 1, 5, 19 })
            {
                var owner = new ContinuousWeaponPhase(2); var authority = new ContinuousWeaponPhase(2);
                var observer = new ContinuousWeaponPhase(2);
                bool agrees = true;
                for (uint tick = 1; tick <= 192; tick++)
                {
                    uint logical = 100 + (uint)phaseOffset + tick;
                    ulong a = owner.Resolve(0, tick, true, true, logical, true, 0, 0, 0, out _, out _);
                    // Jitter changes the latest packet without re-anchoring a held stream.
                    uint age = tick % 6;
                    ulong b = authority.Resolve(0, tick + 700, true, false, 9000, true,
                        logical - age, age, 0, out _, out _);
                    ulong c = observer.Resolve(0, tick + 1300, true, false, 5000, true,
                        logical, 0, 0, out _, out _);
                    agrees &= a == b && b == c && ContinuousWeaponPhase.Amount(damage, a, true) == ContinuousWeaponPhase.Amount(damage, b, true)
                        && ContinuousWeaponPhase.Amount(damage, a, false) == ContinuousWeaponPhase.Amount(damage, c, false);
                }
                Check(agrees, $"ContinuousPhaseAgreesAcrossPeers/damage={damage} initialOffset={phaseOffset}");
            }
        }
    }
}
