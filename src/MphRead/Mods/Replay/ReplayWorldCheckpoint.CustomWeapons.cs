using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

internal sealed partial class ReplayWorldCheckpoint
{
    internal const int CustomWorldAppendixSize = 72;
    private static ReplayReplicaState ValidateCustomWorldDecoders(ReplayReplicaCheckpoint construction,
        ReplayReplicaCheckpoint decoder, string room, GameMode mode, uint frame)
    {
        var initial = new ReplayReplicaState(); initial.RestoreCustomCheckpoint(construction);
        var current = new ReplayReplicaState(); current.RestoreCustomCheckpoint(decoder);
        if (initial.CustomWeaponDescriptor == null || current.CustomWeaponDescriptor == null
            || !initial.CustomWeaponDescriptor.CopyBytes().AsSpan().SequenceEqual(current.CustomWeaponDescriptor.CopyBytes())
            || initial.CustomWeapons is not { CompleteForRestore: true } first
            || current.CustomWeapons is not { CompleteForRestore: true } last || first.Sequence > last.Sequence
            || first.Sequence == last.Sequence && !first.CaptureCheckpointBytes().AsSpan().SequenceEqual(last.CaptureCheckpointBytes())
            || first.World != last.World && !NetLifecycleTracker.Newer(last.World, first.World)
            || initial.RecordingFrame > current.RecordingFrame || current.RecordingFrame != frame
            || initial.Match is not { } firstMatch || current.Match is not { } lastMatch
            || !StringComparer.Ordinal.Equals(firstMatch.RoomKey, room) || !StringComparer.Ordinal.Equals(lastMatch.RoomKey, room)
            || firstMatch.Mode != (byte)mode || lastMatch.Mode != (byte)mode)
            throw new InvalidDataException("Custom world construction, decoder, scope or header clocks disagree.");
        return current;
    }
    // Detached codec writer for a future resource-owning Scene capture. This
    // appendix binds the declared capability to its complete current decoder;
    // hashes detect inconsistency, not authenticity or package availability.
    internal static byte[] CustomWorldAppendix(ReplayReplicaCheckpoint decoder)
    {
        var state = new ReplayReplicaState(); state.RestoreCustomCheckpoint(decoder);
        return CustomWorldAppendix(state);
    }
    private static byte[] CustomWorldAppendix(ReplayReplicaState state)
    {
        if (state.CustomWeaponDescriptor == null || state.CustomWeapons is not { CompleteForRestore: true } custom)
            throw new InvalidDataException("A complete custom decoder is required by custom saved worlds.");
        byte[] bytes = new byte[CustomWorldAppendixSize]; "CWW1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        SHA256.HashData(state.CustomWeaponDescriptor.CopyBytes(), bytes.AsSpan(8, 32));
        SHA256.HashData(custom.CaptureCheckpointBytes(), bytes.AsSpan(40, 32));
        return bytes;
    }
    private static void ValidateCustomWorldAppendix(byte[] bytes, ReplayReplicaState state)
    {
        if (!bytes.AsSpan().SequenceEqual(CustomWorldAppendix(state)))
            throw new InvalidDataException("Custom world capability appendix differs from its exact current decoder.");
    }
    internal void ValidateCustomArchive(ReplayMetadata metadata, ReplayReplicaCheckpoint? current = null)
    {
        if (!HasCustomWeaponContract || metadata.FormatVersion is not (7 or 8) || CustomWeaponDescriptor == null || metadata.CustomWeapons == null
            || metadata.Bootstrap.Packets.Count != 1
            || !CustomWeaponDescriptor.CopyBytes().AsSpan().SequenceEqual(metadata.CustomWeapons.CopyBytes())
            || HasTimedShotContract != metadata.TimedShotCertificates
            || metadata.IndexedFireContinuation is { } indexed && indexed != IndexedFirePresentation)
            throw new InvalidDataException("Custom world differs from its archive capability.");
        if (metadata.WorldCheckpoint.Length != 0)
        {
            using var baseline = FromCustomBytes(metadata.WorldCheckpoint, metadata.BuildId);
            if (HasHeadHistoryContract != baseline.HasHeadHistoryContract || HeadHistoryOrigin != baseline.HeadHistoryOrigin)
                throw new InvalidDataException("Custom checkpoint head-history contract differs from its archive baseline.");
        }
        else if (HasHeadHistoryContract) throw new InvalidDataException("Head-history worlds require an explicit archive baseline.");
        if (ReplayReplicaState.InspectCustomCheckpointLayout(ConstructionState()).Protocol != metadata.ProtocolVersion
            || ReplayReplicaState.InspectCustomCheckpointLayout(DecoderState()).Protocol != metadata.ProtocolVersion)
            throw new InvalidDataException("Custom world decoder protocol differs from its archive.");
        var header = ClipMetadata(0, Array.Empty<ReplayPlayerInfo>());
        if (!StringComparer.Ordinal.Equals(header.RoomKey, metadata.RoomKey) || header.Mode != metadata.Mode || header.MapHash != metadata.MapHash
            || current != null && !DecoderState().Bytes.SequenceEqual(current.Bytes))
            throw new InvalidDataException("Custom world header or current decoder differs from its archive.");
        var seed = new ReplayReplicaState(); seed.RestoreCustomCheckpoint(ReplayTimelineArchive.ReadConstruction(metadata.Bootstrap.Packets[0], custom: true));
        var decoder = new ReplayReplicaState(); decoder.RestoreCustomCheckpoint(DecoderState());
        if (decoder.CustomWeapons!.Sequence < seed.CustomWeapons!.Sequence
            || decoder.CustomWeapons.Sequence == seed.CustomWeapons.Sequence
                && !decoder.CustomWeapons.CaptureCheckpointBytes().AsSpan().SequenceEqual(seed.CustomWeapons.CaptureCheckpointBytes())
            || decoder.CustomWeapons.World != seed.CustomWeapons.World && !NetLifecycleTracker.Newer(decoder.CustomWeapons.World, seed.CustomWeapons.World))
            throw new InvalidDataException("Custom archive world regressed behind its construction frontier.");
    }
}
