using System;
using System.Text;

namespace MphRead.Mods.CustomWeapons;

// Adapted raw projectile fields from SecondHunt41452f2 PirateBlaster (MIT).
// The profile has its own identity and source scope; Power Beam effects are
// only an explicitly declared local visual/effectiveness fallback.
internal static class PirateBlasterDefinition
{
    internal const int BurstBolts = 3, BurstGap = 6, BurstAmmoCost = 5, Cadence = 40;
    internal const int MarkLifetime = 240, HeadshotRange4096 = 15 * 4096;
    internal static readonly Guid ContentId = Guid.Parse("49f3ab6c-bdc0-40ed-bf14-eaaa5ad5aa9f");
    internal static readonly WeaponDefinition Definition = WeaponDefinition.Create(ContentId,
        "Pirate Blaster", WeaponMechanics.CreatePirateBurstV1());
    internal static bool Is(WeaponDefinition? definition) => ReferenceEquals(definition, Definition);
    internal static bool IsProfile(WeaponDefinition? definition) => definition?.Mechanics.Behavior == WeaponBehavior.PirateUaBurst;
    // Independent schema2 bytes. CWM1/public parsing remain precision-only.
    internal static byte[] CanonicalBytes() => Encoding.UTF8.GetBytes(Json);
    private const string Json = """
        {"schema":2,"behaviorVersion":1,"behavior":"pirate-ua-burst","damage":{"body":12,"head":16},"burstOffsets":[0,6,12],"ammo":"native-ua","burstAmmoCost":5,"shotCooldownTicks":40,"projectileSpeed4096":8192,"projectileRadius4096":409,"range4096":0,"lifetimeTicks":510,"headshotRange4096":61440,"maxActiveProjectiles":40,"colorsRgb555":[4511,17183],"effectiveness":"native-power-beam","damageInterpolation":0,"fullBurstReservation":true,"splash":false,"piercing":false,"markDistinctBolts":3,"markLifetimeTicks":240}
        """;
    internal static WeaponInfo BuildAdapter()
        {
            return new WeaponInfo(
                description: "Private Pirate Blaster native projectile adapter",
                beam: BeamType.None,
                beamKind: BeamType.None,
                drawFuncIds: new byte[] { 0, 0 },
                colors: new ushort[] { 4511, 17183 },
                priority: 2,
                flags: WeaponFlags.SurfaceCollision,
                splashDamage: 0,
                minChargeSplashDamage: 0,
                chargedSplashDamage: 0,
                splashDmgTypes: new byte[] { 0, 0 },
                shotCooldown: 20,
                autofireCooldown: 20,
                ammoType: 0,
                colEffects: new byte[] { 4, 95 },
                muzzleEffects: new byte[] { 65, 65 },
                dmgDirTypes: new byte[] { 0, 0 },
                dmgInterp: new byte[] { 0, 0 },
                afflictions: new Affliction[] { Affliction.None, Affliction.None },
                padding21: 0,
                minCharge: 18,
                fullCharge: 30,
                ammoCost: 0,
                minChargeCost: 0,
                chargeCost: 0,
                unchargedDamage: 12,
                minChargeDamage: 12,
                chargedDamage: 12,
                headshotDamage: 16,
                minChargeHeadshotDamage: 16,
                chargedHeadshotDamage: 16,
                unchargedLifespan: 255,
                minChargeLifespan: 255,
                chargedLifespan: 255,
                speedDecay: new ushort[] { 0, 0 },
                padding42: 0,
                speedInterp: new ushort[] { 0, 0 },
                unchargedDmgDirMag: 0,
                minChargeDmgDirMag: 0,
                chargedDmgDirMag: 0,
                zoomFov: 40960,
                unchargedCylRadius: 409,
                minChargeCylRadius: 409,
                chargedCylRadius: 409,
                unchargedSpeed: 16384,
                minChargeSpeed: 16384,
                chargedSpeed: 16384,
                unchargedFinalSpeed: 0,
                minChargeFinalSpeed: 0,
                chargedFinalSpeed: 0,
                unchargedGravity: 0,
                minChargeGravity: 0,
                chargedGravity: 0,
                unchargedHoming: 0,
                minChargeHoming: 0,
                chargedHoming: 0,
                homingRange: 409600,
                homingTolerance: 3896,
                unchargedSplashRadius: 0,
                minChargeSplashRadius: 0,
                chargedSplashRadius: 0,
                unchargedDistance: 0,
                minChargeDistance: 0,
                chargedDistance: 0,
                unchargedSpread: 0,
                minChargeSpread: 0,
                chargedSpread: 0,
                unRicoLossH: 3686,
                minRicoLossH: 3686,
                chRicoLossH: 3686,
                unRicoLossV: 3686,
                minRicoLossV: 3686,
                chRicoLossV: 3686,
                projectileCount: 1,
                minChargedProjectileCount: 1,
                chargeProjectileCount: 1,
                smokeStart: 75,
                smokeMinimum: 25,
                smokeDrain: 5,
                smokeShotAmount: 10,
                smokeChargeAmount: 75
            );
        }
}
