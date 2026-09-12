using System;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedPresentationProfileTests
{
    [Fact]
    public void BeamCatalogHasEightDistinctPresentationIdentities()
    {
        BeamType[] expected =
        {
            BeamType.PowerBeam,
            BeamType.VoltDriver,
            BeamType.Missile,
            BeamType.Battlehammer,
            BeamType.Imperialist,
            BeamType.Judicator,
            BeamType.Magmaul,
            BeamType.ShockCoil
        };

        Assert.Equal(expected, WeaponBeamVisualProfiles.SupportedBeamTypes);
        BeamVisualProfile[] profiles = expected.Select(beam =>
        {
            Assert.True(WeaponBeamVisualProfiles.TryGet(beam, out BeamVisualProfile profile));
            Assert.True(WeaponVisualLightProfiles.TryGet(beam, out VisualLightProfile light));
            Assert.Equal(light, profile.Light);
            Assert.True(profile.GlowWidth >= profile.CoreWidth);
            return profile;
        }).ToArray();

        Assert.Equal(8, profiles.Select(profile => profile.ImpactStyle).Distinct().Count());
        Assert.Equal(8, profiles.Select(profile => profile.CoreColor).Distinct().Count());
        Assert.False(WeaponBeamVisualProfiles.TryGet(BeamType.OmegaCannon, out _));
        Assert.True(profiles[^1].SecondaryArcStrength > 0);
        Assert.All(profiles[..^1], profile => Assert.Equal(0, profile.SecondaryArcStrength));
    }

    [Fact]
    public void BeamSamplingIsDeterministicAndBoundedByProfile()
    {
        Assert.True(WeaponBeamVisualProfiles.TryGet(BeamType.VoltDriver,
            out BeamVisualProfile profile));
        TimeSpan time = TimeSpan.FromSeconds(12.345);
        BeamVisualSample first = profile.Sample(time, 44);
        BeamVisualSample second = profile.Sample(time, 44);

        Assert.Equal(first, second);
        Assert.InRange(first.Pulse, 1 - profile.PulseStrength, 1 + profile.PulseStrength);
        Assert.InRange(first.NoisePhase, 0, 0.99999999f);
        Assert.NotEqual(first, profile.Sample(time, 45));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            profile.Sample(TimeSpan.FromTicks(-1), 44));
    }

    [Fact]
    public void BeamProfileRejectsUnboundedOrUninitializedData()
    {
        Assert.True(WeaponVisualLightProfiles.TryGet(BeamType.PowerBeam,
            out VisualLightProfile light));

        Assert.Throws<ArgumentOutOfRangeException>(() => NewBeam(light,
            coreWidth: 1, glowWidth: 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewBeam(light,
            coreWidth: 1, glowWidth: 2, noiseStrength: float.NaN));
        Assert.Throws<ArgumentException>(() => NewBeam(light,
            coreWidth: 1, glowWidth: 2, pulseFrequency: 0, pulseStrength: 0.1f));
        Assert.Throws<ArgumentException>(() => NewBeam(default,
            coreWidth: 1, glowWidth: 2));
        Assert.Throws<InvalidOperationException>(() =>
            default(BeamVisualProfile).Sample(TimeSpan.Zero, 0));
    }

    [Fact]
    public void ForceFieldDefaultIsBoundedAndSamplesOnlyPresentationTime()
    {
        ForceFieldVisualProfile profile = EnhancedForceFieldProfiles.Default;
        TimeSpan time = TimeSpan.FromSeconds(80.25);
        ForceFieldVisualSample first = profile.Sample(time, 9);
        ForceFieldVisualSample second = profile.Sample(time, 9);

        Assert.Equal(first, second);
        Assert.InRange(first.UvOffset.X, 0, 0.99999999f);
        Assert.InRange(first.UvOffset.Y, 0, 0.99999999f);
        Assert.InRange(first.NoisePhase, 0, 0.99999999f);
        Assert.InRange(profile.DistortionStrength, 0,
            ForceFieldVisualProfile.MaximumDistortionStrength);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            profile.Sample(TimeSpan.FromTicks(-1), 9));
    }

    [Fact]
    public void ForceFieldProfileRejectsInvalidEffectBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ForceFieldVisualProfile(
            1, 0.5f, 1, Vector2.Zero, 4, 1, Vector3.One, 1,
            ForceFieldVisualProfile.MaximumDistortionStrength + 0.001f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ForceFieldVisualProfile(
            1, 0.5f, 1, new Vector2(float.NaN, 0), 4, 1,
            Vector3.One, 1, 0, 1));
        Assert.Throws<InvalidOperationException>(() =>
            default(ForceFieldVisualProfile).Sample(TimeSpan.Zero, 0));
    }

    [Fact]
    public void VisorDefaultsPreserveCenterClarityAndBoundEffects()
    {
        Assert.InRange(EnhancedVisorProfiles.Combat.CenterClearRadius,
            VisorPresentationBounds.MinimumCenterClearRadius, 1);
        Assert.InRange(EnhancedVisorProfiles.Damage.CenterClearRadius,
            VisorPresentationBounds.MinimumCenterClearRadius, 1);
        Assert.InRange(EnhancedVisorProfiles.LowHealth.CenterClearRadius,
            VisorPresentationBounds.MinimumCenterClearRadius, 1);
        Assert.InRange(EnhancedVisorProfiles.Combat.Distortion, 0,
            VisorPresentationBounds.MaximumDistortion);
        Assert.InRange(EnhancedVisorProfiles.Damage.EdgeOpacity, 0,
            VisorPresentationBounds.MaximumEdgeOpacity);
        Assert.InRange(EnhancedVisorProfiles.LowHealth.Interference, 0,
            VisorPresentationBounds.MaximumInterference);
    }

    [Fact]
    public void DamageImpulseDecaysAndExpiresUsingPresentationElapsedTime()
    {
        DamageVisorProfile profile = EnhancedVisorProfiles.Damage;
        DamageVisorSample start = profile.Sample(TimeSpan.Zero, new Vector2(3, 4));
        DamageVisorSample middle = profile.Sample(profile.Duration / 2, new Vector2(3, 4));
        DamageVisorSample expired = profile.Sample(profile.Duration, new Vector2(3, 4));

        Assert.True(start.EdgeOpacity > middle.EdgeOpacity);
        Assert.True(middle.EdgeOpacity > expired.EdgeOpacity);
        Assert.InRange(start.Direction.Length, 0, 1);
        Assert.Equal(0, expired.Distortion);
        Assert.Equal(Vector3.Zero, expired.ColorImpulse);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            profile.Sample(TimeSpan.Zero, new Vector2(float.NaN, 0)));
    }

    [Fact]
    public void LowHealthImpulseIsDeterministicBoundedAndInactiveAboveThreshold()
    {
        LowHealthVisorProfile profile = EnhancedVisorProfiles.LowHealth;
        TimeSpan time = TimeSpan.FromSeconds(31.125);
        LowHealthVisorSample first = profile.Sample(time, 0.1f, 77);
        LowHealthVisorSample second = profile.Sample(time, 0.1f, 77);
        LowHealthVisorSample healthy = profile.Sample(time, profile.HealthThreshold, 77);

        Assert.Equal(first, second);
        Assert.InRange(first.EdgeOpacity, 0, profile.EdgeOpacity);
        Assert.InRange(first.Interference, 0, profile.Interference);
        Assert.Equal(0, healthy.EdgeOpacity);
        Assert.Equal(0, healthy.Interference);
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.Sample(time, 1.1f, 77));
    }

    [Fact]
    public void VisorProfilesRejectCenterObstructionAndUnboundedImpulses()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombatVisorProfile(
            0.4f, 0.1f, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DamageVisorProfile(
            0.7f, TimeSpan.FromSeconds(2), 0.1f, 0, Vector3.One, 0.2f, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LowHealthVisorProfile(
            0.7f, 0.25f, 0.1f, 0.1f, 5));
        Assert.Throws<InvalidOperationException>(() =>
            default(DamageVisorProfile).Sample(TimeSpan.Zero, Vector2.Zero));
        Assert.Throws<InvalidOperationException>(() =>
            default(LowHealthVisorProfile).Sample(TimeSpan.Zero, 0, 0));
    }

    private static BeamVisualProfile NewBeam(VisualLightProfile light,
        float coreWidth, float glowWidth, float noiseStrength = 0,
        float pulseFrequency = 0, float pulseStrength = 0)
        => new(Vector3.One, Vector3.One, coreWidth, glowWidth,
            noiseStrength, noiseScale: 1, noiseSpeed: 0,
            pulseFrequency, pulseStrength, BeamImpactStyle.EnergyFlash,
            impactScale: 1, secondaryArcStrength: 0, light);
}
