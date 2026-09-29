using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.EnhancedHunters;
public static class EnhancedHunterSceneChecks
{
    public static int Run(string room)
    {
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException(message); Console.WriteLine("ENHANCED SCENE PASS " + message); }
        var sim = new ServerSim();
        try
        {
            var match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
                EnhancedHunters = true, PointGoal = 999, TimeLimitSeconds = 3600 };
            var session = new SessionStatePacket { Match = match, MatchId = 1, AuthorityEpoch = 1,
                Phase = SessionPhase.InMatch, MaxPlayers = 8, WorldProfile = MatchWorldProfile.Resolve(8) };
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 8;
            for (byte i = 0; i < 8; i++)
            { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Names[i] = "ENHANCED" + i; roster.Hunters[i] = (byte)(i % 7); }
            Check(sim.Start(room, GameMode.Battle, 8, _ => { }, () => { }, roster, session), "authority loads enhanced match");
            for (int i = 0; i < 240; i++) sim.Step();
            var players = PlayerEntity.Players; var scene = players[0].OwningScene;
            Check(scene.GameState.EnhancedHunters && !scene.GameState.AffinityWeapons, "scene keeps the two rules independent");
            Check(sim.StepFailures == 0, "eight players simulate");
            foreach (var p in players)
            {
                Check(p.ModIsInPlay, "spawned slot " + p.SlotIndex);
                ClearInvulnerability(p); p.Health = 200;
            }
            var victim = players[7];
            void Hit(PlayerEntity attacker, BeamType type, bool direct, bool charged = false, Affliction status = Affliction.None, bool head = false)
            {
                var beam = new BeamProjectileEntity(scene) { Owner = attacker, Beam = type, BeamKind = type,
                    Flags = charged ? BeamFlags.Charged : 0, Afflictions = status, EnhancedDirectHit = direct };
                NetPlayerLifecycle.StampProjectile(beam, null);
                victim.TakeDamage(1, DamageFlags.NoDmgInvuln | (head ? DamageFlags.Headshot : 0), null, beam);
            }
            var kanden = players[1];
            Hit(kanden, BeamType.VoltDriver, false, true, Affliction.Disrupt);
            Check(kanden.EnhancedState.TargetSlot == byte.MaxValue, "charged splash does not create Rod");
            Hit(kanden, BeamType.VoltDriver, true, true, Affliction.Disrupt);
            Check(EnhancedHunters.HasMark(kanden, victim), "accepted charged direct hit creates Rod");
            int before = victim.Health;
            for (int i = 0; i < 3; i++) Hit(kanden, BeamType.VoltDriver, true);
            Check(before - victim.Health == 11 && kanden.EnhancedState.TargetSlot == byte.MaxValue, "third follow-up pays one Overload");
            var trace = players[2];
            Hit(trace, BeamType.Imperialist, true, head: true);
            Check((trace.EnhancedState.Flags & 1) == 1 && trace.EnhancedState.TimerA == 240, "accepted headshot creates Perfect Mark");
            EnhancedHunters.OnAltAttackHit(trace, victim);
            Check(trace.EnhancedState.GhostFrames == 75 && trace.EnhancedState.TargetSlot == byte.MaxValue, "assassination consumes mark and grants Ghost Step");
            var noxus = players[4];
            for (int i = 0; i < 3; i++) Hit(noxus, BeamType.Judicator, true);
            Hit(noxus, BeamType.Judicator, true, true, Affliction.Freeze);
            Check(victim.ModFrozen && noxus.EnhancedState.ValueB == 3, "accepted freeze captures Brittle tier");
            before = victim.Health; Hit(noxus, BeamType.Judicator, true);
            Check(before - victim.Health == 12 && noxus.EnhancedState.Flags == 0, "Shatter adds eleven and consumes Brittle");
            var weavel = players[6];
            scene.GameState.SpawnProtection = true; victim.ModSetSpawnProtectionFromAuthority(true);
            before = victim.Health; Hit(weavel, BeamType.Battlehammer, true);
            Check(victim.Health == before && weavel.EnhancedState.ValueA == 0, "spawn-protected hit cannot earn Siege");
            scene.GameState.SpawnProtection = false; victim.ModSetSpawnProtectionFromAuthority(false);
            Hit(weavel, BeamType.Battlehammer, false); Check(weavel.EnhancedState.ValueA == 0, "splash cannot earn Siege");
            Hit(weavel, BeamType.Battlehammer, true); Check(weavel.EnhancedState.ValueA == 1, "accepted direct hit earns Siege");
            var samus = players[0]; samus.ModArmWeapon(BeamType.Missile); samus.EquipInfo.InfiniteAmmo = true;
            EnhancedHunters.Mark(samus, victim, 18); samus.EnhancedState.ValueA = 3;
            samus.EquipInfo.ChargeLevel = (ushort)(samus.EquipInfo.Weapon.FullCharge * 2);
            NetUnlagged.BeginShot(samus);
            try { BeamProjectileEntity.Spawn(samus, samus.EquipInfo, samus.Position.AddY(1), Vector3.UnitY,
                BeamSpawnFlags.NoMuzzle, samus.NodeRef, scene); }
            finally { NetUnlagged.EndShot(samus); }
            int micro = 0;
            foreach (var beam in samus.EquipInfo.Beams)
                if (beam.Lifespan > 0 && beam.EnhancedMicroSeeker)
                {
                    micro++; Check(beam.Damage == 6 && beam.HeadshotDamage == 6 && beam.SplashDamage == 3
                        && !beam.Flags.TestFlag(BeamFlags.SelfDamage) && beam.Target == victim,
                        "micro seeker has bounded damage, target and no self damage");
                }
            Check(micro == 3 && samus.EnhancedState.ValueA == 0, "charged missile consumes three pips into three children");
            var spire = players[5];
            scene.EnhancedWorld.Add(spire, EnhancedZoneType.MagmaPool, spire.Position);
            scene.EnhancedWorld.Add(spire, EnhancedZoneType.MagmaPool, spire.Position.AddX(8));
            scene.EnhancedWorld.Add(spire, EnhancedZoneType.MagmaPool, spire.Position.AddX(16));
            Check(scene.EnhancedWorld.Count(spire, EnhancedZoneType.MagmaPool) == 2, "pool cap is enforced");
            int health = spire.Health;
            Check(scene.EnhancedWorld.Detonate(spire, spire.Position.AddX(16))
                && scene.EnhancedWorld.Count(spire, EnhancedZoneType.MagmaPool) == 1 && spire.Health == health,
                "detonation consumes pool without owner damage");
            var second = players[3];
            NoxusEnhancement.Frost(noxus, victim); NoxusEnhancement.Frost(noxus, second);
            Check(noxus.EnhancedState.FrostStacks[victim.SlotIndex] == 1
                && noxus.EnhancedState.FrostStacks[second.SlotIndex] == 1, "Frost survives target switching");
            EnhancedHunters.OnPlayerSpawn(second);
            Check(noxus.EnhancedState.FrostExpiry[second.SlotIndex] == 0, "respawn clears incoming Frost");
            var oldImpulse = spire.EnhancedState.Impulse0;
            EnhancedHunterMovement.PushOwner(spire, Vector3.UnitY * .3f);
            Vector3 pushedSpeed = spire.Speed;
            EnhancedHunterMovement.Apply(spire, oldImpulse, spire.EnhancedState.Impulse0);
            Check(spire.Speed == pushedSpeed, "duplicate movement confirmation cannot add a second impulse");
            scene.GameState.EnhancedHunters = false;
            EnhancedHunters.ResetPlayer(weavel); before = victim.Health; Hit(weavel, BeamType.Battlehammer, true);
            Check(victim.Health == before - 1 && weavel.EnhancedState.ValueA == 0, "disabled rule retains vanilla damage without enhanced state");
            Console.WriteLine($"ENHANCED SCENE PASS {checks} checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); }
    }
    private static void ClearInvulnerability(PlayerEntity player)
    {
        foreach (string name in new[] { "_spawnInvulnTimer", "_damageInvulnTimer" })
        {
            var field = typeof(PlayerEntity).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(player, Convert.ChangeType(0, field.FieldType));
        }
    }
}
