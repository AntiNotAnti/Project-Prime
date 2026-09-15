using System;
using System.Collections.Generic;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Entities;

/// <summary>
/// Compact, stateless decision helpers for the Balanced Spire alternate-form
/// ledge transition. Collision queries remain owned by PlayerCollision; this
/// type only validates their finite/static results and derives a bounded
/// landing target.
/// </summary>
internal static class DialancheLedgePolicy
{
    private const float LateralNormalLimit = 0.1f;
    private const float StandingNormalThreshold = 0.5f;
    private const float CeilingNormalLimit = -0.5f;
    private const float PlaneTolerance = 1 / 1024f;

    internal static bool TryResolveProbeDimensions(float altRadius,
        float sweepPadding, out float diameter, out float probeRadius,
        out float forwardDistance)
    {
        diameter = 0;
        probeRadius = 0;
        forwardDistance = 0;
        if (!float.IsFinite(altRadius) || !float.IsFinite(sweepPadding)
            || altRadius <= VectorMath.DefaultEpsilon || sweepPadding < 0)
        {
            return false;
        }
        diameter = altRadius * 2;
        probeRadius = altRadius + sweepPadding;
        forwardDistance = diameter + sweepPadding * 2;
        return float.IsFinite(diameter) && float.IsFinite(probeRadius)
            && float.IsFinite(forwardDistance);
    }

    internal static bool TryResolve(Hunter hunter, bool isAltForm,
        bool canClimbLedges, Vector3 currentCenter, Vector3 contactSpeed,
        Vector3 preservedSpeed, in CollisionResult wall, float wallTop,
        in CollisionResult forwardAbove, bool hasForwardAbove,
        in CollisionResult support, bool hasSupport, bool dialancheClear,
        bool forceFieldBlocked, float altRadius, float sweepPadding,
        out Vector3 landingCenter, out Vector3 landingSpeed)
    {
        landingCenter = Vector3.Zero;
        landingSpeed = Vector3.Zero;
        if (hunter != Hunter.Spire || !isAltForm || !canClimbLedges
            || !VectorMath.IsFinite(currentCenter)
            || !VectorMath.IsFinite(contactSpeed)
            || !VectorMath.IsFinite(preservedSpeed)
            || !float.IsFinite(wallTop)
            || !TryResolveProbeDimensions(altRadius, sweepPadding,
                out float diameter, out float probeRadius,
                out float forwardDistance))
        {
            return false;
        }
        if (!IsStaticFullFace(wall, rejectForbiddenTerrain: true)
            || !TryNormalizePlane(wall.Plane, out Vector3 wallNormal,
                out _)
            || MathF.Abs(wallNormal.Y) > LateralNormalLimit
            || !VectorMath.TryNormalizeHorizontal(-wallNormal,
                out Vector3 forward))
        {
            return false;
        }
        float wallHeight = wallTop - currentCenter.Y;
        if (!float.IsFinite(wallHeight) || wallHeight <= VectorMath.DefaultEpsilon
            || wallHeight > diameter + VectorMath.DefaultEpsilon)
        {
            return false;
        }
        float contactDot = Vector3.Dot(contactSpeed, wallNormal);
        if (!float.IsFinite(contactDot)
            || contactDot >= -VectorMath.DefaultEpsilon)
        {
            return false;
        }
        if (!hasForwardAbove
            || !IsStaticFullFace(forwardAbove, rejectForbiddenTerrain: true)
            || ArePlanesEquivalent(wall.Plane, forwardAbove.Plane)
            || !TryNormalizePlane(forwardAbove.Plane,
                out Vector3 forwardNormal, out _)
            || forwardNormal.Y < CeilingNormalLimit
            || forwardAbove.Position.Y < wallTop - VectorMath.DefaultEpsilon)
        {
            return false;
        }
        float forwardCandidateDistance = Vector3.Dot(
            forwardAbove.Position - currentCenter, forward);
        if (!float.IsFinite(forwardCandidateDistance)
            || forwardCandidateDistance < altRadius - VectorMath.DefaultEpsilon)
        {
            return false;
        }
        Vector3 supportProbeCenter = new(
            currentCenter.X + forward.X * forwardDistance,
            currentCenter.Y,
            currentCenter.Z + forward.Z * forwardDistance);
        if (!hasSupport
            || !IsStaticFullFace(support, rejectForbiddenTerrain: true)
            || !TryNormalizePlane(support.Plane, out Vector3 supportNormal,
                out _)
            || supportNormal.Y <= StandingNormalThreshold
            || !TryGetPlaneHeight(support.Plane, supportProbeCenter,
                out float supportHeight)
            || supportHeight < wallTop - VectorMath.DefaultEpsilon
            || supportHeight > wallTop + probeRadius
            || forceFieldBlocked || !dialancheClear)
        {
            return false;
        }
        Vector3 target = new(
            currentCenter.X + forward.X * forwardDistance,
            supportHeight + altRadius,
            currentCenter.Z + forward.Z * forwardDistance);
        if (!VectorMath.IsFinite(target))
        {
            return false;
        }
        landingCenter = target;
        landingSpeed = new(preservedSpeed.X,
            MathF.Max(preservedSpeed.Y, 0), preservedSpeed.Z);
        return VectorMath.IsFinite(landingSpeed);
    }

    internal static bool TryResolveWallForward(in CollisionResult wall,
        out Vector3 forward)
    {
        forward = Vector3.Zero;
        return TryNormalizePlane(wall.Plane, out Vector3 normal, out _)
            && MathF.Abs(normal.Y) <= LateralNormalLimit
            && VectorMath.TryNormalizeHorizontal(-normal, out forward);
    }

    internal static bool TryGetPlaneHeight(Vector4 plane, Vector3 position,
        out float height)
    {
        height = 0;
        if (!IsFinite(plane) || !VectorMath.IsFinite(position)
            || MathF.Abs(plane.Y) <= VectorMath.DefaultEpsilon)
        {
            return false;
        }
        height = (plane.W - plane.X * position.X
            - plane.Z * position.Z) / plane.Y;
        return float.IsFinite(height);
    }

    internal static bool TryFindStaticLateralWall(
        IReadOnlyList<CollisionCandidate> candidates,
        CollisionResult[] results, int count, Vector3 contactSpeed,
        out CollisionResult wall, out float wallTop)
    {
        wall = default;
        wallTop = 0;
        if (candidates == null || results == null || count <= 0
            || !VectorMath.IsFinite(contactSpeed))
        {
            return false;
        }
        count = Math.Min(count, results.Length);
        for (int i = 0; i < count; i++)
        {
            CollisionResult result = results[i];
            if (!IsStaticFullFace(result, rejectForbiddenTerrain: true)
                || !TryNormalizePlane(result.Plane,
                    out Vector3 normal, out _)
                || MathF.Abs(normal.Y) > LateralNormalLimit
                || Vector3.Dot(contactSpeed, normal)
                    >= -VectorMath.DefaultEpsilon
                || !TryGetStaticFaceBounds(candidates, result,
                    out DialancheStaticFace face))
            {
                continue;
            }
            wall = result;
            wallTop = face.MaxY;
            return float.IsFinite(wallTop);
        }
        return false;
    }

    internal static bool TryFindStaticForwardAboveCandidate(
        IReadOnlyList<CollisionCandidate> candidates,
        in CollisionResult wall, Vector3 currentCenter, Vector3 forward,
        float wallTop, float altRadius, float forwardDistance,
        float probeRadius, out CollisionResult result)
    {
        result = default;
        if (candidates == null || !VectorMath.IsFinite(currentCenter)
            || !VectorMath.TryNormalizeHorizontal(forward,
                out forward) || !float.IsFinite(wallTop)
            || !float.IsFinite(altRadius) || !float.IsFinite(forwardDistance)
            || !float.IsFinite(probeRadius))
        {
            return false;
        }
        bool found = false;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            CollisionCandidate candidate = candidates[i];
            if (candidate == null || candidate.EntityCollision != null
                || candidate.Collision == null || candidate.Collision.IsEntity
                || !candidate.Collision.Active
                || candidate.Collision.Info is not MphCollisionInfo info)
            {
                continue;
            }
            int dataStart = candidate.Entry.DataStartIndex;
            int dataCount = candidate.Entry.DataCount;
            for (int j = 0; j < dataCount; j++)
            {
                int dataIndex = dataStart + j;
                if (dataIndex < 0 || dataIndex >= info.DataIndices.Count)
                {
                    continue;
                }
                CollisionData data = info.Data[info.DataIndices[dataIndex]];
                if (!TryBuildStaticFace(candidate, data, forward,
                    out DialancheStaticFace face)
                    || face.Flags.TestFlag(CollisionFlags.Damaging)
                    || IsForbidden(face.Flags, face.Terrain)
                    || ArePlanesEquivalent(wall.Plane, face.Plane)
                    || face.MaxY < wallTop - VectorMath.DefaultEpsilon
                    || face.Centroid.Y < wallTop - VectorMath.DefaultEpsilon)
                {
                    continue;
                }
                Vector3 faceNormal = face.Plane.Xyz;
                if (!TryNormalizePlane(face.Plane, out faceNormal, out _)
                    || faceNormal.Y < CeilingNormalLimit
                    || face.MaxProjection - Vector3.Dot(currentCenter, forward)
                        < forwardDistance - probeRadius)
                {
                    continue;
                }
                float faceDistance = Vector3.Dot(
                    face.Centroid - currentCenter, forward);
                float score = MathF.Abs(faceDistance - forwardDistance);
                if (!float.IsFinite(faceDistance) || !float.IsFinite(score)
                    || faceDistance < altRadius - VectorMath.DefaultEpsilon
                    || found && score >= bestScore)
                {
                    continue;
                }
                result = new CollisionResult
                {
                    Field0 = 0,
                    Flags = face.Flags,
                    Plane = face.Plane,
                    Position = face.Centroid,
                    Distance = 1
                };
                bestScore = score;
                found = true;
            }
        }
        return found;
    }

    internal static bool TryFindDownwardSupport(CollisionResult[] results,
        int count, float wallTop, float probeRadius, Vector3 probeCenter,
        out CollisionResult support, out float supportHeight)
    {
        support = default;
        supportHeight = 0;
        if (results == null || count <= 0 || !float.IsFinite(wallTop)
            || !float.IsFinite(probeRadius) || probeRadius < 0
            || !VectorMath.IsFinite(probeCenter))
        {
            return false;
        }
        count = Math.Min(count, results.Length);
        bool found = false;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            CollisionResult result = results[i];
            if (!IsStaticFullFace(result, rejectForbiddenTerrain: true)
                || !TryNormalizePlane(result.Plane,
                    out Vector3 normal, out _)
                || normal.Y <= StandingNormalThreshold
                || !TryGetPlaneHeight(result.Plane, probeCenter,
                    out float height)
                || height < wallTop - VectorMath.DefaultEpsilon
                || height > wallTop + probeRadius)
            {
                continue;
            }
            float distance = MathF.Abs(height - wallTop);
            if (!float.IsFinite(distance) || found && distance >= bestDistance)
            {
                continue;
            }
            support = result;
            supportHeight = height;
            bestDistance = distance;
            found = true;
        }
        return found;
    }

    internal static bool IsStationaryDialancheClear(
        CollisionResult[] results, int count, in CollisionResult support)
    {
        if (results == null || count < 0 || !IsStaticFullFace(support,
            rejectForbiddenTerrain: true))
        {
            return false;
        }
        count = Math.Min(count, results.Length);
        for (int i = 0; i < count; i++)
        {
            CollisionResult result = results[i];
            if (!IsStaticFullFace(result, rejectForbiddenTerrain: false)
                || !ArePlanesEquivalent(result.Plane, support.Plane))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsTransitionPathClear(CollisionResult[] results,
        int count, in CollisionResult wall, in CollisionResult forwardAbove,
        in CollisionResult support)
    {
        if (results == null || count < 0)
        {
            return false;
        }
        count = Math.Min(count, results.Length);
        for (int i = 0; i < count; i++)
        {
            CollisionResult result = results[i];
            if (!IsStaticFullFace(result, rejectForbiddenTerrain: true)
                || !ArePlanesEquivalent(result.Plane, wall.Plane)
                    && !ArePlanesEquivalent(result.Plane, forwardAbove.Plane)
                    && !ArePlanesEquivalent(result.Plane, support.Plane))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsTransitionBlockedByForceField(bool active,
        Vector3 start, Vector3 end, float sphereRadius, Vector4 plane,
        Vector3 fieldPosition, Vector3 upVector, Vector3 rightVector,
        float width, float height)
    {
        if (!active)
        {
            return false;
        }
        if (!VectorMath.IsFinite(start) || !VectorMath.IsFinite(end)
            || !VectorMath.IsFinite(fieldPosition)
            || !IsFinite(plane) || !VectorMath.IsFinite(upVector)
            || !VectorMath.IsFinite(rightVector)
            || !float.IsFinite(sphereRadius) || sphereRadius < 0
            || !float.IsFinite(width) || width < 0
            || !float.IsFinite(height) || height < 0
            || !TryNormalizePlane(plane, out Vector3 normal,
                out float planeDistance)
            || !VectorMath.TryNormalize(upVector, out Vector3 up)
            || !VectorMath.TryNormalize(rightVector, out Vector3 right))
        {
            // Active malformed force fields fail closed. They are gameplay
            // blockers, so silently allowing a transition would desync peers.
            return true;
        }
        Vector3 delta = end - start;
        if (!VectorMath.IsFinite(delta))
        {
            return true;
        }
        float distance1 = Vector3.Dot(start, normal) - planeDistance;
        float distance2 = Vector3.Dot(end, normal) - planeDistance;
        if (!float.IsFinite(distance1) || !float.IsFinite(distance2))
        {
            return true;
        }
        float nearestPlaneDistance = MathF.Min(MathF.Abs(distance1),
            MathF.Abs(distance2));
        if (nearestPlaneDistance > sphereRadius
            && distance1 * distance2 > 0)
        {
            return false;
        }
        float expandedWidth = width + sphereRadius;
        float expandedHeight = height + sphereRadius;
        float startX = Vector3.Dot(start - fieldPosition, right);
        float endX = Vector3.Dot(end - fieldPosition, right);
        float startY = Vector3.Dot(start - fieldPosition, up);
        float endY = Vector3.Dot(end - fieldPosition, up);
        return SegmentIntersectsRectangle(startX, endX, startY, endY,
            expandedWidth, expandedHeight);
    }

    internal static bool TryGetStaticFaceBounds(
        IReadOnlyList<CollisionCandidate> candidates,
        in CollisionResult result, out DialancheStaticFace face)
    {
        face = default;
        if (candidates == null || !IsStaticFullFace(result,
            rejectForbiddenTerrain: false))
        {
            return false;
        }
        bool found = false;
        float nearestDistanceSquared = float.PositiveInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            CollisionCandidate candidate = candidates[i];
            if (candidate == null || candidate.EntityCollision != null
                || candidate.Collision == null || candidate.Collision.IsEntity
                || !candidate.Collision.Active
                || candidate.Collision.Info is not MphCollisionInfo info)
            {
                continue;
            }
            int dataStart = candidate.Entry.DataStartIndex;
            int dataCount = candidate.Entry.DataCount;
            for (int j = 0; j < dataCount; j++)
            {
                int dataIndex = dataStart + j;
                if (dataIndex < 0 || dataIndex >= info.DataIndices.Count)
                {
                    continue;
                }
                CollisionData data = info.Data[info.DataIndices[dataIndex]];
                if (!TryBuildStaticFace(candidate, data, Vector3.Zero,
                    out DialancheStaticFace current)
                    || !ArePlanesEquivalent(current.Plane, result.Plane))
                {
                    continue;
                }
                float distanceSquared =
                    (current.Centroid - result.Position).LengthSquared;
                if (!float.IsFinite(distanceSquared))
                {
                    continue;
                }
                if (!found || distanceSquared < nearestDistanceSquared)
                {
                    face = current;
                    nearestDistanceSquared = distanceSquared;
                    found = true;
                }
            }
        }
        return found;
    }

    private static bool TryBuildStaticFace(CollisionCandidate candidate,
        CollisionData data, Vector3 projectionAxis,
        out DialancheStaticFace face)
    {
        face = default;
        if (candidate.Collision.Info is not MphCollisionInfo info
            || data.PlaneIndex >= info.Planes.Count || data.PointIndexCount == 0
            || !VectorMath.IsFinite(candidate.Collision.Translation))
        {
            return false;
        }
        int pointStart = data.PointStartIndex;
        int pointCount = data.PointIndexCount;
        if (pointStart < 0 || pointCount < 1
            || pointStart > info.PointIndices.Count - pointCount)
        {
            return false;
        }
        Vector4 plane = info.Planes[data.PlaneIndex];
        if (!IsFinite(plane))
        {
            return false;
        }
        Vector3 translation = candidate.Collision.Translation;
        Vector4 worldPlane = new(plane.X, plane.Y, plane.Z,
            plane.W + Vector3.Dot(plane.Xyz, translation));
        if (!IsFinite(worldPlane))
        {
            return false;
        }
        Vector3 sum = Vector3.Zero;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        float minProjection = float.PositiveInfinity;
        float maxProjection = float.NegativeInfinity;
        for (int i = 0; i < pointCount; i++)
        {
            int index = pointStart + i;
            ushort pointIndex = info.PointIndices[index];
            if (pointIndex >= info.Points.Count)
            {
                return false;
            }
            Vector3 point = info.Points[pointIndex] + translation;
            if (!VectorMath.IsFinite(point))
            {
                return false;
            }
            sum += point;
            minY = MathF.Min(minY, point.Y);
            maxY = MathF.Max(maxY, point.Y);
            float projection = Vector3.Dot(point, projectionAxis);
            minProjection = MathF.Min(minProjection, projection);
            maxProjection = MathF.Max(maxProjection, projection);
        }
        Vector3 centroid = sum / pointCount;
        if (!VectorMath.IsFinite(centroid) || !float.IsFinite(minY)
            || !float.IsFinite(maxY) || !float.IsFinite(minProjection)
            || !float.IsFinite(maxProjection))
        {
            return false;
        }
        face = new DialancheStaticFace(worldPlane, data.Flags, centroid,
            minY, maxY, minProjection, maxProjection);
        return true;
    }

    private static bool IsStaticFullFace(in CollisionResult result,
        bool rejectForbiddenTerrain)
        => result.Field0 == 0 && result.EntityCollision == null
            && VectorMath.IsFinite(result.Position) && IsFinite(result.Plane)
            && (!rejectForbiddenTerrain || !IsForbidden(result.Flags,
                result.Terrain));

    private static bool IsForbidden(CollisionFlags flags, Terrain terrain)
        => flags.TestFlag(CollisionFlags.Damaging)
            || terrain is Terrain.Lava or Terrain.Acid;

    private static bool TryNormalizePlane(Vector4 plane,
        out Vector3 normal, out float distance)
    {
        normal = Vector3.Zero;
        distance = 0;
        if (!IsFinite(plane) || !VectorMath.TryNormalize(plane.Xyz,
            out normal))
        {
            return false;
        }
        float length = plane.Xyz.Length;
        distance = plane.W / length;
        return float.IsFinite(distance);
    }

    private static bool ArePlanesEquivalent(Vector4 first, Vector4 second)
    {
        if (!TryNormalizePlane(first, out Vector3 firstNormal,
                out float firstDistance)
            || !TryNormalizePlane(second, out Vector3 secondNormal,
                out float secondDistance))
        {
            return false;
        }
        return MathF.Abs(firstNormal.X - secondNormal.X) <= PlaneTolerance
            && MathF.Abs(firstNormal.Y - secondNormal.Y) <= PlaneTolerance
            && MathF.Abs(firstNormal.Z - secondNormal.Z) <= PlaneTolerance
            && MathF.Abs(firstDistance - secondDistance) <= PlaneTolerance;
    }

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool SegmentIntersectsRectangle(float startX, float endX,
        float startY, float endY, float halfWidth, float halfHeight)
    {
        if (!float.IsFinite(startX) || !float.IsFinite(endX)
            || !float.IsFinite(startY) || !float.IsFinite(endY)
            || !float.IsFinite(halfWidth) || halfWidth < 0
            || !float.IsFinite(halfHeight) || halfHeight < 0)
        {
            return true;
        }
        float minT = 0;
        float maxT = 1;
        return ClipAxis(startX, endX, -halfWidth, halfWidth,
            ref minT, ref maxT)
            && ClipAxis(startY, endY, -halfHeight, halfHeight,
                ref minT, ref maxT);
    }

    private static bool ClipAxis(float start, float end, float minimum,
        float maximum, ref float minT, ref float maxT)
    {
        float delta = end - start;
        if (!float.IsFinite(delta))
        {
            return false;
        }
        if (MathF.Abs(delta) <= VectorMath.DefaultEpsilon)
        {
            return start >= minimum && start <= maximum;
        }
        float t1 = (minimum - start) / delta;
        float t2 = (maximum - start) / delta;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }
        minT = MathF.Max(minT, t1);
        maxT = MathF.Min(maxT, t2);
        return minT <= maxT;
    }
}

internal readonly struct DialancheStaticFace
{
    internal Vector4 Plane { get; }
    internal CollisionFlags Flags { get; }
    internal Terrain Terrain
        => (Terrain)(((ushort)Flags & 0x1E0) >> 5);
    internal Vector3 Centroid { get; }
    internal float MinY { get; }
    internal float MaxY { get; }
    internal float MinProjection { get; }
    internal float MaxProjection { get; }

    internal DialancheStaticFace(Vector4 plane, CollisionFlags flags,
        Vector3 centroid, float minY, float maxY, float minProjection,
        float maxProjection)
    {
        Plane = plane;
        Flags = flags;
        Centroid = centroid;
        MinY = minY;
        MaxY = maxY;
        MinProjection = minProjection;
        MaxProjection = maxProjection;
    }
}
