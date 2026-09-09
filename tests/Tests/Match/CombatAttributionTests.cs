using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class CombatAttributionTests
{
    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void DefenderKillPublishesDistinctAuthoritativeObjectiveDefendedFact()
    {
        string data = Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "AMHE1");
        using var content = ServerContent.PreserveContext("AMHE1");
        ServerContent.Open(data, "AMHE1");
        using var simulation = new ServerSimulation(new MatchRules(MatchMode.TeamDefender,
            "MP1 SANCTORUS", maxPlayers: 2));
        Scene scene = simulation.Scene;
        scene.Match.MatchId = 1;
        scene.Match.PhaseRevision = 2;
        scene.Match.Phase = MatchPhase.Playing;
        NodeDefenseEntity? selected = null;
        foreach (NodeDefenseEntity candidate in scene.GetNodeDefenseEntities())
        {
            Assert.Null(selected);
            selected = candidate;
        }
        NodeDefenseEntity node = Assert.IsType<NodeDefenseEntity>(selected);
        PlayerEntity attacker = scene.Players[0];
        PlayerEntity victim = scene.Players[1];
        attacker.ServerActivate(100, Hunter.Samus, team: 0);
        victim.ServerActivate(200, Hunter.Kanden, team: 1);
        PutInside(attacker, node.Volume);
        PutInside(victim, node.Volume);
        var facts = new List<MatchEvent>();
        Assert.True(scene.Match.SemanticEvents.Subscribe((in MatchEvent value) => facts.Add(value)));
        var awards = new List<MatchAward>();
        scene.Match.Awards.Awarded += awards.Add;

        victim.Health = 0;
        simulation.Combat.BeginTick(10);
        simulation.Combat.NoteDamage(victim, attacker, attacker, BeamType.None,
            DamageFlags.None, null, previousHealth: 100, frozen: 0, burn: 0,
            disrupt: 0, afflictionChanged: false);

        MatchEvent defended = Assert.Single(facts,
            value => value.Kind == MatchEventKind.ObjectiveDefended);
        Assert.Equal(attacker.ServerCombatIdentity, defended.Subject);
        Assert.Equal(victim.ServerCombatIdentity, defended.Target);
        Assert.Equal(unchecked((uint)node.Id), defended.EntityId);
        Assert.Contains(facts, value => value.Kind == MatchEventKind.PlayerKilled
            && (value.Flags & MatchEventFlags.DefendingObjective) != 0);
        Assert.Contains(awards, value => value.Kind == MatchAwardKind.Defender
            && value.SourceEventId == defended.Id);
    }

    [Fact]
    public void BeamAndBombLethalsClassifyTheCapturedSourceFormAndSourceKind()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.MatchId = 1;
        match.PhaseRevision = 1;
        PreparePlayer(state, 0, 0, 100, 1);
        PreparePlayer(state, 1, 1, 101, 1, altForm: false);
        PreparePlayer(state, 2, 2, 102, 1);

        var combat = new ServerCombat(spreadSeed: 1);
        PlayerEntity attacker = state.Players[1];

        var beam = new BeamProjectileEntity(state.Scene);
        SetShot(beam, combat.CaptureAttribution(attacker));
        state.Players[0].Health = 0;
        combat.BeginTick(10);
        {
            combat.NoteDamage(state.Players[0], beam, attacker, BeamType.PowerBeam,
                0, null, 100, 0, 0, 0, false);
        }

        Assert.Equal(100, match.Players[1].DamageDealt);
        Assert.Equal(1, match.Players[1].BipedKills);
        Assert.Equal(0, match.Players[1].AltFormKills);
        Assert.True(combat.TryPeekKill(out KillEvent beamKill));
        Assert.Equal(KillSourceKind.Beam, beamKill.SourceKind);
        Assert.Equal(attacker.ServerCombatIdentity, beamKill.Killer);
        combat.ConsumeKill();

        SetForm(attacker, altForm: true);
        var bomb = new BombEntity(state.Scene);
        SetShot(bomb, combat.CaptureAttribution(attacker));
        state.Players[2].Health = 0;
        combat.BeginTick(11);
        {
            combat.NoteDamage(state.Players[2], bomb, attacker, BeamType.None,
                0, null, 100, 0, 0, 0, false);
        }

        Assert.Equal(200, match.Players[1].DamageDealt);
        Assert.Equal(1, match.Players[1].BipedKills);
        Assert.Equal(1, match.Players[1].AltFormKills);
        Assert.True(combat.TryPeekKill(out KillEvent bombKill));
        Assert.Equal(KillSourceKind.Bomb, bombKill.SourceKind);
        Assert.Equal((byte)255, bombKill.Weapon);
    }

    [Fact]
    public void BurnUsesTheCapturedShotAfterTheAttackerChangesForm()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.MatchId = 1;
        match.PhaseRevision = 1;
        PreparePlayer(state, 0, 0, 100, 1);
        PreparePlayer(state, 1, 1, 101, 1, altForm: false);

        var combat = new ServerCombat(lagCompEnabled: false, spreadSeed: 1);
        PlayerEntity attacker = state.Players[1];
        CombatShot shot = combat.CaptureShot(attacker,
            new BeamMechanics(BeamType.Magmaul, BeamType.Magmaul, false, false, 0, 1, 1)) with
        {
            Affinity = true
        };
        Assert.False(shot.SourceAltForm);
        Assert.Equal((byte)BeamType.Magmaul, shot.SourceWeapon);

        SetForm(attacker, altForm: true);
        typeof(PlayerEntity).GetProperty("CombatBurnSource", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(state.Players[0], shot);
        state.Players[0].Health = 0;
        combat.BeginTick(20);
        {
            combat.NoteDamage(state.Players[0], null, null, BeamType.None,
                DamageFlags.Burn, null, 50, 0, 0, 0, false);
        }

        Assert.Equal(50, match.Players[1].DamageDealt);
        Assert.Equal(1, match.Players[1].BipedKills);
        Assert.Equal(0, match.Players[1].AltFormKills);
        Assert.True(combat.TryPeekKill(out KillEvent kill));
        Assert.Equal(KillSourceKind.Beam, kill.SourceKind);
        Assert.Equal((byte)BeamType.Magmaul, kill.Weapon);
        Assert.Equal(KillEventFlags.Burn | KillEventFlags.Affinity,
            kill.Flags & (KillEventFlags.Burn | KillEventFlags.Affinity));
    }

    [Fact]
    public void HalfturretAttributionIsAlwaysAltFormAndTheCapturedFormIsImmutable()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.MatchId = 1;
        match.PhaseRevision = 1;
        PreparePlayer(state, 0, 0, 100, 1);
        PreparePlayer(state, 1, 1, 101, 1, altForm: false);
        PreparePlayer(state, 2, 2, 102, 1);

        var combat = new ServerCombat(spreadSeed: 1);
        PlayerEntity attacker = state.Players[1];
        CombatShot bipedShot;
        combat.BeginTick(30); bipedShot = combat.CaptureAttribution(attacker);
        Assert.False(bipedShot.SourceAltForm);

        // Changing the live player after emission cannot relabel this projectile.
        SetForm(attacker, altForm: true);
        var bipedBeam = new BeamProjectileEntity(state.Scene);
        SetShot(bipedBeam, bipedShot);
        state.Players[0].Health = 0;
        combat.BeginTick(31);
        {
            combat.NoteDamage(state.Players[0], bipedBeam, attacker, BeamType.PowerBeam,
                0, null, 100, 0, 0, 0, false);
        }
        Assert.Equal(1, match.Players[1].BipedKills);
        combat.ConsumeKill();

        SetForm(attacker, altForm: false);
        var turret = new HalfturretEntity(attacker, state.Scene);
        CombatShot turretShot;
        combat.BeginTick(32); turretShot = combat.CaptureAttribution(turret);
        Assert.True(turretShot.SourceAltForm);

        var turretBeam = new BeamProjectileEntity(state.Scene);
        SetShot(turretBeam, turretShot);
        state.Players[2].Health = 0;
        combat.BeginTick(33);
        {
            combat.NoteDamage(state.Players[2], turretBeam, attacker, BeamType.PowerBeam,
                0, null, 100, 0, 0, 0, false);
        }

        Assert.Equal(1, match.Players[1].BipedKills);
        Assert.Equal(1, match.Players[1].AltFormKills);
    }

    [Fact]
    public void RicochetChildKeepsTheCapturedSourceForm()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.MatchId = 1;
        match.PhaseRevision = 1;
        PreparePlayer(state, 0, 0, 100, 1);
        PreparePlayer(state, 1, 1, 101, 1, altForm: true);

        var combat = new ServerCombat(spreadSeed: 1);
        CombatShot shot;
        combat.BeginTick(34); shot = combat.CaptureAttribution(state.Players[1]);
        Assert.True(shot.SourceAltForm);

        var equip = new EquipInfo(Weapons.Ricochets[0], new[] { new BeamProjectileEntity(state.Scene) })
        {
            InfiniteAmmo = true
        };
        // Keep this headless test data-free while retaining the authored
        // ricochet weapon and inherited-shot path.
        equip.DrawFuncIds[0] = 0;
        BeamResultFlags result = BeamProjectileEntity.Spawn(state.Players[1], equip,
            Vector3.Zero, Vector3.UnitZ, BeamSpawnFlags.NoMuzzle, default, state.Scene, shot);

        Assert.Equal(BeamResultFlags.Spawned, result);
        Assert.Equal(shot.Actor, equip.Beams[0].CombatShot.Actor);
        Assert.True(equip.Beams[0].CombatShot.SourceAltForm);
    }

    [Fact]
    public void SelfFriendlyAndStaleLethalsDoNotIncrementHostileCounters()
    {
        using (var state = new MatchBaselineTests.State())
        {
            MatchRuntime match = state.Configure(GameMode.Battle);
            match.MatchId = 1;
            match.PhaseRevision = 1;
            PreparePlayer(state, 0, 0, 100, 1);
            var combat = new ServerCombat(spreadSeed: 1);
            state.Players[0].Health = 0;
            combat.BeginTick(40);
            {
                combat.NoteDamage(state.Players[0], state.Players[0], state.Players[0], BeamType.None,
                    0, null, 100, 0, 0, 0, false);
            }
            Assert.Equal(0, match.Players[0].DamageDealt);
            Assert.Equal(0, match.Players[0].BipedKills);
            Assert.Equal(0, match.Players[0].AltFormKills);
        }

        using (var state = new MatchBaselineTests.State())
        {
            MatchRuntime match = state.Configure(GameMode.BattleTeams);
            match.MatchId = 1;
            match.PhaseRevision = 1;
            PreparePlayer(state, 0, 0, 100, 1);
            PreparePlayer(state, 1, 0, 101, 1);
            var combat = new ServerCombat(spreadSeed: 1);
            state.Players[0].Health = 0;
            combat.BeginTick(41);
            {
                combat.NoteDamage(state.Players[0], state.Players[1], state.Players[1], BeamType.None,
                    0, null, 100, 0, 0, 0, false);
            }
            Assert.Equal(0, match.Players[1].DamageDealt);
            Assert.Equal(0, match.Players[1].BipedKills);
            Assert.Equal(0, match.Players[1].AltFormKills);
        }

        using (var state = new MatchBaselineTests.State())
        {
            MatchRuntime match = state.Configure(GameMode.Battle);
            match.MatchId = 1;
            match.PhaseRevision = 1;
            PreparePlayer(state, 0, 0, 100, 1);
            PreparePlayer(state, 1, 1, 101, 1);
            var combat = new ServerCombat(spreadSeed: 1);
            var staleBeam = new BeamProjectileEntity(state.Scene);
            SetShot(staleBeam, new CombatShot(new CombatActor(1, 999, 1), 1, 1, 1, 1, 0));
            state.Players[0].Health = 0;
            combat.BeginTick(42);
            {
                combat.NoteDamage(state.Players[0], staleBeam, state.Players[1], BeamType.PowerBeam,
                    0, null, 100, 0, 0, 0, false);
            }
            Assert.Equal(0, match.Players[1].DamageDealt);
            Assert.Equal(0, match.Players[1].BipedKills);
            Assert.Equal(0, match.Players[1].AltFormKills);
            Assert.True(combat.TryPeekKill(out KillEvent kill));
            Assert.True(kill.Killer.IsNone);
        }
    }

    [Fact]
    public void OwnProjectileFromAnEarlierLifeIsAValidatedSuicideWithoutCountersOrAssists()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.MatchId = 1;
        match.PhaseRevision = 1;
        // The live slot has already respawned as life 2, while its projectile
        // still carries the prior life identity.
        PreparePlayer(state, 0, 0, 100, 2);
        var combat = new ServerCombat(spreadSeed: 1);
        var beam = new BeamProjectileEntity(state.Scene);
        SetShot(beam, new CombatShot(new CombatActor(0, 100, 1), 1, 1, 1, 1, 0));
        state.Players[0].Health = 0;

        combat.BeginTick(60);
        {
            combat.NoteDamage(state.Players[0], beam, state.Players[0], BeamType.PowerBeam,
                0, null, 100, 0, 0, 0, false);
        }

        Assert.Equal(0, match.Players[0].DamageDealt);
        Assert.Equal(0, match.Players[0].BipedKills);
        Assert.Equal(0, match.Players[0].AltFormKills);
        Assert.True(combat.TryPeekKill(out KillEvent kill));
        Assert.True(kill.IsSuicide);
        Assert.True((kill.Flags & KillEventFlags.Suicide) != 0);
        Assert.Equal(new CombatActor(0, 100, 1), kill.Killer);
        Assert.Equal(new CombatActor(0, 100, 2), kill.Victim);
        Assert.Empty(kill.Assists);

        byte[] bytes = new byte[KillEvent.Size];
        kill.Write(bytes);
        Assert.True(KillEvent.TryRead(bytes, out KillEvent parsed));
        Assert.True(parsed.IsSuicide);
        Assert.Equal(kill.Killer, parsed.Killer);
        Assert.Equal(kill.Victim, parsed.Victim);
        Assert.Equal(kill.Flags, parsed.Flags);
        Assert.Empty(parsed.Assists);
    }

    [Fact]
    public void HostileCountersSaturateAndResetWhileCapturedResultsRemainImmutable()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.MatchId = 1;
        match.PhaseRevision = 1;
        PreparePlayer(state, 0, 0, 100, 1);
        PreparePlayer(state, 1, 1, 101, 1);
        PlayerMatchStats stats = match.Players[1];
        stats.DamageDealt = int.MaxValue;
        stats.BipedKills = int.MaxValue;
        stats.AltFormKills = int.MaxValue;

        var combat = new ServerCombat(spreadSeed: 1);
        var beam = new BeamProjectileEntity(state.Scene);
        SetShot(beam, combat.CaptureAttribution(state.Players[1]));
        state.Players[0].Health = 0;
        combat.BeginTick(50);
        {
            combat.NoteDamage(state.Players[0], beam, state.Players[1], BeamType.PowerBeam,
                0, null, 100, 0, 0, 0, false);
        }

        Assert.Equal(int.MaxValue, stats.DamageDealt);
        Assert.Equal(int.MaxValue, stats.BipedKills);
        Assert.Equal(int.MaxValue, stats.AltFormKills);

        match.CaptureResult(5);
        MatchResult result = match.Result!;
        Assert.Equal(int.MaxValue, result.Players[1].DamageDealt);
        Assert.Equal(int.MaxValue, result.Players[1].BipedKills);
        Assert.Equal(int.MaxValue, result.Players[1].AltFormKills);

        stats.DamageDealt = 0;
        stats.BipedKills = 0;
        stats.AltFormKills = 0;
        Assert.Equal(int.MaxValue, result.Players[1].DamageDealt);
        Assert.Equal(int.MaxValue, result.Players[1].BipedKills);
        Assert.Equal(int.MaxValue, result.Players[1].AltFormKills);

        NetScoreboard.ForgetSlot(state.Scene, 1);
        Assert.Equal(0, stats.DamageDealt);
        Assert.Equal(0, stats.BipedKills);
        Assert.Equal(0, stats.AltFormKills);
        stats.DamageDealt = 7;
        stats.BipedKills = 8;
        stats.AltFormKills = 9;
        match.ResetCompetitiveState();
        Assert.Equal(0, stats.DamageDealt);
        Assert.Equal(0, stats.BipedKills);
        Assert.Equal(0, stats.AltFormKills);
    }

    private static void PreparePlayer(MatchBaselineTests.State state, int slot, int team,
        ulong connectionId, uint life, bool altForm = false)
    {
        state.Activate(slot, team);
        PlayerEntity player = state.Players[slot];
        player._scene = state.Scene;
        typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.SlotIndex), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(player, slot);
        Set(player, "_networkInputActive", true);
        Set(player, "_serverConnectionId", connectionId);
        Set(player, "_serverLife", life);
        SetForm(player, altForm);
        player.Health = 100;
    }

    private static void SetForm(PlayerEntity player, bool altForm)
        => typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.Flags1), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(player, altForm ? PlayerFlags1.AltForm : PlayerFlags1.None);

    private static void SetShot(EntityBase source, CombatShot shot)
        => source.GetType().GetProperty("CombatShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, shot);

    private static void Set(PlayerEntity player, string name, object value)
        => typeof(PlayerEntity).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);

    private static void PutInside(PlayerEntity player, CollisionVolume volume)
    {
        Vector3 center = volume.Type switch
        {
            VolumeType.Sphere => volume.SpherePosition,
            VolumeType.Cylinder => volume.CylinderPosition + volume.CylinderVector * volume.CylinderDot / 2,
            _ => volume.BoxPosition + (volume.BoxVector1 * volume.BoxDot1
                + volume.BoxVector2 * volume.BoxDot2 + volume.BoxVector3 * volume.BoxDot3) / 2
        };
        Vector3 previous = player.Position;
        player.Position = center - (player.Volume.SpherePosition - previous);
        player.ModRefreshNodeRef(previous);
    }
}
