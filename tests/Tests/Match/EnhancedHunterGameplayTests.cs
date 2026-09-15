using System;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class EnhancedHunterGameplayTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AffinityPickupsAndEnhancedBehaviorAreIndependent(
        bool affinityWeapons, bool enhancedHunters)
    {
        MatchRules rules = Rules(enhancedHunters, affinityWeapons);
        Assert.Equal(affinityWeapons, rules.AffinityWeapons);
        Assert.Equal(enhancedHunters, rules.EnhancedHunters);
    }

    [Fact]
    public void DisabledMatchCapturesCanonicalZeroAndCannotShareTargetState()
    {
        using var enabled = new MatchBaselineTests.State();
        using var disabled = new MatchBaselineTests.State();
        enabled.Configure(GameMode.Battle).ApplyRules(Rules(true));
        disabled.Configure(GameMode.Battle).ApplyRules(Rules(false));
        PlayerEntity marked = Prepare(enabled, 0, Hunter.Samus, 100);
        PlayerEntity target = Prepare(enabled, 1, Hunter.Kanden, 101);
        PlayerEntity classic = Prepare(disabled, 0, Hunter.Samus, 200);

        marked.SetEnhancedTarget(target.ServerCombatIdentity, 150);
        Set(classic, "_enhancedTargetTicks", (ushort)150);
        Set(classic, "_enhancedTarget", target.ServerCombatIdentity);

        Assert.Equal((ushort)150, marked.CaptureServerState().EnhancedTargetTicks);
        SnapshotPlayer state = classic.CaptureServerState();
        Assert.Equal((byte)255, state.EnhancedTargetSlot);
        Assert.Equal((ushort)0, state.EnhancedTargetTicks);
        Assert.Equal((byte)0, state.Overcharge);
        Assert.Equal((ushort)0, state.ChilledTicks);
        Assert.Equal((ushort)0, state.CloakFadeTicks);
    }

    [Fact]
    public void EnhancedWorldCaptureAllowsNoActiveScorchPatch()
    {
        using var state = EnhancedState();
        state.Scene.Match.MatchId = 7;
        state.Scene.Services = new WorldIds();
        var capture = new WorldStateCapture(allowValidationFixtureDynamics: true);

        capture.Capture(state.Scene, 7, 1, 10);

        Assert.DoesNotContain(capture.Records.ToArray(),
            record => record.Kind == WorldRecordKind.EnhancedEffect);
    }

    [Fact]
    public void SamusDirectAffinityLockUsesFullLifeIdentityAndConsumesOnce()
    {
        using var state = EnhancedState();
        PlayerEntity samus = Prepare(state, 0, Hunter.Samus, 100);
        PlayerEntity victim = Prepare(state, 1, Hunter.Kanden, 101);
        SetAltFormDamageTarget(victim);
        BeamProjectileEntity missile = Beam(state, samus, BeamType.Missile,
            affinity: true, charged: true);

        Assert.Equal(1, victim.TakeDamageResolved(1,
            DamageFlags.Direct | DamageFlags.IgnoreInvuln, null, missile));
        Assert.Equal(victim.ServerCombatIdentity, samus.EnhancedTarget);
        Assert.Equal((ushort)EnhancedHunterTuning.SamusTargetLockTicks,
            samus.EnhancedTargetTicks);
        Assert.True(samus.TryConsumeEnhancedTarget(out CombatActor actor));
        Assert.Equal(victim.ServerCombatIdentity, actor);
        Assert.False(samus.TryConsumeEnhancedTarget(out _));

        victim.Health = 100;
        samus.ResetEnhancedHunterState();
        victim.TakeDamageResolved(1,
            DamageFlags.Splash | DamageFlags.IgnoreInvuln, null, missile);
        Assert.Equal((ushort)0, samus.EnhancedTargetTicks);

        samus.SetEnhancedTarget(victim.ServerCombatIdentity, 150);
        Set(victim, "_serverLife", victim.ServerCombatIdentity.Life + 1);
        Assert.False(samus.TryConsumeEnhancedTarget(out _));
    }

    [Fact]
    public void SamusLockedMissileAcquiresExactActorBoostsOnceAndFallsBackWhenStale()
    {
        using var state = EnhancedState();
        PlayerEntity samus = Prepare(state, 0, Hunter.Samus, 100);
        PlayerEntity locked = Prepare(state, 1, Hunter.Kanden, 101);
        PlayerEntity decoy = Prepare(state, 2, Hunter.Trace, 102);
        locked.Position = new Vector3(0, -0.5f, 4);
        decoy.Position = new Vector3(0, -0.5f, 2);
        samus.SetEnhancedTarget(locked.ServerCombatIdentity,
            EnhancedHunterTuning.SamusTargetLockTicks);

        BeamProjectileEntity enhanced = SpawnChargedAffinityMissile(state, samus,
            out float retailHoming, out BeamResultFlags enhancedResult);
        Assert.Same(locked, enhanced.Target);
        Assert.True(enhancedResult.TestFlag(BeamResultFlags.Homing));
        Assert.Equal(retailHoming * EnhancedHunterTuning.SamusLockedMissileHomingMultiplier,
            enhanced.Homing, 6);
        Assert.Equal((ushort)0, samus.EnhancedTargetTicks);

        BeamProjectileEntity ordinary = SpawnChargedAffinityMissile(state, samus,
            out retailHoming, out _);
        Assert.Equal(retailHoming, ordinary.Homing, 6);

        CombatActor stale = locked.ServerCombatIdentity;
        samus.SetEnhancedTarget(stale, EnhancedHunterTuning.SamusTargetLockTicks);
        Set(locked, "_serverLife", stale.Life + 1);
        BeamProjectileEntity fallback = SpawnChargedAffinityMissile(state, samus,
            out retailHoming, out _);
        Assert.Equal(retailHoming, fallback.Homing, 6);
        Assert.Equal((ushort)0, samus.EnhancedTargetTicks);
        Assert.NotEqual(stale, (fallback.Target as PlayerEntity)?.ServerCombatIdentity);
    }

    [Fact]
    public void KandenMarkerMatchesRetailDisruptTimerWithoutExtendingIt()
    {
        using var state = EnhancedState();
        PlayerEntity kanden = Prepare(state, 0, Hunter.Kanden, 100);
        PlayerEntity victim = Prepare(state, 1, Hunter.Samus, 101);
        SetAltFormDamageTarget(victim);
        BeamProjectileEntity volt = Beam(state, kanden, BeamType.VoltDriver,
            affinity: true, charged: true, Affliction.Disrupt);

        victim.TakeDamageResolved(1,
            DamageFlags.Direct | DamageFlags.IgnoreInvuln, null, volt);
        SnapshotPlayer victimState = victim.CaptureServerState();
        Assert.Equal((ushort)SimTicks.From30HzFrames(60),
            victimState.DisruptTicks);
        Assert.Equal(victimState.DisruptTicks, kanden.EnhancedTargetTicks);
        Assert.Equal(victim.ServerCombatIdentity, kanden.EnhancedTarget);
    }

    [Fact]
    public void NoxusFreezeProvenanceControlsNaturalThawAndLegitimateShatter()
    {
        using var state = EnhancedState();
        PlayerEntity noxus = Prepare(state, 0, Hunter.Noxus, 100);
        PlayerEntity victim = Prepare(state, 1, Hunter.Samus, 101);
        BeamProjectileEntity freeze = Beam(state, noxus, BeamType.Judicator,
            affinity: true, charged: true, Affliction.Freeze);
        victim.TakeDamageResolved(1,
            DamageFlags.Direct | DamageFlags.IgnoreInvuln, null, freeze);
        ushort retailFreeze = victim.CaptureServerState().FrozenTicks;
        Assert.True(retailFreeze > 0);
        Assert.Equal((ushort)0, victim.ChilledTicks);

        BeamProjectileEntity ordinary = Beam(state, noxus, BeamType.PowerBeam,
            affinity: false, charged: false);
        victim.TakeDamageResolved(1,
            DamageFlags.Direct | DamageFlags.IgnoreInvuln, null, ordinary);
        Assert.Equal((ushort)0, victim.CaptureServerState().FrozenTicks);
        Assert.Equal((ushort)EnhancedHunterTuning.NoxusChillTicks,
            victim.ChilledTicks);

        victim.ResetEnhancedHunterState();
        Set(victim, "_frozenTimer", (ushort)30);
        victim.MarkEnhancedFreeze();
        victim.HandleEnhancedAcceptedHit(noxus, ordinary,
            DamageFlags.Direct | DamageFlags.Burn, resolvedDamage: 1);
        Assert.Equal((ushort)30, victim.CaptureServerState().FrozenTicks);
        victim.HandleEnhancedAcceptedHit(noxus, ordinary,
            DamageFlags.Direct, resolvedDamage: 0);
        Assert.Equal((ushort)30, victim.CaptureServerState().FrozenTicks);

        victim.TakeDamageResolved(1,
            DamageFlags.Direct | DamageFlags.IgnoreInvuln, null, noxus);
        Assert.Equal((ushort)0, victim.CaptureServerState().FrozenTicks);
        Assert.Equal((ushort)EnhancedHunterTuning.NoxusChillTicks,
            victim.ChilledTicks);

        victim.ResetEnhancedHunterState();
        Set(victim, "_frozenTimer", (ushort)0);
        victim.MarkEnhancedFreeze();
        victim.TickEnhancedHunterState(wasFrozen: true);
        Assert.Equal((ushort)EnhancedHunterTuning.NoxusChillTicks,
            victim.ChilledTicks);
    }

    [Fact]
    public void ChillScalesHorizontalAccelerationOnly()
    {
        Vector3 input = new(4, 7, -2);
        Vector3 chilled = PlayerEntity.ApplyEnhancedChillAcceleration(input, 90);
        Assert.Equal(3.8f, chilled.X, 5);
        Assert.Equal(7, chilled.Y);
        Assert.Equal(-1.9f, chilled.Z, 5);
        Assert.Equal(input, PlayerEntity.ApplyEnhancedChillAcceleration(input, 0));
    }

    [Fact]
    public void SyluxOverflowCapGraceAbsorptionAndForcedDeathAreBounded()
    {
        using var state = EnhancedState();
        PlayerEntity sylux = Prepare(state, 0, Hunter.Sylux, 100);
        sylux.Health = 97;
        sylux.GainEnhancedAffinityHealth(8);
        Assert.Equal(100, sylux.Health);
        Assert.Equal((byte)5, sylux.Overcharge);
        sylux.AddEnhancedOvercharge(20);
        Assert.Equal((byte)EnhancedHunterTuning.SyluxOverchargeMaximum,
            sylux.Overcharge);

        sylux.TickEnhancedHunterState(wasFrozen: false);
        Assert.Equal((ushort)(EnhancedHunterTuning.SyluxOverchargeGraceTicks - 1),
            sylux.OverchargeIdleTicks);
        sylux.Health = 90;
        sylux.GainEnhancedAffinityHealth(5);
        Assert.Equal((ushort)EnhancedHunterTuning.SyluxOverchargeGraceTicks,
            sylux.OverchargeIdleTicks);

        sylux.Health = 100;
        Assert.Equal(12, sylux.TakeDamageResolved(12,
            DamageFlags.NoDmgInvuln, null, null));
        Assert.Equal(100, sylux.Health);
        Assert.Equal((byte)0, sylux.Overcharge);

        sylux.AddEnhancedOvercharge(12);
        Assert.Equal(100, sylux.TakeDamageResolved(1,
            DamageFlags.Death | DamageFlags.IgnoreInvuln, null, null));
        Assert.Equal(0, sylux.Health);
        Assert.Equal((byte)0, sylux.Overcharge);

        sylux.Health = 100;
        sylux.AddEnhancedOvercharge(12);
        for (int i = 0; i < EnhancedHunterTuning.SyluxOverchargeGraceTicks; i++)
            sylux.TickEnhancedHunterState(wasFrozen: false);
        Assert.Equal((byte)0, sylux.Overcharge);
    }

    [Fact]
    public void OverchargeAbsorptionCountsForEventsStatsAndAssists()
    {
        using var state = EnhancedState(assistMinimumDamage: 10);
        MatchRuntime match = state.Scene.Match;
        PlayerEntity victim = Prepare(state, 0, Hunter.Sylux, 100);
        PlayerEntity killer = Prepare(state, 1, Hunter.Samus, 101);
        PlayerEntity assister = Prepare(state, 2, Hunter.Kanden, 102);
        var combat = new ServerCombat(spreadSeed: 1);

        combat.BeginTick(10);
        victim.Health = 100;
        combat.NoteDamage(victim, assister, assister, BeamType.None,
            DamageFlags.None, null, previousHealth: 100, frozen: 0, burn: 0,
            disrupt: 0,
            afflictionChanged: false, absorbedOvercharge: 12);
        Span<CombatEvent> events = stackalloc CombatEvent[2];
        Assert.Equal(1, combat.CopyPending(events));
        Assert.Equal((ushort)12, events[0].Amount);
        Assert.True(events[0].Flags.TestFlag(CombatEventFlags.OverchargeAbsorb));
        Assert.Equal(12, match.Players[assister.SlotIndex].DamageDealt);

        combat.Consume(1);
        combat.BeginTick(11);
        victim.Health = 0;
        combat.NoteDamage(victim, killer, killer, BeamType.None,
            DamageFlags.None, null, previousHealth: 100, frozen: 0, burn: 0,
            disrupt: 0,
            afflictionChanged: false);
        Assert.Equal(1, match.Players[assister.SlotIndex].Assists);
    }

    [Fact]
    public void TracePersistenceRequiresEstablishedPersonalCloakAndCancelsCleanly()
    {
        Assert.True(PlayerEntity.IsFullyEstablishedPersonalTraceCloak(
            Hunter.Trace, false, false, (ushort)SimTicks.From30HzFrames(30), 5 / 31f));
        Assert.False(PlayerEntity.IsFullyEstablishedPersonalTraceCloak(
            Hunter.Trace, true, false, (ushort)SimTicks.From30HzFrames(30), 1 / 31f));
        Assert.False(PlayerEntity.IsFullyEstablishedPersonalTraceCloak(
            Hunter.Trace, false, true, (ushort)SimTicks.From30HzFrames(30), 1 / 31f));
        Assert.False(PlayerEntity.IsFullyEstablishedPersonalTraceCloak(
            Hunter.Trace, false, false, (ushort)(SimTicks.From30HzFrames(30) - 1), 1 / 31f));
        Assert.Equal(1 / 31f, PlayerEntity.GetReplicaCloakFadeStartAlpha(
            SnapshotPlayerFlags.AltForm), 6);
        Assert.Equal(5 / 31f, PlayerEntity.GetReplicaCloakFadeStartAlpha(
            SnapshotPlayerFlags.None), 6);

        using var state = EnhancedState();
        PlayerEntity trace = Prepare(state, 0, Hunter.Trace, 100);
        trace.BeginEnhancedCloakFade(5 / 31f);
        Assert.Equal((ushort)EnhancedHunterTuning.TraceCloakFadeTicks,
            trace.CloakFadeTicks);
        Assert.Equal(5 / 31f, trace.EnhancedCloakFadeAlpha, 6);
        Set(trace, "_curAlpha", 5 / 31f);
        trace.CancelEnhancedCloakFade();
        Assert.Equal((ushort)0, trace.CloakFadeTicks);
        Assert.Equal(1, trace.CurAlpha);
        state.Scene.Match.PrimeHunter = trace.SlotIndex;
        trace.BeginEnhancedCloakFade();
        Assert.Equal((ushort)0, trace.CloakFadeTicks);
    }

    [Fact]
    public void WeavelConcussionNeedsEnhancedAcceptedAffinityNearImpact()
    {
        using var state = EnhancedState();
        PlayerEntity weavel = Prepare(state, 0, Hunter.Weavel, 100);
        PlayerEntity victim = Prepare(state, 1, Hunter.Samus, 101);
        SetAltFormDamageTarget(victim);
        BeamProjectileEntity hammer = Beam(state, weavel, BeamType.Battlehammer,
            affinity: true, charged: false);
        Vector3 speed = victim.Speed;

        victim.TakeDamageResolved(1,
            DamageFlags.NearSplash | DamageFlags.IgnoreInvuln, null, hammer);
        Assert.Equal((ushort)EnhancedHunterTuning.WeavelConcussionTicks,
            victim.ConcussionTicks);
        Assert.Equal(speed, victim.Speed);

        victim.ResetEnhancedHunterState();
        victim.TakeDamageResolved(1,
            DamageFlags.Splash | DamageFlags.IgnoreInvuln, null, hammer);
        Assert.Equal((ushort)0, victim.ConcussionTicks);

        victim.ResetEnhancedHunterState();
        SetShot(hammer, hammer.CombatShot with { Affinity = false });
        victim.TakeDamageResolved(1,
            DamageFlags.NearSplash | DamageFlags.IgnoreInvuln, null, hammer);
        Assert.Equal((ushort)0, victim.ConcussionTicks);

        using var classicState = new MatchBaselineTests.State();
        classicState.Configure(GameMode.Battle).ApplyRules(Rules(false));
        PlayerEntity classicWeavel = Prepare(classicState, 0, Hunter.Weavel, 300);
        PlayerEntity classicVictim = Prepare(classicState, 1, Hunter.Samus, 301);
        SetAltFormDamageTarget(classicVictim);
        BeamProjectileEntity classicHammer = Beam(classicState, classicWeavel,
            BeamType.Battlehammer, affinity: true, charged: false);
        classicVictim.TakeDamageResolved(1,
            DamageFlags.NearSplash | DamageFlags.IgnoreInvuln, null,
            classicHammer);
        Assert.Equal((ushort)0, classicVictim.ConcussionTicks);

        SetShot(hammer, hammer.CombatShot with { Affinity = true });
        Set(victim, "_spawnInvulnTimer", (ushort)10);
        Assert.Equal(0, victim.TakeDamageResolved(1,
            DamageFlags.NearSplash, null, hammer));
        Assert.Equal((ushort)0, victim.ConcussionTicks);
    }

    [Fact]
    public void SpirePatchReplacesExpiresHitsOnceHonorsFriendlyFireAndIsolatesMatches()
    {
        using var first = EnhancedState();
        using var second = new MatchBaselineTests.State();
        second.Configure(GameMode.Battle).ApplyRules(Rules(false));
        PlayerEntity spire = Prepare(first, 0, Hunter.Spire, 100);
        PlayerEntity victim = Prepare(first, 1, Hunter.Samus, 101);
        SetAltFormDamageTarget(victim);
        PlayerEntity classicSpire = Prepare(second, 0, Hunter.Spire, 200);
        CombatShot shot = Shot(spire, BeamType.Magmaul, affinity: true);
        CombatShot classicShot = Shot(classicSpire, BeamType.Magmaul,
            affinity: true);

        spire.SpawnEnhancedSpireScorch(Vector3.Zero, default, shot);
        SpireScorchPatchEntity initial = Patch(spire)!;
        spire.SpawnEnhancedSpireScorch(Vector3.Zero, default, shot);
        SpireScorchPatchEntity patch = Patch(spire)!;
        classicSpire.SpawnEnhancedSpireScorch(Vector3.Zero, default, classicShot);
        Assert.Equal((ushort)0, initial.RemainingTicks);
        Assert.NotSame(initial, patch);
        Assert.Null(Patch(classicSpire));

        first.Scene.Match.MatchId = 7;
        first.Scene.Services = new WorldIds();
        var capture = new WorldStateCapture(allowValidationFixtureDynamics: true);
        capture.Capture(first.Scene, 7, 1, 10);
        WorldRecord effect = Assert.Single(capture.Records.ToArray(),
            record => record.Kind == WorldRecordKind.EnhancedEffect);
        Assert.Equal(shot.Actor.Slot, effect.Slot);
        Assert.Equal((uint)shot.Actor.ConnectionId, effect.A);
        Assert.Equal((uint)(shot.Actor.ConnectionId >> 32), effect.B);
        Assert.Equal(shot.Actor.Life, effect.C);
        Assert.Equal((uint)EnhancedHunterTuning.SpireScorchLifetimeTicks, effect.D);

        victim.Position = Vector3.Zero;
        Set(victim, "_spawnInvulnTimer", (ushort)10);
        Assert.True(patch.Process());
        Assert.Equal(100, victim.Health);
        Set(victim, "_spawnInvulnTimer", (ushort)0);
        Assert.True(patch.Process());
        Assert.Equal(99, victim.Health);
        Assert.True(patch.Process());
        Assert.Equal(99, victim.Health);

        Set(victim, "_serverLife", victim.ServerCombatIdentity.Life + 1);
        Set(victim, "_damageInvulnTimer", (ushort)0);
        Set(victim, "_spawnInvulnTimer", (ushort)0);
        victim.Health = 100;
        Assert.True(patch.Process());
        Assert.Equal(99, victim.Health);

        var combat = new ServerCombat(spreadSeed: 1);
        combat.BeginTick(10);
        combat.NoteDamage(victim, patch, spire, BeamType.Magmaul,
            DamageFlags.None, null, previousHealth: 100, frozen: 0, burn: 0,
            disrupt: 0, afflictionChanged: false);
        Span<CombatEvent> events = stackalloc CombatEvent[2];
        Assert.Equal(1, combat.CopyPending(events));
        Assert.Equal(shot.Actor, events[0].Actor);
        Assert.Equal((byte)BeamType.Magmaul, events[0].Weapon);
        Assert.True(events[0].Flags.TestFlag(CombatEventFlags.Affinity));
        Assert.False(events[0].Flags.TestFlag(CombatEventFlags.Deathalt));
        Assert.Equal(1, first.Scene.Match.Players[spire.SlotIndex].DamageDealt);

        for (int i = 0; i < EnhancedHunterTuning.SpireScorchLifetimeTicks - 4; i++)
            patch.Process();
        Assert.Equal((ushort)0, patch.RemainingTicks);

        using var teams = new MatchBaselineTests.State();
        teams.Configure(GameMode.BattleTeams).ApplyRules(new MatchRules(
            MatchMode.TeamBattle, "EH", friendlyFire: false,
            enhancedHunters: true, rulesetPreset: RulesetPreset.Custom,
            rankingEligibility: RankingEligibility.Unranked));
        PlayerEntity teamSpire = Prepare(teams, 0, Hunter.Spire, 300, team: 0);
        PlayerEntity teammate = Prepare(teams, 1, Hunter.Samus, 301, team: 0);
        SetAltFormDamageTarget(teammate);
        teamSpire.SpawnEnhancedSpireScorch(Vector3.Zero, default,
            Shot(teamSpire, BeamType.Magmaul, affinity: true));
        teammate.Position = Vector3.Zero;
        Assert.True(Patch(teamSpire)!.Process());
        Assert.Equal(100, teammate.Health);
    }

    [Fact]
    public void ProjectileRootProvenanceDistinguishesInheritedRicochet()
    {
        using var state = EnhancedState();
        PlayerEntity owner = Prepare(state, 0, Hunter.Spire, 100);
        var rootBeam = new BeamProjectileEntity(state.Scene);
        var childBeam = new BeamProjectileEntity(state.Scene);
        var rootEquip = new EquipInfo(Weapons.Current[(int)BeamType.PowerBeam],
            new[] { rootBeam }) { InfiniteAmmo = true };
        var childEquip = new EquipInfo(Weapons.Current[(int)BeamType.PowerBeam],
            new[] { childBeam }) { InfiniteAmmo = true };
        rootEquip.DrawFuncIds[0] = childEquip.DrawFuncIds[0] = 0;

        Assert.Equal(BeamResultFlags.Spawned, BeamProjectileEntity.Spawn(owner,
            rootEquip, Vector3.Zero, Vector3.UnitZ, BeamSpawnFlags.NoMuzzle,
            default, state.Scene));
        Assert.Equal(BeamResultFlags.Spawned, BeamProjectileEntity.Spawn(owner,
            childEquip, Vector3.Zero, Vector3.UnitZ, BeamSpawnFlags.NoMuzzle,
            default, state.Scene, Shot(owner, BeamType.PowerBeam, affinity: true)));
        Assert.True(rootBeam.IsRootCombatProjectile);
        Assert.False(childBeam.IsRootCombatProjectile);
    }

    private static MatchBaselineTests.State EnhancedState(
        int assistMinimumDamage = 20)
    {
        var state = new MatchBaselineTests.State();
        state.Configure(GameMode.Battle).ApplyRules(Rules(true,
            assistMinimumDamage: assistMinimumDamage));
        return state;
    }

    private static MatchRules Rules(bool enhancedHunters,
        bool affinityWeapons = false, int assistMinimumDamage = 20)
        => new(MatchMode.Battle, "EH", affinityWeapons: affinityWeapons,
            assistMinimumDamage: assistMinimumDamage,
            enhancedHunters: enhancedHunters,
            rulesetPreset: enhancedHunters ? RulesetPreset.Custom : RulesetPreset.Classic,
            rankingEligibility: RankingEligibility.Unranked);

    private static PlayerEntity Prepare(MatchBaselineTests.State state, int slot,
        Hunter hunter, ulong connectionId, int team = -1)
    {
        state.Activate(slot, team < 0 ? slot : team);
        PlayerEntity player = state.Players[slot];
        player._scene = state.Scene;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.SlotIndex),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(player, slot);
        player.ModSetHunter(hunter);
        Set(player, "_networkInputActive", true);
        Set(player, "_serverConnectionId", connectionId);
        Set(player, "_serverLife", 1u);
        player._healthMax = 100;
        player.Health = 100;
        Array.Fill(player.BeamEffectiveness, Effectiveness.Normal);
        bool inserted = false;
        foreach (EntityBase entity in state.Scene.Entities)
        {
            if (ReferenceEquals(entity, player))
            {
                inserted = true;
                break;
            }
        }
        if (!inserted)
        {
            state.Scene.InsertEntity(player);
        }
        return player;
    }

    private static void SetAltFormDamageTarget(PlayerEntity player)
        => typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(player, player.Flags1 | PlayerFlags1.AltForm);

    private static BeamProjectileEntity Beam(MatchBaselineTests.State state,
        PlayerEntity owner, BeamType beam, bool affinity, bool charged,
        Affliction affliction = Affliction.None)
    {
        var projectile = new BeamProjectileEntity(state.Scene)
        {
            Owner = owner,
            Beam = beam,
            Flags = charged ? BeamFlags.Charged : BeamFlags.None,
            Afflictions = affliction
        };
        SetShot(projectile, Shot(owner, beam, affinity));
        return projectile;
    }

    private static BeamProjectileEntity SpawnChargedAffinityMissile(
        MatchBaselineTests.State state, PlayerEntity owner, out float retailHoming,
        out BeamResultFlags result)
    {
        WeaponInfo weapon = Weapons.Current[(int)BeamType.Missile + 9];
        var projectile = new BeamProjectileEntity(state.Scene);
        var equip = new EquipInfo(weapon, new[] { projectile })
        {
            InfiniteAmmo = true,
            ChargeLevel = (ushort)SimTicks.From30HzFrames(weapon.FullCharge)
        };
        equip.DrawFuncIds[1] = 0;
        retailHoming = weapon.ChargedHoming / 4096f / 2;
        result = BeamProjectileEntity.Spawn(owner, equip, Vector3.Zero,
            Vector3.UnitZ, BeamSpawnFlags.NoMuzzle, default, state.Scene);
        return projectile;
    }

    private static CombatShot Shot(PlayerEntity owner, BeamType beam,
        bool affinity) => new(owner.ServerCombatIdentity, 1, 1, 1, 1, 0)
        {
            Affinity = affinity,
            SourceWeapon = (byte)beam
        };

    private static SpireScorchPatchEntity? Patch(PlayerEntity player)
        => (SpireScorchPatchEntity?)typeof(PlayerEntity).GetField(
            "_spireScorchPatch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(player);

    private static void SetShot(BeamProjectileEntity beam, CombatShot shot)
        => typeof(BeamProjectileEntity).GetProperty("CombatShot",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(beam, shot);

    private static void Set(PlayerEntity player, string field, object value)
        => typeof(PlayerEntity).GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);

    private sealed class WorldIds : ISceneServices
    {
        private readonly Dictionary<EntityBase, uint> _ids = new();
        private uint _next = 1;
        public uint GetWorldEntityId(Scene scene, EntityBase entity)
        {
            if (!_ids.TryGetValue(entity, out uint id))
                _ids.Add(entity, id = _next++);
            return id;
        }
        public void ForgetWorldEntity(EntityBase entity) => _ids.Remove(entity);
    }
}
