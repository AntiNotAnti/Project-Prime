using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay;

[Flags]
internal enum ReplayWorldCheckpointAppendices : byte { None = 0, NativeTouch = 1, Pickups = 2, Grapple = 4, CustomWeapons = 8, HeadHistory = 16 }

internal sealed partial class ReplayWorldCheckpoint
{
    private static (ushort Version, ReplayWorldCheckpointAppendices Declared) ReadWorldHeader(BinaryReader reader)
    {
        if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Unsupported replay world checkpoint format.");
        ushort version = reader.ReadUInt16();
        if (version is < 1 or > CustomVersion) throw new InvalidDataException("Unsupported replay world checkpoint format.");
        var declared = version >= 10 ? (ReplayWorldCheckpointAppendices)reader.ReadByte() : ReplayWorldCheckpointAppendices.None;
        if (version >= 10 && declared != (ReplayWorldCheckpointAppendices.NativeTouch | ReplayWorldCheckpointAppendices.Pickups
                | (version >= 11 ? ReplayWorldCheckpointAppendices.Grapple : ReplayWorldCheckpointAppendices.None)
                | (IsCustomWorldVersion(version) ? ReplayWorldCheckpointAppendices.CustomWeapons : ReplayWorldCheckpointAppendices.None)
                | (version == CustomVersion ? ReplayWorldCheckpointAppendices.HeadHistory : ReplayWorldCheckpointAppendices.None)))
            throw new InvalidDataException("Unsupported replay world appendix contract.");
        return (version, declared);
    }

    // The stable object fingerprint covers neither optional appendix. The
    // collided v9 layouts are identified by complete validated decoder bodies,
    // not protocol >= comparisons or a guessed appendix size.
    private static (ReplayWorldCheckpointAppendices Appendices, ReplayReplicaCheckpointFeatures DecoderFeatures, bool IndexedFire,
        ReplayCustomWeaponDescriptor? CustomDescriptor, uint? HeadOrigin)
        InspectWorldBody(BinaryReader reader, ushort version, ReplayWorldCheckpointAppendices declared, string room, GameMode mode, uint frame)
    {
        reader.ReadUInt32(); reader.ReadUInt32(); // RNG values
        bool custom = IsCustomWorldVersion(version);
        int maximum = custom ? ReplayReplicaCheckpoint.MaximumCustomBytes : ReplayReplicaCheckpoint.MaximumBytes;
        var construction = new ReplayReplicaCheckpoint(ReadBytes(reader, maximum));
        var decoder = new ReplayReplicaCheckpoint(ReadBytes(reader, maximum));
        var initialLayout = custom ? ReplayReplicaState.InspectCustomCheckpointLayout(construction) : ReplayReplicaState.InspectCheckpointLayout(construction);
        var currentLayout = custom ? ReplayReplicaState.InspectCustomCheckpointLayout(decoder) : ReplayReplicaState.InspectCheckpointLayout(decoder);
        ReplayReplicaState? customState = custom ? ValidateCustomWorldDecoders(construction, decoder, room, mode, frame) : null;
        if ((initialLayout.Features & ~currentLayout.Features) != 0)
            throw new InvalidDataException("Contradictory replay world decoder contracts.");
        var appendices = declared;
        if (version == 9)
        {
            if (currentLayout.Has(ReplayReplicaCheckpointFeatures.NativeTouch)
                && !currentLayout.Has(ReplayReplicaCheckpointFeatures.Grants))
                appendices = ReplayWorldCheckpointAppendices.NativeTouch;
            else if (!currentLayout.Has(ReplayReplicaCheckpointFeatures.NativeTouch)
                && currentLayout.Features == (ReplayReplicaCheckpointFeatures.Pickups | ReplayReplicaCheckpointFeatures.Grants))
                appendices = ReplayWorldCheckpointAppendices.Pickups;
            else throw new InvalidDataException("Ambiguous or unsupported historical world v9 contract.");
        }
        if (appendices.HasFlag(ReplayWorldCheckpointAppendices.NativeTouch)
            && !currentLayout.Has(ReplayReplicaCheckpointFeatures.NativeTouch)
            || appendices.HasFlag(ReplayWorldCheckpointAppendices.Pickups)
            && !currentLayout.Has(ReplayReplicaCheckpointFeatures.Pickups)
            || appendices.HasFlag(ReplayWorldCheckpointAppendices.Grapple) != currentLayout.Has(ReplayReplicaCheckpointFeatures.Grapple))
            throw new InvalidDataException("Replay world appendix differs from its decoder contract.");
        int count = Count(reader, MaximumObjects);
        var types = new Type[count];
        for (int i = 0; i < count; i++)
        {
            ushort type = reader.ReadUInt16(); reader.ReadUInt64();
            if (type >= ObjectTypes.Length) throw new InvalidDataException("Unknown replay object contract.");
            types[i] = ObjectTypes[type]; ReadBytes(reader, MaximumBytes);
        }
        if (version >= 2) ReadBytes(reader, MaximumBytes);
        if (version >= 3) ReadBytes(reader, 4096);
        if (version >= 4) ReadBytes(reader, MaximumBytes);
        if (version >= 5) ReadBytes(reader, 256 * 1024);
        if (version >= 7) ReadBytes(reader, 4096);
        if (appendices.HasFlag(ReplayWorldCheckpointAppendices.NativeTouch))
            ValidateNativeTouchAppendix(ReadBytes(reader, 4096));
        if (appendices.HasFlag(ReplayWorldCheckpointAppendices.Pickups))
        {
            var state = customState ?? new ReplayReplicaState(); if (!custom) state.RestoreCheckpoint(decoder);
            ValidatePickupAppendix(ReadBytes(reader, 65536), types, state);
        }
        if (appendices.HasFlag(ReplayWorldCheckpointAppendices.Grapple))
        {
            var state = customState ?? new ReplayReplicaState(); if (!custom) state.RestoreCheckpoint(decoder);
            ValidateGrappleAppendix(ReadBytes(reader, 184), state);
        }
        if (custom) ValidateCustomWorldAppendix(ReadBytes(reader, CustomWorldAppendixSize), customState!);
        uint? headOrigin = version == CustomVersion
            ? Scene.ValidateReplayHeadHistory(ReadBytes(reader, ReplayHeadSeedCodec.MaximumBytes), customState!, frame) : null;
        byte indexed = version >= 6 ? reader.ReadByte() : (byte)0;
        if (indexed > 1 || reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("Invalid replay world capsule length/presentation marker.");
        return (appendices, currentLayout.Features, indexed == 1, customState?.CustomWeaponDescriptor, headOrigin);
    }

    private static void ValidateGrappleAppendix(byte[] bytes, ReplayReplicaState state)
    {
        if (bytes.Length != 8 * (4 + PlayerGrappleState.Size)) throw new InvalidDataException("Invalid grapple appendix length.");
        for (int slot = 0; slot < 8; slot++)
        {
            var row = bytes.AsSpan(slot * 23, 23);
            ushort generation = BinaryPrimitives.ReadUInt16LittleEndian(row), life = BinaryPrimitives.ReadUInt16LittleEndian(row[2..]);
            if (!PlayerGrappleState.TryRead(row[4..], out var grapple)) throw new InvalidDataException("Invalid grapple appendix state.");
            if (state.TryGetPlayer(slot, out var player) && player.SlotGeneration != 0 && player.LifeId != 0)
            {
                if (generation != player.SlotGeneration || life != player.LifeId || grapple != player.Grapple)
                    throw new InvalidDataException("Grapple appendix differs from accepted decoder life/state.");
            }
            else if (generation != 0 || life != 0 || grapple != default)
                throw new InvalidDataException("Unknown-life grapple appendix row.");
        }
    }

    private static void ValidateNativeTouchAppendix(byte[] bytes)
    {
        // Frozen native-touch appendix: two owner flags/two ushort identities,
        // 45-byte NativeTouchSample, six-byte boost latch for each of eight slots.
        const int rowBytes = 57;
        if (bytes.Length != rowBytes * PlayerEntity.SlotCapacity)
            throw new InvalidDataException("Invalid native touch checkpoint appendix length.");
        for (int slot = 0; slot < PlayerEntity.SlotCapacity; slot++)
        {
            var row = bytes.AsSpan(slot * rowBytes, rowBytes);
            foreach (int at in new[] { 0, 1, 26, 27, 36, 37, 51 })
                if (row[at] > 1) throw new InvalidDataException("Noncanonical native touch checkpoint boolean.");
            byte flags = row[42];
            if ((flags & ~31) != 0 || (flags & 2) != 0
                && (Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(row[43..])) > 1020
                    || Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(row[45..])) > 1020))
                throw new InvalidDataException("Invalid native touch report in checkpoint.");
            using var stream = new MemoryStream(row.ToArray(), writable: false); using var reader = new BinaryReader(stream);
            reader.ReadBoolean(); reader.ReadBoolean(); reader.ReadUInt16(); reader.ReadUInt16();
            new NativeTouchSample().RestoreCheckpoint(reader);
            new MorphBallBoostStateMachine().RestoreCheckpoint(reader);
            if (stream.Position != stream.Length) throw new InvalidDataException("Invalid native touch checkpoint row.");
        }
    }

    private static void ValidatePickupAppendix(byte[] bytes, Type[] types, ReplayReplicaState state)
    {
        using var stream = new MemoryStream(bytes, writable: false); using var reader = new BinaryReader(stream);
        int count = Count(reader, 2048); var seen = new HashSet<int>(); var identities = new HashSet<PowerUpSpawnKey>();
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            var key = new PowerUpSpawnKey(reader.ReadUInt64(), reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadUInt32());
            byte hidden = reader.ReadByte();
            if ((uint)id >= types.Length || types[id] != typeof(ItemInstanceEntity) || hidden > 1
                || !seen.Add(id) || !identities.Add(key) || !PickupWire.ValidSpawn(key)
                || state.Match is not { } match || key.Epoch != match.AuthorityEpoch || key.Match != match.MatchId
                || state.PickupRoomIdentity != 0 && key.Room != state.PickupRoomIdentity)
                throw new InvalidDataException("Invalid replay pickup item appendix contract.");
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing replay pickup item appendix data.");
    }
}
