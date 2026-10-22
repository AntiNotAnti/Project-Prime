using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

// A hosted peer requests discovery only after its scene has cleared old item
// state. Sequence survives scene/session resets; endpoint supplies the slot.
internal readonly record struct PickupReady(ulong Epoch, ushort Match, ushort Generation, uint Sequence)
{
    internal const int Size = 16;
    internal bool IsValid => Epoch != 0 && Match != 0 && Generation != 0 && Sequence != 0;
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid pickup readiness.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Epoch);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], Match);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[10..], Generation);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], Sequence);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out PickupReady ready)
    {
        ready = default;
        if (bytes.Length != Size) return false;
        var value = new PickupReady(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]));
        if (!value.IsValid) return false;
        ready = value; return true;
    }
}

// Protocol 53 pickup value contracts. Claim intake and gameplay settlement are
// separate from these bounded payload codecs.
internal readonly record struct PickupClaimKey(ulong Epoch, ushort Match, int Slot,
    ushort Generation, ushort Life, uint Sequence)
{
    internal const int Size = 19;
    internal ShotKey LifeKey => new(Epoch, Match, Slot, Generation, Life, 0);
    internal bool IsValid => Epoch != 0 && Match != 0 && (uint)Slot < 8
        && Generation != 0 && Life != 0 && Sequence != 0;
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid pickup claim key or exact payload length.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Epoch);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], Match); bytes[10] = (byte)Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[11..], Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[13..], Life);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[15..], Sequence);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out PickupClaimKey key)
    {
        key = default; if (bytes.Length != Size) return false;
        var value = new PickupClaimKey(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), bytes[10],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[11..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[13..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[15..]));
        if (!value.IsValid) return false; key = value; return true;
    }
}

internal readonly record struct PickupClaim(PickupClaimKey Key, PowerUpSpawnKey Spawn,
    TimedPowerUp Type, uint SourceFrame, Vector3 Position)
{
    internal const int Size = 48;
    internal bool IsValid => Key.IsValid && PickupWire.ValidSpawn(Spawn)
        && Spawn.Epoch == Key.Epoch && Spawn.Match == Key.Match
        && (byte)Type <= 2 && PickupWire.ValidPosition(Position);
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid pickup claim or exact payload length.");
        Key.Write(bytes[..PickupClaimKey.Size]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[19..], Spawn.Room);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[23..], Spawn.SpawnId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[27..], Spawn.Generation);
        bytes[31] = (byte)Type;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[32..], SourceFrame);
        PickupWire.WritePosition(bytes[36..], Position);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out PickupClaim claim)
    {
        claim = default; if (bytes.Length != Size) return false;
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        ushort match = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
        var value = new PickupClaim(new(epoch, match, bytes[10],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[11..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[13..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[15..])),
            new(epoch, match, BinaryPrimitives.ReadUInt32LittleEndian(bytes[19..]),
                BinaryPrimitives.ReadInt32LittleEndian(bytes[23..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[27..])),
            (TimedPowerUp)bytes[31], BinaryPrimitives.ReadUInt32LittleEndian(bytes[32..]), PickupWire.ReadPosition(bytes[36..]));
        if (!value.IsValid) return false; claim = value; return true;
    }
}

internal enum PickupResult : byte
{ Grant = 1, Taken, Gone, TooOld, TooFar, Invalid, Replayed, Capacity, HistoryUnavailable }

internal readonly record struct PickupVerdict(PickupClaim Claim, PickupResult Result,
    uint DecisionFrame, uint ProofFrame, ulong GrantSequence, uint GrantFrame, uint ExpiresAtFrame)
{
    internal const int Size = 73;
    internal bool IsValid => Claim.IsValid && (byte)Result is >= 1 and <= 9
        && (Result == PickupResult.Grant
            ? GrantSequence != 0 && ExpiresAtFrame == unchecked(GrantFrame + 1800u)
            : GrantSequence == 0 && GrantFrame == 0 && ExpiresAtFrame == 0);
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid pickup verdict or exact payload length.");
        Claim.Write(bytes[..PickupClaim.Size]); bytes[48] = (byte)Result;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[49..], DecisionFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[53..], ProofFrame);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[57..], GrantSequence);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[65..], GrantFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[69..], ExpiresAtFrame);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out PickupVerdict verdict)
    {
        verdict = default;
        if (bytes.Length != Size || !PickupClaim.TryRead(bytes[..PickupClaim.Size], out var claim)) return false;
        var value = new PickupVerdict(claim, (PickupResult)bytes[48],
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[49..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[53..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[57..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[65..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[69..]));
        if (!value.IsValid) return false; verdict = value; return true;
    }
}

internal readonly record struct PickupItemFact(PowerUpSpawnKey Spawn, TimedPowerUp Type,
    uint AuthorityFrame, bool Available, Vector3 Position)
{
    internal const int Size = 40;
    internal bool IsValid => PickupWire.ValidSpawn(Spawn) && (byte)Type <= 2 && PickupWire.ValidPosition(Position);
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid pickup item fact or exact payload length.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Spawn.Epoch);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], Spawn.Match);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[10..], Spawn.Room);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[14..], Spawn.SpawnId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[18..], Spawn.Generation);
        bytes[22] = (byte)Type;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[23..], AuthorityFrame);
        bytes[27] = Available ? (byte)1 : (byte)0;
        PickupWire.WritePosition(bytes[28..], Position);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out PickupItemFact fact)
    {
        fact = default; if (bytes.Length != Size || bytes[27] > 1) return false;
        var value = new PickupItemFact(new(BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[10..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[14..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[18..])),
            (TimedPowerUp)bytes[22], BinaryPrimitives.ReadUInt32LittleEndian(bytes[23..]), bytes[27] != 0,
            PickupWire.ReadPosition(bytes[28..]));
        if (!value.IsValid) return false; fact = value; return true;
    }
}

internal static class PickupWire
{
    internal static bool ValidPosition(Vector3 position) => Single.IsFinite(position.X)
        && Single.IsFinite(position.Y) && Single.IsFinite(position.Z)
        && Math.Abs(position.X) < 100000 && Math.Abs(position.Y) < 100000 && Math.Abs(position.Z) < 100000;
    internal static bool ValidSpawn(PowerUpSpawnKey spawn) => spawn.Epoch != 0 && spawn.Match != 0
        && spawn.Room != 0 && spawn.Generation != 0 && spawn.SpawnId >= -1;
    internal static void WritePosition(Span<byte> bytes, Vector3 position)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes, position.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], position.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], position.Z);
    }
    internal static Vector3 ReadPosition(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
}
