using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace MphRead.Mods.CustomWeapons.Packages;

internal readonly record struct CustomPackageManifestRow(CustomManifestRow Weapon, WeaponDigest Archive,
    WeaponDigest Content, WeaponDigest Dependencies)
{
    internal const int Size = 146;
    internal bool IsValid => Weapon.IsValid && Archive.IsValid && Content.IsValid && Dependencies.IsValid;
    internal static CustomPackageManifestRow For(WeaponRuntimeId runtime, WeaponPackage package) => new(
        new(runtime, package.Project.ContentId, WeaponDigest.FromHex(package.Definition.Mechanics.DefinitionHash)),
        WeaponDigest.FromHex(package.PackageHash), WeaponDigest.FromHex(package.ContentHash), WeaponDigest.FromHex(package.DependencyHash));
    internal void Write(Span<byte> bytes)
    {
        Weapon.Write(bytes[..50]); Archive.Write(bytes[50..82]);
        Content.Write(bytes[82..114]); Dependencies.Write(bytes[114..146]);
    }
}

/// <summary>
/// Detached schema-1 exact package metadata. This does not replace CWM1, mark a
/// player Ready, change a packet, install content or activate a gameplay weapon.
/// </summary>
internal sealed class CustomWeaponPackageManifest
{
    internal const ushort Version = 1;
    internal const int HeaderSize = 40, RowSize = CustomPackageManifestRow.Size;
    internal const int MaximumEntries = WeaponRegistry.MaxCustomWeapons;
    internal const int MaximumBytes = HeaderSize + MaximumEntries * RowSize;
    private readonly byte[] _bytes;
    internal ReadOnlyCollection<CustomPackageManifestRow> Rows { get; }
    internal WeaponDigest MechanicsDigest { get; }
    internal WeaponDigest Digest { get; }
    internal int Size => _bytes.Length;

    internal CustomWeaponPackageManifest(IEnumerable<WeaponPackage> packages) : this(BuildRows(packages)) { }
    private CustomWeaponPackageManifest(CustomPackageManifestRow[] rows)
    {
        Rows = Array.AsReadOnly(rows);
        MechanicsDigest = CustomWeaponManifest.ComputeDigest(rows.Select(row => row.Weapon).ToArray());
        _bytes = new byte[HeaderSize + rows.Length * RowSize];
        "CWPK"u8.CopyTo(_bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(6), (ushort)rows.Length);
        MechanicsDigest.Write(_bytes.AsSpan(8, 32));
        for (int i = 0; i < rows.Length; i++) rows[i].Write(_bytes.AsSpan(HeaderSize + i * RowSize, RowSize));
        Digest = WeaponDigest.Read(SHA256.HashData(_bytes));
    }
    private static CustomPackageManifestRow[] BuildRows(IEnumerable<WeaponPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var input = new List<WeaponPackage>();
        foreach (var package in packages)
        {
            if (package == null || input.Count == MaximumEntries) throw new ArgumentException("Invalid/excessive package manifest.", nameof(packages));
            input.Add(package);
        }
        // Reuse the frozen mechanics identity/sort/range contract unchanged.
        var mechanics = new CustomWeaponManifest(input.Select(package => package.Definition));
        input.Sort((a, b) => StringComparer.Ordinal.Compare(a.Definition.Identity.ToString(), b.Definition.Identity.ToString()));
        var rows = new CustomPackageManifestRow[input.Count];
        for (int i = 0; i < rows.Length; i++) rows[i] = CustomPackageManifestRow.For(mechanics.Rows[i].RuntimeId, input[i]);
        return rows;
    }
    internal byte[] CopyBytes() => (byte[])_bytes.Clone();
    internal void Write(Span<byte> destination)
    {
        if (destination.Length < Size) throw new ArgumentException("Complete package manifest buffer required.", nameof(destination));
        _bytes.CopyTo(destination);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out CustomWeaponPackageManifest? manifest)
    {
        manifest = null;
        if (bytes.Length is < HeaderSize + RowSize or > MaximumBytes || !bytes[..4].SequenceEqual("CWPK"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != Version) return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        if (count is < 1 or > MaximumEntries || bytes.Length != HeaderSize + count * RowSize) return false;
        var expectedMechanics = WeaponDigest.Read(bytes[8..40]);
        if (!expectedMechanics.IsValid) return false;
        var rows = new CustomPackageManifestRow[count];
        var ids = new HashSet<Guid>();
        string? previous = null;
        for (int i = 0; i < count; i++)
        {
            var row = bytes.Slice(HeaderSize + i * RowSize, RowSize);
            if (!CustomManifestRow.TryRead(row[..50], out var weapon) || weapon.RuntimeId.Value != 10 + i || !ids.Add(weapon.ContentId)) return false;
            string identity = weapon.Identity.ToString();
            if (previous != null && StringComparer.Ordinal.Compare(previous, identity) >= 0) return false;
            previous = identity;
            rows[i] = new(weapon, WeaponDigest.Read(row[50..82]), WeaponDigest.Read(row[82..114]), WeaponDigest.Read(row[114..146]));
            if (!rows[i].IsValid) return false;
        }
        if (CustomWeaponManifest.ComputeDigest(rows.Select(row => row.Weapon).ToArray()) != expectedMechanics) return false;
        var candidate = new CustomWeaponPackageManifest(rows);
        if (!candidate._bytes.AsSpan().SequenceEqual(bytes)) return false;
        manifest = candidate; return true;
    }
}

/// <summary>Retains exact verified bytes for a future match or historical replay owner.</summary>
internal sealed class WeaponMatchPackageBinding : IDisposable
{
    private WeaponPackageLease[]? _leases;
    private readonly WeaponPackageResources[]? _resources;
    internal CustomWeaponPackageManifest Manifest { get; }
    private WeaponMatchPackageBinding(CustomWeaponPackageManifest manifest, WeaponPackageLease[] leases, WeaponPackageResources[]? resources = null)
    { Manifest = manifest; _leases = leases; _resources = resources; }
    private WeaponPackageLease[] Active => _leases ?? throw new ObjectDisposedException(nameof(WeaponMatchPackageBinding));
    internal int Count => Active.Length;
    internal WeaponPackage Package(int index) => Active[index].Package;
    internal WeaponPackageResources Resources(int index)
    {
        _ = Active;
        return _resources?[index] ?? throw new InvalidOperationException("Package presentation resources have not been prepared.");
    }
    internal WeaponMatchPackageBinding Retain()
    {
        var leases = Active;
        var retained = new List<WeaponPackageLease>(leases.Length);
        try
        {
            foreach (var lease in leases) retained.Add(lease.Retain());
            return new(Manifest, retained.ToArray(), _resources);
        }
        catch { foreach (var lease in retained) lease.Dispose(); throw; }
    }
    internal WeaponDefinition Definition(WeaponRuntimeId runtime)
    {
        int index = runtime.Value - 10;
        if ((uint)index >= Active.Length) throw new ArgumentOutOfRangeException(nameof(runtime));
        return Active[index].Package.Definition;
    }
    internal static WeaponMatchPackageBinding BindInstalled(WeaponPackageStore store, CustomWeaponPackageManifest manifest,
        CancellationToken cancellationToken = default) => Bind(store, manifest, false, cancellationToken);
    internal static WeaponMatchPackageBinding BindHistorical(WeaponPackageStore store, CustomWeaponPackageManifest manifest,
        CancellationToken cancellationToken = default) => Bind(store, manifest, true, cancellationToken);
    internal static WeaponMatchPackageBinding PrepareInstalled(WeaponPackageStore store, CustomWeaponPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        using var verified = BindInstalled(store, manifest, cancellationToken);
        var resources = new WeaponPackageResources[verified.Count];
        for (int i = 0; i < resources.Length; i++) resources[i] = WeaponPackageResources.Prepare(verified.Package(i), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Transfer the verified owner's handles only after all resources succeed.
        return new(manifest, Interlocked.Exchange(ref verified._leases, null)!, resources);
    }
    private static WeaponMatchPackageBinding Bind(WeaponPackageStore store, CustomWeaponPackageManifest manifest,
        bool historical, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        var leases = new List<WeaponPackageLease>(manifest.Rows.Count);
        try
        {
            foreach (var row in manifest.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lease = historical ? store.Acquire(row.Archive.ToString(), cancellationToken)
                    : store.AcquireInstalled(row.Archive.ToString(), cancellationToken);
                leases.Add(lease);
                if (CustomPackageManifestRow.For(row.Weapon.RuntimeId, lease.Package) != row)
                    throw new InvalidDataException("Exact weapon package metadata does not match retained bytes.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(manifest, leases.ToArray());
        }
        catch { foreach (var lease in leases) lease.Dispose(); throw; }
    }
    public void Dispose()
    {
        var leases = Interlocked.Exchange(ref _leases, null);
        if (leases != null) foreach (var lease in leases) lease.Dispose();
    }
}
