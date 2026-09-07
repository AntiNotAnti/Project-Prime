using OpenTK.Mathematics;
using MphRead.Hud.Radar;
using Xunit;

namespace MphRead.Tests;

public sealed class RadarTests
{
    [Fact]
    public void HeadingProjectionPlacesFacingDirectionAtTheTop()
    {
        RadarPoint point = RadarWidget.Project(Contact(new Vector3(0, 0, 10)),
            Vector3.Zero, Vector3.UnitZ, RadarOrientation.Heading);

        Assert.InRange(point.RelativePosition.X, -0.0001f, 0.0001f);
        Assert.InRange(point.RelativePosition.Y, -0.2501f, -0.2499f);
        Assert.False(point.Clamped);
        Assert.True(float.IsFinite(point.RelativePosition.X));
        Assert.True(float.IsFinite(point.RelativePosition.Y));
    }

    [Fact]
    public void NorthProjectionUsesWorldNorthRegardlessOfPlayerFacing()
    {
        RadarContact contact = Contact(new Vector3(10, 0, -10));
        RadarPoint facingEast = RadarWidget.Project(contact, Vector3.Zero,
            Vector3.UnitX, RadarOrientation.North);
        RadarPoint facingSouth = RadarWidget.Project(contact, Vector3.Zero,
            -Vector3.UnitZ, RadarOrientation.North);

        Assert.Equal(facingEast.RelativePosition, facingSouth.RelativePosition);
        Assert.InRange(facingEast.RelativePosition.X, 0.2499f, 0.2501f);
        Assert.InRange(facingEast.RelativePosition.Y, -0.2501f, -0.2499f);
    }

    [Fact]
    public void ElevationUsesTheConfiguredStrictThreshold()
    {
        RadarContact same = Contact(new Vector3(0, RadarSettings.ElevationThreshold, 0));
        RadarContact above = Contact(new Vector3(0, RadarSettings.ElevationThreshold + .01f, 0));
        RadarContact below = Contact(new Vector3(0, -RadarSettings.ElevationThreshold - .01f, 0));

        Assert.Equal(RadarElevation.Same,
            RadarWidget.Project(same, Vector3.Zero, Vector3.UnitZ, RadarOrientation.Heading).Elevation);
        Assert.Equal(RadarElevation.Above,
            RadarWidget.Project(above, Vector3.Zero, Vector3.UnitZ, RadarOrientation.Heading).Elevation);
        Assert.Equal(RadarElevation.Below,
            RadarWidget.Project(below, Vector3.Zero, Vector3.UnitZ, RadarOrientation.Heading).Elevation);
    }

    [Fact]
    public void FarContactIsClampedToTheUnitCircle()
    {
        RadarPoint point = RadarWidget.Project(Contact(new Vector3(0, 0, 80)),
            Vector3.Zero, Vector3.UnitZ, RadarOrientation.Heading);

        Assert.True(point.Clamped);
        Assert.InRange(point.RelativePosition.Length, 0.9999f, 1.0001f);
        Assert.InRange(point.RelativePosition.X, -0.0001f, 0.0001f);
        Assert.InRange(point.RelativePosition.Y, -1.0001f, -0.9999f);
    }

    [Fact]
    public void FrameRejectsZeroAlphaAndNonFiniteContacts()
    {
        var frame = NewFrame();

        Assert.False(frame.AddApproved(Contact(Vector3.Zero, 0)));
        Assert.False(frame.AddApproved(Contact(new Vector3(float.NaN, 0, 0))));
        Assert.False(frame.AddApproved(Contact(new Vector3(0, float.PositiveInfinity, 0))));
        Assert.False(frame.AddApproved(Contact(Vector3.Zero, float.NaN)));
        Assert.Equal(0, frame.Contacts.Length);
        Assert.Equal(0, frame.Dropped);
    }

    [Fact]
    public void FrameClampsVisibilityAndBoundsContactsAt64()
    {
        var frame = NewFrame();

        Assert.True(frame.AddApproved(Contact(Vector3.Zero, 2)));
        for (int i = 1; i < RadarFrame.Capacity; i++)
            Assert.True(frame.AddApproved(Contact(new Vector3(i, 0, 0))));
        Assert.Equal(1, frame.Contacts[0].Visibility);
        Assert.Equal(RadarFrame.Capacity, frame.Contacts.Length);

        for (int i = 0; i < 3; i++)
            Assert.False(frame.AddApproved(Contact(new Vector3(100 + i, 0, 0))));
        Assert.Equal(3, frame.Dropped);
        Assert.Equal(RadarFrame.Capacity, frame.Contacts.Length);
    }

    [Fact]
    public void BeginStartsAFreshFrameWithoutStaleContactsOrDrops()
    {
        var frame = NewFrame();
        Assert.True(frame.AddApproved(Contact(new Vector3(1, 0, 0))));
        for (int i = 1; i <= RadarFrame.Capacity; i++)
            frame.AddApproved(Contact(new Vector3(i, 0, 0)));
        Assert.True(frame.Dropped > 0);

        frame.Begin(new Vector3(4, 5, 6), Vector3.UnitX, 99);

        Assert.Equal(0, frame.Contacts.Length);
        Assert.Equal(0, frame.Dropped);
        Assert.Equal(new Vector3(4, 5, 6), frame.Origin);
        Assert.Equal(Vector3.UnitX, frame.Facing);
        Assert.Equal((ulong)99, frame.Tick);
        Assert.True(frame.AddApproved(Contact(new Vector3(7, 0, 0))));
        Assert.Equal(1, frame.Contacts.Length);
        Assert.Equal(new Vector3(7, 0, 0), frame.Contacts[0].Position);
    }

    private static RadarFrame NewFrame()
    {
        var frame = new RadarFrame();
        frame.Begin(Vector3.Zero, Vector3.UnitZ, 1);
        return frame;
    }

    private static RadarContact Contact(Vector3 position, float visibility = 1)
    {
        return new RadarContact(RadarContactType.Enemy, position, 0,
            RadarObjective.None, visibility);
    }
}
