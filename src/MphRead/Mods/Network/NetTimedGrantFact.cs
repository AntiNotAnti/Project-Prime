using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network;

internal readonly record struct TimedGrantFact(ShotKey Life, uint Room, uint Revision,
    TimedPowerUp Type, ulong Sequence, int SpawnId, uint SpawnGeneration,
    uint ContactFrame, uint EndFrame, TimedGrantEnd Reason, uint AuthorityFrame)
{
    internal const int Size = 53;
    // Bit 7 of the reason byte marks a quiet bootstrap/resume; no new fast-lane bytes.
    internal bool Resume { get; init; }
    internal PowerUpSpawnKey Spawn => new(Life.AuthorityEpoch, Life.MatchId, Room, SpawnId, SpawnGeneration);
    private static bool Before(uint a, uint b) => unchecked((int)(a - b)) < 0;
    internal bool IsValid
    {
        get
        {
            if (Life.AuthorityEpoch == 0 || Life.MatchId == 0 || (uint)Life.ShooterSlot >= 8
                || Life.Generation == 0 || Life.LifeId == 0 || Life.ShotId != 0
                || Room == 0 || Revision == 0 || (byte)Type > 2 || (byte)Reason > 6) return false;
            // Explicit absence still requires a complete authority life/scope.
            // Missing packets are never converted to this value.
            if (Sequence == 0) return SpawnId == -1 && SpawnGeneration == 0
                && ContactFrame == 0 && EndFrame == 0 && Reason == TimedGrantEnd.Retired;
            uint duration = unchecked(EndFrame - ContactFrame);
            if (SpawnId < -1 || SpawnGeneration == 0 || duration > 1800
                || (Reason is TimedGrantEnd.Active or TimedGrantEnd.Expired) && duration != 1800
                || Before(AuthorityFrame, ContactFrame)) return false;
            if (Reason != TimedGrantEnd.Active && Before(AuthorityFrame, EndFrame)
                && !(Reason == TimedGrantEnd.Renewed && unchecked(EndFrame - AuthorityFrame) == 1)) return false;
            return true;
        }
    }
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid timed grant fact or exact payload length.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Life.AuthorityEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], Life.MatchId); bytes[10] = (byte)Life.ShooterSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[11..], Life.Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[13..], Life.LifeId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[15..], Room);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[19..], Revision); bytes[23] = (byte)Type;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[24..], Sequence);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[32..], SpawnId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[36..], SpawnGeneration);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[40..], ContactFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[44..], EndFrame); bytes[48] = (byte)((byte)Reason | (Resume ? 0x80 : 0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[49..], AuthorityFrame);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out TimedGrantFact fact)
    {
        fact = default; if (bytes.Length != Size) return false;
        var value = new TimedGrantFact(new(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), bytes[10],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[11..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[13..]), 0),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[15..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[19..]),
            (TimedPowerUp)bytes[23], BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[32..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[36..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..]),
            (TimedGrantEnd)(bytes[48] & 0x7f), BinaryPrimitives.ReadUInt32LittleEndian(bytes[49..]))
            { Resume = (bytes[48] & 0x80) != 0 };
        if (!value.IsValid) return false; fact = value; return true;
    }
}
