using System;
using System.Buffers.Binary;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal enum ImpactProvenance : byte { NativePoint = 1, AcceptedClaimPoint = 2, ConfirmedBodyAnchor = 3 }

// ShotId/component zero explicitly means no projectile identity (e.g. native
// alt contact). It can produce an endpoint cue, never a projectile association.
internal readonly record struct ConfirmedImpact(ShotKey Key, uint Component, uint Phase,
    byte Victim, ushort VictimGeneration, ushort VictimLife, ushort DamageEvent,
    uint Frame, byte Weapon, ReplayShotFactFlags Flags, ImpactProvenance Provenance, Vector3 Point)
{
    internal const int Size = 53;
    internal bool Valid => Key.AuthorityEpoch != 0 && Key.MatchId != 0 && Key.ShooterSlot is >= 0 and < 8
        && Key.Generation != 0 && Key.LifeId != 0 && Victim < 8 && VictimGeneration != 0 && VictimLife != 0
        && DamageEvent != 0 && (Weapon <= (byte)BeamType.OmegaCannon || Weapon == 255)
        && Provenance is >= ImpactProvenance.NativePoint and <= ImpactProvenance.ConfirmedBodyAnchor
        && (Key.ShotId != 0 || Component == 0 && Provenance == ImpactProvenance.ConfirmedBodyAnchor)
        && ((Flags & ReplayShotFactFlags.Continuous) != 0 || Phase == 0)
        && float.IsFinite(Point.X) && float.IsFinite(Point.Y) && float.IsFinite(Point.Z)
        && Math.Abs(Point.X) < 100000 && Math.Abs(Point.Y) < 100000 && Math.Abs(Point.Z) < 100000;
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length < Size) throw new ArgumentException("Impact destination too small.");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, Key.AuthorityEpoch);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[8..], Key.MatchId); bytes[10] = (byte)Key.ShooterSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[11..], Key.Generation);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[13..], Key.LifeId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[15..], Key.ShotId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[19..], Component);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[23..], Phase); bytes[27] = Victim;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[28..], VictimGeneration);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[30..], VictimLife);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[32..], DamageEvent);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[34..], Frame);
        bytes[38] = Weapon; bytes[39] = (byte)Flags; bytes[40] = (byte)Provenance;
        BinaryPrimitives.WriteSingleLittleEndian(bytes[41..], Point.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[45..], Point.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[49..], Point.Z);
    }
    internal static bool TryRead(ReadOnlySpan<byte> b, out ConfirmedImpact fact)
    {
        fact = default;
        if (b.Length != Size) return false;
        fact = new(new(BinaryPrimitives.ReadUInt64LittleEndian(b), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]),
            b[10], BinaryPrimitives.ReadUInt16LittleEndian(b[11..]), BinaryPrimitives.ReadUInt16LittleEndian(b[13..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[15..])), BinaryPrimitives.ReadUInt32LittleEndian(b[19..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[23..]), b[27], BinaryPrimitives.ReadUInt16LittleEndian(b[28..]),
            BinaryPrimitives.ReadUInt16LittleEndian(b[30..]), BinaryPrimitives.ReadUInt16LittleEndian(b[32..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[34..]), b[38], (ReplayShotFactFlags)b[39], (ImpactProvenance)b[40],
            new(BinaryPrimitives.ReadSingleLittleEndian(b[41..]), BinaryPrimitives.ReadSingleLittleEndian(b[45..]),
                BinaryPrimitives.ReadSingleLittleEndian(b[49..])));
        return fact.Valid;
    }
}
