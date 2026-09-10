using System;
using System.Linq;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using Xunit;

public sealed class VisualLightFrameIntegrationTests
{
    [Fact]
    public void CandidateCaptureIsEnhancedOnlyAndHonorsDynamicLightOption()
    {
        VisualLightCandidate candidate = Candidate(1, Vector3.Zero, priority: 1);

        RenderFrame original = Frame(GraphicsPreset.Original, dynamicLights: true);
        Assert.False(original.AddVisualLightCandidate(candidate));
        Assert.Empty(original.VisualLights);

        RenderFrame performance = Frame(GraphicsPreset.Performance, dynamicLights: true);
        Assert.False(performance.AddVisualLightCandidate(candidate));
        Assert.Empty(performance.VisualLights);

        RenderFrame disabled = Frame(GraphicsPreset.Enhanced, dynamicLights: false);
        Assert.False(disabled.AddVisualLightCandidate(candidate));
        Assert.Empty(disabled.VisualLights);

        RenderFrame enhanced = Frame(GraphicsPreset.Enhanced, dynamicLights: true);
        Assert.True(enhanced.AddVisualLightCandidate(candidate));
        Assert.Single(enhanced.VisualLights);
    }

    [Fact]
    public void FrameSelectionIsBoundedAndIndependentOfCandidateArrivalOrder()
    {
        VisualLightCandidate[] candidates = Enumerable.Range(0, 24)
            .Select(i => Candidate((ulong)(i + 1), new Vector3(i, 0, 0),
                priority: i % 4, intensity: 0.25f + i / 100f))
            .ToArray();
        RenderFrame forward = Frame(GraphicsPreset.Enhanced, dynamicLights: true);
        RenderFrame reverse = Frame(GraphicsPreset.Enhanced, dynamicLights: true);

        foreach (VisualLightCandidate candidate in candidates)
            forward.AddVisualLightCandidate(candidate);
        foreach (VisualLightCandidate candidate in candidates.Reverse())
            reverse.AddVisualLightCandidate(candidate);

        Assert.Equal(RenderFrame.MaximumVisualLights, forward.VisualLights.Count);
        Assert.Equal(forward.VisualLights.Select(light => light.Position),
            reverse.VisualLights.Select(light => light.Position));
        Assert.Equal(forward.VisualLights.Select(light => light.Priority),
            reverse.VisualLights.Select(light => light.Priority));
    }

    [Fact]
    public void LegacyAndProfiledAdmissionShareOneOrderIndependentBound()
    {
        RenderFrame forward = Frame(GraphicsPreset.Enhanced, dynamicLights: true);
        RenderFrame reverse = Frame(GraphicsPreset.Enhanced, dynamicLights: true);

        for (int i = 0; i < 20; i++) Admit(forward, i);
        for (int i = 19; i >= 0; i--) Admit(reverse, i);

        Assert.Equal(RenderFrame.MaximumVisualLights, forward.VisualLights.Count);
        Assert.Equal(forward.VisualLights.ToArray(), reverse.VisualLights.ToArray());

        static void Admit(RenderFrame frame, int index)
        {
            if ((index & 1) == 0)
            {
                frame.AddVisualLight(new RenderVisualLight(new Vector3(index, 0, 0),
                    Vector3.One, radius: 10, intensity: 0.5f,
                    priority: index % 5));
            }
            else
            {
                frame.AddVisualLightCandidate(Candidate((ulong)(100 + index),
                    new Vector3(index, 0, 0), priority: index % 5,
                    intensity: 0.5f));
            }
        }
    }

    [Fact]
    public void LegacyAdmissionCollapsesOnlyExactDuplicateValues()
    {
        RenderFrame frame = Frame(GraphicsPreset.Enhanced, dynamicLights: true);
        var first = new RenderVisualLight(new Vector3(1, 2, 3), Vector3.One,
            radius: 4, intensity: 0.5f, priority: 3);
        var distinct = new RenderVisualLight(new Vector3(1, 2, 4), Vector3.One,
            radius: 4, intensity: 0.5f, priority: 3);

        Assert.True(frame.AddVisualLight(first));
        Assert.False(frame.AddVisualLight(first));
        Assert.True(frame.AddVisualLight(distinct));
        Assert.Equal(2, frame.VisualLights.Count);
        Assert.Equal(VisualLightSourceKey.ForLegacy(first),
            VisualLightSourceKey.ForLegacy(first));
        Assert.NotEqual(VisualLightSourceKey.ForLegacy(first),
            VisualLightSourceKey.ForLegacy(distinct));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            frame.AddVisualLight(default));

        // Even a forced compact-key collision cannot merge distinct values.
        var selected = new VisualLightCandidate[RenderFrame.MaximumVisualLights];
        int selectedCount = 0;
        Assert.True(VisualLightSelection.TryInsert(
            VisualLightCandidate.FromLegacy(123, first), Vector3.Zero,
            selected, ref selectedCount));
        Assert.True(VisualLightSelection.TryInsert(
            VisualLightCandidate.FromLegacy(123, distinct), Vector3.Zero,
            selected, ref selectedCount));
        Assert.Equal(2, selectedCount);
    }

    [Fact]
    public void DuplicateStableSourceKeepsOnlyItsStrongestCandidate()
    {
        RenderFrame frame = Frame(GraphicsPreset.Enhanced, dynamicLights: true);
        Assert.True(frame.AddVisualLightCandidate(Candidate(7, new Vector3(2, 0, 0),
            priority: 1, intensity: 0.2f)));
        Assert.True(frame.AddVisualLightCandidate(Candidate(7, new Vector3(1, 0, 0),
            priority: 5, intensity: 0.8f)));
        Assert.False(frame.AddVisualLightCandidate(Candidate(7, new Vector3(3, 0, 0),
            priority: 0, intensity: 0.1f)));

        RenderVisualLight selected = Assert.Single(frame.VisualLights);
        Assert.Equal(5, selected.Priority);
        Assert.Equal(0.8f, selected.Intensity);
        Assert.Equal(new Vector3(1, 0, 0), selected.Position);
    }

    [Fact]
    public void PresentationIdentitiesAreStableWithinScopeAndResetExplicitly()
    {
        var identities = new VisualLightIdentityAllocator();
        var source = new object();
        ulong first = identities.GetSourceKey(
            VisualLightSourceKind.BeamProjectile, source, generation: 3);
        Assert.Equal(first, identities.GetSourceKey(
            VisualLightSourceKind.BeamProjectile, source, generation: 3));
        Assert.NotEqual(first, identities.GetSourceKey(
            VisualLightSourceKind.BeamProjectile, source, generation: 4));
        Assert.NotEqual(first, identities.GetSourceKey(
            VisualLightSourceKind.Bomb, source, generation: 3));
        Assert.NotEqual(first, identities.GetSourceKey(
            VisualLightSourceKind.BeamProjectile, new object(), generation: 3));

        ulong oldScope = identities.Scope;
        identities.ResetScope();
        Assert.True(identities.Scope > oldScope);
        Assert.NotEqual(first, identities.GetSourceKey(
            VisualLightSourceKind.BeamProjectile, source, generation: 3));

        Assert.Equal(VisualLightSourceKey.ForAuthored(
            VisualLightSourceKind.Teleporter, 42), VisualLightSourceKey.ForAuthored(
                VisualLightSourceKind.Teleporter, 42));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VisualLightSourceKey.ForAuthored(VisualLightSourceKind.Teleporter, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VisualLightSourceKey.ForPresentation(
                VisualLightSourceKind.Teleporter, scope: 0, identity: 1));
    }

    [Fact]
    public void EvidenceBackedAmbientProfilesAreInitializedAndDistinct()
    {
        VisualLightProfile morph = AmbientVisualLightProfiles.Bomb(BombType.MorphBall);
        VisualLightProfile sting = AmbientVisualLightProfiles.Bomb(BombType.Stinglarva);
        VisualLightProfile lockjaw = AmbientVisualLightProfiles.Bomb(BombType.Lockjaw);

        Assert.Equal(WeaponVisualLightProfiles.Bomb, morph);
        Assert.True(sting.Color.X > sting.Color.Z);
        Assert.True(lockjaw.Color.Z > lockjaw.Color.X);
        Assert.True(AmbientVisualLightProfiles.Teleporter.Radius > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AmbientVisualLightProfiles.Bomb((BombType)255));
    }

    [Fact]
    public void SealedFrameRejectsCandidateMutation()
    {
        RenderFrame frame = Frame(GraphicsPreset.Enhanced, dynamicLights: true);
        frame.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            frame.AddVisualLightCandidate(Candidate(1, Vector3.Zero, priority: 1)));
    }

    private static RenderFrame Frame(GraphicsPreset preset, bool dynamicLights)
    {
        var frame = new RenderFrame(1, 1);
        var quality = new RenderQualitySnapshot(preset,
            TextureFilteringPreset.Original, AnisotropyLevel.Off, MsaaLevel.Off,
            Bloom: false, DynamicVisualLights: dynamicLights);
        frame.CaptureState(Matrix4.Identity, Matrix4.Identity, Matrix4.Identity,
            Matrix4.Identity, Vector3.Zero, new Vector2i(640, 480),
            new Vector2i(640, 480), Vector4.UnitW,
            Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            hasFog: false, Vector4.Zero, 0, 0,
            default(RenderFrameOptions) with { Quality = quality });
        return frame;
    }

    private static VisualLightCandidate Candidate(ulong sourceKey, Vector3 position,
        int priority, float intensity = 1)
        => new(sourceKey, position, new VisualLightProfile(Vector3.One,
            radius: 10, intensity, priority, lifetime: 1, falloff: 2));
}
