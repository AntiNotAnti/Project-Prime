using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MphRead.Runtime.HistoricalCollision;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>
/// A server-selected developer diagnostic. This is deliberately a server to
/// client message: there is no request form and therefore no client-selected
/// rewind tick or unrestricted historical query.
/// </summary>
public enum HistoricalCollisionDebugMode : byte
{
    Clear = 0,
    History = 1,
    Dynamic = 2
}

[Flags]
public enum HistoricalCollisionDebugFlags : byte
{
    None = 0,
    HasProjectilePath = 1,
    HistoricalDynamicEnabled = 2,
    RegistryOverflowed = 4,
    Truncated = 8
}

public readonly record struct HistoricalCollisionDebugPlayer(
    byte Slot,
    bool CanBeHit,
    bool AltForm,
    Vector3 Position,
    Vector3 SpherePosition,
    float SphereRadius,
    float MinPickupHeight,
    float MaxPickupHeight);

/// <summary>Only geometry facts needed by the bounded world overlay.</summary>
public readonly record struct HistoricalCollisionDebugCollider(
    HistoricalColliderId Identity,
    HistoricalColliderKind Kind,
    bool Active,
    bool Blocking,
    Vector3 Facing,
    Vector3 Position,
    float RadiusSquared,
    Vector4 Plane,
    Vector3 Up,
    Vector3 Right,
    float Width,
    float Height,
    Vector3 BoundsMin,
    Vector3 BoundsMax);

/// <summary>Bounded monotonic QZ1 counters exported with each host diagnostic.</summary>
public readonly record struct HistoricalCollisionDebugMetrics(
    long DynamicHistoryRecords,
    long DynamicHistoryQueries,
    long DynamicHistoryMissing,
    long HistoricalDoorQueries,
    long HistoricalForceFieldQueries,
    long HistoricalPlatformQueries,
    long HistoricalGeometryChangedOutcome,
    int ClampPositionSamples = 0,
    float ClampPositionErrorP95 = 0,
    float ClampPositionErrorP99 = 0,
    float ClampPositionErrorMax = 0,
    float ClampVerticalErrorP95 = 0,
    float ClampVerticalErrorMax = 0)
{
    public bool IsValid => DynamicHistoryRecords >= 0 && DynamicHistoryQueries >= 0
        && DynamicHistoryMissing >= 0 && HistoricalDoorQueries >= 0
        && HistoricalForceFieldQueries >= 0 && HistoricalPlatformQueries >= 0
        && HistoricalGeometryChangedOutcome >= 0
        && ClampPositionSamples is >= 0 and <= UInt16.MaxValue
        && ValidMetric(ClampPositionErrorP95) && ValidMetric(ClampPositionErrorP99)
        && ValidMetric(ClampPositionErrorMax) && ValidMetric(ClampVerticalErrorP95)
        && ValidMetric(ClampVerticalErrorMax);

    private static bool ValidMetric(float value) => Single.IsFinite(value) && value >= 0;
}

/// <summary>
/// Fixed-shape wire data for QZ1.14. The largest valid payload is below the
/// 1024-byte datagram budget even when all seven dynamic records are present.
/// A history packet carries players; a dynamic packet carries colliders.
/// </summary>
public sealed class HistoricalCollisionDebugPacket
{
    public const int MaxPlayers = 8;
    // Seven leaves room for the F8 clamp headline without increasing the
    // transport MTU. The registry remains complete; this developer overlay
    // packet is explicitly bounded and marks a larger result as truncated.
    public const int MaxColliders = 7;
    private const int HeaderSize = 112;
    private const int PlayerSize = 40;
    private const int ColliderSize = 112;
    public const int MaxSize = HeaderSize + MaxColliders * ColliderSize;

    public uint MatchId { get; }
    public HistoricalCollisionDebugMode Mode { get; }
    public HistoricalCollisionDebugFlags Flags { get; }
    public uint CurrentTick { get; }
    public uint QueryTick { get; }
    public uint RewindTicks { get; }
    public Vector3 ProjectileStart { get; }
    public Vector3 ProjectileEnd { get; }
    public IReadOnlyList<HistoricalCollisionDebugPlayer> Players { get; }
    public IReadOnlyList<HistoricalCollisionDebugCollider> Colliders { get; }
    public HistoricalCollisionDebugMetrics Metrics { get; }

    private HistoricalCollisionDebugPacket(uint matchId, HistoricalCollisionDebugMode mode,
        HistoricalCollisionDebugFlags flags, uint currentTick, uint queryTick, uint rewindTicks,
        Vector3 projectileStart, Vector3 projectileEnd,
        HistoricalCollisionDebugPlayer[] players, HistoricalCollisionDebugCollider[] colliders,
        HistoricalCollisionDebugMetrics metrics)
    {
        MatchId = matchId;
        Mode = mode;
        Flags = flags;
        CurrentTick = currentTick;
        QueryTick = queryTick;
        RewindTicks = rewindTicks;
        ProjectileStart = projectileStart;
        ProjectileEnd = projectileEnd;
        Players = Array.AsReadOnly(players);
        Colliders = Array.AsReadOnly(colliders);
        Metrics = metrics;
    }

    public bool HasProjectilePath => (Flags & HistoricalCollisionDebugFlags.HasProjectilePath) != 0;
    public bool HistoricalDynamicEnabled => (Flags & HistoricalCollisionDebugFlags.HistoricalDynamicEnabled) != 0;
    public bool RegistryOverflowed => (Flags & HistoricalCollisionDebugFlags.RegistryOverflowed) != 0;
    public bool Truncated => (Flags & HistoricalCollisionDebugFlags.Truncated) != 0;
    public bool IsClear => Mode == HistoricalCollisionDebugMode.Clear;

    public static int WriteClear(Span<byte> destination, uint matchId)
    {
        if (matchId == 0) throw new ArgumentOutOfRangeException(nameof(matchId));
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Diagnostic packet destination is too small.", nameof(destination));
        destination[..HeaderSize].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, matchId);
        destination[16] = (byte)HistoricalCollisionDebugMode.Clear;
        return HeaderSize;
    }

    public static int Write(Span<byte> destination, uint matchId,
        HistoricalCollisionDebugMode mode, in HistoricalCollisionDebugFrame frame,
        ReadOnlySpan<HistoricalPlayerVolumeDiagnostic> players,
        ReadOnlySpan<HistoricalCollisionDiagnostic> colliders,
        bool hasProjectilePath,
        HistoricalCollisionDebugMetrics metrics = default)
    {
        if (matchId == 0) throw new ArgumentOutOfRangeException(nameof(matchId));
        if (mode is not (HistoricalCollisionDebugMode.History or HistoricalCollisionDebugMode.Dynamic))
            throw new ArgumentOutOfRangeException(nameof(mode));
        int count = mode == HistoricalCollisionDebugMode.History ? players.Length : colliders.Length;
        if (count > (mode == HistoricalCollisionDebugMode.History ? MaxPlayers : MaxColliders))
            throw new ArgumentOutOfRangeException(nameof(players));
        if (mode == HistoricalCollisionDebugMode.History && colliders.Length != 0
            || mode == HistoricalCollisionDebugMode.Dynamic && players.Length != 0)
            throw new ArgumentException("A diagnostic packet carries only its selected collection.");
        int length = HeaderSize + count * (mode == HistoricalCollisionDebugMode.History ? PlayerSize : ColliderSize);
        if (destination.Length < length) throw new ArgumentException("Diagnostic packet destination is too small.", nameof(destination));
        if (!metrics.IsValid) throw new ArgumentException("Diagnostic metrics must be non-negative.", nameof(metrics));
        EnsureFinite(frame.ProjectileStart, nameof(frame));
        EnsureFinite(frame.ProjectileEnd, nameof(frame));

        BinaryPrimitives.WriteUInt32LittleEndian(destination, matchId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], frame.CurrentTick);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], frame.QueryTick);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], frame.RewindTicks);
        destination[16] = (byte)mode;
        HistoricalCollisionDebugFlags flags = HistoricalCollisionDebugFlags.None;
        if (hasProjectilePath) flags |= HistoricalCollisionDebugFlags.HasProjectilePath;
        if (frame.HistoricalDynamicEnabled) flags |= HistoricalCollisionDebugFlags.HistoricalDynamicEnabled;
        if (frame.RegistryOverflowed) flags |= HistoricalCollisionDebugFlags.RegistryOverflowed;
        if (frame.Truncated || (mode == HistoricalCollisionDebugMode.History
            ? frame.PlayerVolumeCount > count : frame.DynamicColliderCount > count))
            flags |= HistoricalCollisionDebugFlags.Truncated;
        destination[17] = (byte)flags;
        destination[18] = (byte)(mode == HistoricalCollisionDebugMode.History ? count : 0);
        destination[19] = (byte)(mode == HistoricalCollisionDebugMode.Dynamic ? count : 0);
        WriteVector(destination[20..], frame.ProjectileStart);
        WriteVector(destination[32..], frame.ProjectileEnd);
        WriteMetrics(destination[44..], metrics);

        if (mode == HistoricalCollisionDebugMode.History)
        {
            for (int i = 0; i < players.Length; i++)
                WritePlayer(destination.Slice(HeaderSize + i * PlayerSize, PlayerSize), players[i]);
        }
        else
        {
            for (int i = 0; i < colliders.Length; i++)
                WriteCollider(destination.Slice(HeaderSize + i * ColliderSize, ColliderSize), colliders[i]);
        }
        return length;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, uint matchId,
        out HistoricalCollisionDebugPacket packet)
    {
        packet = null!;
        if (source.Length < HeaderSize || matchId == 0
            || BinaryPrimitives.ReadUInt32LittleEndian(source) != matchId
            || (source[17] & ~(byte)(HistoricalCollisionDebugFlags.HasProjectilePath
                | HistoricalCollisionDebugFlags.HistoricalDynamicEnabled
                | HistoricalCollisionDebugFlags.RegistryOverflowed
                | HistoricalCollisionDebugFlags.Truncated)) != 0)
            return false;
        HistoricalCollisionDebugMode mode = (HistoricalCollisionDebugMode)source[16];
        if (mode is not (HistoricalCollisionDebugMode.Clear or HistoricalCollisionDebugMode.History
            or HistoricalCollisionDebugMode.Dynamic)) return false;
        HistoricalCollisionDebugFlags flags = (HistoricalCollisionDebugFlags)source[17];
        int playerCount = source[18];
        int colliderCount = source[19];
        bool clear = mode == HistoricalCollisionDebugMode.Clear;
        if (playerCount > MaxPlayers || colliderCount > MaxColliders
            || clear && (flags != HistoricalCollisionDebugFlags.None || playerCount != 0 || colliderCount != 0)
            || mode == HistoricalCollisionDebugMode.History && colliderCount != 0
            || mode == HistoricalCollisionDebugMode.Dynamic && playerCount != 0)
            return false;
        int count = mode == HistoricalCollisionDebugMode.History ? playerCount
            : mode == HistoricalCollisionDebugMode.Dynamic ? colliderCount : 0;
        int expected = HeaderSize + count * (mode == HistoricalCollisionDebugMode.History ? PlayerSize : ColliderSize);
        if (source.Length != expected)
            return false;
        Vector3 start = ReadVector(source[20..]);
        Vector3 end = ReadVector(source[32..]);
        HistoricalCollisionDebugMetrics metrics = ReadMetrics(source[44..]);
        if (!Finite(start) || !Finite(end) || !metrics.IsValid
            || clear && (BinaryPrimitives.ReadUInt32LittleEndian(source[4..]) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[12..]) != 0
                || start != Vector3.Zero || end != Vector3.Zero || metrics != default)) return false;

        var players = new HistoricalCollisionDebugPlayer[playerCount];
        var colliders = new HistoricalCollisionDebugCollider[colliderCount];
        if (mode == HistoricalCollisionDebugMode.History)
        {
            for (int i = 0; i < playerCount; i++)
                if (!TryReadPlayer(source.Slice(HeaderSize + i * PlayerSize, PlayerSize), out players[i])) return false;
        }
        else
        {
            for (int i = 0; i < colliderCount; i++)
                if (!TryReadCollider(source.Slice(HeaderSize + i * ColliderSize, ColliderSize), out colliders[i])) return false;
        }
        packet = new HistoricalCollisionDebugPacket(matchId, mode, flags,
            BinaryPrimitives.ReadUInt32LittleEndian(source[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[12..]), start, end,
            players, colliders, metrics);
        return true;
    }

    private static void WriteMetrics(Span<byte> destination, HistoricalCollisionDebugMetrics metrics)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination, metrics.DynamicHistoryRecords);
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], metrics.DynamicHistoryQueries);
        BinaryPrimitives.WriteInt64LittleEndian(destination[16..], metrics.DynamicHistoryMissing);
        BinaryPrimitives.WriteInt64LittleEndian(destination[24..], metrics.HistoricalDoorQueries);
        BinaryPrimitives.WriteInt64LittleEndian(destination[32..], metrics.HistoricalForceFieldQueries);
        BinaryPrimitives.WriteInt64LittleEndian(destination[40..], metrics.HistoricalPlatformQueries);
        BinaryPrimitives.WriteInt64LittleEndian(destination[48..], metrics.HistoricalGeometryChangedOutcome);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[56..],
            checked((ushort)metrics.ClampPositionSamples));
        WriteHalf(destination[58..], metrics.ClampPositionErrorP95);
        WriteHalf(destination[60..], metrics.ClampPositionErrorP99);
        WriteHalf(destination[62..], metrics.ClampPositionErrorMax);
        WriteHalf(destination[64..], metrics.ClampVerticalErrorP95);
        WriteHalf(destination[66..], metrics.ClampVerticalErrorMax);
    }

    private static HistoricalCollisionDebugMetrics ReadMetrics(ReadOnlySpan<byte> source)
        => new(BinaryPrimitives.ReadInt64LittleEndian(source),
            BinaryPrimitives.ReadInt64LittleEndian(source[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(source[16..]),
            BinaryPrimitives.ReadInt64LittleEndian(source[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(source[32..]),
            BinaryPrimitives.ReadInt64LittleEndian(source[40..]),
            BinaryPrimitives.ReadInt64LittleEndian(source[48..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[56..]),
            ReadHalf(source[58..]), ReadHalf(source[60..]), ReadHalf(source[62..]),
            ReadHalf(source[64..]), ReadHalf(source[66..]));

    private static void WriteHalf(Span<byte> destination, float value)
    {
        System.Half encoded = (System.Half)MathF.Min(value, (float)System.Half.MaxValue);
        BinaryPrimitives.WriteInt16LittleEndian(destination,
            BitConverter.HalfToInt16Bits(encoded));
    }

    private static float ReadHalf(ReadOnlySpan<byte> source)
        => (float)BitConverter.Int16BitsToHalf(
            BinaryPrimitives.ReadInt16LittleEndian(source));

    private static void WritePlayer(Span<byte> destination, HistoricalPlayerVolumeDiagnostic value)
    {
        if (value.Slot >= 8 || value.SphereRadius < 0 || value.MinPickupHeight > value.MaxPickupHeight)
            throw new ArgumentException("Invalid historical player diagnostic.");
        EnsureFinite(value.Position, nameof(value)); EnsureFinite(value.SpherePosition, nameof(value));
        EnsureFinite(value.SphereRadius, nameof(value)); EnsureFinite(value.MinPickupHeight, nameof(value));
        EnsureFinite(value.MaxPickupHeight, nameof(value));
        destination[0] = (byte)value.Slot;
        destination[1] = (byte)((value.CanBeHit ? 1 : 0) | (value.AltForm ? 2 : 0));
        destination[2] = destination[3] = 0;
        WriteVector(destination[4..], value.Position);
        WriteVector(destination[16..], value.SpherePosition);
        BinaryPrimitives.WriteSingleLittleEndian(destination[28..], value.SphereRadius);
        BinaryPrimitives.WriteSingleLittleEndian(destination[32..], value.MinPickupHeight);
        BinaryPrimitives.WriteSingleLittleEndian(destination[36..], value.MaxPickupHeight);
    }

    private static bool TryReadPlayer(ReadOnlySpan<byte> source, out HistoricalCollisionDebugPlayer value)
    {
        value = default;
        if (source[0] >= 8 || source[1] > 3 || source[2] != 0 || source[3] != 0) return false;
        Vector3 position = ReadVector(source[4..]);
        Vector3 sphere = ReadVector(source[16..]);
        float radius = BinaryPrimitives.ReadSingleLittleEndian(source[28..]);
        float min = BinaryPrimitives.ReadSingleLittleEndian(source[32..]);
        float max = BinaryPrimitives.ReadSingleLittleEndian(source[36..]);
        if (!Finite(position) || !Finite(sphere) || !Finite(radius) || !Finite(min) || !Finite(max)
            || radius < 0 || min > max) return false;
        value = new(source[0], (source[1] & 1) != 0, (source[1] & 2) != 0,
            position, sphere, radius, min, max);
        return true;
    }

    private static void WriteCollider(Span<byte> destination, HistoricalCollisionDiagnostic value)
    {
        HistoricalCollisionState state = value.State;
        if (!value.ColliderId.IsValid || value.ColliderKind is not (HistoricalColliderKind.Door
            or HistoricalColliderKind.ForceField or HistoricalColliderKind.Object or HistoricalColliderKind.Platform))
            throw new ArgumentException("Invalid historical collider diagnostic.");
        destination[0] = (byte)value.ColliderKind;
        destination[1] = (byte)((state.Active ? 1 : 0) | (state.Blocking ? 2 : 0));
        destination[2] = destination[3] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], value.ColliderId.EntityId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], value.ColliderId.Generation);
        WriteVector(destination[12..], state.Facing);
        WriteVector(destination[24..], state.Position);
        BinaryPrimitives.WriteSingleLittleEndian(destination[36..], state.RadiusSquared);
        WriteVector4(destination[40..], state.Plane);
        WriteVector(destination[56..], state.Up);
        WriteVector(destination[68..], state.Right);
        BinaryPrimitives.WriteSingleLittleEndian(destination[80..], state.Width);
        BinaryPrimitives.WriteSingleLittleEndian(destination[84..], state.Height);
        WriteVector(destination[88..], state.BoundsMin);
        WriteVector(destination[100..], state.BoundsMax);
        EnsureFinite(state.Facing, nameof(value)); EnsureFinite(state.Position, nameof(value));
        EnsureFinite(state.RadiusSquared, nameof(value)); EnsureFinite(state.Plane, nameof(value));
        EnsureFinite(state.Up, nameof(value)); EnsureFinite(state.Right, nameof(value));
        EnsureFinite(state.Width, nameof(value)); EnsureFinite(state.Height, nameof(value));
        EnsureFinite(state.BoundsMin, nameof(value)); EnsureFinite(state.BoundsMax, nameof(value));
    }

    private static bool TryReadCollider(ReadOnlySpan<byte> source, out HistoricalCollisionDebugCollider value)
    {
        value = default;
        HistoricalColliderKind kind = (HistoricalColliderKind)source[0];
        if (kind is not (HistoricalColliderKind.Door or HistoricalColliderKind.ForceField
            or HistoricalColliderKind.Object or HistoricalColliderKind.Platform)
            || source[1] > 3 || source[2] != 0 || source[3] != 0) return false;
        int entityId = BinaryPrimitives.ReadInt32LittleEndian(source[4..]);
        uint generation = BinaryPrimitives.ReadUInt32LittleEndian(source[8..]);
        if (entityId < 0 || generation == 0) return false;
        Vector3 facing = ReadVector(source[12..]);
        Vector3 position = ReadVector(source[24..]);
        float radius = BinaryPrimitives.ReadSingleLittleEndian(source[36..]);
        Vector4 plane = ReadVector4(source[40..]);
        Vector3 up = ReadVector(source[56..]);
        Vector3 right = ReadVector(source[68..]);
        float width = BinaryPrimitives.ReadSingleLittleEndian(source[80..]);
        float height = BinaryPrimitives.ReadSingleLittleEndian(source[84..]);
        Vector3 min = ReadVector(source[88..]);
        Vector3 max = ReadVector(source[100..]);
        if (!Finite(facing) || !Finite(position) || !Finite(radius) || !Finite(plane)
            || !Finite(up) || !Finite(right) || !Finite(width) || !Finite(height)
            || !Finite(min) || !Finite(max) || radius < 0 || width < 0 || height < 0) return false;
        value = new(new(entityId, generation), kind, (source[1] & 1) != 0,
            (source[1] & 2) != 0, facing, position, radius, plane, up, right,
            width, height, min, max);
        return true;
    }

    private static void WriteVector(Span<byte> destination, Vector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value.Z);
    }
    private static void WriteVector4(Span<byte> destination, Vector4 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value.Z);
        BinaryPrimitives.WriteSingleLittleEndian(destination[12..], value.W);
    }
    private static Vector3 ReadVector(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadSingleLittleEndian(source), BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(source[8..]));
    private static Vector4 ReadVector4(ReadOnlySpan<byte> source) => new(
        BinaryPrimitives.ReadSingleLittleEndian(source), BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(source[8..]), BinaryPrimitives.ReadSingleLittleEndian(source[12..]));
    private static bool Finite(float value) => float.IsFinite(value);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
    private static void EnsureFinite(Vector3 value, string name) { if (!Finite(value)) throw new ArgumentException("Diagnostic contains a non-finite vector.", name); }
    private static void EnsureFinite(Vector4 value, string name) { if (!Finite(value)) throw new ArgumentException("Diagnostic contains a non-finite vector.", name); }
    private static void EnsureFinite(float value, string name) { if (!float.IsFinite(value)) throw new ArgumentException("Diagnostic contains a non-finite scalar.", name); }
}
