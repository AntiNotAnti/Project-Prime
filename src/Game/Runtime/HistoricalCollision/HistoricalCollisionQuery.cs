using MphRead.Formats;
using MphRead.Formats.Collision;
using OpenTK.Mathematics;

namespace MphRead.Runtime.HistoricalCollision;

/// <summary>Read-only input to a historical collision query.</summary>
public readonly record struct HistoricalCollisionQuery(Vector3 Start, Vector3 End, TestFlags Flags);

/// <summary>
/// Immutable result surface. A static-room hit has <see cref="ColliderKind"/>
/// set to <see cref="HistoricalColliderKind.StaticRoom"/> and no dynamic id.
/// </summary>
public readonly record struct HistoricalCollisionResult
{
    public bool Hit { get; init; }
    public HistoricalColliderKind ColliderKind { get; init; }
    public HistoricalColliderId ColliderId { get; init; }
    public float Distance { get; init; }
    public Vector3 Position { get; init; }
    public Vector4 Plane { get; init; }
    public CollisionFlags Flags { get; init; }

    public static HistoricalCollisionResult None => default;

    public static HistoricalCollisionResult FromCollision(HistoricalColliderKind kind,
        HistoricalColliderId id, in CollisionResult result)
        => new()
        {
            Hit = true,
            ColliderKind = kind,
            ColliderId = id,
            Distance = result.Distance,
            Position = result.Position,
            Plane = result.Plane,
            Flags = result.Flags
        };
}

/// <summary>Pure query contract used by server combat and diagnostic tools.</summary>
public interface IHistoricalCollisionQuery
{
    bool TryQuery(in HistoricalCollisionQuery query, uint tick, out HistoricalCollisionResult result);
}

/// <summary>
/// Bounded server facts for a future visualization surface. This is not a
/// gameplay packet and does not authorize a client to choose a rewind tick.
/// </summary>
public readonly record struct HistoricalCollisionDiagnostic(
    HistoricalColliderId ColliderId,
    HistoricalColliderKind ColliderKind,
    HistoricalCollisionState State);

/// <summary>
/// Bounded player volume facts used by the server-side lag-compensation
/// diagnostic surface. This is presentation data only; it is never a client
/// authority input.
/// </summary>
public readonly record struct HistoricalPlayerVolumeDiagnostic(
    int Slot,
    bool CanBeHit,
    bool AltForm,
    Vector3 Position,
    Vector3 SpherePosition,
    float SphereRadius,
    float MinPickupHeight,
    float MaxPickupHeight);

/// <summary>
/// Header for one bounded <c>netdebug lagcomp-*</c> snapshot. The projectile
/// path and tick facts are server-selected, while the two span payloads remain
/// bounded by the caller's buffers.
/// </summary>
public readonly record struct HistoricalCollisionDebugFrame(
    uint CurrentTick,
    uint QueryTick,
    uint RewindTicks,
    Vector3 ProjectileStart,
    Vector3 ProjectileEnd,
    bool HistoricalDynamicEnabled,
    bool RegistryOverflowed,
    int PlayerVolumeCount,
    int DynamicColliderCount,
    bool Truncated);
