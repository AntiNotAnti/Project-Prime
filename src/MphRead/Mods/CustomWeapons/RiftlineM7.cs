using System;
using System.Text;

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
/// <summary>Nominal custom projectile identity, with no native shot-key conversion.</summary>
internal readonly record struct CustomProjectileIdentity
{
    internal WeaponIdentity Weapon { get; }
    internal WeaponOwnerContext Owner { get; }
    internal uint Sequence { get; }
    internal bool IsValid => Weapon.Kind == WeaponIdentityKind.Custom && Owner.IsValid && Sequence != 0;
    internal CustomProjectileIdentity(WeaponIdentity weapon, WeaponOwnerContext owner, uint sequence)
    {
        if (weapon.Kind != WeaponIdentityKind.Custom || !owner.IsValid || sequence == 0)
            throw new ArgumentException("Exact custom weapon, complete owner and nonzero sequence required.");
        Weapon = weapon; Owner = owner; Sequence = sequence;
    }
}
/// <summary>Immutable provenance supplied by the caller. Factories validate
/// consistency; they do not establish network admission, trade or payment proof.</summary>
internal sealed record class CustomShotSource
{
    internal WeaponDefinition Definition { get; }
    internal CustomProjectileIdentity Identity { get; }
    internal WeaponOwnerContext Owner => Identity.Owner;
    internal uint Sequence => Identity.Sequence;
    internal CustomActionIdentity? Action { get; }
    internal CustomSourceCertificate? Certificate { get; }
    internal bool IsValid => Definition != null && Identity.IsValid && Definition.Identity == Identity.Weapon;
    private CustomShotSource(WeaponDefinition definition, CustomProjectileIdentity identity,
        CustomActionIdentity? action, CustomSourceCertificate? certificate)
    { Definition = definition; Identity = identity; Action = action; Certificate = certificate; }
    internal static CustomShotSource ForOffline(WeaponDefinition definition, WeaponOwnerContext owner, uint sequence)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new(definition,new(definition.Identity,owner,sequence),null,null);
    }
    internal static CustomShotSource FromLaunch(in CustomAcceptedLaunch launch)
    {
        if (!TryValidateLaunch(launch,out var identity,out var action))
            throw new ArgumentException("Consistent accepted custom launch metadata required.",nameof(launch));
        return new(launch.Definition,identity,action,launch.Source);
    }
    internal bool MatchesLaunch(in CustomAcceptedLaunch launch)
        => TryValidateLaunch(launch,out var identity,out var action)
            && Definition == launch.Definition && Identity == identity && Action == action && Certificate == launch.Source;
    private static bool TryValidateLaunch(in CustomAcceptedLaunch launch,
        out CustomProjectileIdentity identity, out CustomActionIdentity actionIdentity)
    {
        identity = default; actionIdentity = default;
        var definition = launch.Definition; var certificate = launch.Source; var action = certificate.Action;
        if (definition == null || definition.Identity.Kind != WeaponIdentityKind.Custom || !action.IsValid
            || action.Kind != CustomWeaponActionKind.Fire || !certificate.CanUseWeapon || !certificate.IsValidFor(action)) return false;
        var owner = action.Owner;
        if (!owner.IsValid || launch.Before.Owner != owner || launch.After.Owner != owner
            || launch.Before.Weapon != definition.Identity || launch.After.Weapon != definition.Identity
            || launch.Before.Tick != action.SourceFrame || launch.After.Tick != action.SourceFrame
            || launch.Before.Magazine <= 0 || launch.After.Magazine != launch.Before.Magazine-1
            || launch.After.LastAcceptedShotId != action.Sequence || launch.After.LastAcceptedShotTick != action.SourceFrame) return false;
        identity = new(definition.Identity,owner,action.Sequence);
        actionIdentity = CustomActionIdentity.For(action);
        return true;
    }
}
internal readonly record struct CustomImpactFact(WeaponIdentity Weapon, CustomProjectileIdentity Source,
    int VictimSlot, uint Tick, int Damage, int TurretDamage, bool HitTurret, bool Headshot, bool Lethal, OpenTK.Mathematics.Vector3 Point);
