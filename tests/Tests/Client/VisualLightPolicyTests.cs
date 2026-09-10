using System;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class VisualLightPolicyTests
{
    [Fact]
    public void ProfileRejectsInvalidPresentationData()
    {
        VisualLightProfile Valid(Vector3? color = null, float radius = 1,
            float intensity = 1, int priority = 1, float lifetime = 1, float falloff = 2)
            => new(color ?? Vector3.One, radius, intensity, priority, lifetime, falloff);

        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(new Vector3(float.NaN, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(radius: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(intensity: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(priority: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(lifetime: float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(falloff: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(falloff: 1));
        Assert.Equal(2f, VisualLightProfile.SupportedFalloff);
        Assert.Equal(Valid(), Valid());
    }

    [Fact]
    public void CatalogContainsRequestedWeaponDirectionsAndSeparateBombProfile()
    {
        BeamType[] expected =
        {
            BeamType.PowerBeam, BeamType.VoltDriver, BeamType.Missile,
            BeamType.Battlehammer, BeamType.Imperialist, BeamType.Judicator,
            BeamType.Magmaul, BeamType.ShockCoil
        };

        Assert.Equal(expected, WeaponVisualLightProfiles.SupportedBeamTypes);
        foreach (BeamType beam in expected)
        {
            Assert.True(WeaponVisualLightProfiles.TryGet(beam, out VisualLightProfile profile));
            Assert.True(profile.Radius > 0);
            Assert.True(profile.Intensity > 0);
        }
        Assert.False(WeaponVisualLightProfiles.TryGet(BeamType.None, out _));
        Assert.False(WeaponVisualLightProfiles.TryGet(BeamType.OmegaCannon, out _));
        Assert.True(WeaponVisualLightProfiles.Bomb.Color.Z
            > WeaponVisualLightProfiles.Bomb.Color.X);
    }

    [Fact]
    public void SelectionIsStableAcrossInputReorderingAndRespectsBound()
    {
        VisualLightCandidate[] candidates = Enumerable.Range(0, 20)
            .Select(i => Candidate((ulong)i, new Vector3(i % 4, 0, 0),
                priority: i % 5, intensity: 0.2f + i / 100f))
            .ToArray();

        ulong[] forward = VisualLightSelection.Select(candidates, Vector3.Zero)
            .Select(candidate => candidate.SourceKey).ToArray();
        ulong[] reverse = VisualLightSelection.Select(candidates.Reverse().ToArray(), Vector3.Zero)
            .Select(candidate => candidate.SourceKey).ToArray();

        Assert.Equal(VisualLightSelection.MaximumSupportedLights, forward.Length);
        Assert.Equal(forward, reverse);
        int[] selectedPriorities = forward
            .Select(key => candidates[(int)key].Profile.Priority).ToArray();
        Assert.Equal(new[] { 4, 4, 4, 4, 3, 3, 3, 3 }, selectedPriorities);
    }

    [Fact]
    public void SelectionRanksPriorityThenCameraRelevanceDistanceIntensityAndKey()
    {
        VisualLightCandidate[] candidates =
        {
            Candidate(9, new Vector3(100, 0, 0), priority: 2, intensity: 0.1f),
            Candidate(8, new Vector3(1, 0, 0), priority: 1, intensity: 1f),
            Candidate(7, new Vector3(0.5f, 0, 0), priority: 2, intensity: 0.5f),
            Candidate(6, new Vector3(-0.5f, 0, 0), priority: 2, intensity: 0.5f)
        };

        VisualLightCandidate[] selected = VisualLightSelection.Select(candidates,
            Vector3.Zero, maximumCount: 3);

        Assert.Equal(new ulong[] { 6, 7, 9 }, selected.Select(item => item.SourceKey));
        Assert.DoesNotContain(selected, item => item.SourceKey == 8);
    }

    [Fact]
    public void SelectionAndAttenuationValidateBounds()
    {
        VisualLightCandidate candidate = Candidate(1, Vector3.Zero, priority: 1, intensity: 1);

        Assert.Empty(VisualLightSelection.Select(new[] { candidate }, Vector3.Zero, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => VisualLightSelection.Select(
            new[] { candidate }, Vector3.Zero, VisualLightSelection.MaximumSupportedLights + 1));
        Assert.Equal(1f, VisualLightSelection.DistanceAttenuation(0, 10, 2));
        Assert.Equal(0.25f, VisualLightSelection.DistanceAttenuation(5, 10, 2), 5);
        Assert.Equal(0f, VisualLightSelection.DistanceAttenuation(10, 10, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VisualLightSelection.DistanceAttenuation(-1, 10, 2));
        Assert.Throws<ArgumentException>(() =>
            new VisualLightCandidate(1, Vector3.Zero, default));
    }

    private static VisualLightCandidate Candidate(ulong key, Vector3 position,
        int priority, float intensity)
        => new(key, position, new VisualLightProfile(Vector3.One, radius: 4,
            intensity, priority, lifetime: 0.2f, falloff: 2));
}
