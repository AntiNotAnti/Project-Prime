using System;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Detached mechanics/table tests only. Stock metadata is read, not executed;
// constructed overlays supply no source, collision, payment or native parity.
internal static class CombatFamilyPolicyTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Combat family policy: " + label);
        _checks++;
    }
    private static CombatFamilyFacts Plain(byte weapon = (byte)BeamType.PowerBeam,
        LagCompensationMode timing = LagCompensationMode.ProjectileCatchUp)
        => new(true, weapon, new WeaponLagPolicy(timing, true, false,
            timing is LagCompensationMode.HistoricalTrace or LagCompensationMode.ProjectileCatchUp
                or LagCompensationMode.HomingProjectileCatchUp or LagCompensationMode.Continuous,
            32, true, true), false, false, false, false, false,
            false, false, false, false, false, false);
    private static void Requires(CombatFamilyCapability cap, CombatProofRequirement required, string label)
        => Check((cap.Requirements & required) == required, label);

    private static WeaponInfo ConstructedPartial() => new(
        beam: BeamType.PowerBeam, beamKind: BeamType.PowerBeam,
        drawFuncIds: new byte[] { 0, 0 }, colors: new ushort[] { 0, 0 }, priority: 0,
        flags: WeaponFlags.CanCharge | WeaponFlags.PartialCharge | WeaponFlags.RicochetCharged | WeaponFlags.AoeCharged,
        splashDamage: 0, minChargeSplashDamage: 5, chargedSplashDamage: 10,
        splashDmgTypes: new byte[] { 0, 0 }, shotCooldown: 1, autofireCooldown: 1, ammoType: 0,
        colEffects: new byte[] { 0, 0 }, muzzleEffects: new byte[] { 0, 0 },
        dmgDirTypes: new byte[] { 0, 0 }, dmgInterp: new byte[] { 0, 0 },
        afflictions: new[] { Affliction.None, Affliction.Burn }, padding21: 0,
        minCharge: 10, fullCharge: 20, ammoCost: 1, minChargeCost: 2, chargeCost: 3,
        unchargedDamage: 6, minChargeDamage: 12, chargedDamage: 20,
        headshotDamage: 8, minChargeHeadshotDamage: 16, chargedHeadshotDamage: 25,
        unchargedLifespan: 1, minChargeLifespan: 2, chargedLifespan: 3,
        speedDecay: new ushort[] { 0, 1 }, padding42: 0, speedInterp: new ushort[] { 0, 0 },
        unchargedDmgDirMag: 0, minChargeDmgDirMag: 0, chargedDmgDirMag: 0, zoomFov: 0,
        unchargedCylRadius: 1, minChargeCylRadius: 2, chargedCylRadius: 3,
        unchargedSpeed: 4096, minChargeSpeed: 8192, chargedSpeed: 12288,
        unchargedFinalSpeed: 4096, minChargeFinalSpeed: 4096, chargedFinalSpeed: 4096,
        unchargedGravity: 0, minChargeGravity: 4096, chargedGravity: 8192,
        unchargedHoming: 0, minChargeHoming: 4096, chargedHoming: 8192,
        homingRange: 4096, homingTolerance: 4096,
        unchargedSplashRadius: 0, minChargeSplashRadius: 4096, chargedSplashRadius: 8192,
        unchargedDistance: 4096, minChargeDistance: 8192, chargedDistance: 12288,
        unchargedSpread: 0, minChargeSpread: 0, chargedSpread: 0,
        unRicoLossH: 0, minRicoLossH: 0, chRicoLossH: 0,
        unRicoLossV: 0, minRicoLossV: 0, chRicoLossV: 0,
        projectileCount: 1, minChargedProjectileCount: 2, chargeProjectileCount: 3,
        smokeStart: 0, smokeMinimum: 0, smokeDrain: 0, smokeShotAmount: 0, smokeChargeAmount: 0,
        description: "CONSTRUCTED partial-boundary metadata; never emitted");

    private static void ConstructedBoundary()
    {
        var metadata = ConstructedPartial();
        var below = CombatFamilyPolicies.From(metadata, 19);
        var minimum = CombatFamilyPolicies.From(metadata, 20);
        var above = CombatFamilyPolicies.From(metadata, 21);
        Check(!below.Charged && !below.Area && !below.Ricochet && !below.Effects && !below.VariableSpeed,
            "constructed below-minimum metadata uses uncharged flags and arrays");
        Check(minimum.Charged && !minimum.ChargedAmounts && minimum.Area && minimum.Ricochet
            && minimum.Effects && minimum.VariableSpeed,
            "constructed exact minimum switches charged flags, affliction and speed-decay arrays");
        Check(!minimum.Homing && !minimum.Gravity && !minimum.Splash && !minimum.MultiComponent,
            "constructed exact minimum retains uncharged numeric amounts despite charged flags");
        Check(above.ChargedAmounts && above.Homing && above.Gravity && above.Splash && above.MultiComponent,
            "constructed next step switches numeric homing, gravity, splash and projectile amounts");
    }

    private static void StockMetadata()
    {
        Check(Weapons.WeaponsMP.Count >= 18, "stock MP normal/affinity rows are present");
        foreach (var weapon in Weapons.WeaponsMP)
        foreach (int level in new[] { 0, weapon.FullCharge * 2 })
        {
            var equip = new EquipInfo(weapon, Array.Empty<BeamProjectileEntity>()) {
                ChargeLevel = checked((ushort)level) };
            var facts = CombatFamilyPolicies.From(weapon, level);
            Check(facts == CombatFamilyPolicies.From(equip),
                weapon.Description + "/" + level + " equip and detached metadata agree");
            Check(facts.Known && facts.Weapon == (byte)weapon.Beam
                && facts.Timing == WeaponLagPolicies.Resolve(equip),
                weapon.Description + "/" + level + " retains native weapon and timing resolver");
            bool canCharge = weapon.Flags.TestFlag(WeaponFlags.CanCharge);
            bool partial = weapon.Flags.TestFlag(WeaponFlags.PartialCharge);
            bool charged = canCharge && level >= (partial ? weapon.MinCharge : weapon.FullCharge) * 2;
            bool amounts = charged && (!partial || level > weapon.MinCharge * 2);
            Check(facts.Charged == charged && facts.ChargedAmounts == amounts,
                weapon.Description + "/" + level + " native charge flag and amount boundary stay distinct");
            var cap = CombatFamilyPolicies.Classify(facts);
            Check(cap.Fresh == CombatFamilySupport.Shadow,
                weapon.Description + "/" + level + " known stock remains missing-proof shadow for fresh claims");
            Requires(cap, CombatProofRequirement.Source | CombatProofRequirement.Funding
                | CombatProofRequirement.HistoricalPlayers | CombatProofRequirement.DynamicWorld
                | CombatProofRequirement.NativePointHead | CombatProofRequirement.LegalDamageEffects
                | CombatProofRequirement.ClosedOrdering | CombatProofRequirement.TargetLife,
                weapon.Description + "/" + level + " known metadata never substitutes for legal payment facts");
            bool receipt = weapon.Beam is BeamType.PowerBeam or BeamType.Imperialist;
            Check(CombatFamilyPolicies.PaidReceiptFamily((byte)weapon.Beam) == receipt
                && cap.PaidReceipt == (receipt ? CombatFamilySupport.Eligible : CombatFamilySupport.Unsupported),
                weapon.Description + "/" + level + " exact paid-receipt support remains narrow independent of charge flight");
            bool native = weapon.Beam == BeamType.Imperialist;
            Check(CombatFamilyPolicies.NativeContactShadowFamily((byte)weapon.Beam) == native
                && cap.NativeContact == (native ? CombatFamilySupport.Shadow : CombatFamilySupport.Unsupported),
                weapon.Description + "/" + level + " only stock Imperialist native contact is shadow-observed");
        }
        foreach (var weapon in Weapons.WeaponsMP.Where(w => w.Flags.TestFlag(WeaponFlags.CanCharge)
            && w.Flags.TestFlag(WeaponFlags.PartialCharge)))
        {
            int minimum = weapon.MinCharge * 2;
            var below = CombatFamilyPolicies.From(weapon, minimum - 1);
            var boundary = CombatFamilyPolicies.From(weapon, minimum);
            var above = CombatFamilyPolicies.From(weapon, minimum + 1);
            Check(!below.Charged && !below.ChargedAmounts && boundary.Charged
                && !boundary.ChargedAmounts && above.Charged && above.ChargedAmounts,
                weapon.Description + " partial minimum activates flag before charged amounts");
        }
        var affinity = Weapons.WeaponsMP.Single(w => w.Beam == BeamType.PowerBeam
            && w.UnchargedHoming == 0 && w.ChargedHoming > 0);
        int min = affinity.MinCharge * 2;
        var atMin = CombatFamilyPolicies.From(affinity, min);
        var afterMin = CombatFamilyPolicies.From(affinity, min + 1);
        Check(!atMin.Homing && atMin.Timing.Mode == LagCompensationMode.ProjectileCatchUp
            && afterMin.Homing && afterMin.Timing.Mode == LagCompensationMode.HomingProjectileCatchUp,
            "affinity PB minimum retains uncharged flight; next charge step requires homing history");
        Requires(CombatFamilyPolicies.Classify(afterMin), CombatProofRequirement.CurveHistory
            | CombatProofRequirement.TargetLife | CombatProofRequirement.SweptFlight
            | CombatProofRequirement.FlightClock, "affinity PB homing cannot borrow an endpoint-only proof");
        var dormant = Weapons.WeaponsMP.First(w => !w.Flags.TestFlag(WeaponFlags.CanCharge)
            && w.UnchargedHoming == 0 && w.ChargedHoming > 0);
        var zero = CombatFamilyPolicies.From(dormant, 0);
        var full = CombatFamilyPolicies.From(dormant, dormant.FullCharge * 2);
        Check(zero == full && !full.Charged && !full.ChargedAmounts && !full.Homing,
            "stock non-chargeable dormant charged metadata never activates homing or charge state");
    }

    private static void MechanicsUnions()
    {
        var basic = Plain();
        var splash = CombatFamilyPolicies.Classify(basic with { Splash = true });
        var area = CombatFamilyPolicies.Classify(basic with { Area = true,
            Timing = WeaponLagPolicies.CurrentVolume with { Mode = LagCompensationMode.AreaHistorical } });
        Requires(splash, CombatProofRequirement.AreaOcclusion,
            "splash requires occlusion without an independent AOE flag");
        Requires(area, CombatProofRequirement.AreaOcclusion,
            "independent AOE requires occlusion without splash metadata");
        Requires(CombatFamilyPolicies.Classify(basic with { Homing = true,
            Timing = WeaponLagPolicies.CurrentVolume }),
            CombatProofRequirement.SweptFlight | CombatProofRequirement.FlightClock
                | CombatProofRequirement.CurveHistory | CombatProofRequirement.TargetLife,
            "constructed homing retains flight and curve obligations even when timing says current volume");
        var overlays = new[] {
            basic with { Homing = true }, basic with { Gravity = true },
            basic with { VariableSpeed = true }, basic with { Ricochet = true },
            basic with { Splash = true }, basic with { Area = true },
            basic with { Effects = true }, basic with { MultiComponent = true },
            basic with { Child = true } };
        var union = (CombatProofRequirement)0;
        foreach (var facts in overlays)
        {
            var cap = CombatFamilyPolicies.Classify(facts); union |= cap.Requirements;
            Check(cap.Fresh == CombatFamilySupport.Shadow,
                "constructed stock-mechanics overlay cannot promote a fresh claim");
        }
        var combined = CombatFamilyPolicies.Classify(basic with { Homing = true, Gravity = true,
            VariableSpeed = true, Ricochet = true, Splash = true, Area = true, Effects = true,
            MultiComponent = true, Child = true });
        Requires(combined, union, "combined child/homing/ricochet/effects preserves every independent requirement");
        Requires(combined, CombatProofRequirement.CurveHistory | CombatProofRequirement.BounceLineage
            | CombatProofRequirement.ComponentLineage
            | CombatProofRequirement.AreaOcclusion | CombatProofRequirement.TimedEffects
            | CombatProofRequirement.SweptFlight | CombatProofRequirement.FlightClock,
            "combined overlay retains curve, lineage, area, effect and flight obligations");
        var continuous = CombatFamilyPolicies.Classify(Plain((byte)BeamType.ShockCoil,
            LagCompensationMode.Continuous) with { Continuous = true, Homing = true, Effects = true });
        Requires(continuous, CombatProofRequirement.ContinuousPhase | CombatProofRequirement.CappedHealing
            | CombatProofRequirement.TargetLife | CombatProofRequirement.ClosedOrdering,
            "continuous contact retains exact phase, capped healing, target life and ordering");
        Check(continuous.Fresh == CombatFamilySupport.Shadow
            && continuous.PaidReceipt == CombatFamilySupport.Unsupported,
            "continuous phases cannot borrow PB/Imperialist paid receipt policy");
        Requires(CombatFamilyPolicies.Classify(basic with { Child = true }),
            CombatProofRequirement.ComponentLineage | CombatProofRequirement.OwnerEntityLife,
            "child identity retains independent component lineage and owner/entity life");
        Requires(CombatFamilyPolicies.Classify(basic with { MultiComponent = true }),
            CombatProofRequirement.ComponentLineage, "multiple primary components retain independent lineage");
        foreach (var facts in new[] { basic with { Turret = true },
            basic with { AltOrBomb = true } })
        {
            var cap = CombatFamilyPolicies.Classify(facts);
            Requires(cap, CombatProofRequirement.OwnerEntityLife,
                "turret/alt entity mechanics retain owner/entity lifetime proof");
            Check(cap.Fresh != CombatFamilySupport.Eligible,
                "owner/entity lifetime facts cannot imply an ordered fresh payment");
        }
    }

    private static void FailClosedAndOrdering()
    {
        foreach (var timing in Enum.GetValues<LagCompensationMode>())
        foreach (byte weapon in new[] { (byte)BeamType.PowerBeam, (byte)BeamType.Imperialist })
        {
            var cap = CombatFamilyPolicies.Classify(Plain(weapon, timing));
            Check(cap.Fresh != CombatFamilySupport.Eligible,
                "neither endpoint nor any existing timing policy grants fresh eligibility");
            var claim = new HitClaimPacket { AuthorityEpoch = 11, MatchId = 7,
                ShooterGeneration = 2, ShooterLifeId = 3, ShotId = 19, ComponentId = 1,
                ClaimId = 19, VictimSlot = 1, VictimGeneration = 4, VictimLifeId = 5,
                Beam = weapon, Flags = HitClaimPacket.FlagDirect, Frame = 99,
                AckFrame = 90, LaunchFrame = 90, Damage = 12 };
            var proof = new ShotProofDecision(ShotProofStatus.Verified, ShotProofReason.Verified,
                ShotProofFamily.Straight, ShotProofReason.Verified, ShotProofReason.Verified);
            var result = CombatFastDecisionFunction.Receipt(claim, default, proof);
            Check(result.State == FastDecisionState.LegacyRequired
                && result.Reason == FastDecisionReason.OrderingDeferred && !result.Eligible
                && !result.WouldPay && !result.WouldReject,
                "complete constructed source/geometry proof still cannot pay a fresh claim");
        }
        var unknown = CombatFamilyPolicies.From((WeaponInfo?)null, 0);
        Check(!unknown.Known, "absent mechanics are explicitly unknown");
        foreach (var facts in new[] { unknown, Plain(byte.MaxValue),
            CombatFamilyPolicies.From(Weapons.WeaponsMP[4], -1),
            CombatFamilyPolicies.From(Weapons.WeaponsMP[4], ushort.MaxValue + 1),
            Plain((byte)BeamType.Imperialist, LagCompensationMode.HistoricalTrace) with { Known = false },
            Plain((byte)BeamType.Imperialist, LagCompensationMode.HistoricalTrace) with { Custom = true } })
        {
            var cap = CombatFamilyPolicies.Classify(facts);
            Check(cap.Fresh == CombatFamilySupport.Unsupported
                && cap.PaidReceipt == CombatFamilySupport.Unsupported
                && cap.NativeContact == CombatFamilySupport.Unsupported,
                "unknown/custom Imperialist alias cannot inherit stock eligibility");
            Requires(cap, facts.Custom ? CombatProofRequirement.CustomDefinition | CombatProofRequirement.NetworkAdapter
                : CombatProofRequirement.UnknownMechanics,
                "unsupported mechanics retain explicit unknown or custom-definition/adapter obligations");
        }
    }

    internal static int Run()
    {
        try
        {
            _checks = 0; StockMetadata(); ConstructedBoundary(); MechanicsUnions(); FailClosedAndOrdering();
            Console.WriteLine($"PASS: {_checks} detached combat family policy assertions.");
            Console.WriteLine("SCOPE: stock metadata charge boundaries and explicitly constructed overlay/unknown facts; no native source, geometry, payment, UDP, fresh settlement, trade or performance certificate.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
