using System;
using System.IO;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Entities;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class AltCollisionReliabilityTests
{
    [Theory]
    [InlineData(Hunter.Spire, 0.5f)]
    [InlineData(Hunter.Sylux, 0.5f)]
    [InlineData(Hunter.Noxus, 0.35f)]
    public void AltSweepPaddingMatchesTheNarrowPhase(Hunter hunter,
        float expected)
    {
        Assert.Equal(expected, PlayerEntity.ResolveAltSweepPadding(hunter));
    }

    [Fact]
    public void CollisionBoundsContainAFullSphereAtEveryKandenSegment()
    {
        Vector3 min = new(-1, -2, -3);
        Vector3 max = new(1, 2, 3);

        PlayerEntity.ExpandCollisionBoundsForSphere(ref min, ref max,
            new Vector3(4, 5, 6), 0.75f);
        PlayerEntity.ExpandCollisionBoundsForSphere(ref min, ref max,
            new Vector3(-5, -6, -7), 0.5f);

        Assert.Equal(new Vector3(-5.5f, -6.5f, -7.5f), min);
        Assert.Equal(new Vector3(4.75f, 5.75f, 6.75f), max);
    }

    [Fact]
    public void GroundedSyluxClearanceUsesTheResolvedBipedOrigin()
    {
        CollisionVolume alt = PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 2];
        CollisionVolume bipedBottom = PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 0];
        CollisionVolume bipedTop = PlayerEntity.PlayerVolumes[(int)Hunter.Sylux, 1];
        Vector3 origin = new(0, 4, 0);

        Vector3 resolved = PlayerEntity.ResolveFormOrigin(origin, alt,
            bipedBottom, grounded: true);
        float actualTop = (resolved + bipedTop.SpherePosition).Y
            + bipedTop.SphereRadius;
        float incorrectlyUnshiftedTop = (origin + bipedTop.SpherePosition).Y
            + bipedTop.SphereRadius;

        Assert.True(actualTop < incorrectlyUnshiftedTop);
        Assert.Equal((origin + alt.SpherePosition).Y - alt.SphereRadius,
            (resolved + bipedBottom.SpherePosition).Y
                - bipedBottom.SphereRadius,
            precision: 5);
    }

    [Fact]
    public void AltEdgeContactUsesClosestPointNormalAndLinearPenetration()
    {
        bool hit = PlayerEntity.TryResolveAltEdgeContact(
            new Vector3(0.6f, 0.6f, 0), new Vector3(-2, 0, 0),
            new Vector3(2, 0, 0), 1, out Vector3 normal,
            out float penetration);

        Assert.True(hit);
        Assert.Equal(0, normal.X, 5);
        Assert.Equal(1, normal.Y, 5);
        Assert.Equal(0, normal.Z, 5);
        Assert.Equal(0.4f, penetration, 5);
        Assert.True(VectorMath.IsFinite(normal));
        Assert.True(float.IsFinite(penetration));
    }

    [Fact]
    public void AltEdgeContactHandlesCornersAndZeroLengthSegments()
    {
        bool cornerHit = PlayerEntity.TryResolveAltEdgeContact(
            new Vector3(0.3f, 0.4f, 0), Vector3.Zero, Vector3.Zero,
            0.75f, out Vector3 cornerNormal, out float cornerPenetration);

        Assert.True(cornerHit);
        Assert.Equal(new Vector3(0.6f, 0.8f, 0), cornerNormal);
        Assert.Equal(0.25f, cornerPenetration, 5);

        bool degenerateHit = PlayerEntity.TryResolveAltEdgeContact(
            Vector3.Zero, Vector3.Zero, Vector3.Zero, 0.75f,
            out Vector3 degenerateNormal, out float degeneratePenetration);

        Assert.False(degenerateHit);
        Assert.Equal(Vector3.Zero, degenerateNormal);
        Assert.Equal(0, degeneratePenetration);
        Assert.True(VectorMath.IsFinite(degenerateNormal));
        Assert.True(float.IsFinite(degeneratePenetration));
    }

    [Fact]
    public void AltEdgeContactRejectsNonFiniteGeometry()
    {
        bool hit = PlayerEntity.TryResolveAltEdgeContact(
            new Vector3(float.NaN, 0, 0), Vector3.Zero, Vector3.UnitX,
            1, out Vector3 normal, out float penetration);

        Assert.False(hit);
        Assert.True(VectorMath.IsFinite(normal));
        Assert.True(float.IsFinite(penetration));
    }

    [Fact]
    public void AltCeilingCorrectionStaysInsideAValidLowTunnel()
    {
        const float floorY = 0;
        const float tunnelHeight = 1.2f;
        const float radius = 0.5f;
        const float upwardEndpointY = 1;
        const float ceilingPenetration = 0.3f;
        float correctedY = upwardEndpointY - ceilingPenetration
            * PlayerEntity.ResolveCollisionVerticalFactor(altForm: true, planeY: -1);

        Assert.Equal(0.7f, correctedY, precision: 5);
        Assert.True(correctedY - radius >= floorY);
        Assert.True(correctedY + radius <= tunnelHeight);
    }

    [Fact]
    public void AltSlopeContactUsesFullVerticalCorrection()
    {
        float factor = PlayerEntity.ResolveCollisionVerticalFactor(
            altForm: true, planeY: 0.6f);
        float correction = 0.6f * 0.2f * factor;

        Assert.Equal(1f, factor);
        Assert.Equal(0.12f, correction, precision: 5);
    }

    [Theory]
    [InlineData(0.5f, 0.25f)]
    [InlineData(-1f, 4f)]
    [InlineData(1f, 1f)]
    public void BipedVerticalCorrectionFactorsRemainCompatible(float planeY,
        float expected)
    {
        Assert.Equal(expected,
            PlayerEntity.ResolveCollisionVerticalFactor(altForm: false,
                planeY: planeY),
            precision: 5);
    }

    [Fact]
    public void JumpPadAccelerationRemovesInwardComponentForMirroredWalls()
    {
        Vector3[] normals = { Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitZ, -Vector3.UnitZ };
        foreach (Vector3 normal in normals)
        {
            Vector3 acceleration = -normal * 2 + Vector3.UnitY * 3;
            Vector3 result = PlayerEntity.RemoveInwardHorizontalComponent(
                acceleration, normal);

            Assert.Equal(0, result.X, precision: 5);
            Assert.Equal(3, result.Y, precision: 5);
            Assert.Equal(0, result.Z, precision: 5);
        }
    }

    [Fact]
    public void JumpPadAccelerationPreservesVerticalAndTangentialComponents()
    {
        Vector3 normal = VectorMath.NormalizeOr(new Vector3(1, 0, 1),
            Vector3.UnitX);
        Vector3 tangent = VectorMath.NormalizeOr(new Vector3(-1, 0, 1),
            Vector3.UnitZ);
        Vector3 acceleration = -normal * 2 + tangent * 3 + Vector3.UnitY * 4;

        Vector3 result = PlayerEntity.RemoveInwardHorizontalComponent(
            acceleration, normal);
        Vector3 expected = tangent * 3 + Vector3.UnitY * 4;

        Assert.Equal(expected.X, result.X, precision: 5);
        Assert.Equal(expected.Y, result.Y, precision: 5);
        Assert.Equal(expected.Z, result.Z, precision: 5);
    }

    [Fact]
    public void JumpPadAccelerationLeavesOutwardAndNonLateralContactsUnchanged()
    {
        Vector3 acceleration = new(1.25f, -2.5f, 3.75f);

        Assert.Equal(acceleration,
            PlayerEntity.RemoveInwardHorizontalComponent(acceleration,
                Vector3.UnitX));
        Assert.Equal(acceleration,
            PlayerEntity.RemoveInwardHorizontalComponent(acceleration,
                Vector3.UnitY));
        Assert.Equal(acceleration,
            PlayerEntity.RemoveInwardHorizontalComponent(acceleration,
                Vector3.Zero));
    }

    [Theory]
    [InlineData(Hunter.Trace, true, true, true, -1f, true)]
    [InlineData(Hunter.Weavel, true, true, true, -1f, true)]
    [InlineData(Hunter.Trace, true, true, false, -1f, false)]
    [InlineData(Hunter.Trace, true, true, true, 0f, false)]
    [InlineData(Hunter.Samus, true, true, true, -1f, false)]
    [InlineData(Hunter.Trace, false, true, true, -1f, false)]
    [InlineData(Hunter.Trace, true, false, true, -1f, false)]
    public void AltLungeEndsOnlyOnAnActualBlockingTerrainImpact(Hunter hunter,
        bool isAltForm, bool altAttackActive, bool blockingLateralCollision,
        float speedDot, bool expected)
    {
        Assert.Equal(expected,
            PlayerEntity.ShouldEndAltLungeOnCollision(hunter, isAltForm,
                altAttackActive, blockingLateralCollision, speedDot));
    }

    [Fact]
    public void AltTerrainContactsOrderFacesBeforeEdgesStably()
    {
        CollisionResult[] edgeFirst =
        {
            Contact(field0: 1, marker: 1),
            Contact(field0: 0, marker: 2),
            Contact(field0: 7, marker: 3),
            Contact(field0: 1, marker: 4),
            Contact(field0: 0, marker: 5)
        };
        CollisionResult[] faceFirst =
        {
            Contact(field0: 7, marker: 6),
            Contact(field0: 0, marker: 7),
            Contact(field0: 1, marker: 8),
            Contact(field0: 0, marker: 9),
            Contact(field0: 1, marker: 10)
        };

        PlayerEntity.OrderAltTerrainContacts(edgeFirst, edgeFirst.Length);
        PlayerEntity.OrderAltTerrainContacts(faceFirst, faceFirst.Length);

        AssertMarkers(edgeFirst, 2, 5, 1, 4, 3);
        AssertMarkers(faceFirst, 7, 9, 8, 10, 6);
    }

    [Trait("RequiresGameContent", "true")]
    [Fact]
    public void AltTerrainOrderingMakesRealCollisionResponseOrderIndependent()
    {
        bool previousServerMode = Read.ServerMode;
        try
        {
            using var saved = ServerContent.PreserveContext("AMHE1");
            ServerContent.Open(FindAmhe1(), "AMHE1");
            using var simulation = new ServerSimulation(new RotationEntry
            {
                RoomKey = "MP1 SANCTORUS",
                Mode = GameMode.Battle
            });

            PlayerEntity edgeFirstPlayer = ActivateTrace(simulation, 0,
                connectionId: 0x51);
            PlayerEntity faceFirstPlayer = ActivateTrace(simulation, 1,
                connectionId: 0x52);

            float radius = Fixed.ToFloat(edgeFirstPlayer.Values.AltColRadius);
            float yOffset = Fixed.ToFloat(edgeFirstPlayer.Values.AltColYPos);
            Vector3 center = new(0, radius * 0.4f, radius * 0.6f);
            Vector3 speed = new(0, -0.1f, -1);
            Vector3 edgePoint1 = new(-2, 0, 0);
            Vector3 edgePoint2 = new(2, 0, 0);
            CollisionResult face = new()
            {
                Field0 = 0,
                Plane = new Vector4(Vector3.UnitY, 0)
            };
            CollisionResult edge = new()
            {
                Field0 = 1,
                Plane = new Vector4(Vector3.UnitY, 0),
                EdgePoint1 = edgePoint1,
                EdgePoint2 = edgePoint2
            };

            InitializeAltCollisionState(edgeFirstPlayer, center, yOffset,
                speed);
            InitializeAltCollisionState(faceFirstPlayer, center, yOffset,
                speed);

            CollisionResult[] edgeFirst = { edge, face };
            CollisionResult[] faceFirst = { face, edge };
            PlayerEntity.OrderAltTerrainContacts(edgeFirst,
                edgeFirst.Length);
            PlayerEntity.OrderAltTerrainContacts(faceFirst,
                faceFirst.Length);
            ApplyContacts(edgeFirstPlayer, edgeFirst);
            ApplyContacts(faceFirstPlayer, faceFirst);

            Assert.Equal(faceFirstPlayer.Position, edgeFirstPlayer.Position);
            Assert.Equal(faceFirstPlayer.Speed, edgeFirstPlayer.Speed);
            Assert.Equal(faceFirstPlayer.Flags1.TestFlag(PlayerFlags1.Standing),
                edgeFirstPlayer.Flags1.TestFlag(PlayerFlags1.Standing));
            Assert.Equal(faceFirstPlayer._standTerrain,
                edgeFirstPlayer._standTerrain);
            Assert.True(edgeFirstPlayer.Flags1.TestFlag(PlayerFlags1.Standing));

            // The pre-sort edge contact is a real seam overlap. Applying it
            // first would project the toward-seam horizontal speed through
            // its upward radial normal; the face-first response removes that
            // stale edge before it can create the upward impulse.
            PlayerEntity unsortedPlayer = ActivateTrace(simulation, 2,
                connectionId: 0x53);
            InitializeAltCollisionState(unsortedPlayer, center, yOffset,
                speed);
            unsortedPlayer.HandleCollision(edge);
            Assert.True(unsortedPlayer.Speed.Y > 0);
        }
        finally
        {
            Read.ServerMode = previousServerMode;
        }
    }

    [Fact]
    public void AltFloorFaceCorrectionRemovesCoplanarSeamPenetration()
    {
        const float radius = 0.5f;
        Vector3 floorEdgeStart = new(-2, 0, 0);
        Vector3 floorEdgeEnd = new(2, 0, 0);
        Vector3 belowFloor = new(0, 0.2f, 0);

        bool penetratesBeforeCorrection = PlayerEntity.TryResolveAltEdgeContact(
            belowFloor, floorEdgeStart, floorEdgeEnd, radius,
            out _, out float beforePenetration);
        float floorPenetration = radius - belowFloor.Y;
        Vector3 corrected = belowFloor.AddY(floorPenetration
            * PlayerEntity.ResolveCollisionVerticalFactor(
                altForm: true, planeY: 1));
        bool penetratesAfterCorrection = PlayerEntity.TryResolveAltEdgeContact(
            corrected, floorEdgeStart, floorEdgeEnd, radius,
            out _, out float afterPenetration);

        // An edge at the floor plane is now exactly tangent and should not
        // re-apply the same correction after its face has been resolved.
        Assert.True(penetratesBeforeCorrection);
        Assert.Equal(0.3f, beforePenetration, precision: 5);
        Assert.False(penetratesAfterCorrection);
        Assert.Equal(0, afterPenetration);

        // A genuinely exposed, non-coplanar edge remains a real contact.
        bool exposedEdgeContact = PlayerEntity.TryResolveAltEdgeContact(
            corrected, new Vector3(0.3f, 0, 0), new Vector3(0.3f, 1, 0),
            radius, out _, out float exposedPenetration);
        Assert.True(exposedEdgeContact);
        Assert.Equal(0.2f, exposedPenetration, precision: 5);
    }

    [Fact]
    public void SyluxHoverUsesTheDeepestContactOnly()
    {
        float depth = float.NaN;
        depth = PlayerEntity.AggregateSyluxHoverDepth(depth, 0.4f);
        depth = PlayerEntity.AggregateSyluxHoverDepth(depth, 0.7f);
        depth = PlayerEntity.AggregateSyluxHoverDepth(depth, 0.7f);

        Assert.Equal(0.7f, depth, 5);
    }

    [Fact]
    public void SyluxHoverResponseIsAppliedOnceAtTheSixtyHertzRate()
    {
        Vector3 speed = new(0, 0, 0);
        float gravity = Fixed.ToFloat(-245);

        Vector3 response = PlayerEntity.ApplySyluxHoverResponse(speed,
            hoverDepth: 0.4f, altAirGravity: gravity);

        float expectedY = gravity / 2 * Fixed.ToFloat(4034)
            + 0.4f * 0.2f / 2;
        Assert.Equal(expectedY, response.Y, 6);

        float duplicateDepth = PlayerEntity.AggregateSyluxHoverDepth(
            PlayerEntity.AggregateSyluxHoverDepth(float.NaN, 0.4f), 0.4f);
        Vector3 duplicateResponse = PlayerEntity.ApplySyluxHoverResponse(
            speed, hoverDepth: duplicateDepth, altAirGravity: gravity);
        Assert.Equal(response.Y, duplicateResponse.Y, 6);
    }

    [Fact]
    public void BalancedSpireDialancheResolvesABoundedLedgeAndPreservesHorizontalMomentum()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(
            new MatchRules(MatchMode.Battle, "ledge", balancedMode: true));
        float radius = 0.5f;
        float padding = PlayerEntity.ResolveAltSweepPadding(Hunter.Spire);
        CollisionResult wall = Wall(Vector3.UnitZ);
        CollisionResult forward = Support(new Vector3(0, 0.5f, -2));
        CollisionResult support = Support(new Vector3(0, 0.5f, -2));

        bool resolved = DialancheLedgePolicy.TryResolve(
            Hunter.Spire, isAltForm: true,
            balance.GetHunter(Hunter.Spire).CanClimbLedges,
            new Vector3(0, 0, 0), new Vector3(0, -0.3f, -1),
            new Vector3(0, -0.3f, -1), wall, wallTop: 0.5f,
            forward, hasForwardAbove: true, support, hasSupport: true,
            dialancheClear: true, forceFieldBlocked: false, radius,
            padding, out Vector3 landing, out Vector3 speed);

        Assert.True(resolved);
        Assert.Equal(new Vector3(0, 1, -2), landing);
        Assert.Equal(new Vector3(0, 0, -1), speed);
    }

    [Fact]
    public void DialancheEvaluatesSlopedSupportAtTheProbePosition()
    {
        CollisionResult support = Support(new Vector3(0, 0.75f, -2));
        support.Plane = new Vector4(new Vector3(0.6f, 0.8f, 0), 0.6f);
        Vector3 probeCenter = new(0, 0.5f, -2);

        bool found = DialancheLedgePolicy.TryFindDownwardSupport(
            new[] { support }, 1, wallTop: 0.5f, probeRadius: 0.5f,
            probeCenter, out _, out float supportHeight);

        Assert.True(found);
        Assert.Equal(0.75f, supportHeight, precision: 5);

        bool resolved = DialancheLedgePolicy.TryResolve(
            Hunter.Spire, isAltForm: true, canClimbLedges: true,
            Vector3.Zero, new Vector3(0, -0.3f, -1),
            new Vector3(0, -0.3f, -1), Wall(Vector3.UnitZ), wallTop: 0.5f,
            Support(new Vector3(0, 0.75f, -2)), hasForwardAbove: true,
            support, hasSupport: true, dialancheClear: true,
            forceFieldBlocked: false, altRadius: 0.5f, sweepPadding: 0.5f,
            out Vector3 landing, out _);

        Assert.True(resolved);
        Assert.Equal(1.25f, landing.Y, precision: 5);
    }

    [Fact]
    public void DialancheUsesTheNearestCoplanarWallFaceForTheLip()
    {
        CollisionCandidate[] candidates =
        {
            CoplanarWallFace(0, 1),
            CoplanarWallFace(5, 6)
        };
        CollisionResult contact = Wall(Vector3.UnitZ);
        contact.Position = new Vector3(0, 0.75f, 0);

        bool found = DialancheLedgePolicy.TryGetStaticFaceBounds(
            candidates, contact, out DialancheStaticFace face);

        Assert.True(found);
        Assert.Equal(1, face.MaxY, precision: 5);
    }

    [Fact]
    public void ClassicSpireDialancheIsRejected()
    {
        MatchBalanceContext balance = MatchBalanceContext.For(
            new MatchRules(MatchMode.Battle, "ledge", balancedMode: false));
        bool resolved = DialancheLedgePolicy.TryResolve(Hunter.Spire, true,
            balance.GetHunter(Hunter.Spire).CanClimbLedges, Vector3.Zero,
            new Vector3(0, -0.1f, -1), new Vector3(0, 0, -1),
            Wall(Vector3.UnitZ), 0.5f,
            Support(new Vector3(0, 0.5f, -2)), true,
            Support(new Vector3(0, 0.5f, -2)), true, true, false,
            0.5f, 0.5f, out _, out _);
        Assert.False(resolved);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void DialancheRejectsBlockedCeilingAndMissingSupport(bool clear,
        bool ceilingValid, bool supportValid)
    {
        CollisionResult forward = Support(new Vector3(0, 0.5f, -2));
        if (!ceilingValid)
        {
            forward.Plane = new Vector4(-Vector3.UnitY, -0.5f);
        }
        CollisionResult support = Support(new Vector3(0, 0.5f, -2));
        bool resolved = DialancheLedgePolicy.TryResolve(
            Hunter.Spire, true, true, Vector3.Zero,
            new Vector3(0, -0.1f, -1), new Vector3(0, -0.1f, -1),
            Wall(Vector3.UnitZ), 0.5f, forward, true, support,
            supportValid, clear, false, 0.5f, 0.5f, out _, out _);
        Assert.Equal(clear && ceilingValid && supportValid, resolved);
    }

    [Fact]
    public void DialancheRejectsSteepDamagingAndCornerSupport()
    {
        CollisionResult wall = Wall(Vector3.UnitZ);
        CollisionResult forward = Support(new Vector3(0, 0.5f, -2));
        CollisionResult steep = Support(new Vector3(0, 0.5f, -2));
        steep.Plane = new Vector4(new Vector3(0, 0.4f, 0.9165151f),
            0.5f);
        Assert.False(DialancheLedgePolicy.TryResolve(Hunter.Spire, true, true,
            Vector3.Zero, new Vector3(0, -0.1f, -1),
            new Vector3(0, 0, -1), wall, 0.5f, forward, true, steep, true,
            true, false, 0.5f, 0.5f, out _, out _));

        CollisionResult damaging = Support(new Vector3(0, 0.5f, -2));
        damaging.Flags = CollisionFlags.Damaging;
        Assert.False(DialancheLedgePolicy.TryResolve(Hunter.Spire, true, true,
            Vector3.Zero, new Vector3(0, -0.1f, -1),
            new Vector3(0, 0, -1), wall, 0.5f, forward, true, damaging, true,
            true, false, 0.5f, 0.5f, out _, out _));

        wall.Field0 = 1;
        Assert.False(DialancheLedgePolicy.TryResolve(Hunter.Spire, true, true,
            Vector3.Zero, new Vector3(0, -0.1f, -1),
            new Vector3(0, 0, -1), wall, 0.5f, forward, true,
            Support(new Vector3(0, 0.5f, -2)), true, true, false, 0.5f,
            0.5f, out _, out _));
    }

    [Fact]
    public void DialancheRejectsNonFiniteAndObstructedGeometry()
    {
        CollisionResult wall = Wall(Vector3.UnitZ);
        CollisionResult forward = Support(new Vector3(0, 0.5f, -2));
        CollisionResult support = Support(new Vector3(0, 0.5f, -2));
        Assert.False(DialancheLedgePolicy.TryResolve(Hunter.Spire, true, true,
            new Vector3(float.NaN, 0, 0), new Vector3(0, -0.1f, -1),
            new Vector3(0, 0, -1), wall, 0.5f, forward, true, support, true,
            true, false, 0.5f, 0.5f, out _, out _));

        CollisionResult obstruction = Support(new Vector3(1, 0, 0));
        obstruction.Plane = new Vector4(Vector3.UnitX, 1);
        Assert.False(DialancheLedgePolicy.IsTransitionPathClear(
            new[] { obstruction }, 1, wall, forward, support));
    }

    [Fact]
    public void DialancheForceFieldBlocksOnlyIntersectingRectangles()
    {
        Vector4 plane = new(Vector3.UnitZ, 0);
        Assert.True(DialancheLedgePolicy.IsTransitionBlockedByForceField(
            true, new Vector3(0, 0, -2), new Vector3(0, 0.5f, 2), 0.5f,
            plane, Vector3.Zero, Vector3.UnitY, Vector3.UnitX, 1, 1));
        Assert.False(DialancheLedgePolicy.IsTransitionBlockedByForceField(
            true, new Vector3(4, 0, -2), new Vector3(4, 0.5f, 2), 0.5f,
            plane, Vector3.Zero, Vector3.UnitY, Vector3.UnitX, 1, 1));
        Assert.False(DialancheLedgePolicy.IsTransitionBlockedByForceField(
            false, new Vector3(0, 0, -2), new Vector3(0, 0.5f, 2), 0.5f,
            plane, Vector3.Zero, Vector3.UnitY, Vector3.UnitX, 1, 1));
    }

    [Fact]
    public void DialancheDecisionIsDeterministicAcrossRepeatedCalls()
    {
        CollisionResult wall = Wall(Vector3.UnitZ);
        CollisionResult forward = Support(new Vector3(0, 0.5f, -2));
        CollisionResult support = Support(new Vector3(0, 0.5f, -2));
        bool first = ResolveSynthetic(true, out Vector3 firstLanding,
            out Vector3 firstSpeed);
        bool second = ResolveSynthetic(true, out Vector3 secondLanding,
            out Vector3 secondSpeed);

        Assert.Equal(first, second);
        Assert.Equal(firstLanding, secondLanding);
        Assert.Equal(firstSpeed, secondSpeed);

        bool ResolveSynthetic(bool enabled, out Vector3 landing,
            out Vector3 speed)
            => DialancheLedgePolicy.TryResolve(Hunter.Spire, true, enabled,
                Vector3.Zero, new Vector3(0, -0.1f, -1),
                new Vector3(0, -0.1f, -1), wall, 0.5f, forward, true,
                support, true, true, false, 0.5f, 0.5f,
                out landing, out speed);
    }

    private static CollisionResult Contact(byte field0, float marker)
        => new() { Field0 = field0, Field14 = marker };

    private static CollisionResult Wall(Vector3 normal)
        => new()
        {
            Field0 = 0,
            Flags = CollisionFlags.None,
            Plane = new Vector4(normal, 0),
            Position = Vector3.Zero
        };

    private static CollisionResult Support(Vector3 position)
        => new()
        {
            Field0 = 0,
            Flags = CollisionFlags.None,
            Plane = new Vector4(Vector3.UnitY, 0.5f),
            Position = position
        };

    private static CollisionCandidate CoplanarWallFace(float minY,
        float maxY)
    {
        Vector3[] points =
        {
            new(-1, minY, 0), new(1, minY, 0),
            new(1, maxY, 0), new(-1, maxY, 0)
        };
        Vector4Fx plane = Struct<Vector4Fx>(
            (nameof(Vector4Fx.Z), new Fixed(4096)));
        CollisionData data = Struct<CollisionData>(
            (nameof(CollisionData.PlaneIndex), (ushort)0),
            (nameof(CollisionData.PointIndexCount), (ushort)4),
            (nameof(CollisionData.PointStartIndex), (ushort)0));
        MphCollisionInfo info = new(default,
            Array.ConvertAll(points, point => new Vector3Fx(
                Fixed.ToInt(point.X), Fixed.ToInt(point.Y),
                Fixed.ToInt(point.Z))), new[] { plane },
            new ushort[] { 0, 1, 2, 3 }, new[] { data },
            new ushort[] { 0 }, new[] { new CollisionEntry(1, 0) },
            Array.Empty<Portal>());
        CollisionInstance instance = new("dialanche-wall", info, false);
        return new CollisionCandidate(instance, new CollisionEntry(1, 0));
    }

    private static T Struct<T>(params (string Name, object Value)[] fields)
        where T : struct
    {
        object value = default(T);
        foreach ((string name, object fieldValue) in fields)
        {
            typeof(T).GetField(name)!.SetValue(value, fieldValue);
        }
        return (T)value;
    }

    private static PlayerEntity ActivateTrace(ServerSimulation simulation,
        int slot, ulong connectionId)
    {
        PlayerEntity player = simulation.Scene.Players[slot];
        player.ServerActivate(connectionId, Hunter.Trace, team: 0);
        player.ModForceForm(altForm: true);
        return player;
    }

    private static void InitializeAltCollisionState(PlayerEntity player,
        Vector3 center, float yOffset, Vector3 speed)
    {
        player.Position = center.AddY(-yOffset);
        player.PrevPosition = player.Position.AddZ(0.1f);
        player.Speed = speed;
        player.PrevSpeed = speed;
    }

    private static void ApplyContacts(PlayerEntity player,
        CollisionResult[] contacts)
    {
        foreach (CollisionResult contact in contacts)
        {
            player.HandleCollision(contact);
        }
    }

    private static string FindAmhe1()
    {
        string? configured = Environment.GetEnvironmentVariable(
            "GAME_DATA_DIRECTORY");
        string[] starts = configured is null
            ? new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
            : new[] { configured, Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory };
        foreach (string start in starts)
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "AMHE1");
                if (File.Exists(Path.Combine(candidate, "_bin", "arm9.bin"))
                    && Directory.Exists(Path.Combine(candidate, "models"))
                    && Directory.Exists(Path.Combine(candidate, "levels")))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "AMHE1 extracted content was not found.");
    }

    private static void AssertMarkers(CollisionResult[] contacts,
        params float[] expected)
    {
        Assert.Equal(expected.Length, contacts.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], contacts[i].Field14);
        }
    }
}
