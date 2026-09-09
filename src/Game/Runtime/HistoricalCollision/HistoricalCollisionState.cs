using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Runtime.HistoricalCollision;

/// <summary>
/// Immutable collision facts captured at one completed authoritative tick.
/// The state contains no live entity reference and no mutable collision
/// instance. Shape data is owned separately by the fixed registry.
/// </summary>
public readonly record struct HistoricalCollisionState
{
    public HistoricalColliderId Identity { get; init; }
    public HistoricalColliderKind Kind { get; init; }

    /// <summary>Whether the source exists and may participate in collision.</summary>
    public bool Active { get; init; }

    /// <summary>For doors, whether the historical snapshot blocks beams.</summary>
    public bool Blocking { get; init; }

    public Vector3 Facing { get; init; }
    public Vector3 Position { get; init; }
    public float RadiusSquared { get; init; }

    public Vector4 Plane { get; init; }
    public Vector3 Up { get; init; }
    public Vector3 Right { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }

    /// <summary>Transform used to place an immutable Object/Platform shape.</summary>
    public Matrix4 Transform { get; init; }

    /// <summary>Inverse transform used to query the immutable shape.</summary>
    public Matrix4 Inverse { get; init; }

    /// <summary>Current-space broadphase bounds captured for diagnostics/fallback.</summary>
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }

    public CollisionFlags CollisionFlags { get; init; }

    public static HistoricalCollisionState ForDoor(HistoricalColliderId identity, bool blocking,
        Vector3 facing, Vector3 lockPosition, float radiusSquared)
        => new()
        {
            Identity = identity,
            Kind = HistoricalColliderKind.Door,
            Active = true,
            Blocking = blocking,
            Facing = facing,
            Position = lockPosition,
            RadiusSquared = radiusSquared,
            Transform = Matrix4.Identity,
            Inverse = Matrix4.Identity
        };

    public static HistoricalCollisionState ForForceField(HistoricalColliderId identity, bool active,
        Vector4 plane, Vector3 position, Vector3 up, Vector3 right, float width, float height)
        => new()
        {
            Identity = identity,
            Kind = HistoricalColliderKind.ForceField,
            Active = active,
            Blocking = active,
            Plane = plane,
            Position = position,
            Up = up,
            Right = right,
            Width = width,
            Height = height,
            Transform = Matrix4.Identity,
            Inverse = Matrix4.Identity
        };

    public static HistoricalCollisionState ForTransformable(HistoricalColliderId identity,
        HistoricalColliderKind kind, bool active, Matrix4 transform, Matrix4 inverse,
        Vector3 boundsMin, Vector3 boundsMax, CollisionFlags collisionFlags)
        => new()
        {
            Identity = identity,
            Kind = kind,
            Active = active,
            Blocking = active,
            Transform = transform,
            Inverse = inverse,
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            CollisionFlags = collisionFlags
        };
}
