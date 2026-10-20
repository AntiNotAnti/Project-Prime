using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

[Flags]
internal enum CombatProofRequirement : uint
{
    Source = 1, Funding = 2, HistoricalPlayers = 4, DynamicWorld = 8,
    NativePointHead = 16, LegalDamageEffects = 32, ClosedOrdering = 64,
    SweptFlight = 128, FlightClock = 256, TargetLife = 512, CurveHistory = 1024,
    BounceLineage = 2048, AreaOcclusion = 4096, TimedEffects = 8192,
    ContinuousPhase = 16384, CappedHealing = 32768, OwnerEntityLife = 65536,
    CustomDefinition = 131072, NetworkAdapter = 262144, UnknownMechanics = 524288,
    ComponentLineage = 1048576
}
internal enum CombatFamilySupport : byte { Unsupported, Shadow, Eligible }
internal readonly record struct CombatFamilyFacts(bool Known, byte Weapon, WeaponLagPolicy Timing,
    bool Charged, bool ChargedAmounts, bool Homing, bool Gravity, bool VariableSpeed,
    bool Ricochet, bool Splash, bool Area, bool Effects, bool Continuous, bool MultiComponent,
    bool Child = false, bool Turret = false, bool AltOrBomb = false, bool Custom = false);
internal readonly record struct CombatFamilyCapability(ShotProofFamily Family,
    CombatProofRequirement Requirements, CombatFamilySupport Fresh,
    CombatFamilySupport PaidReceipt, CombatFamilySupport NativeContact);

// A mechanics/capability table, never an impact or ordering certificate. Timing
// policies do not prove dynamic LOS. Metadata snapshots describe native variants;
// context/derived-emission overlays remain independent and must not be discarded
// by the primary-family precedence. Only NCX04's existing paid receipt is Eligible.
internal static class CombatFamilyPolicies
{
    private readonly record struct Registration(byte Weapon, bool PaidReceipt, bool NativeShadow);
    private static readonly Registration[] Certified = [
        new((byte)BeamType.PowerBeam, true, false),
        new((byte)BeamType.Imperialist, true, true)
    ];
    private static WeaponInfo? NativeMechanics(byte weapon)
    {
        for (int i = 0; i < Weapons.WeaponsMP.Count; i++)
            if ((byte)Weapons.WeaponsMP[i].Beam == weapon) return Weapons.WeaponsMP[i];
        return null;
    }
    internal static bool PaidReceiptFamily(byte weapon)
    {
        foreach (var row in Certified) if (row.Weapon == weapon) return row.PaidReceipt;
        return false;
    }
    internal static bool NativeContactShadowFamily(byte weapon)
    {
        foreach (var row in Certified)
            if (row.Weapon == weapon && row.NativeShadow && NativeMechanics(weapon) is { } mechanics)
                return WeaponLagPolicies.Resolve(mechanics).Mode == LagCompensationMode.HistoricalTrace;
        return false;
    }
    internal static CombatFamilyFacts From(WeaponInfo? mechanics, int chargeLevel)
    {
        if (mechanics == null || chargeLevel < 0 || chargeLevel > ushort.MaxValue)
            return new(false, mechanics == null ? byte.MaxValue : (byte)mechanics.Beam,
                WeaponLagPolicies.Resolve(mechanics), false, false, false, false, false,
                false, false, false, false, false, false);
        return From(new EquipInfo { Weapon = mechanics, ChargeLevel = (ushort)chargeLevel });
    }
    internal static CombatFamilyFacts From(EquipInfo equip)
    {
        var weapon = equip.Weapon;
        var timing = WeaponLagPolicies.Resolve(equip);
        if (weapon == null || weapon.Afflictions.Count < 2 || weapon.SpeedDecayTimes.Count < 2)
            return new(false, weapon == null ? byte.MaxValue : (byte)weapon.Beam, timing,
                false, false, false, false, false, false, false, false, false, false, false,
                Custom: equip.CustomShot != null);
        bool canCharge = weapon.Flags.TestFlag(WeaponFlags.CanCharge);
        bool partial = weapon.Flags.TestFlag(WeaponFlags.PartialCharge);
        bool charged = canCharge && equip.ChargeLevel >= (partial ? weapon.MinCharge : weapon.FullCharge) * 2;
        // Spawn selects flag/affliction arrays at charged==true, but its amount
        // helper still selects UNCHARGED amounts at exactly partial minimum.
        float pct = !charged ? 0 : partial
            ? (equip.ChargeLevel - weapon.MinCharge * 2) / (float)(weapon.FullCharge * 2 - weapon.MinCharge * 2) : 1;
        bool known = float.IsFinite(pct) && NativeMechanics((byte)weapon.Beam) != null;
        float Amount(int basic, int minimum, int full) => pct <= 0 ? basic : minimum + (full - minimum) * pct;
        bool ricochet = weapon.Flags.TestFlag(charged ? WeaponFlags.RicochetCharged : WeaponFlags.RicochetUncharged)
            || (charged ? weapon.ChargedRicochetWeapon : weapon.UnchargedRicochetWeapon) != null;
        bool area = weapon.Flags.TestFlag(charged ? WeaponFlags.AoeCharged : WeaponFlags.AoeUncharged);
        return new(known, (byte)weapon.Beam, timing, charged, pct > 0,
            Amount(weapon.UnchargedHoming, weapon.MinChargeHoming, weapon.ChargedHoming) > 0,
            Amount(weapon.UnchargedGravity, weapon.MinChargeGravity, weapon.ChargedGravity) != 0,
            weapon.SpeedDecayTimes[charged ? 1 : 0] > 0,
            ricochet, (int)Amount(weapon.SplashDamage, weapon.MinChargeSplashDamage, weapon.ChargedSplashDamage) > 0
                && Amount(weapon.UnchargedSplashRadius, weapon.MinChargeSplashRadius, weapon.ChargedSplashRadius) > 0,
            area, weapon.Afflictions[charged ? 1 : 0] != Affliction.None,
            weapon.Flags.TestFlag(WeaponFlags.Continuous),
            (int)Amount(weapon.Projectiles, weapon.MinChargeProjectiles, weapon.ChargedProjectiles) > 1,
            Custom: equip.CustomShot != null);
    }
    internal static CombatFamilyCapability Classify(in CombatFamilyFacts facts)
    {
        var family = facts.AltOrBomb ? ShotProofFamily.BombOrMelee : NetTimedSourceCapabilities.Family(
            facts.Continuous, facts.Turret, facts.Child, facts.Homing, facts.Ricochet, facts.Weapon);
        var required = CombatProofRequirement.Source | CombatProofRequirement.Funding
            | CombatProofRequirement.HistoricalPlayers | CombatProofRequirement.DynamicWorld
            | CombatProofRequirement.NativePointHead | CombatProofRequirement.LegalDamageEffects
            | CombatProofRequirement.ClosedOrdering | CombatProofRequirement.TargetLife;
        if (facts.Timing.ProjectileCatchUp && facts.Timing.Mode != LagCompensationMode.HistoricalTrace && !facts.Continuous
            || facts.Homing && !facts.Continuous || facts.Gravity || facts.VariableSpeed || facts.Ricochet || facts.Child)
            required |= CombatProofRequirement.SweptFlight | CombatProofRequirement.FlightClock;
        // Continuous tracking selects a pulse target, not a projectile curve.
        // Its separate phase/target/healing requirements remain below.
        if (facts.Homing && !facts.Continuous) required |= CombatProofRequirement.CurveHistory;
        if (facts.Ricochet) required |= CombatProofRequirement.BounceLineage | CombatProofRequirement.ComponentLineage;
        if (facts.Child || facts.MultiComponent) required |= CombatProofRequirement.ComponentLineage;
        if (facts.Splash || facts.Area) required |= CombatProofRequirement.AreaOcclusion;
        if (facts.Effects) required |= CombatProofRequirement.TimedEffects;
        if (facts.Continuous) required |= CombatProofRequirement.ContinuousPhase
            | CombatProofRequirement.TargetLife | CombatProofRequirement.CappedHealing;
        if (facts.Child || facts.Turret || facts.AltOrBomb) required |= CombatProofRequirement.OwnerEntityLife;
        if (facts.Custom) required |= CombatProofRequirement.CustomDefinition | CombatProofRequirement.NetworkAdapter;
        bool known = facts.Known && NativeMechanics(facts.Weapon) != null;
        if (!known) required |= CombatProofRequirement.UnknownMechanics;
        bool dependent = facts.Custom || facts.Child || facts.Turret || facts.AltOrBomb || facts.Continuous;
        bool nativeShadow = known && !dependent && NativeContactShadowFamily(facts.Weapon)
            && !facts.Homing && !facts.Gravity && !facts.VariableSpeed && !facts.Ricochet
            && !facts.Splash && !facts.Area && !facts.Effects && !facts.MultiComponent;
        return new(family, required,
            known && !facts.Custom ? CombatFamilySupport.Shadow : CombatFamilySupport.Unsupported,
            known && !dependent && PaidReceiptFamily(facts.Weapon) ? CombatFamilySupport.Eligible : CombatFamilySupport.Unsupported,
            nativeShadow ? CombatFamilySupport.Shadow : CombatFamilySupport.Unsupported);
    }
    internal static object Capture()
    {
        var rows = new List<object>(); // report-time only; no hot-path registry growth
        int row = 0;
        foreach (var weapon in Weapons.WeaponsMP)
        {
            foreach (int level in new[] { 0, weapon.MinCharge * 2, weapon.MinCharge * 2 + 1, weapon.FullCharge * 2 })
            {
                var facts = From(weapon, level); var capability = Classify(facts);
                rows.Add(new { Row = row, facts.Weapon, ChargeLevel = level, Facts = facts, Capability = capability });
            }
            row++;
        }
        return new { Schema = 1, Rows = rows,
            Scope = "Nominal native metadata snapshots and independent proof obligations, not satisfied emission/geometry/funding/order certificates. Fresh Shadow means observational legacy fallback with missing proof, not a completed family evaluator. Context/derived/balanced overlays need actual native facts. No fresh family promoted; only exact existing primary direct paid receipts are Eligible. Native first-segment Imperialist remains Shadow, with existing source/witness/legal guards. Custom live network adapter absent; never alias custom definitions to native weapons." };
    }
}
