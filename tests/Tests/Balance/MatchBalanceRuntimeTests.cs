using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class MatchBalanceRuntimeTests
{
    [Fact]
    public void ClassicIsAnIdentityProfile()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(Rules(balanced: false));

        Assert.Equal(GameplayBalanceProfile.Classic, balance.Profile);
        Assert.True(balance.IsClassic);
        Assert.False(balance.IsBalanced);
        foreach (Hunter hunter in Enum.GetValues<Hunter>())
        {
            HunterBalanceValues values = balance.GetHunter(hunter);
            Assert.Equal(1.0f, values.AltSpeedMultiplier);
            Assert.Equal(0, values.BoostDamageBasisDelta);
            Assert.Equal(0, values.StinglarvaDamageDelta);
            Assert.Equal(0, values.UniversalAmmoCapDelta);
            Assert.False(values.CanClimbLedges);
        }
        for (int beam = 0; beam < 9; beam++)
            Assert.Equal(WeaponBalanceValues.None,
                balance.GetWeapon((BeamType)beam));
    }

    [Fact]
    public void BalancedHunterValuesAreCompactAndEnhancedIndependent()
    {
        MatchBalanceContext balanced = MatchBalanceContext.For(
            Rules(balanced: true, enhanced: false));
        MatchBalanceContext balancedEnhanced = MatchBalanceContext.For(
            Rules(balanced: true, enhanced: true));
        MatchBalanceContext classicEnhanced = MatchBalanceContext.For(
            Rules(balanced: false, enhanced: true));

        Assert.Equal(GameplayBalanceProfile.BalancedV1, balanced.Profile);
        Assert.Equal(balanced.GetHunter(Hunter.Samus),
            balancedEnhanced.GetHunter(Hunter.Samus));
        Assert.Equal(0.90f, balanced.GetHunter(Hunter.Samus).AltSpeedMultiplier);
        Assert.Equal(-12, balanced.GetHunter(Hunter.Samus).BoostDamageBasisDelta);
        Assert.Equal(5, balanced.GetHunter(Hunter.Kanden).StinglarvaDamageDelta);
        Assert.Equal(0.95f, balanced.GetHunter(Hunter.Trace).AltSpeedMultiplier);
        Assert.Equal(-300, balanced.GetHunter(Hunter.Trace).UniversalAmmoCapDelta);
        Assert.Equal(0.95f, balanced.GetHunter(Hunter.Sylux).AltSpeedMultiplier);
        Assert.True(balanced.GetHunter(Hunter.Spire).CanClimbLedges);
        Assert.Equal(HunterBalanceValues.Classic,
            classicEnhanced.GetHunter(Hunter.Samus));
    }

    [Fact]
    public void BalanceAndEnhancedHunterAxesStayIndependentAcrossAllCombinations()
    {
        for (int balanced = 0; balanced <= 1; balanced++)
        {
            for (int enhanced = 0; enhanced <= 1; enhanced++)
            {
                MatchBalanceContext context = MatchBalanceContext.For(
                    Rules(balanced != 0, enhanced != 0));
                float expectedTraceCap = balanced != 0 ? 0.95f : 1.0f;
                Assert.Equal(balanced != 0
                    ? GameplayBalanceProfile.BalancedV1
                    : GameplayBalanceProfile.Classic, context.Profile);
                Assert.Equal(expectedTraceCap,
                    context.GetHunter(Hunter.Trace).AltSpeedMultiplier);
                Assert.Equal(expectedTraceCap,
                    HunterBalanceResolver.ResolveAltSpeedCap(100,
                        Hunter.Trace, boosting: false, context) / 100);
                // Guardian is not a Balanced V1 numeric target. Keep its
                // identity explicit across all four Balanced x Enhanced
                // combinations so future Enhanced changes cannot leak into
                // the balance profile.
                Assert.Equal(HunterBalanceValues.Classic,
                    context.GetHunter(Hunter.Guardian));
            }
        }
    }

    [Fact]
    public void BalancedMovementCapsApplyOnlyToTheNamedAlternateForms()
    {
        MatchBalanceContext classic = MatchBalanceContext.For(Rules(false));
        MatchBalanceContext balanced = MatchBalanceContext.For(Rules(true));

        Assert.Equal(100, HunterBalanceResolver.ResolveAltSpeedCap(
            100, Hunter.Trace, boosting: false, classic));
        Assert.Equal(95, HunterBalanceResolver.ResolveAltSpeedCap(
            100, Hunter.Trace, boosting: false, balanced));
        Assert.Equal(95, HunterBalanceResolver.ResolveAltSpeedCap(
            100, Hunter.Sylux, boosting: false, balanced));
        Assert.Equal(90, HunterBalanceResolver.ResolveAltSpeedCap(
            100, Hunter.Samus, boosting: false, balanced));
        Assert.Equal(100, HunterBalanceResolver.ResolveAltSpeedCap(
            100, Hunter.Kanden, boosting: false, balanced));
        Assert.Equal(100, HunterBalanceResolver.ResolveAltSpeedCap(
            100, Hunter.Samus, boosting: true, balanced));
    }

    [Fact]
    public void SamusBoostAdjustsTheBasisBeforeChargeScalingAndClamps()
    {
        MatchBalanceContext classic = MatchBalanceContext.For(Rules(false));
        MatchBalanceContext balanced = MatchBalanceContext.For(Rules(true));

        Assert.Equal(50, HunterBalanceResolver.ResolveBoostDamage(
            100, 50, 100, Hunter.Samus, classic));
        Assert.Equal(44, HunterBalanceResolver.ResolveBoostDamage(
            100, 50, 100, Hunter.Samus, balanced));
        Assert.Equal(0, HunterBalanceResolver.ResolveBoostDamage(
            12, 100, 100, Hunter.Samus, balanced));
        Assert.Equal(0, HunterBalanceResolver.ResolveBoostDamage(
            1, 100, 100, Hunter.Samus, balanced));
        Assert.Equal(50, HunterBalanceResolver.ResolveBoostDamage(
            100, 50, 100, Hunter.Trace, balanced));
    }

    [Fact]
    public void BlastPolicyPreservesClassicTraceNoxusSplashAndAddsOnlyBalancedTurretHead()
    {
        MatchBalanceContext classic = MatchBalanceContext.For(Rules(false));
        MatchBalanceContext balanced = MatchBalanceContext.For(Rules(true));

        Assert.True(BlastDamagePolicy.AllowsAltFormSplash(classic, Hunter.Trace));
        Assert.True(BlastDamagePolicy.AllowsAltFormSplash(balanced, Hunter.Trace));
        Assert.True(BlastDamagePolicy.AllowsAltFormSplash(classic, Hunter.Noxus));
        Assert.True(BlastDamagePolicy.AllowsAltFormSplash(balanced, Hunter.Noxus));
        Assert.True(BlastDamagePolicy.AllowsPlayerSplash(classic,
            Hunter.Trace, altForm: true));
        Assert.True(BlastDamagePolicy.AllowsPlayerSplash(balanced,
            Hunter.Noxus, altForm: true));
        Assert.False(BlastDamagePolicy.AllowsHalfturretHeadSplash(
            classic, Hunter.Weavel));
        Assert.True(BlastDamagePolicy.AllowsHalfturretHeadSplash(
            balanced, Hunter.Weavel));
        Assert.False(BlastDamagePolicy.AllowsHalfturretHeadSplash(
            balanced, Hunter.Trace));
    }

    [Fact]
    public void BlastPolicySelectsOneNearestVisiblePointAndHonorsDirectSuppression()
    {
        BlastDamageCandidate body = new(ownerSlot: 2, distance: 4,
            eligible: true, visible: true);
        BlastDamageCandidate head = new(ownerSlot: 2, distance: 3,
            eligible: true, visible: true);

        BlastDamageSelection selected = BlastDamagePolicy.SelectTarget(-1,
            body, head);
        Assert.True(selected.IsValid);
        Assert.True(selected.IsHalfturret);
        Assert.Equal(3, selected.Distance);

        selected = BlastDamagePolicy.SelectTarget(-1,
            new BlastDamageCandidate(2, 2, true, true),
            new BlastDamageCandidate(2, 3, true, true));
        Assert.Equal(BlastDamageTargetKind.Body, selected.Kind);

        selected = BlastDamagePolicy.SelectTarget(-1,
            new BlastDamageCandidate(2, 2, true, true),
            new BlastDamageCandidate(2, 1, true, visible: false));
        Assert.Equal(BlastDamageTargetKind.Body, selected.Kind);

        selected = BlastDamagePolicy.SelectTarget(2, body, head);
        Assert.False(selected.IsValid);
    }

    [Fact]
    public void BlastPolicyBodyOnlyCandidateIsSafeForNonWeavelOwners()
    {
        MatchBalanceContext balanced = MatchBalanceContext.For(Rules(true));
        Assert.False(BlastDamagePolicy.AllowsHalfturretHeadSplash(
            balanced, Hunter.Trace));

        BlastDamageSelection selected = BlastDamagePolicy.SelectTarget(-1,
            new BlastDamageCandidate(4, 2, eligible: true, visible: true),
            new BlastDamageCandidate(4, 0, eligible: false, visible: false));
        Assert.Equal(BlastDamageTargetKind.Body, selected.Kind);
        Assert.Equal(2, selected.Distance);
    }

    [Fact]
    public void WarmBlastPolicyDoesNotAllocate()
    {
        BlastDamageCandidate body = new(1, 2, true, true);
        BlastDamageCandidate head = new(1, 1, true, true);
        for (int i = 0; i < 32; i++)
            _ = BlastDamagePolicy.SelectTarget(-1, body, head);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            _ = BlastDamagePolicy.SelectTarget(-1, body, head);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void MatchRuntimeRefreshesItsOwnBalanceContext()
    {
        MatchRules classicRules = Rules(balanced: false);
        MatchRuntime runtime = new(classicRules);
        MatchBalanceContext original = runtime.Balance;

        Assert.True(original.IsClassic);
        MatchRules balancedRules = classicRules.With(balancedMode: true);
        runtime.ApplyRules(balancedRules);

        Assert.Same(balancedRules, runtime.Rules);
        Assert.NotSame(original, runtime.Balance);
        Assert.Same(runtime.Balance, runtime.BalanceContext);
        Assert.True(runtime.Balance.IsBalanced);
        Assert.Equal(0.90f,
            runtime.Balance.GetHunter(Hunter.Samus).AltSpeedMultiplier);

        MatchBalanceContext balancedProfile = runtime.Balance;
        runtime.ApplyRules(balancedRules.With(enhancedHunters: true));
        Assert.Same(balancedProfile, runtime.Balance);
    }

    [Fact]
    public void ChargedVoltSpawnFreezesAffinityInitialAndFinalSpeed()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(Rules(balanced: true));
        WeaponInfo weapon = Weapons.Current[(int)BeamType.VoltDriver + 9];
        using Scene scene = Scene.CreateHeadless();
        var projectile = new BeamProjectileEntity(scene);
        EquipInfo equip = new(weapon, new[] { projectile })
        {
            InfiniteAmmo = true,
            ChargeLevel = (ushort)SimTicks.From30HzFrames(weapon.FullCharge)
        };
        WeaponBalanceResolver.Apply(equip, Hunter.Kanden, balance);

        Assert.Equal(weapon.UnchargedSpeed, equip.ChargedSpeed);
        Assert.Equal(weapon.UnchargedFinalSpeed, equip.ChargedFinalSpeed);
        var owner = new BeamProjectileEntity(scene);
        BeamResultFlags result = BeamProjectileEntity.Spawn(owner, equip,
            Vector3.Zero, Vector3.UnitZ, BeamSpawnFlags.NoMuzzle,
            NodeRef.None, scene);

        Assert.Equal(BeamResultFlags.Spawned, result);
        float initialSpeed = equip.ChargedSpeed / 4096f / 2;
        float finalSpeed = equip.ChargedFinalSpeed / 4096f / 2;
        Assert.Equal(initialSpeed, projectile.InitialSpeed);
        Assert.Equal(finalSpeed, projectile.FinalSpeed);

        equip.ChargedSpeed = weapon.ChargedSpeed;
        equip.ChargedFinalSpeed = weapon.ChargedFinalSpeed;
        Assert.Equal(initialSpeed, projectile.InitialSpeed);
        Assert.Equal(finalSpeed, projectile.FinalSpeed);
    }

    [Fact]
    public void TraceUniversalAmmoCapClampsOnProfileTransition()
    {
        using Scene scene = Scene.CreateHeadless();
        PlayerEntity trace = scene.Players[0];
        SetBackingField(trace, nameof(PlayerEntity.Hunter), Hunter.Trace);
        SetBackingField(trace, nameof(PlayerEntity.Values),
            Metadata.PlayerValues[(int)Hunter.Trace]);
        trace.LoadFlags = LoadFlags.SlotActive;
        trace.EquipInfo.Beams = Array.Empty<BeamProjectileEntity>();
        trace.EquipInfo.Weapon = Weapons.Current[(int)BeamType.PowerBeam];
        trace._ammoMax[0] = trace.Values.MpAmmoCap;
        trace._ammo[0] = trace.Values.MpAmmoCap;

        MatchBalanceContext classic = scene.Match.Balance;
        scene.Match.ApplyRules(Rules(balanced: true));

        Assert.NotSame(classic, scene.Match.Balance);
        Assert.Equal(299, trace._ammoMax[0]);
        Assert.Equal(299, trace._ammo[0]);

        scene.Match.ApplyRules(Rules(balanced: false));
        Assert.Equal(599, trace._ammoMax[0]);
        Assert.Equal(299, trace._ammo[0]);
    }

    [Fact]
    public void ProfileTransitionsReapplyEquipAndInterleavedMatchesRemainIsolated()
    {
        using Scene scene = Scene.CreateHeadless();
        PlayerEntity player = scene.Players[0];
        player.LoadFlags = LoadFlags.SlotActive;
        player.EquipInfo.Beams = Array.Empty<BeamProjectileEntity>();
        player.EquipInfo.Weapon = Weapons.Current[(int)BeamType.Missile + 9];
        player.EquipInfo.ChargeLevel = 7;
        player.EquipInfo.SmokeLevel = 8;
        WeaponBalanceResolver.Apply(player.EquipInfo, Hunter.Samus,
            scene.Match.Balance);

        MatchBalanceContext classic = scene.Match.Balance;
        scene.Match.ApplyRules(Rules(balanced: false, enhanced: true));
        Assert.Same(classic, scene.Match.Balance);
        Assert.Equal((ushort)15, player.EquipInfo.ChargeCost);
        Assert.Equal((ushort)7, player.EquipInfo.ChargeLevel);
        Assert.Equal((ushort)8, player.EquipInfo.SmokeLevel);

        scene.Match.ApplyRules(Rules(balanced: true));
        MatchBalanceContext balanced = scene.Match.Balance;
        Assert.NotSame(classic, balanced);
        Assert.Equal((ushort)20, player.EquipInfo.ChargeCost);
        Assert.Equal((ushort)7, player.EquipInfo.ChargeLevel);
        Assert.Equal((ushort)8, player.EquipInfo.SmokeLevel);

        scene.Match.ApplyRules(Rules(balanced: false));
        Assert.NotSame(balanced, scene.Match.Balance);
        Assert.Equal((ushort)15, player.EquipInfo.ChargeCost);
        Assert.Equal((ushort)7, player.EquipInfo.ChargeLevel);
        Assert.Equal((ushort)8, player.EquipInfo.SmokeLevel);

        MatchRuntime isolatedClassic = new(Rules(balanced: false));
        MatchRuntime isolatedBalanced = new(Rules(balanced: true));
        EquipInfo classicEquip = Equip(Weapons.Current[(int)BeamType.Missile + 9]);
        EquipInfo balancedEquip = Equip(Weapons.Current[(int)BeamType.Missile + 9]);
        WeaponBalanceResolver.Apply(classicEquip, Hunter.Samus,
            isolatedClassic.Balance);
        WeaponBalanceResolver.Apply(balancedEquip, Hunter.Samus,
            isolatedBalanced.Balance);
        Assert.Equal((ushort)15, classicEquip.ChargeCost);
        Assert.Equal((ushort)20, balancedEquip.ChargeCost);

        WeaponBalanceResolver.Apply(balancedEquip, Hunter.Samus,
            isolatedBalanced.Balance);
        WeaponBalanceResolver.Apply(classicEquip, Hunter.Samus,
            isolatedClassic.Balance);
        Assert.Equal((ushort)15, classicEquip.ChargeCost);
        Assert.Equal((ushort)20, balancedEquip.ChargeCost);
    }

    [Fact]
    public void KandenAffinityVoltUsesOnlySpecifiedChargedFields()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(Rules(balanced: true));
        WeaponInfo weapon = Weapons.Current[(int)BeamType.VoltDriver + 9];
        EquipInfo equip = Equip(weapon);

        WeaponBalanceApplier.Apply(equip, Hunter.Kanden, balance);

        Assert.Equal(weapon.UnchargedDamage + 2, equip.UnchargedDamage);
        Assert.Equal(weapon.HeadshotDamage + 2, equip.HeadshotDamage);
        Assert.Equal(weapon.MinChargeDamage + 8, equip.MinChargeDamage);
        Assert.Equal(weapon.ChargedDamage + 8, equip.ChargedDamage);
        Assert.Equal(weapon.MinChargeHeadshotDamage, equip.MinChargeHeadshotDamage);
        Assert.Equal(weapon.ChargedHeadshotDamage, equip.ChargedHeadshotDamage);
        Assert.Equal(weapon.UnchargedSpeed, equip.ChargedSpeed);
        Assert.Equal(weapon.UnchargedFinalSpeed, equip.ChargedFinalSpeed);
    }

    [Fact]
    public void MissileMagmaulJudicatorAndBattlehammerDeltasRespectAffinity()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(Rules(balanced: true));

        WeaponInfo missile = Weapons.Current[(int)BeamType.Missile + 9];
        EquipInfo samusMissile = Equip(missile);
        WeaponBalanceResolver.Apply(samusMissile, Hunter.Samus, balance);
        Assert.Equal(missile.UnchargedDamage + 2, samusMissile.UnchargedDamage);
        Assert.Equal((ushort)15, missile.MinChargeCost);
        Assert.Equal((ushort)20, samusMissile.MinChargeCost);
        Assert.Equal((ushort)20, samusMissile.ChargeCost);
        Assert.Equal(missile.HeadshotDamage, samusMissile.HeadshotDamage);

        WeaponInfo magmaul = Weapons.Current[(int)BeamType.Magmaul];
        EquipInfo nonAffinityMagmaul = Equip(magmaul);
        WeaponBalanceResolver.Apply(nonAffinityMagmaul, Hunter.Samus, balance);
        Assert.Equal(magmaul.UnchargedDamage + 2, nonAffinityMagmaul.UnchargedDamage);
        Assert.Equal(magmaul.SplashDamage + 10, nonAffinityMagmaul.SplashDamage);
        Assert.Equal(magmaul.MinChargeDamage + 10, nonAffinityMagmaul.MinChargeDamage);
        Assert.Equal(magmaul.ChargedDamage + 10, nonAffinityMagmaul.ChargedDamage);
        Assert.Equal(magmaul.MinChargeSplashDamage + 2,
            nonAffinityMagmaul.MinChargeSplashDamage);
        Assert.Equal(magmaul.ChargedSplashDamage + 2,
            nonAffinityMagmaul.ChargedSplashDamage);

        WeaponInfo spireMagmaul = Weapons.Current[(int)BeamType.Magmaul + 9];
        EquipInfo affinityMagmaul = Equip(spireMagmaul);
        WeaponBalanceResolver.Apply(affinityMagmaul, Hunter.Spire, balance);
        Assert.Equal(spireMagmaul.MinChargeDamage + 12,
            affinityMagmaul.MinChargeDamage);
        Assert.Equal(spireMagmaul.ChargedDamage + 12,
            affinityMagmaul.ChargedDamage);
        Assert.Equal(spireMagmaul.MinChargeSplashDamage + 12,
            affinityMagmaul.MinChargeSplashDamage);
        Assert.Equal(spireMagmaul.ChargedSplashDamage + 12,
            affinityMagmaul.ChargedSplashDamage);

        WeaponInfo judicator = Weapons.Current[(int)BeamType.Judicator + 9];
        EquipInfo noxusJudicator = Equip(judicator);
        WeaponBalanceResolver.Apply(noxusJudicator, Hunter.Noxus, balance);
        Assert.Equal(judicator.UnchargedDamage + 2, noxusJudicator.UnchargedDamage);
        Assert.Equal(judicator.HeadshotDamage + 2, noxusJudicator.HeadshotDamage);
        Assert.Equal(judicator.ChargedDamage, noxusJudicator.ChargedDamage);
        Assert.Equal(judicator.ChargedHeadshotDamage,
            noxusJudicator.ChargedHeadshotDamage);

        WeaponInfo hammer = Weapons.Current[(int)BeamType.Battlehammer];
        EquipInfo normalHammer = Equip(hammer);
        WeaponBalanceResolver.Apply(normalHammer, Hunter.Samus, balance);
        Assert.Equal(hammer.UnchargedDamage + 4, normalHammer.UnchargedDamage);
        Assert.Equal(hammer.SplashDamage + 6, normalHammer.SplashDamage);
        Assert.Equal(hammer.UnchargedSplashRadius + 8192,
            normalHammer.UnchargedSplashRadius);

        WeaponInfo affinityHammer = Weapons.Current[(int)BeamType.Battlehammer + 9];
        EquipInfo weavelHammer = Equip(affinityHammer);
        WeaponBalanceResolver.Apply(weavelHammer, Hunter.Weavel, balance);
        Assert.Equal(affinityHammer.UnchargedDamage + 4, weavelHammer.UnchargedDamage);
        Assert.Equal(affinityHammer.SplashDamage + 6, weavelHammer.SplashDamage);
        Assert.Equal(affinityHammer.UnchargedSplashRadius,
            weavelHammer.UnchargedSplashRadius);
    }

    [Fact]
    public void HalfturretUsesNonAffinityBattlehammerResolution()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(Rules(balanced: true));
        WeaponInfo weapon = Weapons.Current[(int)BeamType.Battlehammer];
        EquipInfo turret = Equip(weapon);

        WeaponBalanceResolver.Apply(turret, Hunter.Weavel, balance);

        Assert.Equal(weapon.UnchargedDamage + 4, turret.UnchargedDamage);
        Assert.Equal(weapon.SplashDamage + 6, turret.SplashDamage);
        Assert.Equal(weapon.UnchargedSplashRadius + 8192,
            turret.UnchargedSplashRadius);

        using Scene scene = Scene.CreateHeadless();
        var projectile = new BeamProjectileEntity(scene);
        turret.Beams = new[] { projectile };
        turret.InfiniteAmmo = true;
        var owner = new BeamProjectileEntity(scene);
        BeamResultFlags result = BeamProjectileEntity.Spawn(owner, turret,
            Vector3.Zero, Vector3.UnitZ,
            BeamSpawnFlags.NoMuzzle, NodeRef.None, scene);

        Assert.Equal(BeamResultFlags.Spawned, result);
        Assert.Equal((weapon.UnchargedSplashRadius + 8192) / 4096f,
            projectile.SplashRadius);
    }

    [Fact]
    public void SpawnUsesEffectiveChargedMissileCostAndConsumesOnlyWhenSpawned()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(Rules(balanced: true));
        WeaponInfo weapon = Weapons.Current[(int)BeamType.Missile + 9];
        using Scene scene = Scene.CreateHeadless();
        var projectile = new BeamProjectileEntity(scene);
        int ammo = 19;
        EquipInfo equip = new(weapon, new[] { projectile })
        {
            GetAmmo = () => ammo,
            SetAmmo = value => ammo = value,
            ChargeLevel = (ushort)SimTicks.From30HzFrames(weapon.FullCharge)
        };
        WeaponBalanceResolver.Apply(equip, Hunter.Samus, balance);

        var owner = new BeamProjectileEntity(scene);
        BeamResultFlags result = BeamProjectileEntity.Spawn(owner, equip,
            Vector3.Zero, Vector3.UnitZ,
            BeamSpawnFlags.NoMuzzle, NodeRef.None, scene);

        Assert.Equal(BeamResultFlags.NoSpawn, result);
        Assert.Equal(19, ammo);

        ammo = 20;
        result = BeamProjectileEntity.Spawn(owner, equip,
            Vector3.Zero, Vector3.UnitZ,
            BeamSpawnFlags.NoMuzzle, NodeRef.None, scene);

        Assert.Equal(BeamResultFlags.Spawned, result);
        Assert.Equal(0, ammo);
    }

    [Fact]
    public void ResetRuntimeOverridesRestoresCanonicalFallbacksAndState()
    {
        WeaponInfo weapon = Weapons.Current[(int)BeamType.Missile];
        var beams = Array.Empty<BeamProjectileEntity>();
        Func<int> getAmmo = () => 17;
        Action<int> setAmmo = _ => { };
        EquipInfo equip = new(weapon, beams)
        {
            Zoomed = true,
            ChargeLevel = 11,
            SmokeLevel = 12,
            GetAmmo = getAmmo,
            SetAmmo = setAmmo,
            InfiniteAmmo = true,
            UnchargedDamage = 1,
            MinChargeDamage = 2,
            ChargedDamage = 3,
            HeadshotDamage = 4,
            MinChargeHeadshotDamage = 5,
            ChargedHeadshotDamage = 6,
            SplashDamage = 7,
            MinChargeSplashDamage = 8,
            ChargedSplashDamage = 9,
            HomingTolerance = 10,
            AmmoCost = 11,
            MinChargeCost = 12,
            ChargeCost = 13,
            UnchargedSpeed = 14,
            MinChargeSpeed = 15,
            ChargedSpeed = 16,
            UnchargedFinalSpeed = 17,
            MinChargeFinalSpeed = 18,
            ChargedFinalSpeed = 19,
            UnchargedSplashRadius = 20,
            MinChargeSplashRadius = 21,
            ChargedSplashRadius = 22
        };

        equip.ResetRuntimeOverrides();

        Assert.Same(weapon, equip.Weapon);
        Assert.Same(beams, equip.Beams);
        Assert.Same(getAmmo, equip.GetAmmo);
        Assert.Same(setAmmo, equip.SetAmmo);
        Assert.True(equip.Zoomed);
        Assert.Equal((ushort)11, equip.ChargeLevel);
        Assert.Equal((ushort)12, equip.SmokeLevel);
        Assert.False(equip.HasExplicitChargedDamage);
        Assert.Equal(weapon.UnchargedDamage, equip.UnchargedDamage);
        Assert.Equal(weapon.MinChargeDamage, equip.MinChargeDamage);
        Assert.Equal(weapon.ChargedDamage, equip.ChargedDamage);
        Assert.Equal(weapon.AmmoCost, equip.AmmoCost);
        Assert.Equal(weapon.ChargedSpeed, equip.ChargedSpeed);
        Assert.Equal(weapon.ChargedSplashRadius, equip.ChargedSplashRadius);
    }

    [Fact]
    public void WarmResolverDoesNotAllocate()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(Rules(balanced: true));
        EquipInfo equip = Equip(Weapons.Current[(int)BeamType.Magmaul + 9]);
        for (int warmup = 0; warmup < 20; warmup++)
            WeaponBalanceResolver.Apply(equip, Hunter.Spire, balance);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 10000; iteration++)
            WeaponBalanceResolver.Apply(equip, Hunter.Spire, balance);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static MatchRules Rules(bool balanced, bool enhanced = false)
        => new(MatchMode.Battle, "balance", balancedMode: balanced,
            enhancedHunters: enhanced);

    private static EquipInfo Equip(WeaponInfo weapon)
        => new(weapon, Array.Empty<BeamProjectileEntity>());

    private static void SetBackingField<T>(PlayerEntity player, string property,
        T value)
    {
        FieldInfo field = typeof(PlayerEntity).GetField(
            $"<{property}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(player, value);
    }
}
