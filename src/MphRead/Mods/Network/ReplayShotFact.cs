using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

[Flags]
internal enum ReplayShotFactFlags : byte
{
    None = 0,
    Headshot = 1 << 0,
    Lethal = 1 << 1,
    Direct = 1 << 2,
    Claimed = 1 << 3,
    Turret = 1 << 4,
    Continuous = 1 << 5,
    EnhancedChild = 1 << 6
}

/// <summary>
/// Authority-only answer to "what did this shot actually resolve as?".
///
/// FireEvent describes the authored trigger/pose. This fact describes the
/// accepted result after lag compensation, claims, damage modifiers and
/// lifecycle validation. The pair is keyed by shooter identity + ShotId.
/// DamageEventId also joins the fact to the authoritative snapshot history.
/// </summary>
internal readonly record struct ReplayShotFact(
    ushort MatchId,
    ulong AuthorityEpoch,
    uint ResolveTick,
    uint LaunchFrame,
    uint ShotId,
    ushort DamageEventId,
    byte ShooterSlot,
    ushort ShooterGeneration,
    ushort ShooterLifeId,
    byte VictimSlot,
    ushort VictimGeneration,
    ushort VictimLifeId,
    byte Weapon,
    ReplayShotFactFlags Flags,
    uint Damage,
    ushort HealthAfter,
    Vector3 ImpactPoint)
{
    internal bool Headshot => (Flags & ReplayShotFactFlags.Headshot) != 0;
    internal bool Lethal => (Flags & ReplayShotFactFlags.Lethal) != 0;
    internal bool Direct => (Flags & ReplayShotFactFlags.Direct) != 0;
}

internal static class ReplayShotFactPacket
{
    internal const byte Version = 1;
    internal const int Size = 55;

    internal static void Write(in ReplayShotFact fact, Span<byte> dest)
    {
        if (dest.Length < Size) throw new ArgumentException("Replay shot fact destination is too small.", nameof(dest));
        dest[0] = Version;
        BinaryPrimitives.WriteUInt16LittleEndian(dest[1..], fact.MatchId);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[3..], fact.AuthorityEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[11..], fact.ResolveTick);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[15..], fact.LaunchFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[19..], fact.ShotId);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[23..], fact.DamageEventId);
        dest[25] = fact.ShooterSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(dest[26..], fact.ShooterGeneration);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[28..], fact.ShooterLifeId);
        dest[30] = fact.VictimSlot;
        BinaryPrimitives.WriteUInt16LittleEndian(dest[31..], fact.VictimGeneration);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[33..], fact.VictimLifeId);
        dest[35] = fact.Weapon;
        dest[36] = (byte)fact.Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(dest[37..], fact.Damage);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[41..], fact.HealthAfter);
        BinaryPrimitives.WriteSingleLittleEndian(dest[43..], fact.ImpactPoint.X);
        BinaryPrimitives.WriteSingleLittleEndian(dest[47..], fact.ImpactPoint.Y);
        BinaryPrimitives.WriteSingleLittleEndian(dest[51..], fact.ImpactPoint.Z);
    }

    internal static bool TryRead(ReadOnlySpan<byte> src, out ReplayShotFact fact)
    {
        fact = default;
        if (src.Length != Size || src[0] != Version) return false;

        var flags = (ReplayShotFactFlags)src[36];
        const ReplayShotFactFlags known = ReplayShotFactFlags.Headshot | ReplayShotFactFlags.Lethal
            | ReplayShotFactFlags.Direct | ReplayShotFactFlags.Claimed | ReplayShotFactFlags.Turret
            | ReplayShotFactFlags.Continuous | ReplayShotFactFlags.EnhancedChild;
        if ((flags & ~known) != 0) return false;

        Vector3 point = new(
            BinaryPrimitives.ReadSingleLittleEndian(src[43..]),
            BinaryPrimitives.ReadSingleLittleEndian(src[47..]),
            BinaryPrimitives.ReadSingleLittleEndian(src[51..]));
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) return false;

        ushort match = BinaryPrimitives.ReadUInt16LittleEndian(src[1..]);
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(src[3..]);
        uint tick = BinaryPrimitives.ReadUInt32LittleEndian(src[11..]);
        uint launch = BinaryPrimitives.ReadUInt32LittleEndian(src[15..]);
        uint shot = BinaryPrimitives.ReadUInt32LittleEndian(src[19..]);
        ushort damageEvent = BinaryPrimitives.ReadUInt16LittleEndian(src[23..]);
        byte shooter = src[25], victim = src[30], weapon = src[35];
        ushort shooterGeneration = BinaryPrimitives.ReadUInt16LittleEndian(src[26..]);
        ushort shooterLife = BinaryPrimitives.ReadUInt16LittleEndian(src[28..]);
        ushort victimGeneration = BinaryPrimitives.ReadUInt16LittleEndian(src[31..]);
        ushort victimLife = BinaryPrimitives.ReadUInt16LittleEndian(src[33..]);
        uint damage = BinaryPrimitives.ReadUInt32LittleEndian(src[37..]);
        ushort healthAfter = BinaryPrimitives.ReadUInt16LittleEndian(src[41..]);

        if (match == 0 || epoch == 0 || shot == 0 || damageEvent == 0
            || shooter >= 8 || victim >= 8 || shooterGeneration == 0 || shooterLife == 0
            || victimGeneration == 0 || victimLife == 0 || weapon > (byte)BeamType.OmegaCannon
            || damage == 0 || (flags & ReplayShotFactFlags.Lethal) != 0 && healthAfter != 0)
        {
            return false;
        }

        fact = new(match, epoch, tick, launch, shot, damageEvent, shooter,
            shooterGeneration, shooterLife, victim, victimGeneration, victimLife,
            weapon, flags, damage, healthAfter, point);
        return true;
    }
}
