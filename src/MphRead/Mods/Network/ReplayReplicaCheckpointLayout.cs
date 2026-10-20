using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Network;

[Flags]
internal enum ReplayReplicaCheckpointFeatures : byte
{
    None = 0, NativeTouch = 1, Pickups = 2,
    Grants = 4, TimedShots = 8
}

internal readonly record struct ReplayReplicaCheckpointLayout(ushort Version, int Protocol,
    ReplayReplicaCheckpointFeatures Features, int HeaderBytes)
{
    internal const ushort DefaultVersion = 12, CurrentVersion = 13;
    internal const ReplayReplicaCheckpointFeatures CurrentFeatures
        = ReplayReplicaCheckpointFeatures.NativeTouch | ReplayReplicaCheckpointFeatures.Pickups | ReplayReplicaCheckpointFeatures.Grants;
    internal bool Has(ReplayReplicaCheckpointFeatures feature) => (Features & feature) != 0;
    internal int IntentBytes => Has(ReplayReplicaCheckpointFeatures.NativeTouch) ? IntentPacket.TouchCanonicalSize
        : Protocol >= 43 ? IntentPacket.FullSize : Protocol >= 39 ? IntentPacket.Protocol42FullSize
        : Protocol >= 30 ? IntentPacket.Protocol38FullSize : IntentPacket.LegacyFullSize;

    internal static ReplayReplicaCheckpointLayout[] Candidates(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 7 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x43525050)
            throw new InvalidDataException("Incompatible replica checkpoint header.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        int protocol = bytes[6];
        if (protocol < 24 || !ReplayIdentityCompatibility.Supports(protocol))
            throw new InvalidDataException("Incompatible replica checkpoint protocol.");
        if (version is >= 1 and <= 9 && protocol <= 52)
            return [new(version, protocol, ReplayReplicaCheckpointFeatures.None, 7)];
        // Published main and staged A2 both used v10/protocol53, for distinct
        // layouts. Neither protocol nor the world object fingerprint identifies
        // the source. Validate both complete detached bodies before selecting.
        if (version == 10 && protocol == 53)
            return [new(version, protocol, ReplayReplicaCheckpointFeatures.NativeTouch, 7),
                new(version, protocol, ReplayReplicaCheckpointFeatures.Pickups, 7)];
        if (version == 11 && protocol == 54)
            return [new(version, protocol, ReplayReplicaCheckpointFeatures.Pickups | ReplayReplicaCheckpointFeatures.Grants, 7)];
        if (version == DefaultVersion && bytes.Length >= 8)
        {
            var features = (ReplayReplicaCheckpointFeatures)bytes[7];
            if (protocol == 55 && features == (ReplayReplicaCheckpointFeatures.NativeTouch | ReplayReplicaCheckpointFeatures.Pickups)
                || (protocol is 56 or 57 or 58 or 59 or 60 or 61 or 62 or 63 or 64 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 73 or 74 or 75 or 76) && features == CurrentFeatures)
                return [new(version, protocol, features, 8)];
        }
        if (version == CurrentVersion && bytes.Length >= 8 && (protocol is 56 or 57 or 58 or 59 or 60 or 61 or 62 or 63 or 64 or 65 or 66 or 67 or 68 or 69 or 70 or 71 or 72 or 73 or 74 or 75 or 76)
            && (ReplayReplicaCheckpointFeatures)bytes[7] == (CurrentFeatures | ReplayReplicaCheckpointFeatures.TimedShots))
            return [new(version, protocol, (ReplayReplicaCheckpointFeatures)bytes[7], 8)];
        throw new InvalidDataException("Unsupported replica checkpoint layout/features.");
    }

    internal static T DecodeUnique<T>(ReadOnlySpan<byte> bytes, Func<ReplayReplicaCheckpointLayout, T> decode)
    {
        T selected = default!;
        int successes = 0;
        InvalidDataException? failure = null;
        foreach (var layout in Candidates(bytes))
        {
            T candidate;
            try { candidate = decode(layout); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
            { failure = new InvalidDataException("Invalid replica checkpoint body.", ex); continue; }
            selected = candidate;
            successes++;
        }
        if (successes == 0) throw failure ?? new InvalidDataException("No valid replica checkpoint layout.");
        if (successes != 1) throw new InvalidDataException("Ambiguous historical replica checkpoint layout.");
        return selected;
    }
}
