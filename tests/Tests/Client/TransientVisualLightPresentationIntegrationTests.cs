using System;
using System.IO;
using System.Linq;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class TransientVisualLightPresentationIntegrationTests
{
    [Fact]
    public void QueuedEventsCaptureIntoCandidatesAndExpireByCapturedFrameTime()
    {
        var state = new TransientVisualLightPresentationState();
        VisualLightProfile profile = Profile(priority: 4, lifetime: 0.25f);

        Assert.True(state.TryQueue(7, new Vector3(1, 2, 3), profile));
        Assert.False(state.TryQueue(7, Vector3.Zero, Profile(priority: 9)));
        VisualLightCandidate candidate = Assert.Single(state.CaptureFrame(
            10, TimeSpan.FromSeconds(1)));
        Assert.Equal(7ul, candidate.SourceKey);
        Assert.Equal(new Vector3(1, 2, 3), candidate.Position);
        Assert.Equal(0, state.PendingCount);
        Assert.Equal(1, state.ActiveCount);

        Assert.Single(state.CaptureFrame(11, TimeSpan.FromMilliseconds(1249)));
        Assert.Empty(state.CaptureFrame(12, TimeSpan.FromMilliseconds(1250)));
    }

    [Fact]
    public void LocalAcknowledgementQueuesExactlyOnceWithoutExtendingLifetime()
    {
        var state = new TransientVisualLightPresentationState();
        ulong key = TransientVisualLightSourceKey.ForEvent(
            TransientVisualLightSourceKind.MuzzleFlash, 3, eventId: 41);
        VisualLightProfile profile = Profile(priority: 4, lifetime: 0.25f);

        Assert.True(state.TryQueue(key, Vector3.Zero, profile));
        Assert.Single(state.CaptureFrame(20, TimeSpan.FromSeconds(2)));

        // The same authoritative event may be observed again, but its stable
        // key cannot add a second light or move the original expiry boundary.
        Assert.True(state.TryQueue(key, Vector3.One, profile));
        VisualLightCandidate retained = Assert.Single(state.CaptureFrame(
            21, TimeSpan.FromMilliseconds(2100)));
        Assert.Equal(Vector3.Zero, retained.Position);
        Assert.Empty(state.CaptureFrame(22, TimeSpan.FromMilliseconds(2250)));
    }

    [Fact]
    public void PendingOverflowAndCaptureOrderArePriorityAndKeyStable()
    {
        var forward = new TransientVisualLightPresentationState();
        var reverse = new TransientVisualLightPresentationState();
        ulong[] keys = Enumerable.Range(1,
            TransientVisualLightPool.MaximumLights + 4)
            .Select(value => (ulong)value).ToArray();

        foreach (ulong key in keys)
            forward.TryQueue(key, Vector3.Zero, Profile(priority: 1));
        foreach (ulong key in keys.Reverse())
            reverse.TryQueue(key, Vector3.Zero, Profile(priority: 1));
        Assert.True(forward.TryQueue(1000, Vector3.Zero, Profile(priority: 2)));
        Assert.True(reverse.TryQueue(1000, Vector3.Zero, Profile(priority: 2)));

        ulong[] expected = new[] { 1000ul }.Concat(Enumerable.Range(1,
            TransientVisualLightPool.MaximumLights - 1)
            .Select(value => (ulong)value)).ToArray();
        Assert.Equal(expected, forward.CaptureFrame(1, TimeSpan.Zero)
            .Select(candidate => candidate.SourceKey));
        Assert.Equal(expected, reverse.CaptureFrame(1, TimeSpan.Zero)
            .Select(candidate => candidate.SourceKey));
    }

    [Fact]
    public void EventKeysAreStableAndScopeSeparated()
    {
        ulong first = TransientVisualLightSourceKey.ForEvent(
            TransientVisualLightSourceKind.MuzzleFlash, 5, 9);

        Assert.NotEqual(0ul, first);
        Assert.Equal(first, TransientVisualLightSourceKey.ForEvent(
            TransientVisualLightSourceKind.MuzzleFlash, 5, 9));
        Assert.NotEqual(first, TransientVisualLightSourceKey.ForEvent(
            TransientVisualLightSourceKind.MuzzleFlash, 6, 9));
        Assert.NotEqual(first, TransientVisualLightSourceKey.ForEvent(
            TransientVisualLightSourceKind.MuzzleFlash, 5, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TransientVisualLightSourceKey.ForEvent(
                TransientVisualLightSourceKind.MuzzleFlash, 0, 9));
        var state = new TransientVisualLightPresentationState();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.TryQueue(0, Vector3.Zero, Profile(priority: 1)));
    }

    [Fact]
    public void PresentationResetDropsActiveAndPendingEvents()
    {
        var state = new TransientVisualLightPresentationState();
        state.TryQueue(1, Vector3.Zero, Profile(priority: 1));
        Assert.Single(state.CaptureFrame(5, TimeSpan.FromSeconds(1)));
        state.TryQueue(2, Vector3.One, Profile(priority: 2));

        state.Reset();

        Assert.Equal(0, state.ActiveCount);
        Assert.Equal(0, state.PendingCount);
        Assert.Empty(state.CaptureFrame(0, TimeSpan.Zero));
    }

    [Fact]
    public void RendererFlushesTransientSnapshotBeforeFrameSeal()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root,
            "src", "Client.Presentation", "Rendering", "Renderer.cs"));
        int draw = source.IndexOf("public void OnDrawFrame()",
            StringComparison.Ordinal);
        int getDrawItems = source.IndexOf("GetDrawItems();", draw,
            StringComparison.Ordinal);
        int submit = source.IndexOf("SubmitTransientVisualLights(", draw,
            StringComparison.Ordinal);
        int seal = source.IndexOf("_renderFrame.Seal();", submit,
            StringComparison.Ordinal);

        Assert.True(draw >= 0);
        Assert.True(getDrawItems > draw);
        Assert.True(submit > getDrawItems);
        Assert.True(seal > submit);
        Assert.Contains("EnvironmentalParticlePresentationClock.Capture(",
            source[draw..submit], StringComparison.Ordinal);

        string combatPresentation = File.ReadAllText(Path.Combine(root,
            "src", "Client.Presentation", "Presentation", "Players",
            "PresentationPlayerEntityCombat.cs"));
        int muzzleAdmission = combatPresentation.IndexOf(
            "TransientVisualLightSourceKind.MuzzleFlash, value.Id",
            StringComparison.Ordinal);
        int predictedSuppression = combatPresentation.IndexOf(
            "if (predictedLocalShot)", muzzleAdmission, StringComparison.Ordinal);
        int remoteMuzzleEffect = combatPresentation.IndexOf(
            "Presentation.SpawnEffect(Metadata.MuzzleEffectIds[value.Weapon]",
            predictedSuppression, StringComparison.Ordinal);
        Assert.True(muzzleAdmission >= 0);
        Assert.True(predictedSuppression > muzzleAdmission);
        Assert.True(remoteMuzzleEffect > predictedSuppression);
        Assert.DoesNotContain("TrySpawnTransientVisualLight(effectId",
            combatPresentation, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Game.sln"))) return directory;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    private static VisualLightProfile Profile(int priority, float lifetime = 10)
        => new(Vector3.One, radius: 4, intensity: 1, priority,
            lifetime, falloff: VisualLightProfile.SupportedFalloff);
}
