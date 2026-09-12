using System;

namespace MphRead
{
    public enum VisualLightSourceKind : byte
    {
        BeamProjectile = 1,
        Bomb = 2,
        Teleporter = 3,
        ForceField = 4
    }

    /// <summary>
    /// Stable local keys for presentation lights. Authored keys are stable for
    /// a room entity id. Reset-scoped runtime identities are supplied by the
    /// owning presentation. Neither form is gameplay identity or network state.
    /// </summary>
    public static class VisualLightSourceKey
    {
        public static ulong ForAuthored(VisualLightSourceKind kind, int entityId)
        {
            ValidateKind(kind);
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            return Mix(((ulong)(uint)entityId << 8) | (byte)kind);
        }

        public static ulong ForPresentation(VisualLightSourceKind kind, ulong scope,
            uint identity, uint generation = 0)
        {
            ValidateKind(kind);
            if (scope == 0) throw new ArgumentOutOfRangeException(nameof(scope));
            if (identity == 0) throw new ArgumentOutOfRangeException(nameof(identity));
            ulong hash = Append(OffsetBasis, (byte)kind);
            hash = Append(hash, scope);
            hash = Append(hash, identity);
            return Append(hash, generation);
        }

        /// <summary>
        /// Compatibility key over every exact finite light value. Selection
        /// still compares the full candidate before collapsing a duplicate,
        /// so a compact-hash collision cannot merge distinct legacy lights.
        /// </summary>
        public static ulong ForLegacy(RenderVisualLight light)
        {
            ulong hash = Append(OffsetBasis, 0x4C45474143594C54ul);
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Position.X));
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Position.Y));
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Position.Z));
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Color.X));
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Color.Y));
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Color.Z));
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Radius));
            hash = Append(hash, BitConverter.SingleToUInt32Bits(light.Intensity));
            return Append(hash, unchecked((uint)light.Priority));
        }

        private static void ValidateKind(VisualLightSourceKind kind)
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9ul;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBul;
            return value ^ (value >> 31);
        }

        private const ulong OffsetBasis = 14695981039346656037ul;
        private const ulong Prime = 1099511628211ul;

        private static ulong Append(ulong hash, byte value)
            => (hash ^ value) * Prime;

        private static ulong Append(ulong hash, uint value)
        {
            hash = Append(hash, (byte)value);
            hash = Append(hash, (byte)(value >> 8));
            hash = Append(hash, (byte)(value >> 16));
            return Append(hash, (byte)(value >> 24));
        }

        private static ulong Append(ulong hash, ulong value)
        {
            hash = Append(hash, (uint)value);
            return Append(hash, (uint)(value >> 32));
        }
    }
}
