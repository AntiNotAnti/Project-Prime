using System;
using MphRead.Entities;
using MphRead.Formats.Collision;
using MphRead.Runtime.HistoricalCollision;

namespace MphRead.Mods.Network;

/// <summary>
/// Fixed registration of the dynamic collision sources in one loaded Scene.
/// The registry is sealed after room setup; it owns the immutable collision
/// shape references used by historical queries and never owns a live rewind.
/// </summary>
public sealed class HistoricalCollisionRegistry
{
    public const int DefaultHardColliderCap = 256;

    private readonly Registration[] _registrations;
    private Scene? _scene;
    private int _count;
    private bool _sealed;

    private readonly struct Registration
    {
        public readonly HistoricalColliderId Identity;
        public readonly HistoricalColliderKind Kind;
        public readonly EntityBase Entity;
        public readonly int CollisionSlot;
        public readonly CollisionInfo? Shape;

        public Registration(HistoricalColliderId identity, HistoricalColliderKind kind,
            EntityBase entity, int collisionSlot, CollisionInfo? shape)
        {
            Identity = identity;
            Kind = kind;
            Entity = entity;
            CollisionSlot = collisionSlot;
            Shape = shape;
        }
    }

    public HistoricalCollisionRegistry(Scene? scene = null, int hardColliderCap = DefaultHardColliderCap)
    {
        if (hardColliderCap <= 0) throw new ArgumentOutOfRangeException(nameof(hardColliderCap));
        _scene = scene;
        _registrations = new Registration[hardColliderCap];
        HardColliderCap = hardColliderCap;
    }

    public int HardColliderCap { get; }
    public int Count => _count;
    public bool IsSealed => _sealed;
    public int RegistrationRejected { get; private set; }
    public int UnsupportedRegistrations { get; private set; }
    public bool RegistrationOverflowed => RegistrationRejected != 0;

    internal void BindScene(Scene scene)
    {
        if (_scene != null && !ReferenceEquals(_scene, scene))
            throw new InvalidOperationException("Historical collision registry already belongs to another scene.");
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <summary>
    /// Registers every collision source that participates in beam collision.
    /// Calling this twice is idempotent for the same entity/slot.
    /// </summary>
    public void RegisterScene()
    {
        if (_scene == null) throw new InvalidOperationException("Historical collision registry has no Scene.");
        if (_sealed) return;
        foreach (DoorEntity door in _scene.GetDoorEntities()) TryRegister(door, 0, out _);
        foreach (ForceFieldEntity field in _scene.GetForceFieldEntities()) TryRegister(field, 0, out _);
        // CollisionDetection builds a LIFO candidate workspace. Its current
        // broadphase therefore observes Object before Platform and the higher
        // collision slot before slot zero; preserve that tie ordering here.
        foreach (ObjectEntity obj in _scene.GetObjectEntities())
        {
            for (int slot = obj.EntityCollision.Length - 1; slot >= 0; slot--)
                if (obj.EntityCollision[slot] != null) TryRegister(obj, slot, out _);
        }
        foreach (PlatformEntity platform in _scene.GetPlatformEntities())
        {
            for (int slot = platform.EntityCollision.Length - 1; slot >= 0; slot--)
                if (platform.EntityCollision[slot] != null) TryRegister(platform, slot, out _);
        }
        _sealed = true;
    }

    /// <summary>Test-only and diagnostic entry point for bounded setup.</summary>
    public bool TryRegister(EntityBase entity, int collisionSlot, out HistoricalColliderId identity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        HistoricalColliderKind kind = KindOf(entity);
        if (kind == HistoricalColliderKind.None || entity.Id < 0)
        {
            identity = HistoricalColliderId.None;
            UnsupportedRegistrations++;
            return false;
        }
        if (kind is not (HistoricalColliderKind.Object or HistoricalColliderKind.Platform))
            collisionSlot = 0;
        else if ((uint)collisionSlot >= (uint)entity.EntityCollision.Length)
            throw new ArgumentOutOfRangeException(nameof(collisionSlot));

        for (int i = 0; i < _count; i++)
        {
            ref readonly Registration existing = ref _registrations[i];
            if (ReferenceEquals(existing.Entity, entity) && existing.CollisionSlot == collisionSlot)
            {
                identity = existing.Identity;
                return true;
            }
        }
        if (_sealed || _count == _registrations.Length)
        {
            RegistrationRejected++;
            identity = HistoricalColliderId.None;
            return false;
        }

        uint generation = 1;
        for (int i = 0; i < _count; i++)
        {
            if (_registrations[i].Identity.EntityId == entity.Id)
                generation = Math.Max(generation, _registrations[i].Identity.Generation + 1);
        }
        identity = new HistoricalColliderId(entity.Id, generation);
        CollisionInfo? shape = kind is HistoricalColliderKind.Object or HistoricalColliderKind.Platform
            ? entity.EntityCollision[collisionSlot]?.Collision?.Info : null;
        _registrations[_count++] = new Registration(identity, kind, entity, collisionSlot, shape);
        return true;
    }

    public void Seal() => _sealed = true;

    public HistoricalColliderKind GetKind(int index) => Get(index).Kind;
    public HistoricalColliderId GetIdentity(int index) => Get(index).Identity;
    internal CollisionInfo? GetShape(int index) => Get(index).Shape;
    internal int CountKind(HistoricalColliderKind kind)
    {
        int count = 0;
        for (int index = 0; index < _count; index++)
            if (_registrations[index].Kind == kind) count++;
        return count;
    }

    /// <summary>
    /// Shape references are frozen at registration. A runtime replacement is
    /// treated as missing historical geometry instead of silently querying a
    /// new shape under an old generation.
    /// </summary>
    public bool IsCurrentShape(int index)
    {
        ref readonly Registration item = ref Get(index);
        if (item.Kind is not (HistoricalColliderKind.Object or HistoricalColliderKind.Platform)) return true;
        EntityCollision? current = item.Entity.EntityCollision[item.CollisionSlot];
        return current?.Collision != null && ReferenceEquals(current.Collision.Info, item.Shape);
    }

    public bool TryGetIndex(HistoricalColliderId identity, out int index)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_registrations[i].Identity == identity)
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    /// <summary>
    /// Resolve a callback only if the scene still owns the exact registered
    /// entity for this id/generation. A replacement with the same entity id
    /// cannot inherit a historical callback.
    /// </summary>
    public bool TryResolveCurrent(HistoricalColliderId identity, out EntityBase entity)
    {
        entity = null!;
        if (!TryGetIndex(identity, out int index)) return false;
        EntityBase registered = _registrations[index].Entity;
        if (_scene == null || !_scene.TryGetEntity(identity.EntityId, out EntityBase? current)
            || !ReferenceEquals(registered, current)
            || !IsCurrentShape(index)) return false;
        entity = current;
        return true;
    }

    public HistoricalCollisionState CaptureState(int index)
    {
        ref readonly Registration item = ref Get(index);
        HistoricalCollisionState state = item.Kind switch
        {
            HistoricalColliderKind.Door => CaptureDoor(item),
            HistoricalColliderKind.ForceField => CaptureForceField(item),
            HistoricalColliderKind.Object or HistoricalColliderKind.Platform => CaptureTransformable(item),
            _ => default
        };
        if (_scene == null || !_scene.TryGetEntity(item.Identity.EntityId, out EntityBase? current)
            || !ReferenceEquals(current, item.Entity))
        {
            state = state with { Active = false, Blocking = false };
        }
        return state;
    }

    /// <summary>
    /// Captures the current registered endpoint only when the exact entity is
    /// still owned by the scene. A removed/replaced id has no safe current
    /// fallback; callers must treat that as unknown geometry and fail closed.
    /// </summary>
    internal bool TryCaptureCurrentState(int index, out HistoricalCollisionState state)
    {
        ref readonly Registration item = ref Get(index);
        if (_scene == null || !_scene.TryGetEntity(item.Identity.EntityId, out EntityBase? current)
            || !ReferenceEquals(current, item.Entity))
        {
            state = default;
            return false;
        }
        state = CaptureState(index);
        return true;
    }

    internal CollisionInfo? GetCurrentShape(int index)
    {
        ref readonly Registration item = ref Get(index);
        if (item.Kind is not (HistoricalColliderKind.Object or HistoricalColliderKind.Platform)) return null;
        return item.Entity.EntityCollision[item.CollisionSlot]?.Collision?.Info;
    }

    private static HistoricalCollisionState CaptureDoor(in Registration item)
    {
        DoorEntity door = (DoorEntity)item.Entity;
        return HistoricalCollisionState.ForDoor(item.Identity,
            blocking: !door.Flags.TestFlag(DoorFlags.Open), door.FacingVector,
            door.LockPosition, door.RadiusSquared);
    }

    private static HistoricalCollisionState CaptureForceField(in Registration item)
    {
        ForceFieldEntity field = (ForceFieldEntity)item.Entity;
        return HistoricalCollisionState.ForForceField(item.Identity, field.Active, field.Plane,
            field.Position, field.FieldUpVector, field.FieldRightVector, field.Width, field.Height);
    }

    private static HistoricalCollisionState CaptureTransformable(in Registration item)
    {
        EntityCollision? collision = item.Entity.EntityCollision[item.CollisionSlot];
        if (collision == null)
            return HistoricalCollisionState.ForTransformable(item.Identity, item.Kind, false,
                OpenTK.Mathematics.Matrix4.Identity, OpenTK.Mathematics.Matrix4.Identity,
                default, default, 0);

        OpenTK.Mathematics.Vector3 center = collision.CurrentCenter;
        OpenTK.Mathematics.Vector3 radius = new(collision.MaxDistance);
        CollisionFlags flags = 0;
        if (collision.Collision?.Info is MphCollisionInfo info && info.Data.Count > 0)
            flags = info.Data[0].Flags;
        return HistoricalCollisionState.ForTransformable(item.Identity, item.Kind,
            collision.Collision?.Active == true, collision.Transform, collision.Inverse1,
            center - radius, center + radius, flags);
    }

    private ref readonly Registration Get(int index)
    {
        if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        return ref _registrations[index];
    }

    private static HistoricalColliderKind KindOf(EntityBase entity)
        => entity.Type switch
        {
            EntityType.Door => HistoricalColliderKind.Door,
            EntityType.ForceField => HistoricalColliderKind.ForceField,
            EntityType.Object => HistoricalColliderKind.Object,
            EntityType.Platform => HistoricalColliderKind.Platform,
            _ => HistoricalColliderKind.None
        };
}
