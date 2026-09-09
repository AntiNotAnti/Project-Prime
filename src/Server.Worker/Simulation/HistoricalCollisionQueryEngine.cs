using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Runtime.HistoricalCollision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>
/// Side-effect-free beam collision query over immutable room candidates plus
/// registry-owned dynamic shapes. Registry order is intentionally grouped as
/// static room, Object/Platform, Door, ForceField to preserve the observable
/// current beam ordering on equal distances.
/// </summary>
internal sealed class HistoricalCollisionQueryEngine : IHistoricalCollisionQuery
{
    private readonly Scene _scene;
    private readonly HistoricalCollisionRegistry _registry;
    private readonly DynamicCollisionHistory _history;
    private readonly ServerCombat _combat;
    private readonly CollisionWorkspace _staticWorkspace;
    internal long TransformableShapeQueries { get; private set; }

    public HistoricalCollisionQueryEngine(Scene scene, HistoricalCollisionRegistry registry,
        DynamicCollisionHistory history, ServerCombat combat)
    {
        _scene = scene;
        _registry = registry;
        _history = history;
        _combat = combat;
        _staticWorkspace = CollisionDetection.CreateStaticPointWorkspace(scene);
    }

    public bool TryQuery(in HistoricalCollisionQuery query, uint tick,
        out HistoricalCollisionResult result)
        => TryQuery(query, tick, useHistory: true, out result);

    internal bool TryQueryCurrent(in HistoricalCollisionQuery query,
        out HistoricalCollisionResult result)
        => TryQuery(query, _combat.Tick, useHistory: false, out result);

    private bool TryQuery(in HistoricalCollisionQuery query, uint tick, bool useHistory,
        out HistoricalCollisionResult result)
    {
        result = HistoricalCollisionResult.None;
        CollisionResult collision = default;

        // Static room geometry is immutable and remains shared with the live
        // room. includeEntities:false is the important boundary: the current
        // broadphase cannot leak moving Object/Platform shapes into history.
        if (CollisionDetection.CheckStaticBetweenPoints(query.Start, query.End,
            query.Flags, _scene, _staticWorkspace, ref collision))
        {
            float dot = Vector3.Dot(query.Start, collision.Plane.Xyz) - collision.Plane.W;
            if (dot >= 0)
            {
                result = HistoricalCollisionResult.FromCollision(HistoricalColliderKind.StaticRoom,
                    HistoricalColliderId.None, collision);
            }
        }

        QueryTransformables(query, tick, useHistory, ref result);
        QueryDoors(query, tick, useHistory, ref result);
        QueryForceFields(query, tick, useHistory, ref result);
        return result.Hit;
    }

    private void QueryTransformables(in HistoricalCollisionQuery query, uint tick,
        bool useHistory, ref HistoricalCollisionResult result)
    {
        for (int index = 0; index < _registry.Count; index++)
        {
            HistoricalColliderKind kind = _registry.GetKind(index);
            if (kind is not (HistoricalColliderKind.Object or HistoricalColliderKind.Platform)) continue;
            if (useHistory) _combat.NoteHistoricalQuery(kind);
            HistoricalColliderId identity = _registry.GetIdentity(index);
            if (!TryGetState(index, identity, tick, useHistory, out HistoricalCollisionState state,
                out bool unknownGeometry))
            {
                if (unknownGeometry)
                    ConsiderUnknown(identity, kind, query.Start, query.End, ref result);
                continue;
            }
            if (useHistory && !_registry.IsCurrentShape(index))
            {
                // A live shape replacement cannot inherit an old transform or
                // callback identity. There is no supported current fallback
                // for the historical shape, so retain the blocker.
                _history.NoteMissing();
                _combat.NoteHistoricalMissing(identity);
                ConsiderUnknown(identity, kind, query.Start, query.End, ref result);
                continue;
            }
            CollisionInfo? shape = useHistory ? _registry.GetShape(index) : _registry.GetCurrentShape(index);
            if (!state.Active) continue;
            if (!SegmentIntersectsBounds(query.Start, query.End, state.BoundsMin, state.BoundsMax))
                continue;
            if (shape is not MphCollisionInfo info)
            {
                // An active collider whose historical shape is unavailable is
                // unknown geometry. Preserve the fail-closed rule rather than
                // allowing a shot through it.
                if (useHistory)
                {
                    _history.NoteMissing();
                    _combat.NoteHistoricalMissing(identity);
                }
                ConsiderUnknown(identity, kind, query.Start, query.End, ref result);
                continue;
            }
            CollisionResult collision = default;
            TransformableShapeQueries++;
            if (CollisionDetection.CheckBetweenPoints(info, state.Transform, state.Inverse,
                query.Start, query.End, query.Flags, ref collision))
            {
                Consider(kind, identity, collision, ref result);
            }
        }
    }

    internal static bool SegmentIntersectsBounds(Vector3 start, Vector3 end,
        Vector3 first, Vector3 second)
    {
        Vector3 min = Vector3.ComponentMin(first, second);
        Vector3 max = Vector3.ComponentMax(first, second);
        Vector3 direction = end - start;
        float near = 0;
        float far = 1;
        return IntersectsAxis(start.X, direction.X, min.X, max.X, ref near, ref far)
            && IntersectsAxis(start.Y, direction.Y, min.Y, max.Y, ref near, ref far)
            && IntersectsAxis(start.Z, direction.Z, min.Z, max.Z, ref near, ref far);
    }

    private static bool IntersectsAxis(float origin, float direction, float min, float max,
        ref float near, ref float far)
    {
        if (direction == 0) return origin >= min && origin <= max;
        float first = (min - origin) / direction;
        float second = (max - origin) / direction;
        if (first > second) (first, second) = (second, first);
        if (first > near) near = first;
        if (second < far) far = second;
        return near <= far;
    }

    private void QueryDoors(in HistoricalCollisionQuery query, uint tick,
        bool useHistory, ref HistoricalCollisionResult result)
    {
        for (int index = 0; index < _registry.Count; index++)
        {
            if (_registry.GetKind(index) != HistoricalColliderKind.Door) continue;
            if (useHistory) _combat.NoteHistoricalQuery(HistoricalColliderKind.Door);
            HistoricalColliderId identity = _registry.GetIdentity(index);
            if (!TryGetState(index, identity, tick, useHistory, out HistoricalCollisionState state,
                out bool unknownGeometry))
            {
                if (unknownGeometry)
                    ConsiderUnknown(identity, HistoricalColliderKind.Door, query.Start, query.End, ref result);
                continue;
            }
            if (!state.Active || !state.Blocking) continue;
            if (TryIntersectDoor(state, query.Start, query.End, out CollisionResult collision))
            {
                Consider(HistoricalColliderKind.Door, identity, collision, ref result);
            }
        }
    }

    private void QueryForceFields(in HistoricalCollisionQuery query, uint tick,
        bool useHistory, ref HistoricalCollisionResult result)
    {
        for (int index = 0; index < _registry.Count; index++)
        {
            if (_registry.GetKind(index) != HistoricalColliderKind.ForceField) continue;
            if (useHistory) _combat.NoteHistoricalQuery(HistoricalColliderKind.ForceField);
            HistoricalColliderId identity = _registry.GetIdentity(index);
            if (!TryGetState(index, identity, tick, useHistory, out HistoricalCollisionState state,
                out bool unknownGeometry))
            {
                if (unknownGeometry)
                    ConsiderUnknown(identity, HistoricalColliderKind.ForceField, query.Start, query.End, ref result);
                continue;
            }
            if (!state.Active) continue;
            if (TryIntersectForceField(state, query.Start, query.End, out CollisionResult collision))
            {
                Consider(HistoricalColliderKind.ForceField, identity, collision, ref result);
            }
        }
    }

    internal static bool TryIntersectDoor(in HistoricalCollisionState state,
        Vector3 start, Vector3 end, out CollisionResult collision)
    {
        collision = default;
        if (!state.Active || !state.Blocking) return false;
        Vector4 plane = new(state.Facing, 0);
        if (Vector3.Dot(start - state.Position, state.Facing) < 0) plane *= -1;
        Vector3 wvec = plane.Xyz * (state.Position + 0.4f * plane.Xyz);
        plane.W = wvec.X + wvec.Y + wvec.Z;
        if (!CollisionDetection.CheckCylinderIntersectPlane(start, end, plane, ref collision)
            || (collision.Position - state.Position).LengthSquared >= state.RadiusSquared)
            return false;
        collision.Field0 = 0;
        collision.Flags = CollisionFlags.None;
        collision.Plane = plane;
        return true;
    }

    internal static bool TryIntersectForceField(in HistoricalCollisionState state,
        Vector3 start, Vector3 end, out CollisionResult collision)
    {
        collision = default;
        if (!state.Active
            || !CollisionDetection.CheckCylinderIntersectPlane(start, end, state.Plane, ref collision))
            return false;
        Vector3 between = collision.Position - state.Position;
        float dot = Vector3.Dot(between, state.Up);
        if (dot > state.Height || dot < -state.Height) return false;
        dot = Vector3.Dot(between, state.Right);
        if (dot > state.Width || dot < -state.Width) return false;
        collision.Field0 = 0;
        collision.Flags = CollisionFlags.None;
        collision.Plane = state.Plane;
        return true;
    }

    private bool TryGetState(int index, HistoricalColliderId identity, uint tick,
        bool useHistory, out HistoricalCollisionState state, out bool unknownGeometry)
    {
        unknownGeometry = false;
        if (!useHistory)
        {
            state = default;
            return _registry.TryCaptureCurrentState(index, out state);
        }
        if (_history.TryGet(identity, tick, out state)) return true;

        // Missing history may use the exact registered current collider as the
        // bounded fallback. A removed/replaced entity has no safe state, so it
        // is explicitly marked unknown and the caller blocks the shot.
        _combat.NoteHistoricalMissing(identity);
        if (_registry.TryCaptureCurrentState(index, out state)) return true;
        unknownGeometry = true;
        state = default;
        return false;
    }

    private static void Consider(HistoricalColliderKind kind, HistoricalColliderId identity,
        in CollisionResult collision, ref HistoricalCollisionResult result)
    {
        // Strictly less preserves the first source for equal distances. The
        // caller invokes sources in the same static/entity/door/field order as
        // the live beam path.
        if (!collision.Distance.IsFinite() || result.Hit && collision.Distance >= result.Distance) return;
        result = HistoricalCollisionResult.FromCollision(kind, identity, collision);
    }

    private static void ConsiderUnknown(HistoricalColliderId identity, HistoricalColliderKind kind,
        Vector3 start, Vector3 end, ref HistoricalCollisionResult result)
    {
        if (result.Hit && result.Distance <= 0) return;
        Vector3 normal = start - end;
        normal = normal.LengthSquared > 0 ? normal.Normalized() : Vector3.UnitY;
        result = new HistoricalCollisionResult
        {
            Hit = true,
            ColliderKind = kind,
            ColliderId = identity,
            Distance = 0,
            Position = start,
            Plane = new Vector4(normal, Vector3.Dot(start, normal)),
            Flags = CollisionFlags.None
        };
    }
}

internal static class HistoricalCollisionFloatExtensions
{
    public static bool IsFinite(this float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
