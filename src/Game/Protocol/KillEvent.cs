using System;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace MphRead.Mods.Network
{
    public enum KillSourceKind : byte { Beam = 0, Bomb = 1, Alt = 2, Environment = 3 }
    [Flags]
    public enum KillEventFlags : byte { None = 0, Headshot = 1, Burn = 2, Deathalt = 4, Suicide = 8, TeamKill = 16, Affinity = 32 }

    public readonly record struct KillEvent(uint Id, uint Tick, uint MatchId, uint PhaseRevision,
        CombatActor Killer, CombatActor Victim, byte Weapon, KillEventFlags Flags,
        ImmutableArray<CombatActor> Assists, KillSourceKind SourceKind = KillSourceKind.Beam)
    {
        public const int Size = 137;
        public bool IsSuicide => Killer.IsValid && Killer.Slot == Victim.Slot
            && Killer.ConnectionId == Victim.ConnectionId;
        public bool IsValid
        {
            get
            {
                if (MatchId == 0 || PhaseRevision == 0 || !Victim.IsValid || (!Killer.IsValid && !Killer.IsNone)
                    || (Weapon > 10 && Weapon != 255) || SourceKind > KillSourceKind.Environment
                    || (SourceKind == KillSourceKind.Beam ? Weapon == 255 : Weapon != 255) || (Flags & ~(KillEventFlags)63) != 0
                    || Assists.IsDefault || Assists.Length > 7
                    || ((Flags & KillEventFlags.Suicide) != 0) != IsSuicide
                    || ((Flags & KillEventFlags.TeamKill) != 0 && (!Killer.IsValid || IsSuicide))
                    || ((!Killer.IsValid || IsSuicide || (Flags & KillEventFlags.TeamKill) != 0) && Assists.Length != 0)) return false;
                int slots = 0;
                foreach (CombatActor actor in Assists)
                {
                    if (!actor.IsValid || actor.Slot == Killer.Slot || actor.Slot == Victim.Slot
                        || (slots & (1 << actor.Slot)) != 0) return false;
                    slots |= 1 << actor.Slot;
                }
                return true;
            }
        }
        public void Write(Span<byte> bytes)
        {
            if (bytes.Length != Size || !IsValid) throw new ArgumentException("Invalid kill event.");
            bytes.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Id);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], Tick);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], PhaseRevision);
            WriteActor(bytes[16..], Killer); WriteActor(bytes[29..], Victim);
            bytes[42] = Weapon; bytes[43] = (byte)Flags; bytes[44] = (byte)Assists.Length; bytes[45] = (byte)SourceKind;
            for (int i = 0; i < 7; i++) WriteActor(bytes[(46 + i * 13)..], i < Assists.Length ? Assists[i] : CombatActor.None);
        }
        public static bool TryRead(ReadOnlySpan<byte> bytes, out KillEvent value)
        {
            value = default;
            if (bytes.Length != Size || bytes[44] > 7 || bytes[45] > (byte)KillSourceKind.Environment) return false;
            var assists = ImmutableArray.CreateBuilder<CombatActor>(bytes[44]);
            for (int i = 0; i < 7; i++)
            {
                CombatActor actor = ReadActor(bytes[(46 + i * 13)..]);
                if (i < bytes[44]) assists.Add(actor);
                else if (!actor.IsNone) return false;
            }
            var parsed = new KillEvent(BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]), ReadActor(bytes[16..]), ReadActor(bytes[29..]),
                bytes[42], (KillEventFlags)bytes[43], assists.MoveToImmutable(), (KillSourceKind)bytes[45]);
            if (!parsed.IsValid) return false;
            value = parsed; return true;
        }
        private static void WriteActor(Span<byte> bytes, CombatActor actor)
        {
            bytes[0] = actor.Slot;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[1..], actor.ConnectionId);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[9..], actor.Life);
        }
        private static CombatActor ReadActor(ReadOnlySpan<byte> bytes) => new(bytes[0],
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[1..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[9..]));
    }
}
