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
                var initialPosition = shooter.Position;
                var initialFacing = shooter.FacingVector;
                var initialNode = shooter.NodeRef;
                InvalidHitClaimIsRefused();
                InvulnerableClaimIsRefused();
                MutualKillOrdering();
                ClaimArbitrationHasDeadline();
                ContinuousPhaseAgreesAcrossPeers();
                RecoveredShotIdentity();
                HistoricalCacheEquivalence();
                GameState.PointGoal = 1000; GameState.MatchTime = 3600;
                Array.Clear(GameState.Points);
                // Recovery/history fixtures move the shooter above the room. Restore its
                // settled pose before testing held input so falling cannot kill it.
                shooter.Spawn(initialPosition, initialFacing, Vector3.UnitY, initialNode, respawn: true);
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
                shooter.ModSetAmmo(0, 0);
                typeof(PlayerEntity).GetField("_timeSinceShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shooter, (ushort)1000);
                shooter.Controls.Shoot.IsDown = shooter.Controls.Shoot.IsPressed = true;
                int fired = NetDamage.Fired[0];
                Check(!(bool)fire.Invoke(shooter, null)! && NetDamage.Fired[0] == fired
                    && NetShotDiagnostics.Outcomes[(int)BeamType.Missile, (int)ShotAttemptResult.NoAmmo] > 0,
                    "FiredCounterRequiresActualSpawn/empty missile");
                shooter.ModArmWeapon(BeamType.PowerBeam);
                shooter.ModSetAmmo(400, 50);
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
            NetHitClaims.Receive(shooter, bytes);
        }
        private static void InvalidHitClaimIsRefused()
        {
            PrepareClaims();
            var judge = typeof(NetHitClaims).GetMethod("Judge", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (float offset in new[] { .5f, 1f, 1.5f, 2f, 2.5f, 4f })
            {
                var claim = Claim(0, NetSession.NetFrame - 1); claim.HitPoint += Vector3.UnitX * offset;
                byte result = (byte)judge.Invoke(null, new object[] { 0, claim })!;
                Check(offset <= 2 ? result == HitVerdictPacket.ResultApplied : result != HitVerdictPacket.ResultApplied,
                    $"InvalidHitClaimIsRefused/offset={offset} result={HitVerdictPacket.Describe(result)}");
            }
            var impulse = Claim(0, NetSession.NetFrame - 1);
            impulse.Beam = (byte)BeamType.Missile;
            impulse.Direction = new Vector3(.3f, .03f, 0);
            Check((byte)judge.Invoke(null, new object[] { 0, impulse })! == HitVerdictPacket.ResultApplied,
                "InvalidHitClaimIsRefused/valid missile impulse");
            impulse.Direction = new Vector3(2f, 0, 0);
            Check((byte)judge.Invoke(null, new object[] { 0, impulse })! == HitVerdictPacket.ResultImpulseLimit,
                "InvalidHitClaimIsRefused/forged impulse rejected");

            MethodInfo validImpulse = typeof(NetHitClaims).GetMethod("ValidClaimImpulse",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Check((bool)validImpulse.Invoke(null, new object[]
                { (byte)BeamType.Battlehammer, Hunter.Weavel, new Vector3(.49f, 0, 0) })!,
                "InvalidHitClaimIsRefused/affinity Battlehammer impulse accepted");
            // TryFireWeapon selects the +9 production row for every Prime
            // Hunter, including Samus. Hunter identity alone cannot prove that
            // an affinity-sized Battlehammer impulse was forged.
            var primeWeapon = PlayerEntity.Players[0].OwningScene.WeaponRules[(int)BeamType.Battlehammer + 9];
            float primeImpulse = primeWeapon.UnchargedDmgDirMag / 4096f;
            Check(primeImpulse == .5f && (bool)validImpulse.Invoke(null, new object[]
                { (byte)BeamType.Battlehammer, Hunter.Samus, new Vector3(primeImpulse, 0, 0) })!,
                "InvalidHitClaimIsRefused/non-affinity Prime Hunter production impulse accepted");
            foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Weavel })
            {
                Check((bool)validImpulse.Invoke(null, new object[]
                    { (byte)BeamType.Battlehammer, hunter, new Vector3(.526f, 0, 0) })!,
                    $"InvalidHitClaimIsRefused/Battlehammer rescue ceiling accepted/{hunter}");
                foreach (Vector3 direction in new[] { new Vector3(.527f, 0, 0), new Vector3(.4f, .4f, 0),
                    new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0) })
                {
                    Check(!(bool)validImpulse.Invoke(null, new object[]
                        { (byte)BeamType.Battlehammer, hunter, direction })!,
                        $"InvalidHitClaimIsRefused/oversized or nonfinite Battlehammer impulse rejected/{hunter}/{direction}");
                }
            }
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
            uint now = NetSession.NetFrame;
            bool savedPlayback = NetFireEvents.AcceptedPlaybackEnabled;
            NetFireEvents.AcceptedPlaybackEnabled = true;
            var cooldown = typeof(PlayerEntity).GetField("_timeSinceShot", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ushort sourceLife = NetPlayerLifecycle.Get(0);
            FireEvent Event(uint id, BeamType weapon, FireEventKind kind = FireEventKind.PressFire,
                byte charge = 0, uint state = 0) => new(id, now - 2, now - 2, 128, kind,
                    (byte)weapon, charge, state, FireEvent.FlagPose,
                    shooter.Position + Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY,
                    SourceLife: sourceLife);
            IntentPacket Carrier(params FireEvent[] events)
            {
                var packet = Intent(shooter, now, playing: false, shoot: false);
                packet.HasFireEvents = true; packet.FireEventCount = (byte)events.Length;
                for (int i = 0; i < events.Length; i++) packet.FireEvents[i] = events[i];
                return packet;
            }
            void PresentState()
            {
                shooter.ModArmWeapon(BeamType.PowerBeam); shooter.ModSetAmmo(0, 0);
                shooter.ModSetZoom(false); shooter.EquipInfo.ChargeLevel = 0;
                shooter.Controls.ClearAll(); shooter.Health = 0; cooldown.SetValue(shooter, (ushort)0);
            }
            void Unchanged()
            {
                Check(shooter.Health == 0 && shooter.CurrentWeapon == BeamType.PowerBeam
                    && shooter.EquipInfo.Ammo == 0 && shooter.EquipInfo.ChargeLevel == 0
                    && !shooter.EquipInfo.Zoomed && (ushort)cooldown.GetValue(shooter)! == 0
                    && !shooter.Controls.Shoot.IsDown && !shooter.Controls.Shoot.IsPressed
                    && !shooter.Controls.Shoot.IsReleased,
                    "accepted fire preserves present health, weapon, ammo, charge, zoom, cooldown and controls");
                Check(!NetFireEvents.TryAcceptedKey(shooter, out _), "accepted fire context closes after emission");
            }
            BeamProjectileEntity Find(uint id)
            {
                var beam = shooter.EquipInfo.Beams.FirstOrDefault(candidate => candidate.ModShotId == id);
                Check(beam != null, $"accepted shot {id} emits a stamped projectile");
                return beam!;
            }
            void Drain(in IntentPacket packet)
            {
                NetSession.RemoteIntents[0] = packet; NetSession.RemoteIntentValid[0] = true;
                Check(NetFireEvents.Accept(0, packet), "fixture carrier admitted to accepted ledger");
                NetFireEvents.DrainAccepted(shooter); Unchanged();
            }
            try
            {
                PresentState(); victim.Health = 999;
                var source = Event(41, BeamType.Missile);
                var carrier = Carrier(source);
                Drain(carrier);
                var beam = Find(41);
                Check(beam.Beam == BeamType.Missile && beam.ModLaunchFrame == source.AckFrame
                    && beam.ModLaunchKey.ShotId == 41 && beam.ModLaunchLife == sourceLife,
                    "dead owner recovered shot retains authored weapon, launch clock and full identity");
                int before = victim.Health;
                victim.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
                Check(victim.Health == before - 1, "authority applies recovered physical hit once");
                var claim = Claim(0, now); claim.Beam = (byte)BeamType.Missile;
                claim.ShotId = 41; claim.LaunchFrame = source.AckFrame;
                claim.ComponentId = beam.ModComponentId;
                if (beam.EnhancedDirectHit) claim.Flags |= HitClaimPacket.FlagDirect;
                else claim.Flags &= unchecked((byte)~HitClaimPacket.FlagDirect);
                Receive(0, claim); NetHitClaims.Tick();
                Check(victim.Health == before - 1 && NetHitClaims.DuplicateHere == 1 && NetHitClaims.AppliedHere == 0,
                    "recovered shot later claim never rescues duplicate physical damage");
                long fired = NetFireEvents.CaptureLedger(0).Fired;
                Drain(carrier); carrier.Frame--; Drain(carrier);
                Check(NetFireEvents.CaptureLedger(0).Fired == fired,
                    "repeated and reordered carriers emit no duplicate projectiles");

                // Release pool slots between fixtures: Spawn must not recycle a live
                // fixture and accidentally apply collision damage during the next case.
                foreach (var candidate in shooter.EquipInfo.Beams) candidate.Lifespan = 0;
                var second = Event(42, BeamType.Missile);
                Drain(Carrier(second)); beam = Find(42);
                victim.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
                claim.ClaimId = 2; claim.ShotId = 42;
                Receive(0, claim); NetHitClaims.Tick();
                Check(victim.Health == before - 2 && NetHitClaims.DuplicateHere == 2 && NetHitClaims.AppliedHere == 0,
                    "distinct ShotIds at the same launch frame both pay exactly once");
                NetHitClaims.ValidateLedgerCounters();

                foreach (var candidate in shooter.EquipInfo.Beams) candidate.Lifespan = 0;
                fired = NetFireEvents.CaptureLedger(0).Fired;
                var burstCarrier = Carrier(Event(43, BeamType.OmegaCannon), Event(44, BeamType.PowerBeam, FireEventKind.AutomaticFire));
                NetSession.RemoteIntents[0] = burstCarrier; NetSession.RemoteIntentValid[0] = true;
                Check(NetFireEvents.Accept(0, burstCarrier), "burst admitted before normal remote input hook");
                Check(NetHooks.TryApplyRemoteInput(shooter, 0), "normal remote input hook owns accepted burst playback");
                Unchanged();
                Check(Find(43).Beam == BeamType.OmegaCannon && Find(44).Beam == BeamType.PowerBeam
                    && NetFireEvents.CaptureLedger(0).Fired == fired + 2,
                    "same-frame Omega-to-Power Beam burst drains both authored weapons once");

                foreach (var candidate in shooter.EquipInfo.Beams) candidate.Lifespan = 0;
                byte missileCharge = (byte)(shooter.OwningScene.WeaponRules[(int)BeamType.Missile].FullCharge * 2);
                Drain(Carrier(Event(45, BeamType.Missile, FireEventKind.ReleaseFire, missileCharge)));
                Check(Find(45).Beam == BeamType.Missile && Find(45).Flags.TestFlag(BeamFlags.Charged),
                    "charged missile release uses authored charge while present charge stays zero");

                foreach (var candidate in shooter.EquipInfo.Beams) candidate.Lifespan = 0;
                Drain(Carrier(Event(46, BeamType.Imperialist, state: FireEvent.ScopedStateBit)));
                Check(Find(46).HeadshotDamage == shooter.OwningScene.WeaponRules[(int)BeamType.Imperialist].HeadshotDamage,
                    "recovered scoped Imperialist retains full headshot damage while currently unscoped");

                foreach (var candidate in shooter.EquipInfo.Beams) candidate.Lifespan = 0;
                shooter.Spawn(shooter.Position, Vector3.UnitZ, Vector3.UnitY, shooter.NodeRef, respawn: true);
                Check(NetPlayerLifecycle.Get(0) != sourceLife, "fixture advances actual owner life");
                PresentState();
                var oldCarrier = Carrier(Event(47, BeamType.Missile));
                oldCarrier.LifeId = sourceLife;
                var currentPosition = shooter.Position;
                oldCarrier.Position = currentPosition + Vector3.UnitX * 100;
                oldCarrier.WeaponSelect = (byte)BeamType.OmegaCannon;
                Check(NetSession.AcceptSlotIntent(0, oldCarrier), "old-life carrier admits retained event without movement intake");
                Check(!NetSession.RemoteIntentValid[0] && shooter.Position == currentPosition,
                    "old-life carrier cannot install movement or current input state");
                NetFireEvents.DrainAccepted(shooter); Unchanged();
                var impossibleCarrier = Carrier(Event(49, BeamType.PowerBeam) with { SourceLife = NetPlayerLifecycle.Get(0) });
                impossibleCarrier.LifeId = sourceLife;
                Check(!NetSession.AcceptSlotIntent(0, impossibleCarrier),
                    "event source life newer than its carrier is rejected");
                beam = Find(47);
                Check(beam.ModLaunchLife == sourceLife && beam.ModLaunchKey.LifeId == sourceLife
                    && NetPlayerLifecycle.CurrentProjectile(beam),
                    "late old-life event after respawn keeps original launch key and valid flight ownership");
                int oldLifeBefore = victim.Health;
                victim.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
                Check(victim.Health == oldLifeBefore - 1, "old-life accepted flight deals authority physical hit");
                long duplicatesBefore = NetHitClaims.DuplicateHere;
                long rescuesBefore = NetHitClaims.AppliedHere;
                var oldClaim = Claim(0, now); oldClaim.ClaimId = 50;
                oldClaim.ShotId = 47; oldClaim.Beam = (byte)BeamType.Missile;
                oldClaim.ShooterLifeId = sourceLife; oldClaim.LaunchFrame = now - 2;
                oldClaim.ComponentId = beam.ModComponentId;
                if (beam.EnhancedDirectHit) oldClaim.Flags |= HitClaimPacket.FlagDirect;
                else oldClaim.Flags &= unchecked((byte)~HitClaimPacket.FlagDirect);
                byte oldVerdict = 255;
                var savedSink = NetHitClaims.CombatAckSink;
                NetHitClaims.CombatAckSink = (_, entries) =>
                {
                    foreach (var entry in entries) if (entry.ClaimId == oldClaim.ClaimId) oldVerdict = entry.Result;
                };
                try
                {
                    Receive(0, oldClaim); NetHitClaims.Tick();
                    Check(oldVerdict == HitVerdictPacket.ResultDuplicate
                        && victim.Health == oldLifeBefore - 1
                        && NetHitClaims.DuplicateHere == duplicatesBefore + 1
                        && NetHitClaims.AppliedHere == rescuesBefore,
                        "old-life full-key claim pairs resolved physical hit without rescue");
                    oldVerdict = 255;
                    Receive(0, oldClaim); NetHitClaims.Tick();
                    Check(oldVerdict == HitVerdictPacket.ResultDuplicate
                        && NetHitClaims.DuplicateHere == duplicatesBefore + 1
                        && victim.Health == oldLifeBefore - 1,
                        "old-life duplicate claim retry repeats verdict without consuming another hit");
                    oldClaim.ClaimId++; oldClaim.ShotId = 48; oldVerdict = 255;
                    Receive(0, oldClaim); NetHitClaims.Tick();
                    Check(oldVerdict == HitVerdictPacket.ResultWrongLife
                        && NetHitClaims.ClaimsPendingCurrent == 0
                        && NetHitClaims.AppliedHere == rescuesBefore && victim.Health == oldLifeBefore - 1,
                        "unresolved old-life claim receives explicit wrong-life verdict without damage rescue");
                }
                finally { NetHitClaims.CombatAckSink = savedSink; }
                NetHitClaims.ValidateLedgerCounters();
                foreach (var candidate in shooter.EquipInfo.Beams) candidate.Lifespan = 0;
                var sceneState = shooter.OwningScene.GameState;
                bool savedEnhanced = sceneState.EnhancedHunters, savedBalanced = sceneState.BalancedMode;
                var savedHunter = shooter.Hunter;
                var enhanced = shooter.EnhancedState;
                byte savedPips = enhanced.ValueA, savedB = enhanced.ValueB;
                int savedTimer = enhanced.TimerB, savedContacts = enhanced.ContactFrames;
                try
                {
                    sceneState.EnhancedHunters = true; sceneState.BalancedMode = false;
                    shooter.Hunter = Hunter.Samus;
                    enhanced.ValueA = 3; enhanced.ValueB = 7; enhanced.TimerB = 91; enhanced.ContactFrames = 17;
                    victim.Health = 999;
                    var enhancedFire = Event(50, BeamType.Missile, FireEventKind.ReleaseFire,
                        missileCharge, FireEvent.EnhancedStateBit | FireEvent.AffinityStateBit | 2u) with { Target = NetTargetIdentity.ForSlot(1), AckFrame = now, AckSubFrame = 0 };
                    Drain(Carrier(enhancedFire));
                    var flights = shooter.EquipInfo.Beams.Where(candidate => candidate.Lifespan > 0
                        && candidate.ModShotId == 50).ToArray();
                    Check(flights.Length == 1 && flights.All(candidate => !candidate.EnhancedMicroSeeker
                            && candidate.Beam == BeamType.Missile && candidate.ModLaunchLife == sourceLife),
                        "retired Enhanced context cannot spawn a live Samus barrage; native old-life Missile remains: "
                        + string.Join(";", shooter.EquipInfo.Beams.Where(b => b.ModShotId == 50).Select(b => $"{b.Beam}/micro={b.EnhancedMicroSeeker}/life={b.Lifespan}/key={b.ModLaunchKey}/target={b.Target?.Id}")));
                    Check(enhanced.ValueA == 3 && enhanced.ValueB == 7
                        && enhanced.TimerB == 91 && enhanced.ContactFrames == 17,
                        "retired old-life context preserves inert compatibility pips and timers");
                    Unchanged();
                }
                finally
                {
                    sceneState.EnhancedHunters = savedEnhanced; sceneState.BalancedMode = savedBalanced;
                    shooter.Hunter = savedHunter; enhanced.ValueA = savedPips; enhanced.ValueB = savedB;
                    enhanced.TimerB = savedTimer; enhanced.ContactFrames = savedContacts;
                }
                var ledger = NetFireEvents.CaptureLedger(0);
                Check(ledger.Pending == 0 && ledger.Rejected == 0 && ledger.Expired == 0,
                    "every retained fixture shot has one fired terminal disposition");
            }
            finally
            {
                NetSession.RemoteIntentValid[0] = false; NetFireEvents.Reset();
                NetFireEvents.AcceptedPlaybackEnabled = savedPlayback; PrepareClaims();
            }
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
