using System;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;
using MphRead.Hud.Radar;
using Xunit;

namespace MphRead.Tests;

public sealed class RadarTests
{
    [Fact]
    public void PlayerRadarAdmissionUsesEffectiveFlagAndExcludesSelfDeadAndInactivePlayers()
    {
        Assert.True(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.Battle, radarPlayers: true,
            localSlot: 0, localTeam: 0, contactSlot: 1, contactTeam: 1, active: true, health: 99,
            primeSlot: -1, out RadarContactType type));
        Assert.Equal(RadarContactType.Enemy, type);

        Assert.False(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.Battle, radarPlayers: false,
            0, 0, 1, 1, active: true, health: 99, -1, out _));
        Assert.False(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.Battle, radarPlayers: true,
            0, 0, 0, 0, active: true, health: 99, -1, out _));
        Assert.False(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.Battle, radarPlayers: true,
            0, 0, 1, 1, active: true, health: 0, -1, out _));
        Assert.False(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.Battle, radarPlayers: true,
            0, 0, 1, 1, active: false, health: 99, -1, out _));
    }

    [Fact]
    public void PlayerRadarClassifiesTeammateEnemyAndExactlyOnePrimeHunter()
    {
        Assert.True(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.TeamBattle, radarPlayers: true,
            localSlot: 0, localTeam: 0, contactSlot: 2, contactTeam: 0, active: true, health: 99,
            primeSlot: -1, out RadarContactType teammate));
        Assert.Equal(RadarContactType.Teammate, teammate);
        Assert.True(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.TeamBattle, radarPlayers: true,
            0, 0, 1, 1, active: true, health: 99, -1, out RadarContactType enemy));
        Assert.Equal(RadarContactType.Enemy, enemy);

        RadarContactType[] contacts = new RadarContactType[3];
        for (int slot = 1; slot <= 3; slot++)
        {
            Assert.True(PlayerPresentation.TryClassifyRadarPlayer(MatchMode.PrimeHunter, radarPlayers: true,
                0, 0, slot, slot, active: true, health: 99, primeSlot: 2,
                out contacts[slot - 1]));
        }
        Assert.Single(contacts, contact => contact == RadarContactType.PrimeHunter);
        Assert.Equal(RadarContactType.Enemy, contacts[0]);
        Assert.Equal(RadarContactType.PrimeHunter, contacts[1]);
        Assert.Equal(RadarContactType.Enemy, contacts[2]);
    }

    [Theory]
    [InlineData(MatchMode.Survival)]
    [InlineData(MatchMode.TeamSurvival)]
    public void AllPlayerRadarProducerNeverOverridesSurvivalRevealPolicy(MatchMode mode)
    {
        Assert.False(PlayerPresentation.TryClassifyRadarPlayer(mode, radarPlayers: true,
            localSlot: 0, localTeam: 0, contactSlot: 1, contactTeam: 1, active: true, health: 99,
            primeSlot: -1, out _));
    }

    [Fact]
    public void SurvivalRadarRejectsSelfTeammatesDeadAndInactiveReplicas()
    {
        Assert.True(PlayerPresentation.IsEligibleSurvivalRadarPlayer(
            localSlot: 0, localTeam: 0, contactSlot: 1, contactTeam: 1,
            active: true, health: 99));
        Assert.False(PlayerPresentation.IsEligibleSurvivalRadarPlayer(
            0, 0, 0, 1, active: true, health: 99));
        Assert.False(PlayerPresentation.IsEligibleSurvivalRadarPlayer(
            0, 0, 1, 0, active: true, health: 99));
        Assert.False(PlayerPresentation.IsEligibleSurvivalRadarPlayer(
            0, 0, 1, 1, active: false, health: 99));
        Assert.False(PlayerPresentation.IsEligibleSurvivalRadarPlayer(
            0, 0, 1, 1, active: true, health: 0));
    }

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
    public void ConfiguredProjectionRangeChangesZoomWithoutChangingAdmission()
    {
        RadarContact contact = Contact(new Vector3(0, 0, 20));
        RadarPoint near = RadarWidget.Project(contact, Vector3.Zero,
            Vector3.UnitZ, RadarOrientation.Heading, range: 20);
        RadarPoint far = RadarWidget.Project(contact, Vector3.Zero,
            Vector3.UnitZ, RadarOrientation.Heading, range: 80);

        Assert.False(near.Clamped);
        Assert.False(far.Clamped);
        Assert.Equal(-1, near.RelativePosition.Y, 3);
        Assert.Equal(-.25f, far.RelativePosition.Y, 3);
    }

    [Fact]
    public void ObjectiveSymbolsRemainDistinct()
    {
        Assert.Equal("F", RadarWidget.Symbol(Objective(RadarObjective.Flag)));
        Assert.Equal("B", RadarWidget.Symbol(Objective(RadarObjective.Base)));
        Assert.Equal("N", RadarWidget.Symbol(Objective(RadarObjective.Node)));
        Assert.Equal("D", RadarWidget.Symbol(Objective(RadarObjective.Defender)));
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

    [Fact]
    public void BeginSanitizesNonFiniteViewerPoseBeforeProjection()
    {
        var frame = new RadarFrame();
        frame.Begin(new Vector3(float.NaN), new Vector3(float.PositiveInfinity), 1);
        Assert.Equal(Vector3.Zero, frame.Origin);
        Assert.Equal(-Vector3.UnitZ, frame.Facing);

        RadarPoint point = RadarWidget.Project(Contact(Vector3.Zero), frame.Origin,
            frame.Facing, RadarOrientation.Heading);
        Assert.True(float.IsFinite(point.RelativePosition.X));
        Assert.True(float.IsFinite(point.RelativePosition.Y));
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
    public void VerticalMeterShortensToClearTopRadarWithoutGrowing()
    {
        RadarLayout radar = RadarLayoutCalculator.Calculate(RadarAnchor.TopRight,
            1, 0, 0, .75f);

        Assert.Equal(56, RadarLayoutCalculator.FitVerticalMeterBelow(radar, 112, 72));
        Assert.Equal(40, RadarLayoutCalculator.FitVerticalMeterBelow(radar, 100, 72));
        Assert.Equal(64, RadarLayoutCalculator.FitVerticalMeterBelow(radar, 180, 64));
        Assert.Equal(8, RadarLayoutCalculator.FitVerticalMeterBelow(radar, 40, 8));
    }

    [Fact]
    public void KillFeedMovesAroundAnOverlappingRightSideRadar()
    {
        RadarLayout topRight = RadarLayoutCalculator.Calculate(RadarAnchor.TopRight,
            1, 0, 0, .75f);
        RadarLayout bottomRight = RadarLayoutCalculator.Calculate(RadarAnchor.BottomRight,
            1, 0, 0, .75f);

        Assert.Equal(64, PlayerPresentation.KillFeedStartY(true, topRight));
        Assert.Equal(22, PlayerPresentation.KillFeedStartY(false, topRight));
        Assert.Equal(22, PlayerPresentation.KillFeedStartY(true, bottomRight));
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

    [Fact]
    public void BuiltInProfilesCoverClassicCompetitiveMinimalAccessibilityObjectiveAndBroadcast()
    {
        Assert.Equal(RadarStyle.Classic, RadarProfile.Create(RadarPreset.ClassicMph).Style);
        Assert.Equal(RadarStyle.Enhanced, RadarProfile.Create(RadarPreset.Competitive).Style);
        Assert.False(RadarProfile.Create(RadarPreset.Minimal).Labels);
        Assert.Equal(RadarColorPreset.HighContrast,
            RadarProfile.Create(RadarPreset.Accessibility).ColorPreset);
        Assert.False(RadarProfile.Create(RadarPreset.ObjectiveFocus).ShowEnemies);
        Assert.Equal(RadarFloorMode.All, RadarProfile.Create(RadarPreset.Broadcast).FloorMode);
    }

    [Fact]
    public void ProfileNormalizationClampsImportedValuesAndResolvesPalette()
    {
        RadarProfile profile = (new RadarProfile
        {
            Name = new string('x', 100), Scale = float.NaN, Range = 999,
            MarkerScale = -4, ElevationThreshold = float.PositiveInfinity,
            AutomaticMinimumRange = -1, AutomaticMaximumRange = 900,
            ColorPreset = RadarColorPreset.Deuteranopia
        }).Normalize();

        Assert.Equal(48, profile.Name.Length);
        Assert.Equal(1, profile.Scale);
        Assert.Equal(80, profile.Range);
        Assert.Equal(.5f, profile.MarkerScale);
        Assert.Equal(2, profile.ElevationThreshold);
        Assert.Equal(20, profile.AutomaticMinimumRange);
        Assert.Equal(80, profile.AutomaticMaximumRange);
        Assert.Equal(RadarColors.For(RadarColorPreset.Deuteranopia).Enemy, profile.Colors.Enemy);
    }

    [Fact]
    public void ProfileImportExportPreservesCustomColorsAndLayout()
    {
        RadarProfile source = new RadarProfile
        {
            Name = "My radar", Preset = RadarPreset.Custom, ColorPreset = RadarColorPreset.Custom,
            Colors = new RadarColors { Enemy = new RadarColor(1, 2, 3, 4) },
            Anchor = RadarAnchor.Custom, OffsetX = 17, OffsetY = -9, Scale = 1.2f,
            ZoomMode = RadarZoomMode.CombatSensitive
        };

        RadarProfile restored = RadarProfileSerializer.Import(RadarProfileSerializer.Export(source));

        Assert.Equal(source.Name, restored.Name);
        Assert.Equal(new RadarColor(1, 2, 3, 4), restored.Colors.Enemy);
        Assert.Equal(RadarAnchor.Custom, restored.Anchor);
        Assert.Equal(17, restored.OffsetX);
        Assert.Equal(RadarZoomMode.CombatSensitive, restored.ZoomMode);
    }

    [Fact]
    public void PresentationFiltersOnlyRemoveAlreadyApprovedContacts()
    {
        RadarProfile profile = new RadarProfile
        {
            ShowEnemies = false, ShowTeammates = true, ShowObjectives = true,
            ShowFlags = false, ShowBases = true
        };

        Assert.False(RadarPresentationPolicy.IsVisible(profile, Contact(Vector3.Zero)));
        Assert.True(RadarPresentationPolicy.IsVisible(profile,
            new RadarContact(RadarContactType.Teammate, Vector3.Zero, 0, RadarObjective.None, 1)));
        Assert.False(RadarPresentationPolicy.IsVisible(profile, Objective(RadarObjective.Flag)));
        Assert.True(RadarPresentationPolicy.IsVisible(profile, Objective(RadarObjective.Base)));
    }

    [Fact]
    public void ApprovedObjectiveReplacesLowerPriorityContactAtCapacity()
    {
        var frame = NewFrame();
        for (int i = 0; i < RadarFrame.Capacity; i++)
            Assert.True(frame.AddApproved(Contact(new Vector3(i, 0, 0))));

        Assert.True(frame.AddApproved(Objective(RadarObjective.Flag)));

        Assert.Equal(RadarFrame.Capacity, frame.Contacts.Length);
        Assert.Equal(1, frame.Dropped);
        Assert.Contains(frame.Contacts.ToArray(), contact => contact.Objective == RadarObjective.Flag);
    }

    [Fact]
    public void ContactAgeFadesWithinApprovedPersistenceWindow()
    {
        var profile = new RadarProfile { ContactPersistenceSeconds = 1, ContactPulse = false };
        RadarContact fresh = Contact(Vector3.Zero) with { AgeTicks = 0 };
        RadarContact half = fresh with { AgeTicks = 30 };
        RadarContact expired = fresh with { AgeTicks = 60 };

        Assert.Equal(1, RadarPresentationPolicy.Alpha(profile, fresh), 3);
        Assert.Equal(.5f, RadarPresentationPolicy.Alpha(profile, half), 3);
        Assert.Equal(0, RadarPresentationPolicy.Alpha(profile, expired), 3);
    }

    [Fact]
    public void PriorityPulseRemainsARestrainedSecondaryCue()
    {
        var profile = new RadarProfile { ContactPulse = true };
        RadarContact objective = Objective(RadarObjective.Flag);
        float minimum = 1;
        float maximum = 0;
        for (ulong tick = 0; tick < 120; tick++)
        {
            float alpha = RadarPresentationPolicy.Alpha(profile, objective, tick);
            minimum = Math.Min(minimum, alpha);
            maximum = Math.Max(maximum, alpha);
        }

        Assert.InRange(minimum, .879f, .881f);
        Assert.InRange(maximum, .999f, 1f);
    }

    [Fact]
    public void ContactShapeAndColorRemainSemanticAcrossElevation()
    {
        var profile = new RadarProfile();
        RadarContact enemy = Contact(Vector3.Zero);
        var teammate = enemy with { Type = RadarContactType.Teammate };
        RadarContact objective = Objective(RadarObjective.Flag);
        var prime = enemy with { Type = RadarContactType.PrimeHunter };

        Assert.Equal(RadarMarkerShape.Diamond,
            RadarPresentationPolicy.MarkerShape(enemy));
        Assert.Equal(RadarMarkerShape.Square,
            RadarPresentationPolicy.MarkerShape(teammate));
        Assert.Equal(RadarMarkerShape.Triangle,
            RadarPresentationPolicy.MarkerShape(objective));
        Assert.Equal(RadarMarkerShape.DoubleDiamond,
            RadarPresentationPolicy.MarkerShape(prime));
        Assert.Equal(profile.Colors.Enemy.Vector,
            RadarPresentationPolicy.Color(profile, enemy, RadarElevation.Above));
        Assert.Equal(profile.Colors.Above.Vector,
            RadarPresentationPolicy.ElevationColor(profile, RadarElevation.Above));
        Assert.Equal(profile.Colors.Below.Vector,
            RadarPresentationPolicy.ElevationColor(profile, RadarElevation.Below));
    }

    [Theory]
    [InlineData(24, 1, 1, 18.5f)]
    [InlineData(24, 2, 2, 2)]
    [InlineData(float.NaN, 1, 1, 0)]
    public void EdgeMarkersRemainInsideTheRadarFrame(float radius,
        float markerScale, float edgeScale, float expected)
    {
        Assert.Equal(expected, RadarPresentationPolicy.EdgeMarkerRadius(
            radius, markerScale, edgeScale), 3);
    }

    [Fact]
    public void AdaptiveZoomUsesPreparedContactsAndCombatSensitiveMinimum()
    {
        var frame = NewFrame();
        frame.AddApproved(Contact(new Vector3(0, 0, 50)));
        var automatic = new RadarProfile
        {
            ZoomMode = RadarZoomMode.Automatic, AutomaticMinimumRange = 20,
            AutomaticMaximumRange = 80
        };
        var combat = automatic with { ZoomMode = RadarZoomMode.CombatSensitive };

        Assert.InRange(RadarZoomController.Target(automatic, Vector3.Zero, frame.Contacts), 57.4f, 57.6f);
        Assert.Equal(20, RadarZoomController.Target(combat, Vector3.Zero, frame.Contacts));
    }

    [Fact]
    public void FloorModesReturnDeterministicCurrentAdjacentAndAllOrdering()
    {
        Assert.Equal(new[] { 2 }, RadarPresentationPolicy.VisibleFloors(RadarFloorMode.Current, 2, 5));
        Assert.Equal(new[] { 1, 3, 2 }, RadarPresentationPolicy.VisibleFloors(RadarFloorMode.Adjacent, 2, 5));
        Assert.Equal(new[] { 0, 1, 3, 4, 2 }, RadarPresentationPolicy.VisibleFloors(RadarFloorMode.All, 2, 5));
    }

    [Fact]
    public void LayoutEditorSnapsDragsResizesAndResets()
    {
        RadarProfile moved = RadarLayoutEditor.Drag(
            new RadarProfile { Anchor = RadarAnchor.Custom }, new Vector2(5.9f, -6.1f));
        Assert.Equal(RadarPreset.Custom, moved.Preset);
        Assert.Equal("Custom", moved.Name);
        Assert.Equal(RadarAnchor.Custom, moved.Anchor);
        Assert.Equal(4, moved.OffsetX);
        Assert.Equal(-8, moved.OffsetY);
        RadarProfile resized = RadarLayoutEditor.Resize(moved, .08f);
        Assert.Equal(RadarPreset.Custom, resized.Preset);
        Assert.Equal(1.1f, resized.Scale, 3);

        RadarProfile reset = RadarLayoutEditor.Reset(moved);
        Assert.Equal(RadarPreset.Custom, reset.Preset);
        Assert.Equal(RadarAnchor.TopRight, reset.Anchor);
        Assert.Equal(0, reset.OffsetX);
        Assert.Equal(1, reset.Scale);
    }

    [Fact]
    public void ModeProfileOverridesDeviceProfileThenFallsBackToDefault()
    {
        RadarProfile previous = RadarSettings.DefaultProfile;
        try
        {
            RadarSettings.Apply(new RadarProfile { Name = "Default", Range = 40 });
            RadarSettings.SetDeviceProfile(RadarDeviceClass.Handheld,
                new RadarProfile { Name = "Handheld", Range = 30 });
            RadarSettings.SetModeProfile("Capture", new RadarProfile { Name = "Capture", Range = 60 });

            Assert.Equal(60, RadarSettings.ForContext("Capture", RadarDeviceClass.Handheld).Range);
            Assert.Equal(30, RadarSettings.ForContext("Battle", RadarDeviceClass.Handheld).Range);
            Assert.Equal(40, RadarSettings.ForContext("Battle", RadarDeviceClass.Desktop).Range);
        }
        finally
        {
            RadarSettings.ClearModeProfiles();
            RadarSettings.ClearDeviceProfiles();
            RadarSettings.Apply(previous);
        }
    }

    [Fact]
    public void RasterizerCanProduceIndependentFillAndOutlineLayers()
    {
        var polygon = new RadarPolygon(new[]
        {
            new Vector2(-2, -2), new Vector2(2, -2), new Vector2(2, 2), new Vector2(-2, 2)
        }, 0, RadarSurfaceKind.Floor);
        var bounds = new RadarPolygon(new[]
        {
            new Vector2(-4, -4), new Vector2(4, -4), new Vector2(4, 4), new Vector2(-4, 4)
        }, 0, RadarSurfaceKind.Floor);
        RadarMapGeometry geometry = RadarMapBuilder.BuildGeometry(new[] { bounds, polygon });
        RadarMap fill = RadarMapRasterizer.Rasterize(geometry, 32, RadarMapRasterLayers.Fill);
        RadarMap outline = RadarMapRasterizer.Rasterize(geometry, 32, RadarMapRasterLayers.Outline);

        int center = 16 * 32 + 16;
        Assert.True(fill.Floors[0].Pixels[center].Alpha > 0);
        Assert.Equal(0, outline.Floors[0].Pixels[center].Alpha);
        Assert.Contains(outline.Floors[0].Pixels, pixel => pixel.Alpha > 0);
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

    private static RadarContact Objective(RadarObjective objective)
        => new(RadarContactType.Objective, Vector3.Zero, -1, objective, 1);
}
