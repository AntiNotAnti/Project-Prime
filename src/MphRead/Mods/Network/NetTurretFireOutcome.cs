using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal enum TurretFireOutcomeStatus : byte { Emitted = 1, Refused = 2 }
internal enum TurretFireOutcomeReason : byte
{
    None = 0, RecoveryExpired = 1, OwnerRetired = 2, TurretDestroyed = 3, Capacity = 4
}

/// <summary>Exact source identity and authority result; source and authority clocks remain separate.</summary>
internal readonly record struct TurretFireOutcome(ShotKey Key, uint AuthorityFrame,
    TurretFireOutcomeStatus Status, TurretFireOutcomeReason Reason, FireEvent Fire,
    Vector3 EmissionOrigin, Vector3 EmissionDirection)
{
    internal const int Size = 123;
    private const int FireOffset = 25, OriginOffset = 99, DirectionOffset = 111;

    private bool Valid()
    {
        if (Key.AuthorityEpoch == 0 || Key.MatchId == 0 || (uint)Key.ShooterSlot >= 8
            || Key.Generation == 0 || Key.LifeId == 0 || Key.ShotId == 0
            || Fire.ShotId != Key.ShotId || Fire.SourceLife != Key.LifeId
            || Fire.Kind != FireEventKind.TurretFire || Fire.Weapon != (byte)BeamType.Battlehammer
            || Fire.Charge != 0 || !Fire.HasPose) return false;
        var intent = new IntentPacket { Frame = Fire.SourceFrame, HasFireEvents = true, FireEventCount = 1 };
        intent.FireEvents[0] = Fire;
        if (!NetFireEvents.Validate(intent)) return false;
        if (Status == TurretFireOutcomeStatus.Refused)
            return (byte)Reason is >= 1 and <= 4
                && EmissionOrigin == Vector3.Zero && EmissionDirection == Vector3.Zero;
        return Status == TurretFireOutcomeStatus.Emitted && Reason == TurretFireOutcomeReason.None
            && Finite(EmissionOrigin) && Math.Abs(EmissionOrigin.X) < 100000
            && Math.Abs(EmissionOrigin.Y) < 100000 && Math.Abs(EmissionOrigin.Z) < 100000
            && Finite(EmissionDirection) && Math.Abs(EmissionDirection.LengthSquared - 1) < .05f;
    }

    private static bool Finite(Vector3 vector)
        => Single.IsFinite(vector.X) && Single.IsFinite(vector.Y) && Single.IsFinite(vector.Z);
    private static void WriteVector(Span<byte> bytes, Vector3 vector)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes, vector.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], vector.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], vector.Z);
    }
    private static Vector3 ReadVector(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));

    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length != Size) throw new ArgumentException("Turret outcome requires exact payload length.", nameof(bytes));
        if (!Valid()) throw new ArgumentException("Invalid turret fire outcome.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Key.AuthorityEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], Key.MatchId);
        bytes[10] = (byte)Key.ShooterSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[11..], Key.Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[13..], Key.LifeId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[15..], Key.ShotId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[19..], AuthorityFrame);
        bytes[23] = (byte)Status; bytes[24] = (byte)Reason;
        Fire.Write(bytes.Slice(FireOffset, FireEvent.Size));
        WriteVector(bytes.Slice(OriginOffset, 12), EmissionOrigin);
        WriteVector(bytes.Slice(DirectionOffset, 12), EmissionDirection);
    }

    internal static bool TryRead(ReadOnlySpan<byte> bytes, out TurretFireOutcome outcome)
    {
        outcome = default;
        if (bytes.Length != Size) return false;
        var key = new ShotKey(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), bytes[10],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[11..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[13..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[15..]));
        var decoded = new TurretFireOutcome(key, BinaryPrimitives.ReadUInt32LittleEndian(bytes[19..]),
            (TurretFireOutcomeStatus)bytes[23], (TurretFireOutcomeReason)bytes[24],
            FireEvent.Read(bytes.Slice(FireOffset, FireEvent.Size)),
            ReadVector(bytes.Slice(OriginOffset, 12)), ReadVector(bytes.Slice(DirectionOffset, 12)));
        if (!decoded.Valid()) return false;
        outcome = decoded;
        return true;
    }
}
