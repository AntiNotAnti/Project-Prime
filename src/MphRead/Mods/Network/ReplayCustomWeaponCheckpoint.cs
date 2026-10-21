using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

// Independent file-only passive checkpoint. Native replica/world checkpoint
// versions do not admit this block until their explicit custom capability exists.
internal sealed partial class ReplayCustomWeaponState
{
    internal const ushort CheckpointVersion = 1;
    internal const int MaximumCheckpointBytes = 768 * 1024;
    internal byte[] CaptureCheckpointBytes()
    {
        if (!CompleteForRestore) throw new InvalidDataException("Custom checkpoint requires complete healthy state.");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("CWRS"u8); writer.Write(CheckpointVersion); writer.Write((ushort)0);
        Span<byte> buffer = stackalloc byte[CustomReplicaCodec.FlightSize];
        Context.Digest.Write(buffer[..32]); writer.Write(buffer[..32]);
        writer.Write(World); writer.Write(RecordingFrame); writer.Write(_stateWorld); writer.Write(Sequence);
        foreach (var slot in _slots) { CustomReplicaCodec.WriteSlot(buffer, slot); writer.Write(buffer[..CustomReplicaCodec.SlotSize]); }
        writer.Write((byte)Context.Layout.Count);
        for (int i = 0; i < Context.Layout.Count; i++)
        {
            var item = _pickups[i]!.Value;
            writer.Write(_pickupWorlds[i]); writer.Write(item.Key.Incarnation); writer.Write(item.ReadyAt); writer.Write(item.Available); writer.Write(item.Revision);
        }
        int count = 0; foreach (var anchor in _flights) if (anchor.Used) count++;
        writer.Write((ushort)count);
        for (int i = 0; i < _flights.Length; i++) if (_flights[i].Used)
        {
            writer.Write((ushort)i); writer.Write(_flights[i].World);
            CustomReplicaCodec.WriteFlight(buffer, _flights[i].Flight); writer.Write(buffer[..CustomReplicaCodec.FlightSize]);
        }
        count = 0; foreach (var entry in _actions) if (entry.Used) count++;
        writer.Write((ushort)count);
        for (int i = 0; i < _actions.Length; i++) if (_actions[i].Used)
        {
            var entry = _actions[i]; writer.Write((ushort)i);
            writer.Write((byte)((entry.BaselineLaunch.HasValue ? 1 : 0) | (entry.Launch != null ? 2 : 0) | (entry.Contact != null ? 4 : 0)));
            writer.Write(entry.TouchedWorld); writer.Write(entry.LaunchFrame); writer.Write(entry.ContactFrame);
            if (entry.BaselineLaunch is { } baseline)
            { CustomReplicaCodec.WriteLaunch(buffer, baseline); writer.Write(buffer[..CustomReplicaCodec.LaunchSize]); }
            WriteFact(writer, entry.Launch); WriteFact(writer, entry.Contact);
        }
        count = (int)Math.Min(Sequence, (ulong)SeenCapacity); writer.Write((ushort)count);
        // Ordered by sequence; modulo positions are reconstructed exactly.
        ulong first = Sequence - (ulong)count + 1;
        for (int i = 0; i < count; i++)
        {
            ulong sequence = first + (ulong)i; int index = (int)(sequence % SeenCapacity);
            writer.Write(_seen[index]); _digests[index].Write(buffer[..32]); writer.Write(buffer[..32]);
        }
        if (stream.Length + 32 > MaximumCheckpointBytes) throw new InvalidDataException("Excessive custom checkpoint.");
        byte[] bytes = new byte[checked((int)stream.Length + 32)]; stream.GetBuffer().AsSpan(0, (int)stream.Length).CopyTo(bytes);
        SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32), bytes.AsSpan(bytes.Length - 32)); return bytes;
    }
    private static void WriteFact(BinaryWriter writer, ReplayCustomWeaponFact? fact)
    {
        if (fact == null) return;
        byte[] bytes = fact.CopyBytes(); writer.Write(checked((ushort)bytes.Length)); writer.Write(bytes);
    }
    private static byte[] ReadFixed(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new InvalidDataException("Truncated custom checkpoint."); return bytes;
    }
    private static ReplayCustomWeaponFact ReadFact(BinaryReader reader, ReplayCustomWeaponContext context, ReplayCustomWeaponFactKind kind, ulong sequence)
    {
        int count = reader.ReadUInt16();
        int expected = ReplayCustomWeaponFact.HeaderSize + (kind == ReplayCustomWeaponFactKind.Launch ? CustomReplicaCodec.LaunchSize : CustomCombatResults.ContactSemanticSize);
        if (count != expected || !ReplayCustomWeaponFact.TryRead(ReadFixed(reader, count), context, out var fact)
            || fact!.Kind != kind || fact.Sequence < 3 || fact.Sequence > sequence) throw new InvalidDataException("Malformed custom checkpoint action fact.");
        return fact;
    }
    internal static ReplayCustomWeaponState RestoreCheckpointBytes(ReadOnlySpan<byte> bytes, ReplayCustomWeaponContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (bytes.Length is < 40 + 20 + 8 * CustomReplicaCodec.SlotSize + 1 + 32 or > MaximumCheckpointBytes
            || !bytes[..4].SequenceEqual("CWRS"u8)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes[..^32]), bytes[^32..]))
            throw new InvalidDataException("Invalid custom checkpoint size, signature or digest.");
        try
        {
            using var stream = new MemoryStream(bytes[..^32].ToArray(), writable: false); using var reader = new BinaryReader(stream);
            reader.ReadUInt32();
            if (reader.ReadUInt16() != CheckpointVersion || reader.ReadUInt16() != 0 || WeaponDigest.Read(ReadFixed(reader, 32)) != context.Digest)
                throw new InvalidDataException("Different custom checkpoint schema or context.");
            var state = new ReplayCustomWeaponState(context)
            { World = reader.ReadUInt32(), RecordingFrame = reader.ReadUInt32(), _stateWorld = reader.ReadUInt32(), Sequence = reader.ReadUInt64() };
            uint floor = context.Bootstrap.World.AuthorityFrame;
            if (state.Sequence < 2UL + (ulong)context.Layout.Count || Before(state.World, floor) || Before(state._stateWorld, floor) || Before(state.World, state._stateWorld))
                throw new InvalidDataException("Invalid custom checkpoint clocks.");
            for (int i = 0; i < 8; i++)
                if (!CustomReplicaCodec.ReadSlot(ReadFixed(reader, CustomReplicaCodec.SlotSize), i, context.Validation, out state._slots[i]))
                    throw new InvalidDataException("Invalid custom checkpoint slot.");
            if (reader.ReadByte() != context.Layout.Count) throw new InvalidDataException("Incomplete custom checkpoint pickups.");
            for (int i = 0; i < context.Layout.Count; i++)
            {
                uint world = reader.ReadUInt32(); ulong incarnation = reader.ReadUInt64(); uint ready = reader.ReadUInt32(); byte available = reader.ReadByte(); ulong revision = reader.ReadUInt64();
                if (available > 1 || Before(state.World, world)) throw new InvalidDataException("Invalid custom checkpoint pickup clock.");
                var item = new CustomPickupItemState(new(context.PickupWorld, context.Layout.Placements[i].SpawnId, incarnation), ready, available == 1, revision);
                // Reuse strict semantic validation; no acceptance/grant is run.
                ReplayCustomWeaponFact.Pickup(context, 2, world, i, item);
                state._pickups[i] = item; state._pickupWorlds[i] = world; state.KnownPickups++;
            }
            int count = reader.ReadUInt16(), previousIndex = -1;
            if (count > FlightCapacity) throw new InvalidDataException("Excessive custom checkpoint flights.");
            for (int i = 0; i < count; i++)
            {
                int index = reader.ReadUInt16(); uint world = reader.ReadUInt32();
                if (index <= previousIndex || index >= FlightCapacity || Before(world, floor) || Before(state.World, world)
                    || !CustomReplicaCodec.ReadFlight(ReadFixed(reader, CustomReplicaCodec.FlightSize), context.Validation, world, out var flight)
                    || state.FindFlight(flight.Action) >= 0) throw new InvalidDataException("Invalid custom checkpoint flight anchor.");
                var slot = state._slots[flight.Action.Owner.Slot];
                if (!slot.Occupied || slot.Spectating || slot.Generation != flight.Action.Owner.Generation)
                    throw new InvalidDataException("Retired custom checkpoint flight owner.");
                var anchor = new FlightAnchor { Used = true, World = world, Flight = flight };
                if (!state.FlightAt(anchor, out _)) throw new InvalidDataException("Expired custom checkpoint flight.");
                state._flights[index] = anchor; previousIndex = index;
            }
            count = reader.ReadUInt16(); previousIndex = -1;
            if (count > ActionCapacity) throw new InvalidDataException("Excessive custom checkpoint history.");
            for (int i = 0; i < count; i++)
            {
                int index = reader.ReadUInt16(); byte flags = reader.ReadByte();
                if (index <= previousIndex || index >= ActionCapacity || flags is 0 or > 7) throw new InvalidDataException("Invalid custom checkpoint action index.");
                var entry = new ActionEntry { Used = true, TouchedWorld = reader.ReadUInt32(), LaunchFrame = reader.ReadUInt32(), ContactFrame = reader.ReadUInt32() };
                if (Before(entry.TouchedWorld, floor) || Before(state.World, entry.TouchedWorld)
                    || entry.LaunchFrame > state.RecordingFrame || entry.ContactFrame > state.RecordingFrame
                    || (flags & 2) == 0 && entry.LaunchFrame != 0 || (flags & 4) == 0 && entry.ContactFrame != 0)
                    throw new InvalidDataException("Invalid custom checkpoint history clocks.");
                CustomReplicaLaunch? canonical = null;
                if ((flags & 1) != 0)
                {
                    if (!CustomReplicaCodec.ReadLaunch(ReadFixed(reader, CustomReplicaCodec.LaunchSize), context.Validation, state.World, out var initial))
                        throw new InvalidDataException("Invalid custom checkpoint baseline launch.");
                    canonical = entry.BaselineLaunch = initial;
                }
                if ((flags & 2) != 0)
                {
                    entry.Launch = ReadFact(reader, context, ReplayCustomWeaponFactKind.Launch, state.Sequence); entry.Launch.TryLaunch(context, out var launch);
                    if (Before(state.World, launch.World) || canonical is { } initial && initial != launch)
                        throw new InvalidDataException("Contradictory custom checkpoint launch.");
                    canonical = launch;
                }
                if (canonical is { } known) entry.Identity = known.Identity;
                if ((flags & 4) != 0)
                {
                    entry.Contact = ReadFact(reader, context, ReplayCustomWeaponFactKind.Contact, state.Sequence);
                    entry.Contact.TryContact(context, out var source, out var contact);
                    if (Before(state.World, contact.AuthorityFrame) || canonical is { } knownLaunch
                        && (knownLaunch.Identity != contact.Contact.Key.Action || knownLaunch.Source.Action.Bootstrap != source))
                        throw new InvalidDataException("Contradictory custom checkpoint terminal.");
                    entry.Identity = contact.Contact.Key.Action;
                }
                for (int n = 0; n < index; n++) if (state._actions[n].Used && state._actions[n].Identity == entry.Identity)
                    throw new InvalidDataException("Duplicate custom checkpoint action.");
                int flightIndex = state.FindFlight(entry.Identity);
                if (flightIndex >= 0)
                {
                    var active = state._flights[flightIndex].Flight;
                    if (entry.Contact != null || canonical is not { } launch || active.Certificate != launch.Source || active.Direction != launch.Direction
                        || active.SpawnPosition != launch.Origin || active.LaunchWorld != launch.World)
                        throw new InvalidDataException("Custom checkpoint flight/history contradiction.");
                }
                state._actions[index] = entry; previousIndex = index;
            }
            foreach (var anchor in state._flights) if (anchor.Used)
            {
                bool found = false; foreach (var entry in state._actions) if (entry.Used && entry.Identity == anchor.Flight.Action) { found = true; break; }
                if (!found) throw new InvalidDataException("Custom checkpoint flight has no source history.");
            }
            count = reader.ReadUInt16();
            if (count != (int)Math.Min(state.Sequence, (ulong)SeenCapacity)) throw new InvalidDataException("Incomplete custom checkpoint retry window.");
            ulong first = state.Sequence - (ulong)count + 1;
            for (int i = 0; i < count; i++)
            {
                ulong sequence = reader.ReadUInt64(); var digest = WeaponDigest.Read(ReadFixed(reader, 32));
                if (sequence != first + (ulong)i || !digest.IsValid) throw new InvalidDataException("Invalid custom checkpoint retry history.");
                int index = (int)(sequence % SeenCapacity); state._seen[index] = sequence; state._digests[index] = digest;
            }
            var retainedSequences = new HashSet<ulong>();
            void VerifyRetained(ReplayCustomWeaponFact? fact)
            {
                if (fact == null) return;
                if (!retainedSequences.Add(fact.Sequence)) throw new InvalidDataException("Reused custom checkpoint fact sequence.");
                if (fact.Sequence >= first && state._digests[(int)(fact.Sequence % SeenCapacity)] != WeaponDigest.Read(SHA256.HashData(fact.CopyBytes())))
                    throw new InvalidDataException("Custom checkpoint retained fact contradicts retry digest.");
            }
            foreach (var entry in state._actions) if (entry.Used) { VerifyRetained(entry.Launch); VerifyRetained(entry.Contact); }
            if (state.Sequence < 2UL + (ulong)context.Layout.Count + (ulong)retainedSequences.Count)
                throw new InvalidDataException("Insufficient custom checkpoint sequence for retained facts.");
            if (first == 1 && state._digests[1] != WeaponDigest.Read(SHA256.HashData(ReplayCustomWeaponFact.Bootstrap(context).CopyBytes())))
                throw new InvalidDataException("Custom checkpoint bootstrap contradicts retry digest.");
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing custom checkpoint data.");
            state._bootstrap = state.HasBaseline = true;
            // Every decode creates an independent temporary owner. Publish only
            // after all bounds, identities and canonical bytes have passed.
            if (!state.CaptureCheckpointBytes().AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Noncanonical custom checkpoint.");
            return state;
        }
        catch (Exception error) when (error is EndOfStreamException or ArgumentException)
        { throw new InvalidDataException("Malformed custom checkpoint.", error); }
    }
}
