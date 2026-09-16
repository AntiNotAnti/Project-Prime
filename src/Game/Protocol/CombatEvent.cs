using System;
using System.Buffers.Binary;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    public readonly record struct CombatActor(byte Slot, ulong ConnectionId, uint Life)
    {
        public static CombatActor None => new(255, 0, 0);
        public bool IsValid => Slot < 8 && ConnectionId != 0 && Life != 0;
        public bool IsNone => this == None;
    }

    public enum CombatEventKind : byte { Shot = 1, Damage, Death, Spawn, Affliction, Bomb, Effect, Impact }
    [Flags]
    public enum CombatEventFlags : ushort
    {
        None = 0, Charged = 1, Headshot = 2, Burn = 4, Deathalt = 8, Silent = 16,
        Affinity = 32, Concussive = 64, OverchargeAbsorb = 128, LingeringHeat = 256,
        NoSplat = 512
    }

    /// <summary>Presentation facts only. State snapshots remain the source of health, ammo and score.</summary>
    public readonly record struct CombatEvent(uint Id, uint Tick, uint CommandSequence, CombatEventKind Kind,
        byte Weapon, CombatEventFlags Flags, CombatActor Actor, CombatActor Target, ushort Health,
        ushort Amount, Vector3 Position, Vector3 Direction, ushort FrozenTicks, ushort BurnTicks, ushort DisruptTicks, ushort ChargeLevel = 0, uint SpreadSeed = 0)
    {
        public const int Size = 82;
        public bool IsValid => Kind >= CombatEventKind.Shot && Kind <= CombatEventKind.Impact
            && (Flags & ~(CombatEventFlags)1023) == 0
            && (Actor.IsValid || Actor.IsNone) && (Target.IsValid || Target.IsNone)
            && (Kind is CombatEventKind.Shot or CombatEventKind.Bomb or CombatEventKind.Effect or CombatEventKind.Impact
                ? Actor.IsValid : Target.IsValid)
            && (Kind != CombatEventKind.Effect || Actor.IsValid && Target.IsNone
                && Weapon == (byte)BeamType.Magmaul && Flags == CombatEventFlags.LingeringHeat
                && Health == 0 && Amount == 0 && FrozenTicks == 0 && BurnTicks == 0
                && DisruptTicks == 0 && ChargeLevel == 0 && SpreadSeed == 0
                && Direction == Vector3.Zero)
            && (Kind != CombatEventKind.Impact || Actor.IsValid && Target.IsNone
                && Weapon <= 10 && Amount < byte.MaxValue
                && (Flags & ~(CombatEventFlags.Charged | CombatEventFlags.Affinity
                    | CombatEventFlags.NoSplat)) == 0
                && Health == 0 && FrozenTicks == 0 && BurnTicks == 0
                && DisruptTicks == 0 && ChargeLevel == 0 && SpreadSeed == 0
                && Single.IsFinite(Direction.LengthSquared)
                && Direction.LengthSquared > 0.000001f)
            && ((Flags & CombatEventFlags.LingeringHeat) == 0 || Kind == CombatEventKind.Effect)
            && ((Flags & CombatEventFlags.NoSplat) == 0 || Kind == CombatEventKind.Impact)
            && ((Flags & (CombatEventFlags.Concussive | CombatEventFlags.OverchargeAbsorb)) == 0
                || Kind is CombatEventKind.Damage or CombatEventKind.Death)
            && (Kind == CombatEventKind.Bomb ? Weapon <= 2 : Weapon <= 10 || Weapon == 255)
            && (Kind == CombatEventKind.Shot || SpreadSeed == 0)
            && Finite(Position) && Finite(Direction);
        private static bool Finite(Vector3 v) => Single.IsFinite(v.X) && Single.IsFinite(v.Y) && Single.IsFinite(v.Z);
        public void Write(Span<byte> bytes)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Id);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], Tick);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], CommandSequence);
            bytes[12] = (byte)Kind; bytes[13] = Weapon;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[14..], (ushort)Flags);
            WriteActor(bytes[16..], Actor); WriteActor(bytes[29..], Target);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[42..], Health);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[44..], Amount);
            WriteVector(bytes[46..], Position); WriteVector(bytes[58..], Direction);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[70..], FrozenTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[72..], BurnTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[74..], DisruptTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes[76..], ChargeLevel);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[78..], SpreadSeed);
        }
        public static bool TryRead(ReadOnlySpan<byte> bytes, out CombatEvent value)
        {
            value = default;
            if (bytes.Length != Size) return false;
            value = new(BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]), (CombatEventKind)bytes[12], bytes[13],
                (CombatEventFlags)BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]), ReadActor(bytes[16..]), ReadActor(bytes[29..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[42..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[44..]),
                ReadVector(bytes[46..]), ReadVector(bytes[58..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[70..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[72..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[74..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[76..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[78..]));
            return value.IsValid;
        }
        private static void WriteActor(Span<byte> bytes, CombatActor actor)
        {
            bytes[0] = actor.Slot;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[1..], actor.ConnectionId);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[9..], actor.Life);
        }
        private static CombatActor ReadActor(ReadOnlySpan<byte> bytes) => new(bytes[0],
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[1..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[9..]));
        private static void WriteVector(Span<byte> bytes, Vector3 vector)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes, vector.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], vector.Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], vector.Z);
        }
        private static Vector3 ReadVector(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadSingleLittleEndian(bytes),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
    }

    public static class CombatEventBatch
    {
        public const int MaxCount = 6;
        public const int MaxSize = 1 + MaxCount * CombatEvent.Size;
        public static int Write(Span<byte> bytes, ReadOnlySpan<CombatEvent> events)
        {
            if (events.Length is < 1 or > MaxCount) throw new ArgumentOutOfRangeException(nameof(events));
            bytes[0] = (byte)events.Length;
            for (int i = 0; i < events.Length; i++) events[i].Write(bytes[(1 + i * CombatEvent.Size)..]);
            return 1 + events.Length * CombatEvent.Size;
        }
        public static bool TryRead(ReadOnlySpan<byte> bytes, Span<CombatEvent> events, out int count)
        {
            count = 0;
            if (bytes.IsEmpty || bytes[0] is < 1 or > MaxCount || events.Length < bytes[0]
                || bytes.Length != 1 + bytes[0] * CombatEvent.Size) return false;
            // Validate the entire packet before writing any result used by presentation.
            for (int i = 0; i < bytes[0]; i++)
                if (!CombatEvent.TryRead(bytes.Slice(1 + i * CombatEvent.Size, CombatEvent.Size), out _)) return false;
            count = bytes[0];
            for (int i = 0; i < count; i++)
            {
                CombatEvent.TryRead(bytes.Slice(1 + i * CombatEvent.Size, CombatEvent.Size), out CombatEvent value);
                events[i] = value;
            }
            return true;
        }
    }
}
