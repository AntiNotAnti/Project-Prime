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
    [InlineData(false, false, false)] // stock, dynamic weapon
    [InlineData(false, true, false)]  // stock, static weapon
    [InlineData(true, false, false)]  // Pro, dynamic weapon
    [InlineData(true, true, false)]   // Pro, static weapon
    [InlineData(true, false, true)]   // fixed crosshair style, dynamic weapon
    public void CanonicalPositionIsIndependentOfHudWeaponAndCrosshairStyle(bool proHud,
        bool proHudFixedWeapon, bool fixedCrosshair)
    {
        _ = Features.ResolveFixedWeapon(proHud, proHudFixedWeapon, fixedWeapon: proHudFixedWeapon);
        _ = fixedCrosshair;
        Assert.Equal(new Vector2(0.7f, 0.3f),
            PlayerPresentation.NormalizeReticlePosition(1, new Vector2(0.7f, 0.3f)));
    }
}
