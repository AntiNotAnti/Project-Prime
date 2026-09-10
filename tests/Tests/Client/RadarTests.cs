using System;
using System.Reflection;
using MphRead.Formats;
using MphRead.Formats.Collision;
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

    [Theory]
    [InlineData(RadarAnchor.TopRight, true, true)]
    [InlineData(RadarAnchor.TopLeft, false, true)]
    [InlineData(RadarAnchor.BottomRight, true, false)]
    [InlineData(RadarAnchor.BottomLeft, false, false)]
    public void LayoutAnchorsAndAspectCorrectsWithoutLeavingLogicalHud(RadarAnchor anchor,
        bool right, bool top)
    {
        RadarLayout layout = RadarLayoutCalculator.Calculate(anchor, 1, 0, 0, .75f);
        Assert.Equal(52, layout.Height, 3);
        Assert.Equal(39, layout.Width, 3);
        Assert.Equal(right, layout.CenterX > 128);
        Assert.Equal(top, layout.CenterY < 96);
        Assert.InRange(layout.Left, 0, 256 - layout.Width);
        Assert.InRange(layout.Top, 0, 192 - layout.Height);
    }

    [Fact]
    public void LayoutSanitizesScaleOffsetsAndAspect()
    {
        RadarLayout layout = RadarLayoutCalculator.Calculate(RadarAnchor.TopRight,
            float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.NaN);
        Assert.Equal(RadarLayoutCalculator.BaseDiameter, layout.Width, 3);
        Assert.Equal(RadarLayoutCalculator.BaseDiameter, layout.Height, 3);
        Assert.InRange(layout.Left, 0, 256 - layout.Width);
        Assert.InRange(layout.Top, 0, 192 - layout.Height);
        Assert.Equal(RadarSettings.MinimumScale * RadarLayoutCalculator.BaseDiameter,
            RadarLayoutCalculator.Calculate(RadarAnchor.Custom, -100, -1000, -1000, 1).Height, 3);
    }

    [Fact]
    public void WorldProjectionHandlesCornersCenterAndDegenerateBounds()
    {
        var geometry = new RadarMapGeometry(new Vector2(-10, -20), new Vector2(30, 60),
            Array.Empty<RadarFloorBand>());
        Assert.Equal(Vector2.Zero, geometry.WorldToMap(new Vector3(-10, 0, -20)));
        Assert.Equal(Vector2.One, geometry.WorldToMap(new Vector3(30, 0, 60)));
        Assert.Equal(new Vector2(.5f), geometry.WorldToMap(new Vector3(10, 0, 20)));
        var degenerate = new RadarMapGeometry(Vector2.One, Vector2.One, Array.Empty<RadarFloorBand>());
        Assert.Equal(new Vector2(.5f), degenerate.WorldToMap(Vector3.Zero));
    }

    [Fact]
    public void FloorClusteringIsDeterministicAndNearestSelectionCoversExtremes()
    {
        RadarPolygon Polygon(float elevation) => new(new[]
        {
            new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 4), new Vector2(0, 4)
        }, elevation, RadarSurfaceKind.Floor);
        RadarMapGeometry geometry = RadarMapBuilder.BuildGeometry(new[]
        {
            Polygon(10), Polygon(.4f), Polygon(0), Polygon(10.5f)
        });
        Assert.Equal(2, geometry.Floors.Count);
        Assert.Equal(0, geometry.FindCurrentFloor(-100));
        Assert.Equal(1, geometry.FindCurrentFloor(100));
        Assert.Equal(2, geometry.Floors[0].Polygons.Count);
        Assert.Equal(2, geometry.Floors[1].Polygons.Count);
    }

    [Fact]
    public void RasterizationIsDeterministicAndKeepsTransparentOuterBorder()
    {
        var polygon = new RadarPolygon(new[]
        {
            new Vector2(-2, -2), new Vector2(2, -2), new Vector2(2, 2), new Vector2(-2, 2)
        }, 0, RadarSurfaceKind.Floor);
        RadarMapGeometry geometry = RadarMapBuilder.BuildGeometry(new[] { polygon });
        RadarMap first = RadarMapRasterizer.Rasterize(geometry, 32);
        RadarMap second = RadarMapRasterizer.Rasterize(geometry, 32);
        Assert.Single(first.Floors);
        Assert.Equal(first.Floors[0].Pixels, second.Floors[0].Pixels);
        for (int i = 0; i < 32; i++)
        {
            Assert.Equal((byte)0, first.Floors[0].Pixels[i].Alpha);
            Assert.Equal((byte)0, first.Floors[0].Pixels[31 * 32 + i].Alpha);
        }
        Assert.Contains(first.Floors[0].Pixels, pixel => pixel.Alpha > 0);
    }

    [Fact]
    public void MphBuilderExtractsTranslatedFloorAndRejectsWallInactiveAndInvalidData()
    {
        var points = new[]
        {
            new Vector3Fx(0, 0, 0), new Vector3Fx(4096, 0, 0),
            new Vector3Fx(4096, 0, 4096), new Vector3Fx(0, 0, 4096)
        };
        var planes = new[]
        {
            Struct<Vector4Fx>((nameof(Vector4Fx.Y), new Fixed(4096))),
            Struct<Vector4Fx>((nameof(Vector4Fx.X), new Fixed(4096)))
        };
        var floor = Struct<CollisionData>((nameof(CollisionData.PlaneIndex), (ushort)0),
            (nameof(CollisionData.PointIndexCount), (ushort)4),
            (nameof(CollisionData.PointStartIndex), (ushort)0));
        var wall = Struct<CollisionData>((nameof(CollisionData.PlaneIndex), (ushort)1),
            (nameof(CollisionData.PointIndexCount), (ushort)4),
            (nameof(CollisionData.PointStartIndex), (ushort)0));
        var invalid = Struct<CollisionData>((nameof(CollisionData.PlaneIndex), ushort.MaxValue),
            (nameof(CollisionData.PointIndexCount), (ushort)4));
        var info = new MphCollisionInfo(default, points, planes, new ushort[] { 0, 1, 2, 3 },
            new[] { floor, wall, invalid }, Array.Empty<ushort>(), Array.Empty<CollisionEntry>(), Array.Empty<Portal>());
        var active = new CollisionInstance("active", info, false) { Translation = new Vector3(10, 5, -3) };
        var inactive = new CollisionInstance("inactive", info, false) { Active = false };

        RadarMapGeometry geometry = RadarMapBuilder.Build(new[] { active, inactive });

        Assert.Single(geometry.Floors);
        Assert.Single(geometry.Floors[0].Polygons);
        Assert.Equal(5, geometry.Floors[0].CenterY, 3);
        Assert.Equal(new Vector2(10, -3), geometry.Min);
        Assert.Equal(new Vector2(11, -2), geometry.Max);
    }

    [Fact]
    public void FhBuilderUsesPoint2AndSkipsPortalPrefixAndCeilings()
    {
        var points = new[]
        {
            new Vector3Fx(0, 0, 0), new Vector3Fx(4096, 0, 0),
            new Vector3Fx(4096, 0, 4096), new Vector3Fx(0, 0, 4096),
            new Vector3Fx(8192, 0, 0), new Vector3Fx(12288, 0, 0),
            new Vector3Fx(12288, 0, 4096), new Vector3Fx(8192, 0, 4096)
        };
        var planes = new[]
        {
            Struct<Vector4Fx>((nameof(Vector4Fx.Y), new Fixed(4096))),
            Struct<Vector4Fx>((nameof(Vector4Fx.Y), new Fixed(-4096)))
        };
        var vectors = new[]
        {
            FhVector(0), FhVector(1), FhVector(2), FhVector(3),
            FhVector(4), FhVector(5), FhVector(6), FhVector(7)
        };
        var portalPrefix = Struct<FhCollisionData>((nameof(FhCollisionData.PlaneIndex), (ushort)0),
            (nameof(FhCollisionData.VectorCount), (ushort)4),
            (nameof(FhCollisionData.VectorStartIndex), (ushort)0));
        var floor = Struct<FhCollisionData>((nameof(FhCollisionData.PlaneIndex), (ushort)0),
            (nameof(FhCollisionData.VectorCount), (ushort)4),
            (nameof(FhCollisionData.VectorStartIndex), (ushort)4));
        var ceiling = Struct<FhCollisionData>((nameof(FhCollisionData.PlaneIndex), (ushort)1),
            (nameof(FhCollisionData.VectorCount), (ushort)4),
            (nameof(FhCollisionData.VectorStartIndex), (ushort)0));
        var portal = new Portal("a", "b", new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitZ },
            Array.Empty<Vector4>(), Vector4.UnitY);
        var info = new FhCollisionInfo(default, points, planes, new[] { portalPrefix, floor, ceiling },
            vectors, Array.Empty<ushort>(), new[] { portal }, Array.Empty<FhCollisionEntry>(),
            Array.Empty<int>(), Array.Empty<FhCollisionTreeNode>());

        RadarMapGeometry geometry = RadarMapBuilder.Build(new[] { new CollisionInstance("fh", info, false) });

        Assert.Single(geometry.Floors);
        Assert.Single(geometry.Floors[0].Polygons);
        Assert.Equal(new Vector2(2, 0), geometry.Min);
        Assert.Equal(new Vector2(3, 1), geometry.Max);
    }

    private static FhCollisionVector FhVector(ushort point)
        => Struct<FhCollisionVector>((nameof(FhCollisionVector.Point2Index), point));

    private static T Struct<T>(params (string Name, object Value)[] fields) where T : struct
    {
        object value = default(T);
        foreach ((string name, object fieldValue) in fields)
            typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.Public)!.SetValue(value, fieldValue);
        return (T)value;
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
