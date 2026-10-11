using System;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
namespace MphRead.NetTest;
internal static class ImpactAuthorityTests
{
    private static int _checks;
    private static void Check(bool ok, string name)
    { if (!ok) throw new InvalidOperationException(name); _checks++; Console.WriteLine($"IMPACT AUTHORITY PASS {name}"); }
    private static void Set(PlayerEntity player, string field, object value)
        => typeof(PlayerEntity).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
    public static int Run(string profile, string room)
    {
        var sim = new ServerSim(); if (!sim.Start(room, GameMode.Battle, 2, _ => { }, () => { })) return 1;
        bool enabled = NetConfirmedImpacts.Enabled; var oldSink = NetSession.ConfirmedImpactSink;
        var facts = new List<ConfirmedImpact>();
        try
        {
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1, RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte i = 0; i < 2; i++) { roster.Slots[i] = i; roster.Generations[i] = 1; }
            NetSession.ApplyRoster(roster); for (int i = 0; i < 120; i++) sim.Step();
            var owner = PlayerEntity.Players[0]; var victim = PlayerEntity.Players[1];
            owner.Health = victim.Health = 99; owner.ModReleaseSpawnProtection(); victim.ModReleaseSpawnProtection();
            Set(victim, "_spawnInvulnTimer", (ushort)0); Set(victim, "_damageInvulnTimer", (ushort)0);
            var key = ShotKey.For(owner.SlotIndex, 701);
            var beam = new BeamProjectileEntity(owner.OwningScene) { Owner = owner, Beam = BeamType.PowerBeam,
                ModLaunchKey = key, ModLaunchAuthority = key.AuthorityEpoch, ModLaunchMatch = key.MatchId,
                ModLaunchGeneration = key.Generation, ModLaunchLife = key.LifeId, ModShotId = key.ShotId,
                ModComponentId = 1, ModLaunchFrame = NetSession.NetFrame };
            NetConfirmedImpacts.Enabled = true; NetSession.ConfirmedImpactSink = fact => facts.Add(fact);
            ushort beforeSequence = NetDamage.Sequence(victim.SlotIndex); int beforeHealth = victim.Health;
            victim.TakeDamage(5, DamageFlags.NoDmgInvuln, null, beam);
            Check(victim.Health < beforeHealth && facts.Count == 1 && facts[0].Key == key && facts[0].Component == 1
                && facts[0].DamageEvent == NetDamage.Sequence(victim.SlotIndex) && facts[0].DamageEvent != beforeSequence,
                "native finalized debit emits exact key component and payment sequence");
            Check(facts[0].Provenance == ImpactProvenance.ConfirmedBodyAnchor && facts[0].Point == victim.Position,
                "missing native collision point is truthful body anchor");
            victim.TakeDamage(0, DamageFlags.NoDmgInvuln, null, beam); Check(facts.Count == 1, "zero debit emits no fact");
            Set(victim, "_spawnInvulnTimer", (ushort)100); beforeHealth = victim.Health;
            victim.TakeDamage(5, DamageFlags.NoDmgInvuln, null, beam); Check(victim.Health == beforeHealth && facts.Count == 1, "immune hit emits no fact");
            Set(victim, "_spawnInvulnTimer", (ushort)0);
            Check(NetConfirmedImpacts.Describe(victim, null, beam, null, 1, 0) == null, "absent attacker cannot describe authority fact");
            var invalid = new BeamProjectileEntity(owner.OwningScene) { Owner = owner, Beam = BeamType.PowerBeam };
            Check(NetConfirmedImpacts.Describe(victim, owner, invalid, null, 1, 0) == null, "invalid native source identity cannot describe fact");
            var roleProperty = typeof(NetSession).GetProperty(nameof(NetSession.Role))!; var role = NetSession.Role;
            var authorityProperty = typeof(NetSession).GetProperty(nameof(NetSession.IsAuthority))!; bool authority = NetSession.IsAuthority;
            try
            {
                roleProperty.SetValue(null, NetRole.Client); authorityProperty.SetValue(null, false);
                Check(NetConfirmedImpacts.Describe(victim, owner, beam, null, 1, 0) == null, "non-authority cannot describe fact");
                NetConfirmedImpacts.Publish(facts[0]); Check(facts.Count == 1, "non-authority cannot publish fact");
            }
            finally { roleProperty.SetValue(null, role); authorityProperty.SetValue(null, authority); }
            NetConfirmedImpacts.Enabled = false; beforeHealth = victim.Health;
            victim.TakeDamage(5, DamageFlags.NoDmgInvuln, null, owner);
            Check(victim.Health < beforeHealth && facts.Count == 1, "disabled cosmetic option leaves native damage intact");
            Check(sim.StepFailures == 0, "authority fixture has no simulation failures");
            Console.WriteLine($"PASS: {_checks} impact authority assertions ({profile})"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { NetSession.ConfirmedImpactSink = oldSink; NetConfirmedImpacts.Enabled = enabled; sim.Stop(); NetSession.Stop(); }
    }
}
