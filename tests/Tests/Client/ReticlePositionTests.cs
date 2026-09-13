using MphRead.Entities;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ReticlePositionTests
{
    [Fact]
    public void NormalizedProjectionUsesTopLeftCoordinatesWithoutChangingOffscreenAim()
    {
        Assert.Equal(new Vector2(0.25f, 0.75f),
            PlayerPresentation.NormalizeReticlePosition(1, new Vector2(0.25f, 0.75f)));
        Assert.Equal(new Vector2(-2, 3),
            PlayerPresentation.NormalizeReticlePosition(1, new Vector2(-2, 3)));
    }

    [Theory]
    [InlineData(0, 0.1, 0.2)]
    [InlineData(-1, 0.1, 0.2)]
    [InlineData(double.NaN, 0.1, 0.2)]
    [InlineData(1, double.NaN, 0.2)]
    [InlineData(1, 0.1, double.PositiveInfinity)]
    public void InvalidProjectionFallsBackToCenter(double w, double x, double y)
    {
        Assert.Equal(new Vector2(0.5f, 0.5f), PlayerPresentation.NormalizeReticlePosition(
            (float)w, new Vector2((float)x, (float)y)));
        Assert.False(PlayerPresentation.TryNormalizeReticlePosition(
            (float)w, new Vector2((float)x, (float)y), out _));
    }

    [Fact]
    public void DynamicReticleUsesNearlyTheFullViewportWithSmoothEdgeSaturation()
    {
        Assert.Equal(new Vector2(0.5f, 0.5f),
            PlayerPresentation.ExpandDynamicReticleRange(new Vector2(0.5f)));

        Vector2 expanded = PlayerPresentation.ExpandDynamicReticleRange(
            new Vector2(0.75f, 0.25f));
        Assert.InRange(expanded.X, 0.89f, 0.90f);
        Assert.InRange(expanded.Y, 0.10f, 0.11f);

        Vector2 edges = PlayerPresentation.ExpandDynamicReticleRange(
            new Vector2(0, 1));
        Assert.InRange(edges.X, 0.02f, 0.04f);
        Assert.InRange(edges.Y, 0.96f, 0.98f);
        Assert.Equal(1, edges.X + edges.Y, 5);
    }

    [Fact]
    public void DynamicReticleHasNoCenterBiasedResponse()
    {
        var center = new Vector2(0.5f);
        var displaced = new Vector2(0.9f, 0.5f);

        Vector2 outward = PlayerPresentation.SmoothDynamicReticlePosition(
            center, displaced, 1f / 60);
        Vector2 returning = PlayerPresentation.SmoothDynamicReticlePosition(
            displaced, center, 1f / 60);

        Assert.Equal(outward.X - center.X, displaced.X - returning.X, 5);
        Assert.InRange(outward.X, 0.64f, 0.66f);
        Assert.InRange(returning.X, 0.74f, 0.76f);
    }

    [Fact]
    public void DynamicReticleHoldsLastPositionForInvalidTarget()
    {
        var current = new Vector2(0.82f, 0.21f);

        Assert.Equal(current, PlayerPresentation.SmoothDynamicReticlePosition(
            current, new Vector2(float.NaN, 0.5f), 1f / 60));
    }

    [Fact]
    public void DynamicReticleSmoothingIsRefreshRateIndependent()
    {
        var center = new Vector2(0.5f);
        var displaced = new Vector2(0.9f, 0.5f);
        Vector2 at60Hz = displaced;
        Vector2 at144Hz = displaced;

        for (int i = 0; i < 30; i++)
            at60Hz = PlayerPresentation.SmoothDynamicReticlePosition(
                at60Hz, center, 1f / 60);
        for (int i = 0; i < 72; i++)
            at144Hz = PlayerPresentation.SmoothDynamicReticlePosition(
                at144Hz, center, 1f / 144);

        Assert.Equal(at60Hz.X, at144Hz.X, 5);
        Assert.Equal(at60Hz.Y, at144Hz.Y, 5);
    }

    [Theory]
    [InlineData(0, 0, -1, 1)]
    [InlineData(0.5, 0.5, 0, 0)]
    [InlineData(1, 1, 1, -1)]
    public void ConvertsCanonicalTopLeftPositionToNdc(double x, double y, double expectedX, double expectedY)
    {
        Vector2 ndc = Crosshair.ToNdc(new Vector2((float)x, (float)y));
        Assert.Equal((float)expectedX, ndc.X, 5);
        Assert.Equal((float)expectedY, ndc.Y, 5);
    }

    [Fact]
    public void CapturedCrosshairGeometryOffsetsAroundCanonicalPosition()
    {
        Vector3 vertex = Crosshair.OffsetNdc(new Vector2(0.75f, 0.25f),
            pixelX: 10, pixelY: -5, halfWidth: 100, halfHeight: 50);
        Assert.Equal(0.6f, vertex.X, 5);
        Assert.Equal(0.4f, vertex.Y, 5);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    public void DynamicReticleContractsFromItsCurrentlyDisplayedFrame(int currentFrame,
        int expectedStart)
    {
        Assert.Equal(expectedStart,
            PlayerPresentation.ReticleShotAnimationStart(currentFrame));
    }

    [Theory]
    [InlineData(false, false, false)] // stock, dynamic weapon
    [InlineData(false, true, false)]  // stock, static weapon
    [InlineData(true, false, false)]  // Pro, dynamic weapon
    [InlineData(true, true, false)]   // Pro, static weapon
    [InlineData(true, false, true)]   // static crosshair, dynamic weapon
    public void CanonicalPositionIsIndependentOfHudWeaponAndCrosshairStyle(bool proHud,
        bool proHudFixedWeapon, bool fixedCrosshair)
    {
        _ = Features.ResolveFixedWeapon(proHud, proHudFixedWeapon, fixedWeapon: proHudFixedWeapon);
        _ = fixedCrosshair;
        Assert.Equal(new Vector2(0.7f, 0.3f),
            PlayerPresentation.NormalizeReticlePosition(1, new Vector2(0.7f, 0.3f)));
    }
}
