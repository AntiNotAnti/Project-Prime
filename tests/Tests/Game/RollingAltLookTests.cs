using MphRead.Entities;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

public sealed class RollingAltLookTests
{
    [Fact]
    public void HorizontalLookRotatesTheOrbitAndMovementBasisTogether()
    {
        (Vector3 position, Vector3 forward, Vector3 right) =
            PlayerEntity.RotateRollingAltCamera(new Vector3(0, 1, -3),
                Vector3.Zero, new Vector2(90, 0));

        Assert.Equal(-3, position.X, 5);
        Assert.Equal(1, position.Y, 5);
        Assert.Equal(0, position.Z, 5);
        Assert.Equal(1, forward.X, 5);
        Assert.Equal(0, forward.Y, 5);
        Assert.Equal(0, forward.Z, 5);
        Assert.Equal(0, right.X, 5);
        Assert.Equal(0, right.Y, 5);
        Assert.Equal(-1, right.Z, 5);
    }

    [Fact]
    public void VerticalLookPreservesRadiusAndReturnsFiniteHorizontalAxes()
    {
        Vector3 original = new(0, 1, -3);

        (Vector3 position, Vector3 forward, Vector3 right) =
            PlayerEntity.RotateRollingAltCamera(original, Vector3.Zero,
                new Vector2(15, 20));

        Assert.Equal(original.Length, position.Length, 5);
        Assert.True(VectorMath.IsFinite(position));
        Assert.True(VectorMath.IsFinite(forward));
        Assert.True(VectorMath.IsFinite(right));
        Assert.Equal(1, forward.Length, 5);
        Assert.Equal(1, right.Length, 5);
        Assert.Equal(0, forward.Y, 6);
        Assert.Equal(0, right.Y, 6);
        Assert.Equal(0, Vector3.Dot(forward, right), 5);
    }
}
