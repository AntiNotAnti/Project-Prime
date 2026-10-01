using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Multiplayer;
namespace MphRead.Mods.MatchEvents;
/// <summary>F2: real headless authority, accepted damage and independent published replay markers.</summary>
public static class MatchSemanticSceneCheck
{
    public static int Run()
    {
        var sim = new ServerSim();
        int checks = 0;
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        try
        {
            const string room = "MP1 SANCTORUS";
            var session = new SessionStatePacket { MatchId = 1, AuthorityEpoch = 1, MaxPlayers = 4,
                Phase = SessionPhase.InMatch, WorldProfile = MatchWorldProfile.Resolve(4),
                Match = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle, PointGoal = 100, TimeLimitSeconds = 600 } };
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Count = 4; roster.Revision = 1;
            for (byte i = 0; i < 4; i++)
            { roster.Slots[i] = i; roster.Generations[i] = 1; roster.Hunters[i] = (byte)Hunter.Samus; roster.Teams[i] = -1; roster.Names[i] = "SEMANTIC CHECK"; }
            Require(sim.Start(room, GameMode.Battle, 4, _ => { }, () => { }, roster, session), "authority starts with real content");
            NetSlotManager.Sync();
            for (int i = 0; i < 120; i++) sim.Step();
            var shooter = PlayerEntity.Players[0]; var victim = PlayerEntity.Players[1]; var helper = PlayerEntity.Players[2];
            var scene = shooter.OwningScene;
            Require(shooter.ModIsInPlay && victim.ModIsInPlay && helper.ModIsInPlay, "real players spawned");
            shooter.Health = victim.Health = helper.Health = 99;
            var beam = new BeamProjectileEntity(scene) { Owner = shooter, Beam = BeamType.Imperialist, BeamKind = BeamType.Imperialist };
            NetPlayerLifecycle.StampProjectile(beam);
            victim.TakeDamage(10, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, helper);
            victim.TakeDamage(10, DamageFlags.Headshot | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
            sim.Step();
            Require(victim.Health == 79, "passive stream leaves actual accepted damage unchanged");
            var parity = scene.MatchEvents.ParityDiagnostics;
            Require(parity.Matched == 1 && parity.MissingSemantic == 0 && parity.MissingLegacy == 0, "real headshot legacy marker agrees");
            victim.TakeDamage(500, DamageFlags.Headshot | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, beam);
            sim.Step();
            Require(victim.Health == 0 && scene.GameState.Kills[0] == 1 && scene.GameState.Deaths[1] == 1, "real kill score and health unchanged");
            Require(parity.Matched == 3 && parity.MissingSemantic == 0 && parity.MissingLegacy == 0 && parity.Pending == 0,
                $"exact death and headshot legacy parity matched={parity.Matched} missingSemantic={parity.MissingSemantic} missingLegacy={parity.MissingLegacy} pending={parity.Pending}");
            Require(scene.MatchEvents.Bus.Events.Count(e => e.Type == MatchSemanticEventType.PlayerAssisted && e.Actor.Slot == 2) == 1,
                "real accepted helper damage awards one assist");
            Require(scene.MatchEvents.Bus.Awards.Awards.Any(a => a.Kind == MatchAwardKind.FirstBlood)
                && scene.MatchEvents.Bus.Awards.Awards.Any(a => a.Kind == MatchAwardKind.Assist), "authority awards generated");
            foreach (var weapon in new[] { BeamType.PowerBeam, BeamType.Missile, BeamType.Imperialist,
                BeamType.Judicator, BeamType.Magmaul, BeamType.Battlehammer, BeamType.VoltDriver, BeamType.ShockCoil, BeamType.OmegaCannon })
            {
                victim.Spawn(victim.Position, OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector3.UnitY, victim.NodeRef, respawn: true);
                sim.Step(); // establish the new victim-life baseline before its death
                victim.Health = 99;
                var projectile = new BeamProjectileEntity(scene) { Owner = shooter, Beam = weapon, BeamKind = weapon };
                NetPlayerLifecycle.StampProjectile(projectile);
                long matched = parity.Matched;
                var flags = DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln;
                if (weapon == BeamType.Imperialist) flags |= DamageFlags.Headshot;
                victim.TakeDamage(500, flags, null, projectile);
                sim.Step();
                Require(victim.Health == 0 && parity.Matched == matched + (weapon == BeamType.Imperialist ? 2 : 1)
                    && parity.MissingSemantic == 0 && parity.MissingLegacy == 0 && parity.Pending == 0,
                    $"{weapon} real authority exact-marker parity matched={parity.Matched} missing={parity.MissingSemantic}/{parity.MissingLegacy} pending={parity.Pending}");
            }
            Require(Sound.CombatFeedbackAudio.OnCanonicalAward(scene, MatchAwardKind.FirstBlood, true) == "First Blood"
                && Sound.CombatFeedbackAudio.OnCanonicalAward(scene, MatchAwardKind.DoubleKill, true) == "Double Kill"
                && Sound.CombatFeedbackAudio.OnCanonicalAward(scene, MatchAwardKind.KillingSpree, true) == "Killing Spree",
                "canonical award identities select existing audio and medal labels");
            Require(scene.GameState.LongestKillStreak[shooter.SlotIndex] == scene.GameState.KillStreak[shooter.SlotIndex],
                "canonical post-match best streak equals independent gameplay streak");
            Require(scene.GameState.HeadshotKills[shooter.SlotIndex] == 2, "post-match headshot kills consume lethal headshot facts only");
            var lastDeath = scene.MatchEvents.Bus.Events.Last(e => e.Type == MatchSemanticEventType.PlayerKilled);
            Require(MatchDeathDetails.TryDecode(lastDeath.Value, out var details) && details.Kind == MatchDeathKind.Weapon,
                "authority preserves full weapon death classification");
            Require(Combat.KillFeed.TryBuildCanonical(scene, lastDeath, out var entry)
                && entry.Beam == BeamType.OmegaCannon && entry.Kind == Combat.KillFeedKind.Weapon
                && entry.KillerName == scene.GameState.Nicknames[shooter.SlotIndex], "canonical feed enriches exact current occupant");
            var oldOccupant = lastDeath with { Actor = lastDeath.Actor with { SlotGeneration = (ushort)(lastDeath.Actor.SlotGeneration + 1) } };
            Require(Combat.KillFeed.TryBuildCanonical(scene, oldOccupant, out entry) && entry.KillerName == "PLAYER 1",
                "stale occupant cannot borrow replacement display name");
            foreach (var classification in new[] { (DamageFlags.Burn, MatchDeathKind.Burn), (DamageFlags.Deathalt, MatchDeathKind.Deathalt) })
            {
                victim.Spawn(victim.Position, OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector3.UnitY, victim.NodeRef, respawn: true);
                victim.Health = 99;
                victim.TakeDamage(500, classification.Item1 | DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, shooter);
                var death = scene.MatchEvents.Bus.Events.Last(e => e.Type == MatchSemanticEventType.PlayerKilled);
                Require(MatchDeathDetails.TryDecode(death.Value, out details) && details.Kind == classification.Item2,
                    "actual accepted damage preserves " + classification.Item2);
            }
            var bomb = new BombEntity(scene);
            typeof(BombEntity).GetProperty(nameof(BombEntity.Owner))!.SetValue(bomb, shooter);
            foreach (var classification in new (EntityBase? Source, MatchDeathKind Kind)[] {
                (bomb, MatchDeathKind.Bomb), (shooter, MatchDeathKind.Alt), (victim, MatchDeathKind.Suicide), (null, MatchDeathKind.Environment) })
            {
                victim.Spawn(victim.Position, OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector3.UnitY, victim.NodeRef, respawn: true);
                victim.Health = 99;
                victim.TakeDamage(500, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, classification.Source);
                var death = scene.MatchEvents.Bus.Events.Last(e => e.Type is MatchSemanticEventType.PlayerKilled or MatchSemanticEventType.PlayerSuicide);
                Require(MatchDeathDetails.TryDecode(death.Value, out details) && details.Kind == classification.Kind,
                    "actual accepted damage preserves " + classification.Kind);
            }
            int priorDeaths = scene.MatchEvents.Bus.Events.Count(e => e.Type == MatchSemanticEventType.PlayerKilled);
            int priorScore = scene.GameState.Kills[shooter.SlotIndex];
            for (int life = 0; life < 2; life++)
            {
                victim.Spawn(victim.Position, OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector3.UnitY, victim.NodeRef, respawn: true);
                victim.Health = 99;
                victim.TakeDamage(500, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, shooter);
            }
            Require(scene.MatchEvents.Bus.Events.Count(e => e.Type == MatchSemanticEventType.PlayerKilled) == priorDeaths + 2
                && scene.GameState.Kills[shooter.SlotIndex] == priorScore + 2,
                "two victim lives before snapshot publication retain both facts and exact score outcomes");
            scene.GameState.Teams = true; scene.GameState.TeamCount = 2; shooter.TeamIndex = 0; helper.TeamIndex = victim.TeamIndex = 1;
            int assistsBefore = scene.MatchEvents.Bus.Events.Count(e => e.Type == MatchSemanticEventType.PlayerAssisted && e.Actor.Slot == helper.SlotIndex);
            foreach (bool friendlyFire in new[] { false, true })
            {
                scene.GameState.FriendlyFire = friendlyFire;
                victim.Spawn(victim.Position, OpenTK.Mathematics.Vector3.UnitZ, OpenTK.Mathematics.Vector3.UnitY, victim.NodeRef, respawn: true);
                victim.Health = 99;
                victim.TakeDamage(10, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, helper);
                Require(friendlyFire ? victim.Health is > 0 and < 99 : victim.Health == 99,
                    $"friendly contribution follows accepted damage rule FF={friendlyFire} health={victim.Health} teams={GameState.Teams}/{scene.GameState.Teams} count={GameState.TeamCount} indices={helper.TeamIndex}/{victim.TeamIndex}");
                victim.TakeDamage(0, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, helper);
                victim.TakeDamage(500, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, shooter);
                Require(scene.MatchEvents.Bus.Events.Count(e => e.Type == MatchSemanticEventType.PlayerAssisted && e.Actor.Slot == helper.SlotIndex)
                    == assistsBefore + (friendlyFire ? 1 : 0), "accepted teammate contributes once; rejected teammate damage contributes nothing");
            }
            Require(scene.SemanticPublisher.HistoryGaps == 0 && sim.StepFailures == 0, "recording pipeline has no gaps or simulation failures");
            Console.WriteLine($"SEMANTIC F2 PASS {checks} real-authority damage/parity checks");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("SEMANTIC F2 FAIL " + ex); return 1; }
        finally { sim.Stop(); }
    }
}
