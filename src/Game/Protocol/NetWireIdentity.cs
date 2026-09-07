using System;

namespace MphRead.Mods.Network
{
    public enum NetWireFamily : byte
    {
        Unknown = 0,
        LegacyRelay = 1,
        Authoritative = 2
    }

    /// <summary>Live compatibility requires both architecture and protocol agreement.</summary>
    public static class NetWireIdentity
    {
        public const NetWireFamily Family = NetWireFamily.Authoritative;
        public static byte Protocol => NetHeader.Version;

        public static bool IsCompatible(NetWireFamily family, int protocol)
            => family == Family && protocol == Protocol;

        public static string IncompatibilityReason(NetWireFamily family, int protocol)
        {
            if (family == NetWireFamily.LegacyRelay)
            {
                return "Incompatible architecture: legacy relay. An authoritative server is required.";
            }
            if (family != Family)
            {
                return "Incompatible architecture: unknown family.";
            }
            return protocol == Protocol ? String.Empty
                : $"Incompatible protocol: server {protocol}, client {Protocol}. Update to matching builds.";
        }

        internal static bool IsKnownFamily(byte family)
            => family is (byte)NetWireFamily.LegacyRelay or (byte)NetWireFamily.Authoritative;

        internal static bool TryReadFamily(ReadOnlySpan<byte> source, int legacySize, out NetWireFamily family)
        {
            family = NetWireFamily.Unknown;
            if (source.Length == legacySize)
            {
                family = NetWireFamily.LegacyRelay;
                return true;
            }
            if (source.Length != legacySize + 1 || !IsKnownFamily(source[legacySize]))
            {
                return false;
            }
            family = (NetWireFamily)source[legacySize];
            return true;
        }

        internal static bool ValidCounts(byte players, byte maximum)
            => maximum is >= 1 and <= 8 && players <= maximum;

        internal static bool ValidMode(byte mode)
            => mode != 1 && mode <= (byte)GameMode.Unknown15;

        internal static bool ValidText(ReadOnlySpan<byte> source)
        {
            bool terminated = false;
            foreach (byte value in source)
            {
                if (value == 0)
                {
                    terminated = true;
                }
                else if (terminated || value < 32 || value > 126)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
