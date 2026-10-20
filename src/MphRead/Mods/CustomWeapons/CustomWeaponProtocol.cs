using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Bounded v1 payloads for the explicit developer lane. Endpoint/bootstrap,
// readiness, source and resource authority are enforced by their live owners.
internal static class CustomWeaponProtocol
{
    internal const byte Version = 1;
    internal const int HeaderSize = 1 + WorldBootstrapIdentity.Size + WeaponDigest.Size;
    internal static bool ValidBootstrap(WorldBootstrapIdentity value) => value.Start.MatchId != 0
        && value.Start.AuthorityEpoch != 0 && value.Start.StartGeneration != 0 && value.Revision != 0
        && value.SlotGeneration != 0 && value.AuthorityFrame != 0 && value.SlowRevision != 0 && value.WorldRevision != 0;
    internal static bool ReadHeader(ReadOnlySpan<byte> bytes, out WorldBootstrapIdentity bootstrap, out WeaponDigest digest)
    {
        bootstrap = default; digest = default;
        if (bytes.Length < HeaderSize || bytes[0] != Version
            || !WorldBootstrapIdentity.TryRead(bytes.Slice(1, WorldBootstrapIdentity.Size), out var candidate)) return false;
        var candidateDigest = WeaponDigest.Read(bytes.Slice(33, WeaponDigest.Size));
        if (!ValidBootstrap(candidate) || !candidateDigest.IsValid) return false;
        bootstrap = candidate; digest = candidateDigest; return true;
    }
    internal static void WriteHeader(Span<byte> bytes, WorldBootstrapIdentity bootstrap, WeaponDigest digest)
    {
        bytes[0] = Version; bootstrap.Write(bytes[1..33]); digest.Write(bytes[33..65]);
    }
    internal static void WriteVector(Span<byte> bytes, Vector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], value.Z);
    }
    internal static Vector3 ReadVector(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
}

internal enum CustomWeaponActionKind : byte { Fire = 1, Reload, Optic, Equip, Unequip }

/// <summary>Intent only, with no damage/ammunition claims. Bootstrap belongs to the
/// sending occupant; ingress must derive Slot from the authenticated peer, compare
/// the exact bootstrap/digest, then apply source-time pose/resource admission.</summary>
internal readonly record struct CustomWeaponAction(WorldBootstrapIdentity Bootstrap, WeaponDigest Manifest,
    byte Slot, ushort Life, WeaponRuntimeId Weapon, uint Sequence, uint SourceFrame, uint AckFrame,
    byte AckSubFrame, CustomWeaponActionKind Kind, byte Optic, Vector3 Origin, Vector3 Direction)
{
    internal const int Size = 109;
    internal WeaponOwnerContext Owner => new(Bootstrap.Start.MatchId, Bootstrap.Start.AuthorityEpoch,
        Slot, Bootstrap.SlotGeneration, Life);
    internal bool IsValid
    {
        get
        {
            if (!CustomWeaponProtocol.ValidBootstrap(Bootstrap) || !Manifest.IsValid || Slot >= 8 || Life == 0
                || Weapon.Value is < 10 or > 73 || Sequence == 0 || (byte)Kind is < 1 or > 5
                || Optic > 2) return false;
            if (Kind != CustomWeaponActionKind.Fire)
                return Origin == Vector3.Zero && Direction == Vector3.Zero && AckFrame == 0 && AckSubFrame == 0
                    && (Kind == CustomWeaponActionKind.Optic || Optic == 0);
            return float.IsFinite(Origin.X) && float.IsFinite(Origin.Y) && float.IsFinite(Origin.Z)
                && MathF.Abs(Origin.X) <= 1_000_000 && MathF.Abs(Origin.Y) <= 1_000_000 && MathF.Abs(Origin.Z) <= 1_000_000
                && float.IsFinite(Direction.LengthSquared) && MathF.Abs(Direction.LengthSquared-1) <= .02f;
        }
    }
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length < Size || !IsValid) throw new ArgumentException("Valid action and complete buffer required.");
        CustomWeaponProtocol.WriteHeader(bytes, Bootstrap, Manifest);
        bytes[65] = Slot; BinaryPrimitives.WriteUInt16LittleEndian(bytes[66..], Life);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[68..], Weapon.Value);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[70..], Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[74..], SourceFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[78..], AckFrame);
        bytes[82] = AckSubFrame; bytes[83] = (byte)Kind; bytes[84] = Optic;
        CustomWeaponProtocol.WriteVector(bytes[85..], Origin); CustomWeaponProtocol.WriteVector(bytes[97..], Direction);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out CustomWeaponAction action)
    {
        action = default;
        if (bytes.Length != Size || !CustomWeaponProtocol.ReadHeader(bytes, out var bootstrap, out var digest)) return false;
        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(bytes[68..]);
        if (id is < 10 or > 73) return false;
        var candidate = new CustomWeaponAction(bootstrap, digest, bytes[65], BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..]),
            new(id), BinaryPrimitives.ReadUInt32LittleEndian(bytes[70..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[74..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[78..]), bytes[82], (CustomWeaponActionKind)bytes[83], bytes[84],
            CustomWeaponProtocol.ReadVector(bytes[85..]), CustomWeaponProtocol.ReadVector(bytes[97..]));
        if (!candidate.IsValid) return false;
        action = candidate; return true;
    }
}

/// <summary>A distinct domain, deliberately without a conversion to native ShotKey.
/// Every future custom proof/payment join must retain this whole identity.</summary>
internal readonly record struct CustomActionIdentity(WeaponOwnerContext Owner, WeaponDigest Manifest,
    WeaponRuntimeId Weapon, uint Sequence)
{
    internal bool IsValid => Owner.IsValid && Manifest.IsValid && Weapon.Value is >= 10 and <= 73 && Sequence != 0;
    internal static CustomActionIdentity For(CustomWeaponAction action)
        => action.IsValid ? new(action.Owner, action.Manifest, action.Weapon, action.Sequence)
            : throw new ArgumentException("Valid custom action required.", nameof(action));
}

internal readonly record struct CustomWeaponReady(WorldBootstrapIdentity Bootstrap, WeaponDigest Manifest, byte Slot,
    ushort Schema, ushort Behavior, ushort Capabilities)
{
    internal const ushort PrecisionCapability = 1;
    internal const int Size = 72;
    internal bool IsValid => CustomWeaponProtocol.ValidBootstrap(Bootstrap) && Manifest.IsValid && Slot < 8
        && Schema == WeaponMechanics.SchemaVersion && Behavior == WeaponMechanics.BehaviorVersion && Capabilities == PrecisionCapability;
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length < Size || !IsValid) throw new ArgumentException("Valid readiness and complete buffer required.");
        CustomWeaponProtocol.WriteHeader(bytes, Bootstrap, Manifest); bytes[65] = Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[66..], Schema);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[68..], Behavior);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[70..], Capabilities);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out CustomWeaponReady ready)
    {
        ready = default;
        if (bytes.Length != Size || !CustomWeaponProtocol.ReadHeader(bytes, out var bootstrap, out var digest)) return false;
        var candidate = new CustomWeaponReady(bootstrap, digest, bytes[65], BinaryPrimitives.ReadUInt16LittleEndian(bytes[66..]),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[68..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[70..]));
        if (!candidate.IsValid) return false;
        ready = candidate; return true;
    }
}

/// <summary>At most four pages of sixteen rows. Each page fits one ordinary datagram;
/// parsing never trusts a caller-provided count before checking exact payload size.</summary>
internal sealed class CustomManifestPage
{
    internal const int RowsPerPage = 16, PrefixSize = 68, MaxSize = PrefixSize + RowsPerPage*CustomManifestRow.Size;
    internal WorldBootstrapIdentity Bootstrap { get; }
    internal WeaponDigest Manifest { get; }
    internal byte TotalRows { get; }
    internal byte Index { get; }
    internal ReadOnlyCollection<CustomManifestRow> Rows { get; }
    internal int Size => PrefixSize + Rows.Count*CustomManifestRow.Size;
    internal int PageCount => (TotalRows+RowsPerPage-1)/RowsPerPage;
    private static bool Shape(int total, int index, int count) => total is >= 1 and <= CustomWeaponManifest.MaxEntries
        && index >= 0 && index < (total+RowsPerPage-1)/RowsPerPage
        && count == Math.Min(RowsPerPage, total-index*RowsPerPage);
    private static bool ValidRows(int index, ReadOnlySpan<CustomManifestRow> rows)
    {
        for (int i = 0; i < rows.Length; i++)
            if (!rows[i].IsValid || rows[i].RuntimeId.Value != 10+index*RowsPerPage+i
                || i > 0 && rows[i-1].ContentId.CompareTo(rows[i].ContentId) >= 0) return false;
        return true;
    }
    internal CustomManifestPage(WorldBootstrapIdentity bootstrap, WeaponDigest manifest, byte total, byte index,
        ReadOnlySpan<CustomManifestRow> rows)
    {
        if (!CustomWeaponProtocol.ValidBootstrap(bootstrap) || !manifest.IsValid || !Shape(total,index,rows.Length) || !ValidRows(index,rows))
            throw new ArgumentException("Invalid custom manifest page.");
        Bootstrap = bootstrap; Manifest = manifest; TotalRows = total; Index = index;
        Rows = Array.AsReadOnly(rows.ToArray());
    }
    internal void Write(Span<byte> bytes)
    {
        if (bytes.Length < Size) throw new ArgumentException("Complete page buffer required.", nameof(bytes));
        CustomWeaponProtocol.WriteHeader(bytes, Bootstrap, Manifest);
        bytes[65] = TotalRows; bytes[66] = Index; bytes[67] = (byte)Rows.Count;
        for (int i = 0; i < Rows.Count; i++) Rows[i].Write(bytes[(PrefixSize+i*CustomManifestRow.Size)..]);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out CustomManifestPage? page)
    {
        page = null;
        if (bytes.Length < PrefixSize || bytes.Length > MaxSize
            || !CustomWeaponProtocol.ReadHeader(bytes, out var bootstrap, out var digest)) return false;
        int count = bytes[67];
        if (!Shape(bytes[65],bytes[66],count) || bytes.Length != PrefixSize+count*CustomManifestRow.Size) return false;
        Span<CustomManifestRow> rows = stackalloc CustomManifestRow[RowsPerPage];
        for (int i = 0; i < count; i++)
            if (!CustomManifestRow.TryRead(bytes.Slice(PrefixSize+i*CustomManifestRow.Size,CustomManifestRow.Size),out rows[i])) return false;
        if (!ValidRows(bytes[66],rows[..count])) return false;
        page = new(bootstrap,digest,bytes[65],bytes[66],rows[..count]); return true;
    }
}

internal enum CustomManifestReceipt : byte { Accepted, Duplicate, Complete, Refused, Invalidated }

/// <summary>One peer/bootstrap, fixed storage, arbitrary page order, no partial readiness.
/// A corrupt complete manifest permanently closes this instance until fresh admission.</summary>
internal sealed class CustomManifestAssembler
{
    private readonly WorldBootstrapIdentity _bootstrap;
    private readonly WeaponDigest _digest;
    private readonly CustomManifestRow[] _rows = new CustomManifestRow[CustomWeaponManifest.MaxEntries];
    private byte _mask, _total;
    private bool _failed;
    internal bool IsComplete { get; private set; }
    internal CustomManifestAssembler(WorldBootstrapIdentity bootstrap, WeaponDigest digest)
    {
        if (!CustomWeaponProtocol.ValidBootstrap(bootstrap) || !digest.IsValid) throw new ArgumentException("Exact admission identity required.");
        _bootstrap = bootstrap; _digest = digest;
    }
    internal CustomManifestReceipt Receive(CustomManifestPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (_failed) return CustomManifestReceipt.Invalidated;
        if (page.Bootstrap != _bootstrap || page.Manifest != _digest || _total != 0 && page.TotalRows != _total)
            return CustomManifestReceipt.Refused;
        int offset = page.Index*CustomManifestPage.RowsPerPage;
        bool duplicate = (_mask & (1 << page.Index)) != 0;
        if (duplicate)
        {
            for (int i = 0; i < page.Rows.Count; i++)
                if (_rows[offset+i] != page.Rows[i]) return CustomManifestReceipt.Refused;
            return CustomManifestReceipt.Duplicate;
        }
        _total = page.TotalRows;
        for (int i = 0; i < page.Rows.Count; i++) _rows[offset+i] = page.Rows[i];
        _mask |= (byte)(1 << page.Index);
        if (_mask != (1 << page.PageCount)-1) return CustomManifestReceipt.Accepted;
        for (int i = 1; i < _total; i++)
            if (_rows[i-1].ContentId.CompareTo(_rows[i].ContentId) >= 0) return Fail();
        if (CustomWeaponManifest.ComputeDigest(_rows.AsSpan(0,_total)) != _digest) return Fail();
        IsComplete = true; return CustomManifestReceipt.Complete;
    }
    private CustomManifestReceipt Fail()
    { _failed = true; IsComplete = false; Array.Clear(_rows); return CustomManifestReceipt.Invalidated; }
    internal bool CanResolve(WeaponRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!IsComplete || registry.IsDisposed || registry.Count != 9+_total) return false;
        for (int i = 0; i < _total; i++)
            if (!registry.TryResolve(_rows[i].RuntimeId,out var entry) || entry?.Custom?.Identity != _rows[i].Identity) return false;
        return true;
    }
}
