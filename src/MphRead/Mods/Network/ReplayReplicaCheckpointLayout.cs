using System;
using System.Buffers.Binary;
using System.IO;

namespace MphRead.Mods.Network;

[Flags]
internal enum ReplayReplicaCheckpointFeatures : byte
{
    None = 0, NativeTouch = 1, Pickups = 2,
    // Reserved for A3. A2 must refuse it until its decoder is installed.
    Grants = 4
}

internal readonly record struct ReplayReplicaCheckpointLayout(ushort Version, int Protocol,
    ReplayReplicaCheckpointFeatures Features, int HeaderBytes)
{
    internal const ushort CurrentVersion = 12;
    internal const ReplayReplicaCheckpointFeatures CurrentFeatures
        = ReplayReplicaCheckpointFeatures.NativeTouch | ReplayReplicaCheckpointFeatures.Pickups;
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
        if (version == 11)
            throw new InvalidDataException("Staged A3 grant checkpoints require the A3 decoder.");
        if (version != CurrentVersion || protocol < 55 || bytes.Length < 8
            || (ReplayReplicaCheckpointFeatures)bytes[7] != CurrentFeatures)
            throw new InvalidDataException("Unsupported replica checkpoint layout/features.");
        return [new(version, protocol, CurrentFeatures, 8)];
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
