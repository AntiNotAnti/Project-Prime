using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using MphRead.Mods.CustomWeapons;

namespace MphRead.Mods.Network;

// Detached validation values, never readiness evidence or a network connection.
internal sealed class ReplayCustomWeaponContext
{
    internal ReplayCustomWeaponBootstrap Bootstrap { get; }
    internal WeaponDefinition Definition { get; }
    internal CustomPickupLayout Layout { get; }
    internal WeaponDigest Digest { get; }
    internal CustomReadinessWire Validation { get; }
    internal ReplayCustomWeaponContext(ReplayCustomWeaponBootstrap bootstrap, WeaponDefinition definition,
        CustomPickupLayout layout)
    {
        ArgumentNullException.ThrowIfNull(bootstrap); ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(layout);
        var mechanics = new CustomWeaponManifest(new[] { definition });
        if (mechanics.Digest != bootstrap.Packages.MechanicsDigest || layout.Manifest != mechanics.Digest
            || layout.Digest != bootstrap.PickupLayout)
            throw new InvalidDataException("Replay weapon definition or pickup layout contradicts its exact bootstrap.");
        Bootstrap = bootstrap; Definition = definition; Layout = layout;
        Digest = WeaponDigest.Read(SHA256.HashData(bootstrap.CopyBytes()));
        // Slot is unused by the shared row/source validators. No Ready message
        // or recipient ACK is consumed or emitted by this validation context.
        Validation = new(bootstrap.World, 0, definition);
    }
    internal CustomPickupWorld PickupWorld => new(Bootstrap.World.Start, Bootstrap.Packages.MechanicsDigest, Layout.Digest);
}

internal enum ReplayCustomWeaponFactKind : byte { Bootstrap = 1, Baseline, State, Launch, Contact, Pickup }

// File-only v1 semantic projections. Outer recipient delivery IDs, transport ACK envelopes,
// retries and private acquisition knowledge are deliberately absent. Original
// accepted source certificates retain their full source/bootstrap identity.
internal sealed class ReplayCustomWeaponFact
{
    internal const byte Tag = 250, Version = 1;
    internal const int HeaderSize = 49, MaximumBytes = 16 * 1024, PickupSize = 25;
    private readonly byte[] _bytes;
    internal ReplayCustomWeaponFactKind Kind { get; }
    internal ulong Sequence { get; }
    internal uint World { get; }
    internal WeaponDigest Context { get; }
    private ReplayCustomWeaponFact(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes.ToArray(); Kind = (ReplayCustomWeaponFactKind)bytes[2];
        Sequence = BinaryPrimitives.ReadUInt64LittleEndian(bytes[5..]);
        World = BinaryPrimitives.ReadUInt32LittleEndian(bytes[13..]); Context = WeaponDigest.Read(bytes[17..49]);
    }
    internal byte[] CopyBytes() => (byte[])_bytes.Clone();
    private static bool Before(uint a, uint b) => unchecked((int)(a - b)) < 0;
    private static ReplayCustomWeaponFact Make(ReplayCustomWeaponContext context, ReplayCustomWeaponFactKind kind,
        ulong sequence, uint world, ReadOnlySpan<byte> body)
    {
        if (body.Length > MaximumBytes - HeaderSize) throw new InvalidDataException("Custom replay fact exceeds its byte budget.");
        byte[] bytes = new byte[HeaderSize + body.Length]; bytes[0] = Tag; bytes[1] = Version; bytes[2] = (byte)kind;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(3), (ushort)body.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(5), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(13), world); context.Digest.Write(bytes.AsSpan(17, 32));
        body.CopyTo(bytes.AsSpan(HeaderSize));
        if (!TryRead(bytes, context, out var fact)) throw new InvalidDataException("Invalid accepted custom replay fact.");
        return fact!;
    }
    internal static ReplayCustomWeaponFact Bootstrap(ReplayCustomWeaponContext context)
        => Make(context, ReplayCustomWeaponFactKind.Bootstrap, 1, context.Bootstrap.World.AuthorityFrame, context.Bootstrap.CopyBytes());
    internal static ReplayCustomWeaponFact Baseline(ReplayCustomWeaponContext context, ulong sequence,
        in CustomReplicaCapture capture, ReadOnlySpan<CustomReplicaSlot> slots, ReadOnlySpan<CustomReplicaFlight> flights)
    {
        if (capture.MatchId != context.Bootstrap.World.Start.MatchId || capture.AuthorityEpoch != context.Bootstrap.World.Start.AuthorityEpoch
            || capture.Manifest != context.Bootstrap.Packages.MechanicsDigest || capture.SlotCount != 8
            || capture.FlightCount is < 0 or > 64 || slots.Length < 8 || flights.Length < capture.FlightCount)
            throw new InvalidDataException("Custom replay baseline has a different world or exceeds its bounds.");
        byte[] body = new byte[8 * CustomReplicaCodec.SlotSize + 1 + capture.FlightCount * CustomReplicaCodec.FlightSize];
        for (int i = 0; i < 8; i++)
        {
            if (!CustomReplicaCodec.ValidSlot(slots[i], i, context.Validation)) throw new InvalidDataException("Invalid custom replay slot.");
            CustomReplicaCodec.WriteSlot(body.AsSpan(i * CustomReplicaCodec.SlotSize), slots[i]);
        }
        body[8 * CustomReplicaCodec.SlotSize] = (byte)capture.FlightCount;
        for (int i = 0; i < capture.FlightCount; i++)
        {
            if (!CustomReplicaCodec.ValidFlight(flights[i], context.Validation, capture.World)) throw new InvalidDataException("Invalid custom replay flight.");
            CustomReplicaCodec.WriteFlight(body.AsSpan(8 * CustomReplicaCodec.SlotSize + 1 + i * CustomReplicaCodec.FlightSize), flights[i]);
        }
        return Make(context, ReplayCustomWeaponFactKind.Baseline, sequence, capture.World, body);
    }
    internal static ReplayCustomWeaponFact State(ReplayCustomWeaponContext context, ulong sequence, uint world, ReadOnlySpan<CustomReplicaSlot> slots)
    {
        if (slots.Length < 8) throw new InvalidDataException("Complete custom replay slot table required.");
        Span<byte> body = stackalloc byte[8 * CustomReplicaCodec.SlotSize];
        for (int i = 0; i < 8; i++)
        {
            if (!CustomReplicaCodec.ValidSlot(slots[i], i, context.Validation)) throw new InvalidDataException("Invalid custom replay slot.");
            CustomReplicaCodec.WriteSlot(body[(i * CustomReplicaCodec.SlotSize)..], slots[i]);
        }
        return Make(context, ReplayCustomWeaponFactKind.State, sequence, world, body);
    }
    internal static ReplayCustomWeaponFact Launch(ReplayCustomWeaponContext context, ulong sequence, in CustomReplicaLaunch launch)
    {
        if (!CustomReplicaCodec.ValidLaunch(launch, context.Validation, launch.World)) throw new InvalidDataException("Invalid custom replay launch.");
        Span<byte> body = stackalloc byte[CustomReplicaCodec.LaunchSize]; CustomReplicaCodec.WriteLaunch(body, launch);
        return Make(context, ReplayCustomWeaponFactKind.Launch, sequence, launch.World, body);
    }
    internal static ReplayCustomWeaponFact Contact(ReplayCustomWeaponContext context, ulong sequence,
        WorldBootstrapIdentity source, in CustomContactOutcome outcome)
    {
        Span<byte> body = stackalloc byte[CustomCombatResults.ContactSemanticSize];
        CustomCombatResults.WriteOutcome(source, outcome, body, context.Validation.Manifest, context.Definition);
        return Make(context, ReplayCustomWeaponFactKind.Contact, sequence, outcome.AuthorityFrame, body);
    }
    internal static ReplayCustomWeaponFact Pickup(ReplayCustomWeaponContext context, ulong sequence, uint world, int index, in CustomPickupItemState item)
    {
        if ((uint)index >= context.Layout.Count) throw new InvalidDataException("Unknown custom replay pickup index.");
        Span<byte> body = stackalloc byte[PickupSize]; body[0] = (byte)index; body[1] = (byte)context.Layout.Count;
        BinaryPrimitives.WriteUInt16LittleEndian(body[2..], item.Key.SpawnId);
        BinaryPrimitives.WriteUInt64LittleEndian(body[4..], item.Key.Incarnation); BinaryPrimitives.WriteUInt32LittleEndian(body[12..], item.ReadyAt);
        body[16] = item.Available ? (byte)1 : (byte)0; BinaryPrimitives.WriteUInt64LittleEndian(body[17..], item.Revision);
        if (item.Key.World != context.PickupWorld) throw new InvalidDataException("Different custom replay pickup world.");
        return Make(context, ReplayCustomWeaponFactKind.Pickup, sequence, world, body);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, ReplayCustomWeaponContext context, out ReplayCustomWeaponFact? fact)
    {
        fact = null;
        if (context == null || bytes.Length is < HeaderSize or > MaximumBytes || bytes[0] != Tag || bytes[1] != Version
            || bytes.Length != HeaderSize + BinaryPrimitives.ReadUInt16LittleEndian(bytes[3..])
            || WeaponDigest.Read(bytes[17..49]) != context.Digest) return false;
        var kind = (ReplayCustomWeaponFactKind)bytes[2]; ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(bytes[5..]);
        uint world = BinaryPrimitives.ReadUInt32LittleEndian(bytes[13..]); var body = bytes[HeaderSize..];
        if (sequence == 0 || Before(world, context.Bootstrap.World.AuthorityFrame)) return false;
        if (kind == ReplayCustomWeaponFactKind.Bootstrap)
        {
            if (sequence != 1 || world != context.Bootstrap.World.AuthorityFrame || !body.SequenceEqual(context.Bootstrap.CopyBytes())) return false;
        }
        else
        {
            if (sequence == 1) return false;
            switch (kind)
            {
                case ReplayCustomWeaponFactKind.Baseline:
                case ReplayCustomWeaponFactKind.State:
                    if (!ValidTable(body, context, world, kind == ReplayCustomWeaponFactKind.Baseline)) return false;
                    break;
                case ReplayCustomWeaponFactKind.Launch:
                    if (body.Length != CustomReplicaCodec.LaunchSize || !CustomReplicaCodec.ReadLaunch(body, context.Validation, world, out var launch) || launch.World != world) return false;
                    break;
                case ReplayCustomWeaponFactKind.Contact:
                    if (!CustomCombatResults.TryReadOutcome(body, context.Validation.Manifest, context.Definition, out var source, out var outcome)
                        || source.Start != context.Bootstrap.World.Start || outcome.AuthorityFrame != world) return false;
                    break;
                case ReplayCustomWeaponFactKind.Pickup:
                    if (body.Length != PickupSize || body[0] >= context.Layout.Count || body[1] != context.Layout.Count || body[16] > 1
                        || BinaryPrimitives.ReadUInt16LittleEndian(body[2..]) != context.Layout.Placements[body[0]].SpawnId
                        || BinaryPrimitives.ReadUInt64LittleEndian(body[4..]) == 0
                        || (BinaryPrimitives.ReadUInt64LittleEndian(body[17..]) == 0) != (BinaryPrimitives.ReadUInt64LittleEndian(body[4..]) == 1)
                        || (body[16] == 1) != !Before(world, BinaryPrimitives.ReadUInt32LittleEndian(body[12..]))) return false;
                    break;
                default: return false;
            }
        }
        fact = new(bytes); return true;
    }
    private static bool ValidTable(ReadOnlySpan<byte> body, ReplayCustomWeaponContext context, uint world, bool baseline)
    {
        const int table = 8 * CustomReplicaCodec.SlotSize;
        if (!baseline && body.Length != table || baseline && body.Length < table + 1) return false;
        int count = baseline ? body[table] : 0;
        if (count > 64 || baseline && body.Length != table + 1 + count * CustomReplicaCodec.FlightSize) return false;
        for (int i = 0; i < 8; i++)
            if (!CustomReplicaCodec.ReadSlot(body.Slice(i * CustomReplicaCodec.SlotSize, CustomReplicaCodec.SlotSize), i, context.Validation, out _)) return false;
        for (int i = 0; i < count; i++)
        {
            if (!CustomReplicaCodec.ReadFlight(body.Slice(table + 1 + i * CustomReplicaCodec.FlightSize, CustomReplicaCodec.FlightSize), context.Validation, world, out var flight)) return false;
            if (!CustomReplicaCodec.ReadSlot(body.Slice(flight.Action.Owner.Slot * CustomReplicaCodec.SlotSize, CustomReplicaCodec.SlotSize), flight.Action.Owner.Slot, context.Validation, out var slot)
                || !slot.Occupied || slot.Spectating || slot.Generation != flight.Action.Owner.Generation
                || slot.Life == 0 || unchecked((short)(slot.Life - flight.Action.Owner.Life)) < 0) return false;
            for (int j = 0; j < i; j++)
            {
                CustomReplicaCodec.ReadFlight(body.Slice(table + 1 + j * CustomReplicaCodec.FlightSize, CustomReplicaCodec.FlightSize), context.Validation, world, out var previous);
                if (previous.Action == flight.Action) return false;
            }
        }
        return true;
    }
    internal bool CopyTable(ReplayCustomWeaponContext context, Span<CustomReplicaSlot> slots, Span<CustomReplicaFlight> flights, out CustomReplicaCapture capture)
    {
        capture = default;
        if (Context != context.Digest || Kind is not (ReplayCustomWeaponFactKind.Baseline or ReplayCustomWeaponFactKind.State) || slots.Length < 8) return false;
        var body = _bytes.AsSpan(HeaderSize); int count = Kind == ReplayCustomWeaponFactKind.Baseline ? body[8 * CustomReplicaCodec.SlotSize] : 0;
        if (flights.Length < count) return false;
        for (int i = 0; i < 8; i++) CustomReplicaCodec.ReadSlot(body.Slice(i * CustomReplicaCodec.SlotSize, CustomReplicaCodec.SlotSize), i, context.Validation, out slots[i]);
        for (int i = 0; i < count; i++) CustomReplicaCodec.ReadFlight(body.Slice(8 * CustomReplicaCodec.SlotSize + 1 + i * CustomReplicaCodec.FlightSize, CustomReplicaCodec.FlightSize), context.Validation, World, out flights[i]);
        capture = new(context.Bootstrap.World.Start.MatchId, context.Bootstrap.World.Start.AuthorityEpoch, context.Bootstrap.Packages.MechanicsDigest, World, 8, count); return true;
    }
    internal bool TryLaunch(ReplayCustomWeaponContext context, out CustomReplicaLaunch launch)
    { launch = default; return Context == context.Digest && Kind == ReplayCustomWeaponFactKind.Launch && CustomReplicaCodec.ReadLaunch(_bytes.AsSpan(HeaderSize), context.Validation, World, out launch); }
    internal bool TryContact(ReplayCustomWeaponContext context, out WorldBootstrapIdentity source, out CustomContactOutcome outcome)
    { source = default; outcome = default; return Context == context.Digest && Kind == ReplayCustomWeaponFactKind.Contact && CustomCombatResults.TryReadOutcome(_bytes.AsSpan(HeaderSize), context.Validation.Manifest, context.Definition, out source, out outcome); }
    internal bool TryPickup(ReplayCustomWeaponContext context, out int index, out CustomPickupItemState item)
    {
        index = 0; item = default; if (Context != context.Digest || Kind != ReplayCustomWeaponFactKind.Pickup) return false;
        var body = _bytes.AsSpan(HeaderSize); index = body[0];
        item = new(new(context.PickupWorld, BinaryPrimitives.ReadUInt16LittleEndian(body[2..]), BinaryPrimitives.ReadUInt64LittleEndian(body[4..])),
            BinaryPrimitives.ReadUInt32LittleEndian(body[12..]), body[16] == 1, BinaryPrimitives.ReadUInt64LittleEndian(body[17..])); return true;
    }
}
