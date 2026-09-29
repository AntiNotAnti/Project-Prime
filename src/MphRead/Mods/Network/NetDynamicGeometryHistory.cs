using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public readonly record struct NetGeometryState(Matrix4 Transform, Matrix4 Inverse1, Matrix4 Inverse2,
    Vector3 Center, bool Enabled, bool Secondary = false);
public interface INetRewindableGeometry
{
    int NetGeometryId { get; }
    bool Continuous { get; }
    NetGeometryState CaptureNetworkCollisionState();
    void ApplyNetworkCollisionState(in NetGeometryState state);
}

/// <summary>Room-scoped collision-only history. Every slot is sampled at the same
/// publication point as player history. No entities, animation or scripts step here.</summary>
public sealed class NetDynamicGeometryHistory
{
    public const int Depth = NetUnlagged.HistoryFrames, MaximumObjects = 512;
    private readonly INetRewindableGeometry[] _objects;
    private readonly NetGeometryState[,] _states;
    private readonly int[] _transformIndex;
    private readonly ulong[,] _enabledBits, _secondaryBits;
    private readonly NetGeometryState[] _latest;
    private readonly uint[] _currentRevision;
    private readonly uint[,] _revisions;
    private bool _recorded;
    public static long GeometryObjectsConsidered, GeometryObjectsSkippedUnchanged, GeometryObjectsCaptured,
        GeometryObjectsApplied, GeometryObjectsRestored, GeometryFallbackCaptures,
        GeometryInterpolationsRequested, GeometryInterpolationsPerformed, GeometryInterpolationsAvoided,
        GeometryInversePerformed, GeometryInverseAvoided, GeometryApplyPerformed, GeometryApplyAvoided;
    private static readonly Dictionary<EntityBase, int> _entityIds = new();
    private static readonly Dictionary<EntityCollision, int> _collisionIds = new();
    private readonly NetGeometryState[] _present;
    private readonly bool[] _moved;
    private readonly uint[] _frames = new uint[Depth];
    private readonly bool[] _valid = new bool[Depth];
    public static long HistoryMiss, ObjectsRewound, RewindFrames, InterpolationCount, DiscreteSamples;
    public static long ShadowSame, ShadowHistoricalBlocked, ShadowCurrentBlocked, ShadowDifferent, ShadowUnavailable;
    public static bool ProductionEnabled { get; set; } = true;
    public static bool ShadowEnabled { get; set; }
    private static Scene? _scene;
    private static NetDynamicGeometryHistory? _room;
    public NetDynamicGeometryHistory(INetRewindableGeometry[] objects)
    {
        if (objects.Length > MaximumObjects) throw new ArgumentOutOfRangeException(nameof(objects));
        _objects = (INetRewindableGeometry[])objects.Clone();
        _transformIndex = new int[objects.Length];
        int transforms = 0;
        for (int i = 0; i < objects.Length; i++)
            _transformIndex[i] = objects[i] is DoorGeometry or FieldGeometry ? -1 : transforms++;
        _states = new NetGeometryState[transforms, Depth];
        _enabledBits = new ulong[(objects.Length + 63) / 64, Depth];
        _secondaryBits = new ulong[(objects.Length + 63) / 64, Depth];
        _latest = new NetGeometryState[objects.Length];
        _currentRevision = new uint[objects.Length];
        _revisions = new uint[objects.Length, Depth];
        _present = new NetGeometryState[objects.Length]; _moved = new bool[objects.Length];
        for (int i = 0; i < objects.Length; i++)
            for (int j = 0; j < i; j++)
                if (objects[i].NetGeometryId == objects[j].NetGeometryId) throw new ArgumentException("Duplicate room collision identity");
    }
    public void Record(uint frame)
    {
        Restore(); int at = (int)(frame % Depth);
        for (int i = 0; i < _objects.Length; i++)
        {
            var state = _objects[i].CaptureNetworkCollisionState();
            if (!_recorded || state != _latest[i]) _currentRevision[i]++;
            _latest[i] = state; _revisions[i, at] = _currentRevision[i];
            if (_transformIndex[i] >= 0) _states[_transformIndex[i], at] = state;
            else
            {
                ulong mask = 1UL << (i & 63); int word = i >> 6;
                _enabledBits[word, at] = (_enabledBits[word, at] & ~mask) | (state.Enabled ? mask : 0);
                _secondaryBits[word, at] = (_secondaryBits[word, at] & ~mask) | (state.Secondary ? mask : 0);
            }
        }
        _recorded = true;
        _frames[at] = frame; _valid[at] = true;
    }
    private NetGeometryState State(int index, int at)
    {
        int transform = _transformIndex[index];
        if (transform >= 0) return _states[transform, at];
        ulong mask = 1UL << (index & 63); int word = index >> 6;
        return new(default, default, default, default,
            (_enabledBits[word, at] & mask) != 0, (_secondaryBits[word, at] & mask) != 0);
    }

    public bool Reconcile(double frame)
    {
        Restore(); uint whole = (uint)Math.Floor(frame); int at = (int)(whole % Depth), next = (at + 1) % Depth;
        if (!_valid[at] || _frames[at] != whole) { HistoryMiss++; return false; }
        float fraction = (float)(frame - whole);
        bool interpolate = fraction > .0001f && _valid[next] && _frames[next] == unchecked(whole + 1);
        try
        {
            for (int i = 0; i < _objects.Length; i++)
            {
                GeometryObjectsConsidered++;
                var geometry = _objects[i]; var state = State(i, at);
                // Engine collision can change after Record (doors hit during a shot,
                // platform processing, scripts). Until adapters expose mutation stamps,
                // prove live equality by capture rather than trusting a stale revision.
                var live = geometry.CaptureNetworkCollisionState();
                GeometryObjectsCaptured++; GeometryFallbackCaptures++;
                if (geometry.Continuous && interpolate && state.Enabled && State(i, next).Enabled)
                {
                    GeometryInterpolationsRequested++;
                    var after = State(i, next);
                    if (state == after)
                    { GeometryInterpolationsAvoided++; GeometryInverseAvoided++; }
                    else
                    {
                    var rotation = Quaternion.Slerp(state.Transform.ExtractRotation(), after.Transform.ExtractRotation(), fraction);
                    var scale = Vector3.Lerp(state.Transform.ExtractScale(), after.Transform.ExtractScale(), fraction);
                    var transform = Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(rotation);
                    transform.Row3 = new Vector4(Vector3.Lerp(state.Transform.Row3.Xyz, after.Transform.Row3.Xyz, fraction), 1);
                    // Preserve scale from animated collision attachment nodes too.
                    var inverse = transform.Inverted();
                    state = state with { Transform = transform, Inverse1 = inverse, Inverse2 = inverse,
                        Center = Matrix.Vec3MultMtx4(Matrix.Vec3MultMtx4(state.Center, state.Inverse1), transform) };
                    InterpolationCount++; GeometryInterpolationsPerformed++; GeometryInversePerformed++;
                    }
                }
                else if (!geometry.Continuous) DiscreteSamples++;
                if (state == live)
                {
                    GeometryApplyAvoided++;
                    if (live == _latest[i] && _revisions[i, at] == _currentRevision[i]) GeometryObjectsSkippedUnchanged++;
                    continue;
                }
                _present[i] = live; _moved[i] = true;
                geometry.ApplyNetworkCollisionState(state); ObjectsRewound++;
                GeometryObjectsApplied++; GeometryApplyPerformed++;
            }
            RewindFrames++; return true;
        }
        catch { Restore(); throw; }
    }
    public void Restore()
    {
        for (int i = 0; i < _objects.Length; i++)
            if (_moved[i]) { _moved[i] = false; _objects[i].ApplyNetworkCollisionState(_present[i]); GeometryObjectsRestored++; }
    }
    public Scope Begin(double frame) { bool applied = Reconcile(frame); return new(this, applied); }
    public readonly struct Scope : IDisposable
    {
        private readonly NetDynamicGeometryHistory _history;
        public bool Applied { get; }
        internal Scope(NetDynamicGeometryHistory history, bool applied) { _history = history; Applied = applied; }
        public void Dispose() => _history.Restore();
    }
    public static void ResetRoom() { _room?.Restore(); _room = null; _scene = null; _entityIds.Clear(); _collisionIds.Clear(); }
    internal static void RecordWorld(uint frame)
    {
        if (PlayerEntity.Players.Count == 0) return;
        Scene scene = PlayerEntity.Players[0].OwningScene;
        if (_scene != scene)
        {
            ResetRoom(); var objects = new List<INetRewindableGeometry>(); int index = 0;
            foreach (var entity in scene.Entities)
            {
                // Entity-list position plus collision component is deterministic
                // for the lifetime of this room; replacing the room replaces history.
                int id = index++ * 3;
                _entityIds[entity] = id;
                if (entity is DoorEntity door) objects.Add(new DoorGeometry(id, door));
                else if (entity is ForceFieldEntity field) objects.Add(new FieldGeometry(id, field));
                else if (entity.Type is EntityType.Platform or EntityType.Object)
                    for (int slot = 0; slot < entity.EntityCollision.Length; slot++)
                        if (entity.EntityCollision[slot] is { Collision: not null } collision)
                            { objects.Add(new MeshGeometry(id + slot, collision)); _collisionIds[collision] = id + slot; }
            }
            _room = new(objects.ToArray()); _scene = scene;
        }
        _room!.Record(frame);
    }
    internal static void ReconcileWorld(double frame)
    { if (ProductionEnabled) { if (_room == null) HistoryMiss++; else _room.Reconcile(frame); } }
    internal static void RestoreWorld() => _room?.Restore();

    private sealed class DoorGeometry(int id, DoorEntity door) : INetRewindableGeometry
    {
        public int NetGeometryId => id;
        public bool Continuous => false;
        public NetGeometryState CaptureNetworkCollisionState() => new(default, default, default, default,
            !door.Flags.TestFlag(DoorFlags.Open), door.ConnectorInactive);
        public void ApplyNetworkCollisionState(in NetGeometryState state)
        {
            // Preserve ShotOpen and other simulation effects caused by a real hit.
            if (state.Enabled) door.Flags &= ~DoorFlags.Open; else door.Flags |= DoorFlags.Open;
            door.ConnectorInactive = state.Secondary;
        }
    }
    private sealed class FieldGeometry(int id, ForceFieldEntity field) : INetRewindableGeometry
    {
        public int NetGeometryId => id;
        public bool Continuous => false;
        public NetGeometryState CaptureNetworkCollisionState() => new(default, default, default, default, field.Active);
        public void ApplyNetworkCollisionState(in NetGeometryState state) => field.ModSetNetworkCollisionActive(state.Enabled);
    }
    private sealed class MeshGeometry(int id, EntityCollision collision) : INetRewindableGeometry
    {
        public int NetGeometryId => id;
        public bool Continuous => true;
        public NetGeometryState CaptureNetworkCollisionState() => new(collision.Transform, collision.Inverse1,
            collision.Inverse2, collision.CurrentCenter, collision.Collision!.Active);
        public void ApplyNetworkCollisionState(in NetGeometryState state)
        {
            collision.Transform = state.Transform; collision.Inverse1 = state.Inverse1; collision.Inverse2 = state.Inverse2;
            collision.CurrentCenter = state.Center; collision.Collision!.Active = state.Enabled;
        }
    }
    internal static void CompareShadow(Scene scene, Vector3 origin, Vector3 direction, double frame, float distance)
    {
        if (!ShadowEnabled || direction.LengthSquared < .001f) return;
        if (_room == null) { ShadowUnavailable++; return; }
        var end = origin + direction.Normalized() * distance;
        var present = Trace(scene, origin, end);
        using var scope = _room.Begin(frame);
        if (!scope.Applied) { ShadowUnavailable++; return; }
        var historical = Trace(scene, origin, end);
        if (present == historical) ShadowSame++;
        else if (present == -1) ShadowHistoricalBlocked++;
        else if (historical == -1) ShadowCurrentBlocked++;
        else ShadowDifferent++;
    }
    internal static bool TryTraceDistanceAtFrame(Scene scene, Vector3 start, Vector3 end,
        double frame, out float closest)
    {
        closest = 1;
        if (_room == null) return false;
        if (frame >= NetSession.NetFrame) { closest = TraceDistance(scene, start, end); return true; }
        using var scope = _room.Begin(frame);
        if (!scope.Applied) return false;
        closest = TraceDistance(scene, start, end);
        return true;
    }

    private static float TraceDistance(Scene scene, Vector3 start, Vector3 end)
        => TraceObstacle(scene, start, end).Distance;

    public readonly record struct GeometryTrace(float Distance, int NetGeometryId);
    // Both distance and identity come from the SAME winning collision result.
    internal static GeometryTrace TraceObstacle(Scene scene, Vector3 start, Vector3 end)
    {
        CollisionResult result = default;
        bool hit = CollisionDetection.CheckBetweenPoints(start, end, TestFlags.Beams, scene, ref result);
        float closest = hit ? result.Distance : 1;
        int obstacle = hit ? result.EntityCollision is { } mesh && _collisionIds.TryGetValue(mesh, out int id) ? id : -2 : -1;
        foreach (var door in scene.GetDoorEntities())
        {
            if (door.Flags.TestFlag(DoorFlags.Open) || door.ConnectorInactive) continue;
            var normal = door.FacingVector; var position = door.LockPosition;
            if (Vector3.Dot(start - position, normal) < 0) normal = -normal;
            var plane = new Vector4(normal, Vector3.Dot(normal, position + .4f * normal));
            if (CollisionDetection.CheckCylinderIntersectPlane(start, end, plane, ref result)
                && result.Distance < closest && (result.Position - position).LengthSquared < door.RadiusSquared)
            { closest = result.Distance; obstacle = _entityIds.TryGetValue(door, out int doorId) ? doorId : -2; }
        }
        foreach (var field in scene.GetForceFieldEntities())
        {
            if (!field.Active || !CollisionDetection.CheckCylinderIntersectPlane(start, end, field.Plane, ref result)
                || result.Distance >= closest) continue;
            var relative = result.Position - field.Position;
            if (Math.Abs(Vector3.Dot(relative, field.FieldUpVector)) <= field.Height
                && Math.Abs(Vector3.Dot(relative, field.FieldRightVector)) <= field.Width)
            { closest = result.Distance; obstacle = _entityIds.TryGetValue(field, out int fieldId) ? fieldId : -2; }
        }
        return new(closest, obstacle);
    }

    private static int Trace(Scene scene, Vector3 start, Vector3 end) => TraceObstacle(scene, start, end).NetGeometryId;
}
