using System;
using System.Text;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

internal static class RiftlineM7
{
    internal const string ProductId = "prime.riftline-m7";
    internal static readonly Guid ContentId = Guid.Parse("9a776851-f39f-4a76-8c13-c5d88875ef61");
    internal static WeaponDefinition Create() => WeaponDefinition.Create(ContentId,"Riftline M7",
        WeaponMechanics.Parse(Encoding.UTF8.GetBytes(Json)));
    private const string Json = """
        {"schema":1,"behaviorVersion":1,"behavior":"semi-auto-precision","damage":{"body":65,"head":150},"magazineCapacity":4,"initialReserve":16,"maximumReserve":64,"shotCooldownTicks":75,"reloadTicks":138,"projectileSpeed4096":819200,"projectileRadius4096":41,"range4096":4096000,"lifetimeTicks":6,"hipSpreadMilliDegrees":1200,"scopedSpreadMilliDegrees":0,"maxActiveProjectiles":8,"opticsMilli":[2500,4000],"splash":false,"piercing":false}
        """;
}
internal readonly record struct CustomShotSource(WeaponDefinition Definition, ShotKey Key)
{
    internal static CustomShotSource Accepted(WeaponDefinition definition,WeaponOwnerContext owner,uint shotId)
        => new(definition,new ShotKey(AuthorityEpoch:owner.AuthorityEpoch,MatchId:owner.MatchId,
            ShooterSlot:owner.Slot,Generation:owner.Generation,LifeId:owner.Life,ShotId:shotId));
}
internal readonly record struct CustomImpactFact(WeaponIdentity Weapon, ShotKey Source,
    int VictimSlot, uint Tick, int Damage, int TurretDamage, bool HitTurret, bool Headshot, bool Lethal, OpenTK.Mathematics.Vector3 Point);
