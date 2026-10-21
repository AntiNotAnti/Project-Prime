using System;
using System.IO;

namespace MphRead.Mods.Network;

internal static partial class ReplayFormatV3
{
    private const uint CustomWeaponCapabilityMagic = 0x31574350; // PCW1
    internal const int MaxCustomConstruction = ReplayReplicaCheckpoint.MaximumCustomBytes + 65536 + 1;
    internal const int MaxCustomHeader = MaxHeader + MaxCustomConstruction + ReplayCustomWeaponDescriptor.MaximumBytes
        + Replay.ReplayWorldCheckpoint.MaximumBytes + 24;

    // An explicit detached archive/world contract. Live Scene recording and
    // playback remain gated until a resource owner exists. Metadata cannot
    // substitute for a verified historical package lease at Scene creation.
    internal static ReplayReplicaState? ValidateCustomArchiveMetadata(ReplayMetadata metadata)
    {
        if (metadata.FormatVersion != 7)
        {
            if (metadata.CustomWeapons != null) throw new InvalidDataException("Custom metadata requires archive format7.");
            return null;
        }
        if (metadata.ProtocolVersion is not (83 or 84 or 85) || metadata.CustomWeapons is not { MetadataDefinition: not null } descriptor
            || metadata.WorldCheckpoint.Length > Replay.ReplayWorldCheckpoint.MaximumBytes || metadata.Bootstrap.Packets.Count != 1
            || metadata.Bootstrap.Packets[0] is not { Length: > 1 } packet || packet.Length > MaxCustomConstruction || packet[0] != 253
            || (ulong)metadata.OriginRecordingFrame + metadata.LeadInFrames > uint.MaxValue)
            throw new InvalidDataException("Format7 requires an exact custom descriptor and custom construction; an optional world must be explicit version12/13.");
        var checkpoint = ReplayTimelineArchive.ReadConstruction(packet, custom: true);
        if (ReplayReplicaState.InspectCustomCheckpointLayout(checkpoint).Protocol != metadata.ProtocolVersion)
            throw new InvalidDataException("Custom construction protocol differs from its archive.");
        var state = new ReplayReplicaState(); state.RestoreCustomCheckpoint(checkpoint);
        if (state.CustomWeaponDescriptor == null || !state.CustomWeaponDescriptor.CopyBytes().AsSpan().SequenceEqual(descriptor.CopyBytes())
            || state.CustomWeapons is not { CompleteForRestore: true }
            || state.Match is not { } match || !StringComparer.Ordinal.Equals(match.RoomKey, metadata.RoomKey)
            || match.Mode != (byte)metadata.Mode || state.RecordingFrame != metadata.OriginRecordingFrame
            || state.TimedShots.Enabled != metadata.TimedShotCertificates)
            throw new InvalidDataException("Custom archive descriptor, construction, clocks or match disagree.");
        if (metadata.WorldCheckpoint.Length != 0)
        {
            using var world = Replay.ReplayWorldCheckpoint.FromCustomBytes(metadata.WorldCheckpoint, metadata.BuildId);
            if (world.Frame != metadata.OriginRecordingFrame) throw new InvalidDataException("Custom archive world clock differs from its origin.");
            world.ValidateCustomArchive(metadata, checkpoint);
        }
        return state;
    }
    private static void WriteCustomArchiveCapability(BinaryWriter writer, ReplayMetadata metadata)
    {
        byte[] descriptor = metadata.CustomWeapons!.CopyBytes();
        writer.Write(CustomWeaponCapabilityMagic); writer.Write((byte)1);
        writer.Write((byte)(metadata.TimedShotCertificates ? 1 : 0));
        writer.Write((byte)(metadata.IndexedFireContinuation is null ? 0 : metadata.IndexedFireContinuation.Value ? 2 : 1));
        writer.Write((byte)0); writer.Write(descriptor.Length); writer.Write(descriptor);
    }
    private static (ReplayCustomWeaponDescriptor Descriptor, bool Timed, bool? Continuation)
        ReadCustomArchiveCapability(BinaryReader reader, byte protocol)
    {
        if (protocol is not (83 or 84 or 85) || reader.ReadUInt32() != CustomWeaponCapabilityMagic || reader.ReadByte() != 1)
            throw new InvalidDataException("Unsupported custom archive capability.");
        byte flags = reader.ReadByte(), continuation = reader.ReadByte(), reserved = reader.ReadByte();
        int length = reader.ReadInt32();
        if (flags > 1 || continuation > 2 || reserved != 0 || length is < 1 or > ReplayCustomWeaponDescriptor.MaximumBytes)
            throw new InvalidDataException("Invalid custom archive capability bounds.");
        if (!ReplayCustomWeaponDescriptor.TryRead(ReadBytes(reader, length), out var descriptor) || descriptor!.MetadataDefinition == null)
            throw new InvalidDataException("Custom archive requires an inspection descriptor.");
        descriptor.InspectContext();
        return (descriptor, flags == 1, continuation == 0 ? null : continuation == 2);
    }
    internal static void ValidateCustomArchiveRecord(ReadOnlySpan<byte> packet, ReplayCustomWeaponContext? context)
    {
        if (packet.Length == 0 || packet[0] != ReplayCustomWeaponFact.Tag) return;
        if (context == null || !ReplayCustomWeaponFact.TryRead(packet, context, out _))
            throw new InvalidDataException("Invalid or undeclared custom weapon archive record.");
    }
    internal static int MaximumRecordBytes(bool custom) => custom ? ReplayCustomWeaponFact.MaximumBytes : NetConfig.MaxSnapshotSize;
}
