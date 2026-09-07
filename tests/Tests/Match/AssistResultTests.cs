using Xunit;
using System.Reflection;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class AssistResultTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void ResolvedDamageEmitsKillAndRejectsAnAssistFromAnEarlierLife(bool replacedLife, int expectedAssists)
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        match.MatchId = 1; match.PhaseRevision = 1;
        for (int slot = 0; slot < 3; slot++)
        {
            state.Activate(slot, slot);
            PlayerEntity player = state.Players[slot];
            player._scene = state.Scene;
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.SlotIndex))!.SetValue(player, slot);
            Set(player, "_networkInputActive", true);
            Set(player, "_serverConnectionId", (ulong)slot + 100);
            Set(player, "_serverLife", 1u);
        }
        var combat = new ServerCombat(spreadSeed: 1);
        PlayerEntity victim = state.Players[0];
        victim.Health = 80;
        using (combat.Enter(100))
            combat.NoteDamage(victim, state.Players[2], state.Players[2], BeamType.PowerBeam,
                0, null, 100, 0, 0, 0, false);
        if (replacedLife) Set(state.Players[2], "_serverLife", 2u);
        victim.Health = 0;
        using (combat.Enter(101))
            combat.NoteDamage(victim, state.Players[1], state.Players[1], BeamType.Imperialist,
                DamageFlags.Headshot, null, 80, 0, 0, 0, false);
        Assert.Equal(20, match.Players[2].DamageDealt);
        Assert.Equal(80, match.Players[1].DamageDealt);
        Assert.Equal(expectedAssists, match.Players[2].Assists);
        Assert.Equal(0, match.Players[2].Points);
        Assert.True(combat.TryPeekKill(out KillEvent kill));
        Assert.Equal(expectedAssists, kill.Assists.Length);
        Assert.Equal(state.Players[1].ServerCombatIdentity, kill.Killer);
        Assert.True((kill.Flags & KillEventFlags.Headshot) != 0);
        Assert.True(kill.IsValid);
        combat.ConsumeKill();
        Assert.False(combat.TryPeekKill(out _));
    }
    [Theory]
    [InlineData(BeamType.PowerBeam, KillSourceKind.Beam, 99)]
    [InlineData(BeamType.Imperialist, KillSourceKind.Beam, 7)]
    [InlineData(BeamType.Platform, KillSourceKind.Beam, 99)]
    [InlineData(BeamType.Enemy, KillSourceKind.Beam, 99)]
    [InlineData(BeamType.None, KillSourceKind.Bomb, 99)]
    [InlineData(BeamType.None, KillSourceKind.Alt, 99)]
    [InlineData(BeamType.None, KillSourceKind.Environment, 99)]
    public void SourceKindAndAffinityPreserveRealBeamNumbers(BeamType weapon, KillSourceKind sourceKind, int remainingHealth)
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle); match.MatchId = 1; match.PhaseRevision = 1;
        for (int slot = 0; slot < 2; slot++)
        {
            state.Activate(slot, slot); PlayerEntity player = state.Players[slot]; player._scene = state.Scene;
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.SlotIndex))!.SetValue(player, slot);
            Set(player, "_networkInputActive", true); Set(player, "_serverConnectionId", (ulong)slot + 100); Set(player, "_serverLife", 1u);
        }
        EntityBase? source = sourceKind switch
        {
            KillSourceKind.Beam => new BeamProjectileEntity(state.Scene),
            KillSourceKind.Bomb => new BombEntity(state.Scene),
            KillSourceKind.Alt => state.Players[1], _ => null
        };
        if (source is BeamProjectileEntity or BombEntity)
            source.GetType().GetProperty("CombatShot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source,
                new CombatShot(state.Players[1].ServerCombatIdentity, 1, 1, 1, 1, 0) { Affinity = sourceKind == KillSourceKind.Beam });
        var combat = new ServerCombat(spreadSeed: 1);
        using (combat.Enter(100)) combat.NoteDamage(state.Players[0], source,
            source == null ? null : state.Players[1], weapon, 0, null, remainingHealth, 0, 0, 0, false);
        Assert.True(combat.TryPeekKill(out KillEvent kill));
        Assert.Equal(sourceKind == KillSourceKind.Environment ? 0 : remainingHealth, match.Players[1].DamageDealt);
        Assert.Equal(sourceKind, kill.SourceKind);
        Assert.Equal(weapon == BeamType.None ? (byte)255 : (byte)weapon, kill.Weapon);
        Assert.Equal(sourceKind == KillSourceKind.Beam, (kill.Flags & KillEventFlags.Affinity) != 0);
        var events = new CombatEvent[4]; int count = combat.CopyPending(events);
        Assert.True(count >= 2);
        Assert.Equal(sourceKind == KillSourceKind.Beam, (events[0].Flags & CombatEventFlags.Affinity) != 0);
        byte[] bytes = new byte[KillEvent.Size]; kill.Write(bytes);
        Assert.True(KillEvent.TryRead(bytes, out var parsed)); Assert.Equal(sourceKind, parsed.SourceKind);
    }
    [Fact]
    public void BurnRetainsCapturedWeaponAffinityAndActualHealthLoss()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle); match.MatchId = 1; match.PhaseRevision = 1;
        for (int slot = 0; slot < 2; slot++)
        {
            state.Activate(slot, slot); PlayerEntity player = state.Players[slot]; player._scene = state.Scene;
            typeof(PlayerEntity).GetProperty(nameof(PlayerEntity.SlotIndex))!.SetValue(player, slot);
            Set(player, "_networkInputActive", true); Set(player, "_serverConnectionId", (ulong)slot + 100); Set(player, "_serverLife", 1u);
        }
        var combat = new ServerCombat(lagCompEnabled: false, spreadSeed: 1);
        CombatShot shot = combat.CaptureShot(state.Players[1].ServerCombatIdentity,
            new BeamMechanics(BeamType.Magmaul, BeamType.Magmaul, false, false, 0, 1, 1)) with { Affinity = true };
        Assert.Equal((byte)BeamType.Magmaul, shot.SourceWeapon);
        typeof(PlayerEntity).GetProperty("CombatBurnSource", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(state.Players[0], shot);
        using (combat.Enter(100)) combat.NoteDamage(state.Players[0], null, null, BeamType.None,
            DamageFlags.Burn, null, 3, 0, 0, 0, false);
        Assert.True(combat.TryPeekKill(out var kill));
        Assert.Equal(KillSourceKind.Beam, kill.SourceKind); Assert.Equal((byte)BeamType.Magmaul, kill.Weapon);
        Assert.True((kill.Flags & (KillEventFlags.Burn | KillEventFlags.Affinity)) == (KillEventFlags.Burn | KillEventFlags.Affinity));
        Assert.Equal(3, match.Players[1].DamageDealt);
    }
    private static void Set(PlayerEntity player, string name, object value)
        => typeof(PlayerEntity).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);

    [Fact]
    public void AssistsFreezeIntoResultAndResetWithoutChangingClassicPoints()
    {
        using var state = new MatchBaselineTests.State();
        MatchRuntime match = state.Configure(GameMode.Battle);
        state.Activate(0, 0);
        match.Players[0].Points = 5;
        match.Players[0].Assists = 3;
        match.CaptureResult(12);
        Assert.Equal(3, match.Result!.Players[0].Assists);
        Assert.Equal(5, match.Result.Players[0].Points);
        match.Players[0].Assists = 9;
        Assert.Equal(3, match.Result.Players[0].Assists);
        match.ResetCompetitiveState();
        Assert.Equal(0, match.Players[0].Assists);
    }
}
