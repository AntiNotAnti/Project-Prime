using System;
using System.IO;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

public sealed class SdlGpuVisorIntegrationTests
{
    [Fact]
    public void EligibilityIsEnhancedLocalFirstPersonOnly()
    {
        Assert.True(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Enhanced,
            playerHud: true, spectator: false, alive: true, altForm: false,
            morphing: false, firstPerson: true, cameraSequence: false));
        Assert.False(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Original,
            true, false, true, false, false, true, false));
        Assert.False(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Performance,
            true, false, true, false, false, true, false));
        Assert.False(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Enhanced,
            true, true, true, false, false, true, false));
        Assert.False(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Enhanced,
            true, false, false, false, false, true, false));
        Assert.False(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Enhanced,
            true, false, true, true, false, true, false));
        Assert.False(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Enhanced,
            true, false, true, false, false, false, false));
        Assert.False(EnhancedVisorPolicy.IsEligible(GraphicsPreset.Enhanced,
            true, false, true, false, false, true, true));
    }

    [Fact]
    public void DamageStateProjectsDeduplicatesAndExpiresDeterministically()
    {
        var state = new VisorDamagePresentationState();
        CombatActor local = new(0, 17, 3);
        CombatEvent damage = Damage(id: 41, tick: 100, local,
            direction: Vector3.UnitX);

        Assert.True(state.Observe(damage, local, roomId: 7,
            Vector3.UnitZ, Vector3.UnitX));
        DamageVisorSample start = state.Sample(local, 7, 100, 0,
            EnhancedVisorProfiles.Damage);
        Assert.True(start.Direction.X > 0.7f);
        Assert.InRange(MathF.Abs(start.Direction.Y), 0, 0.00001f);
        DamageVisorSample interpolated = state.Sample(local, 7, 100, 0.5f,
            EnhancedVisorProfiles.Damage);
        Assert.True(interpolated.EdgeOpacity < start.EdgeOpacity);

        CombatEvent duplicate = Damage(id: 41, tick: 101, local,
            direction: -Vector3.UnitX);
        Assert.False(state.Observe(duplicate, local, 7,
            Vector3.UnitZ, Vector3.UnitX));
        Assert.True(state.Sample(local, 7, 100, 0,
            EnhancedVisorProfiles.Damage).Direction.X > 0.7f);

        DamageVisorSample expired = state.Sample(local, 7, 120, 0,
            EnhancedVisorProfiles.Damage);
        Assert.Equal(0, expired.EdgeOpacity);
        Assert.Equal(0, expired.Distortion);
    }

    [Fact]
    public void DamageStateResetsAcrossRoomLifeAndReplayFences()
    {
        var state = new VisorDamagePresentationState();
        CombatActor firstLife = new(1, 99, 4);
        Assert.True(state.Observe(Damage(1, 50, firstLife, Vector3.UnitZ),
            firstLife, 2, Vector3.UnitZ, Vector3.UnitX));

        Assert.Equal(0, state.Sample(firstLife, roomId: 3, 50, 0,
            EnhancedVisorProfiles.Damage).EdgeOpacity);
        Assert.False(state.HasDamage);

        Assert.True(state.Observe(Damage(2, 60, firstLife, Vector3.UnitZ),
            firstLife, 3, Vector3.UnitZ, Vector3.UnitX));
        CombatActor nextLife = firstLife with { Life = 5 };
        Assert.Equal(0, state.Sample(nextLife, 3, 60, 0,
            EnhancedVisorProfiles.Damage).EdgeOpacity);

        Assert.True(state.Observe(Damage(3, 70, nextLife, Vector3.UnitZ),
            nextLife, 3, Vector3.UnitZ, Vector3.UnitX));
        state.Reset();
        Assert.False(state.HasDamage);
        Assert.Equal(0, state.Sample(nextLife, 3, 70, 0,
            EnhancedVisorProfiles.Damage).EdgeOpacity);
    }

    [Fact]
    public void RenderFrameOwnsBoundedImmutableVisorSnapshot()
    {
        DamageVisorSample damage = EnhancedVisorProfiles.Damage.Sample(
            TimeSpan.Zero, Vector2.UnitX);
        LowHealthVisorSample low = EnhancedVisorProfiles.LowHealth.Sample(
            TimeSpan.FromSeconds(2), 0.1f, 9);
        var visor = new RenderVisorState(EnhancedVisorProfiles.Combat,
            damage, low, distortionPhase: 0.25f, interferencePhase: 0.75f);
        var frame = new RenderFrame(1, 1);

        frame.CaptureVisor(visor);
        frame.Seal();
        Assert.Equal(visor, frame.Visor);
        Assert.Throws<InvalidOperationException>(() =>
            frame.CaptureVisor(RenderVisorState.Disabled));
        frame.Reset();
        Assert.False(frame.Visor.Enabled);

        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderVisorState(
            EnhancedVisorProfiles.Combat,
            damage with { EdgeOpacity = VisorPresentationBounds.MaximumEdgeOpacity + 0.01f },
            low, 0, 0));
    }

    [Fact]
    public void VisorShaderKeepsDisplayLinearCenterClearAndBounded()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Rendering", "Shaders", "visor.hlsl"));

        Assert.Contains("cbuffer VisorConstants : register(b0, space3)", source,
            StringComparison.Ordinal);
        Assert.Contains("Texture2D sourceTexture : register(t0, space2)", source,
            StringComparison.Ordinal);
        Assert.Contains("smoothstep(saturate(clearRadius), 1.0f", source,
            StringComparison.Ordinal);
        Assert.Contains("MaximumChromaticSeparation = 0.006f", source,
            StringComparison.Ordinal);
        Assert.Contains("MaximumDistortion = 0.02f", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LinearToSRGB", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToneMap", source, StringComparison.Ordinal);
    }

    private static CombatEvent Damage(uint id, uint tick, CombatActor local,
        Vector3 direction)
        => new(Id: id, Tick: tick, CommandSequence: id,
            Kind: CombatEventKind.Damage, Weapon: 0, Flags: CombatEventFlags.None,
            Actor: new CombatActor(2, 44, 1), Target: local,
            Health: 50, Amount: 10, Position: Vector3.Zero, Direction: direction,
            FrozenTicks: 0, BurnTicks: 0, DisruptTicks: 0);
}
