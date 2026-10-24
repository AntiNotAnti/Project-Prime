using System;
using System.Collections.Generic;
using System.Linq;
using ProjectPrime.CaptureZone;

namespace MphRead.Mods.HeroModes;

public static class HeroRules
{
    public static bool IsZone(GameMode mode) => mode == GameMode.CaptureTheZone;
    public static bool IsPayload(GameMode mode) => mode == GameMode.PushThePayload;
    public static bool Active(GameMode mode) => IsZone(mode) || IsPayload(mode);
    public static ZoneHunter ToZone(Hunter hunter) => hunter switch
    {
        Hunter.Samus => ZoneHunter.Samus, Hunter.Kanden => ZoneHunter.Kanden,
        Hunter.Trace => ZoneHunter.Trace, Hunter.Sylux => ZoneHunter.Sylux,
        Hunter.Noxus => ZoneHunter.Noxus, Hunter.Spire => ZoneHunter.Spire,
        Hunter.Weavel => ZoneHunter.Weavel, _ => throw new ArgumentOutOfRangeException(nameof(hunter))
    };
    public static Hunter ToGame(ZoneHunter hunter) => hunter switch
    {
        ZoneHunter.Samus => Hunter.Samus, ZoneHunter.Kanden => Hunter.Kanden,
        ZoneHunter.Trace => Hunter.Trace, ZoneHunter.Sylux => Hunter.Sylux,
        ZoneHunter.Noxus => Hunter.Noxus, ZoneHunter.Spire => Hunter.Spire,
        ZoneHunter.Weavel => Hunter.Weavel, _ => throw new ArgumentOutOfRangeException(nameof(hunter))
    };
    public static HeroDefinition Definition(Hunter hunter) => HeroCatalog.For(ToZone(hunter));
    // Aether Pulse deliberately reuses the internal beam slot, not its weapon mechanics.
    public static BeamType Primary(Hunter hunter) => hunter == Hunter.Samus ? BeamType.PowerBeam : Weapons.GetAffinityBeam(hunter);
    public static uint ReduceDamage(Hunter hunter, uint damage) => CtzRules.ReducedDamage(damage, Definition(hunter).DamageReduction);
    public static bool AllowedPickup(ItemType item) => item is ItemType.HealthSmall or ItemType.HealthMedium or ItemType.HealthBig;
    private static readonly Lazy<IReadOnlyList<WeaponInfo>> Table = new(BuildWeaponTable);
    public static IReadOnlyList<WeaponInfo> WeaponTable => Table.Value;
    private static IReadOnlyList<WeaponInfo> BuildWeaponTable()
    {
        var rows = Weapons.WeaponsMP.ToArray();
        rows[(int)BeamType.PowerBeam] = rows[(int)BeamType.PowerBeam].ForHeroMode(true);
        rows[(int)BeamType.PowerBeam + 9] = rows[(int)BeamType.PowerBeam + 9].ForHeroMode(true);
        rows[(int)BeamType.Battlehammer] = rows[(int)BeamType.Battlehammer].ForHeroMode(false);
        rows[(int)BeamType.Battlehammer + 9] = rows[(int)BeamType.Battlehammer + 9].ForHeroMode(false);
        return Array.AsReadOnly(rows);
    }
    public static string WeaponDetails(Hunter hunter)
    {
        BeamType beam = Primary(hunter);
        int row = (int)beam + (Weapons.GetAffinityBeam(hunter) == beam ? 9 : 0);
        WeaponInfo w = WeaponTable[row];
        string cadence = hunter == Hunter.Weavel
            ? $"Three real rounds; {CtzRules.BurstSpacingTicks / 60.0 * 1000:0.0} ms spacing; {CtzRules.BurstRecoveryTicks / 60.0 * 1000:0.0} ms recovery."
            : $"Shot recovery {w.ShotCooldown / 30.0:0.###} s; automatic recovery {w.AutofireCooldown / 30.0:0.###} s.";
        float damageScale = hunter == Hunter.Weavel ? CtzRules.BattlehammerDamageMultiplier : 1;
        string support = hunter switch
        {
            Hunter.Noxus => $"Allied body hit: +{CtzRules.NoxusBodyHeal} HP; headshot: +{CtzRules.NoxusHeadHeal} HP. Charged freeze: +{CtzRules.NoxusFreezeHeal} HP to each ally hit; {CtzRules.NoxusFreezeCooldownTicks/60} s cooldown after release.",
            Hunter.Weavel => $"Damage reduced 25%. Each projectile heals +{CtzRules.WeavelDirectHeal} HP direct or +{CtzRules.WeavelSplashHeal} HP splash. Three heals per target per burst; direct and splash never double-count.",
            Hunter.Spire => $"Normal allied direct hit: +{CtzRules.SpireNormalHeal} HP. Charged aura: +{CtzRules.SpireTotalHeal} HP over {CtzRules.HealingAuraTicks/60} s ({CtzRules.SpireHealPerPulse}/s). Normal heals add during the aura; charged auras do not stack.",
            _ => ""
        };
        return $"{Definition(hunter).Primary}\nDirect damage {w.UnchargedDamage*damageScale:0.##}; charged {w.ChargedDamage*damageScale:0.##}; headshot {w.HeadshotDamage*damageScale:0.##}.\n"
            + cadence + "\n" + (((w.Flags & WeaponFlags.CanCharge) != 0) ? $"Full charge {w.FullCharge / 30.0:0.###} s." : "No charged shot.") + " Unlimited primary ammunition.\n"
            + $"Projectile speed {w.UnchargedSpeed / 4096.0 * 30:0.0} world units/s; gravity {w.UnchargedGravity / 4096.0:0.###}.\n"
            + $"Respawn: {CtzRules.RespawnTicks(hunter)/60} seconds.\n{support}\n"
            + "Capture the zone; leave the objective for health.\nNative alternate-form limits remain in force.";
    }
}
