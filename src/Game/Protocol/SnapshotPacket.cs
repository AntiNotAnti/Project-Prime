using System;
using System.Buffers.Binary;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    [Flags]
    public enum SnapshotPlayerFlags : ushort
    {
        None = 0,
        Active = 1,
        Spawned = 2,
        AltForm = 4,
        Morphing = 8,
        Unmorphing = 16,
        Frozen = 32,
        Spectating = 64,
        Grounded = 128,
        Burning = 256,
        Disrupted = 512,
        Zoomed = 1024,
        RadarReveal = 2048,
        RadarRevealPrevious = 4096,
        WaitingForMatch = 8192,
        AltAttack = 16384,
        // Compatibility alias for protocol-21 callers and old replay tests.
        // The bit value is unchanged; the semantic is no longer Spire-only.
        [Obsolete("Use AltAttack; the wire bit is shared by all alt attacks.")]
        SpireAltAttack = AltAttack,
        Cloaking = 32768,
        All = 65535
    }

    /// <summary>
    /// The authoritative phase of an alternate-form action. Recovery is a
    /// wire-level phase reserved for a future authored action; protocol 25
    /// does not synthesize recovery timing for any Hunter.
    /// </summary>
    public enum AltActionPhase : byte
    {
        None = 0,
        Charging = 1,
        Active = 2,
        Recovery = 3
    }

    /// <summary>
    /// An alternate-form action phase and its elapsed authoritative simulation
    /// ticks. <see cref="Ticks"/> is measured from the beginning of the
    /// current phase, not from the beginning of the action.
    /// </summary>
    public readonly record struct AltActionState(AltActionPhase Phase,
        ushort Ticks)
    {
        public static AltActionState None => new(AltActionPhase.None, 0);

        /// <summary>Hunters for which protocol 25 defines an alt-action state.</summary>
        public static bool SupportsHunter(Hunter hunter)
            => hunter is Hunter.Trace or Hunter.Weavel or Hunter.Spire
                or Hunter.Guardian or Hunter.Noxus;

        /// <summary>
        /// Legacy snapshots carried only the compatibility active bit. These
        /// four Hunters can safely map that bit to an Active phase at tick 0;
        /// Noxus startup timing was not present in the legacy wire format.
        /// </summary>
        public static bool SupportsLegacyActive(Hunter hunter)
            => hunter is Hunter.Trace or Hunter.Weavel or Hunter.Spire
                or Hunter.Guardian;

        public static bool IsValid(AltActionPhase phase)
            => phase <= AltActionPhase.Recovery;

        /// <summary>
        /// Validate a protocol-25 state against the surrounding snapshot
        /// facts. A non-empty phase and the compatibility bit must agree.
        /// </summary>
        public static bool IsValidFor(Hunter hunter, bool alive, bool altForm,
            SnapshotPlayerFlags flags, AltActionState state)
        {
            if (!IsValid(state.Phase)) return false;
            bool activeBit = (flags & SnapshotPlayerFlags.AltAttack) != 0;
            if (state.Phase == AltActionPhase.None)
            {
                return state.Ticks == 0 && !activeBit;
            }
            return alive && altForm && SupportsHunter(hunter) && activeBit;
        }

        public static AltActionState FromLegacy(Hunter hunter,
            SnapshotPlayerFlags flags)
            => (flags & SnapshotPlayerFlags.AltAttack) != 0
                && SupportsLegacyActive(hunter)
                ? new(AltActionPhase.Active, 0) : None;
    }

    public struct SnapshotPlayer
    {
        public const int LegacySize = 112;
        public const int Size = 115;
        public byte Slot;
        public Hunter Hunter;
        public byte TeamIndex;
        public byte Weapon;
        public SnapshotPlayerFlags Flags;
        public ushort Health;
        public ushort AmmoUa;
        public ushort AmmoMissiles;
        public uint Life;
        public ulong ConnectionId;
        public Vector3 Position;
        public Vector3 Speed;
        public Vector3 Aim;
        public Vector3 Facing;
        public int Points;
        public int Kills;
        public int Deaths;
        public ushort AvailableWeapons;
        public ushort FrozenTicks;
        public ushort BurnTicks;
        public ushort DisruptTicks;
        public int Assists;
        /// <summary>
        /// Authoritative weapon charge in 60 Hz simulation ticks. Remote
        /// presentation cannot reconstruct this from intermittent snapshots:
        /// without it, every non-local gun remains visually uncharged.
        /// </summary>
        public ushort ChargeLevel;
        public ushort DoubleDamageTicks;
        public ushort CloakTicks;
        public ushort DeathaltTicks;
        public byte EnhancedTargetSlot;
        public byte Overcharge;
        public ushort EnhancedTargetTicks;
        public ushort ChilledTicks;
        public ushort CloakFadeTicks;
        /// <summary>
        /// Protocol-25 authoritative alternate-form action state. Protocols
        /// through 24 end at <see cref="LegacySize"/> and are decoded through
        /// their frozen replay wrappers.
        /// </summary>
        public AltActionState AltAction;

        public readonly void Write(Span<byte> destination)
        {
            if (destination.Length != Size) throw new ArgumentException("Snapshot player requires exactly 115 bytes.", nameof(destination));
            // Existing callers that construct a snapshot with the compatibility
            // bit but predate the phase field still get a canonical protocol-25
            // record. Network readers remain strict; this normalization is only
            // a writer-side source-compatibility bridge.
            SnapshotPlayerFlags flags = Flags;
            AltActionState action = AltAction;
            if (action.Phase == AltActionPhase.None && action.Ticks == 0
                && (flags & SnapshotPlayerFlags.AltAttack) != 0)
            {
                action = new AltActionState(AltActionPhase.Active, 0);
            }
            if (action.Phase != AltActionPhase.None)
            {
                flags |= SnapshotPlayerFlags.AltAttack;
            }
            else
            {
                flags &= ~SnapshotPlayerFlags.AltAttack;
            }
            destination[0] = Slot;
            destination[1] = (byte)Hunter;
            destination[2] = TeamIndex;
            destination[3] = Weapon;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)flags);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], Health);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], AmmoUa);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[10..], AmmoMissiles);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], Life);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], ConnectionId);
            WriteVector(destination[24..], Position);
            WriteVector(destination[36..], Speed);
            WriteVector(destination[48..], Aim);
            WriteVector(destination[60..], Facing);
            BinaryPrimitives.WriteInt32LittleEndian(destination[72..], Points);
            BinaryPrimitives.WriteInt32LittleEndian(destination[76..], Kills);
            BinaryPrimitives.WriteInt32LittleEndian(destination[80..], Deaths);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[84..], AvailableWeapons);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[86..], FrozenTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[88..], BurnTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[90..], DisruptTicks);
            BinaryPrimitives.WriteInt32LittleEndian(destination[92..], Assists);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[96..], ChargeLevel);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[98..], DoubleDamageTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[100..], CloakTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[102..], DeathaltTicks);
            destination[104] = EnhancedTargetTicks == 0 ? (byte)255 : EnhancedTargetSlot;
            destination[105] = Overcharge;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[106..], EnhancedTargetTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[108..], ChilledTicks);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[110..], CloakFadeTicks);
            destination[112] = (byte)action.Phase;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[113..], action.Ticks);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out SnapshotPlayer player)
        {
            player = default;
            if (source.Length != Size || source[0] >= 8
                || !PlayableHunterCatalog.IsPlayable((Hunter)source[1])
                || source[2] >= 8 || source[3] > 8
                || (BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & ~(ushort)SnapshotPlayerFlags.All) != 0
                || BinaryPrimitives.ReadUInt64LittleEndian(source[16..]) == 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[76..]) < 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[80..]) < 0
                || BinaryPrimitives.ReadInt32LittleEndian(source[92..]) < 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[12..]) == 0
                || ((BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & (ushort)SnapshotPlayerFlags.WaitingForMatch) != 0
                    && ((BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & (ushort)(SnapshotPlayerFlags.Active | SnapshotPlayerFlags.Spawned)) != 0
                        || (BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & (ushort)SnapshotPlayerFlags.Spectating) == 0
                        || BinaryPrimitives.ReadUInt16LittleEndian(source[6..]) != 0))
                || (((SnapshotPlayerFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & SnapshotPlayerFlags.Burning) != 0) != (BinaryPrimitives.ReadUInt16LittleEndian(source[88..]) > 0)
                || (((SnapshotPlayerFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & SnapshotPlayerFlags.Disrupted) != 0) != (BinaryPrimitives.ReadUInt16LittleEndian(source[90..]) > 0)
                || (((SnapshotPlayerFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) & SnapshotPlayerFlags.Cloaking) != 0
                    && BinaryPrimitives.ReadUInt16LittleEndian(source[100..]) == 0)
                || (BinaryPrimitives.ReadUInt16LittleEndian(source[84..]) & ~0x1FF) != 0
                || source[104] != 255 && source[104] >= 8
                || (source[104] == 255) != (BinaryPrimitives.ReadUInt16LittleEndian(source[106..]) == 0)
                || BinaryPrimitives.ReadUInt16LittleEndian(source[106..]) > EnhancedHunterTuning.SamusTargetLockTicks
                || (source[1] != (byte)Hunter.Samus && source[1] != (byte)Hunter.Kanden)
                    && (source[104] != 255 || BinaryPrimitives.ReadUInt16LittleEndian(source[106..]) != 0)
                || source[1] != (byte)Hunter.Sylux && source[105] != 0
                || source[105] > 12
                || BinaryPrimitives.ReadUInt16LittleEndian(source[108..]) > 90
                || source[1] != (byte)Hunter.Trace && BinaryPrimitives.ReadUInt16LittleEndian(source[110..]) != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(source[110..]) > 30
                || !AltActionState.IsValid((AltActionPhase)source[112]))
            {
                return false;
            }
            Vector3 position = ReadVector(source[24..]);
            Vector3 speed = ReadVector(source[36..]);
            Vector3 aim = ReadVector(source[48..]);
            Vector3 facing = ReadVector(source[60..]);
            SnapshotPlayerFlags flags = (SnapshotPlayerFlags)
                BinaryPrimitives.ReadUInt16LittleEndian(source[4..]);
            AltActionState altAction = new((AltActionPhase)source[112],
                BinaryPrimitives.ReadUInt16LittleEndian(source[113..]));
            bool alive = flags.TestFlag(SnapshotPlayerFlags.Spawned)
                && BinaryPrimitives.ReadUInt16LittleEndian(source[6..]) > 0;
            bool altForm = flags.TestFlag(SnapshotPlayerFlags.AltForm);
            if (!AltActionState.IsValidFor((Hunter)source[1], alive, altForm,
                flags, altAction))
            {
                return false;
            }
            if (!Finite(position) || !Finite(speed) || !Finite(aim) || !Finite(facing)
                || aim.LengthSquared < 0.5f || aim.LengthSquared > 1.5f
                || facing.LengthSquared < 0.5f || facing.LengthSquared > 1.5f)
            {
                return false;
            }
            player = new SnapshotPlayer
            {
                Slot = source[0], Hunter = (Hunter)source[1], TeamIndex = source[2], Weapon = source[3],
                Flags = flags,
                Health = BinaryPrimitives.ReadUInt16LittleEndian(source[6..]),
                AmmoUa = BinaryPrimitives.ReadUInt16LittleEndian(source[8..]),
                AmmoMissiles = BinaryPrimitives.ReadUInt16LittleEndian(source[10..]),
                Life = BinaryPrimitives.ReadUInt32LittleEndian(source[12..]),
                ConnectionId = BinaryPrimitives.ReadUInt64LittleEndian(source[16..]),
                Position = position, Speed = speed, Aim = aim, Facing = facing,
                Points = BinaryPrimitives.ReadInt32LittleEndian(source[72..]),
                Kills = BinaryPrimitives.ReadInt32LittleEndian(source[76..]),
                Deaths = BinaryPrimitives.ReadInt32LittleEndian(source[80..]),
                AvailableWeapons = BinaryPrimitives.ReadUInt16LittleEndian(source[84..]),
                FrozenTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[86..]),
                BurnTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[88..]),
                DisruptTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[90..]),
                Assists = BinaryPrimitives.ReadInt32LittleEndian(source[92..]),
                ChargeLevel = BinaryPrimitives.ReadUInt16LittleEndian(source[96..]),
                DoubleDamageTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[98..]),
                CloakTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[100..]),
                DeathaltTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[102..]),
                EnhancedTargetSlot = source[104],
                Overcharge = source[105],
                EnhancedTargetTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[106..]),
                ChilledTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[108..]),
                CloakFadeTicks = BinaryPrimitives.ReadUInt16LittleEndian(source[110..]),
                AltAction = altAction
            };
            return true;
        }

        /// <summary>
        /// Decode a frozen protocol-24 (or older expanded) player record.
        /// The legacy record is copied into the current shape only after its
        /// fixed 112-byte boundary is checked; its compatibility bit maps to
        /// Active/tick 0 for the Hunters that had a reconstructible action.
        /// </summary>
        internal static bool TryReadLegacy(ReadOnlySpan<byte> source,
            out SnapshotPlayer player)
        {
            player = default;
            if (source.Length != LegacySize) return false;
            Span<byte> expanded = stackalloc byte[Size];
            source.CopyTo(expanded);
            if (!TryMapLegacyAltAction(expanded)) return false;
            return TryRead(expanded, out player);
        }

        /// <summary>Append the protocol-25 suffix to a copied legacy record.</summary>
        internal static bool TryMapLegacyAltAction(Span<byte> expanded)
        {
            if (expanded.Length != Size) return false;
            SnapshotPlayerFlags flags = (SnapshotPlayerFlags)
                BinaryPrimitives.ReadUInt16LittleEndian(expanded[4..]);
            Hunter hunter = (Hunter)expanded[1];
            bool active = (flags & SnapshotPlayerFlags.AltAttack) != 0;
            if (active && !AltActionState.SupportsLegacyActive(hunter))
                return false;
            expanded[112] = active ? (byte)AltActionPhase.Active : (byte)AltActionPhase.None;
            BinaryPrimitives.WriteUInt16LittleEndian(expanded[113..], 0);
            return true;
        }

        private static bool Finite(Vector3 value) => Single.IsFinite(value.X)
            && Single.IsFinite(value.Y) && Single.IsFinite(value.Z);

        private static Vector3 ReadVector(ReadOnlySpan<byte> source) => new(
            BinaryPrimitives.ReadSingleLittleEndian(source), BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
            BinaryPrimitives.ReadSingleLittleEndian(source[8..]));

        private static void WriteVector(Span<byte> destination, Vector3 value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(destination, value.X);
            BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value.Y);
            BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value.Z);
        }
    }

    public readonly record struct SnapshotPacket(uint ServerTick, uint Sequence, uint MatchId,
        uint LastProcessedInput, bool HasProcessedInput, uint Rng1, uint Rng2)
    {
        public const int HeaderSize = 26;
        public const int MaxSize = HeaderSize + 8 * SnapshotPlayer.Size;

        public int Write(Span<byte> destination, ReadOnlySpan<SnapshotPlayer> players)
        {
            if (players.Length > 8) { throw new ArgumentOutOfRangeException(nameof(players)); }
            BinaryPrimitives.WriteUInt32LittleEndian(destination, ServerTick);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], LastProcessedInput);
            destination[16] = HasProcessedInput ? (byte)1 : (byte)0;
            destination[17] = (byte)players.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[18..], Rng1);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[22..], Rng2);
            for (int i = 0; i < players.Length; i++)
            {
                players[i].Write(destination.Slice(HeaderSize + i * SnapshotPlayer.Size, SnapshotPlayer.Size));
            }
            return HeaderSize + players.Length * SnapshotPlayer.Size;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, Span<SnapshotPlayer> players,
            out SnapshotPacket packet, out int count)
        {
            packet = default;
            count = 0;
            if (source.Length < HeaderSize || source[16] > 1 || source[17] > 8 || source[17] > players.Length
                || source.Length != HeaderSize + source[17] * SnapshotPlayer.Size)
            {
                return false;
            }
            int mask = 0;
            for (int i = 0; i < source[17]; i++)
            {
                if (!SnapshotPlayer.TryRead(source.Slice(HeaderSize + i * SnapshotPlayer.Size, SnapshotPlayer.Size), out SnapshotPlayer decoded)
                    || (mask & (1 << decoded.Slot)) != 0)
                {
                    return false;
                }
                mask |= 1 << decoded.Slot;
            }
            for (int i = 0; i < source[17]; i++)
                SnapshotPlayer.TryRead(source.Slice(HeaderSize + i * SnapshotPlayer.Size, SnapshotPlayer.Size), out players[i]);
            packet = new SnapshotPacket(BinaryPrimitives.ReadUInt32LittleEndian(source),
                BinaryPrimitives.ReadUInt32LittleEndian(source[4..]), BinaryPrimitives.ReadUInt32LittleEndian(source[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[12..]), source[16] != 0,
                BinaryPrimitives.ReadUInt32LittleEndian(source[18..]), BinaryPrimitives.ReadUInt32LittleEndian(source[22..]));
            count = source[17];
            return true;
        }
    }
}
