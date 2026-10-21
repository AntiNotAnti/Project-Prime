using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

[Flags]
internal enum ReplayCustomWeaponCapabilities : ushort
{
    ExactPackages = 1
}

// Detached file-only contract. Parsing/preparing this value cannot activate a
// match, admit a packet, grant inventory or enable unsupported replay recording.
// Actual archive/checkpoint/custom-fact integration is a subsequent contract.
internal sealed class ReplayCustomWeaponBootstrap
{
    internal const ushort Version = 1;
    internal const int HeaderSize = 146, MaximumBytes = 16 * 1024;
    private readonly byte[] _bytes;
    internal WorldBootstrapIdentity World { get; }
    internal ulong PackageConfiguration { get; }
    internal WeaponDigest PickupLayout { get; }
    internal CustomWeaponPackageManifest Packages { get; }
    internal ReplayCustomWeaponCapabilities Capabilities => ReplayCustomWeaponCapabilities.ExactPackages;

    internal ReplayCustomWeaponBootstrap(WorldBootstrapIdentity world, ulong packageConfiguration,
        WeaponDigest pickupLayout, CustomWeaponPackageManifest packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        Span<byte> identity = stackalloc byte[WorldBootstrapIdentity.Size];
        world.Write(identity);
        if (!WorldBootstrapIdentity.TryRead(identity, out var validated) || validated != world
            || packageConfiguration == 0 || !pickupLayout.IsValid
            || packages.Rows.Count != 1 || packages.Rows[0].Weapon.RuntimeId.Value != 10
            || packages.Size > MaximumBytes - HeaderSize)
            throw new ArgumentException("A complete single-package historical weapon bootstrap is required.");
        World = world; PackageConfiguration = packageConfiguration;
        PickupLayout = pickupLayout; Packages = packages;
        _bytes = new byte[HeaderSize + packages.Size];
        "CWRB"u8.CopyTo(_bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(6), (ushort)Capabilities);
        identity.CopyTo(_bytes.AsSpan(8, WorldBootstrapIdentity.Size));
        BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(40), packageConfiguration);
        pickupLayout.Write(_bytes.AsSpan(48, 32));
        packages.MechanicsDigest.Write(_bytes.AsSpan(80, 32));
        packages.Digest.Write(_bytes.AsSpan(112, 32));
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(144), checked((ushort)packages.Size));
        packages.Write(_bytes.AsSpan(HeaderSize));
    }

    internal byte[] CopyBytes() => (byte[])_bytes.Clone();
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ReplayCustomWeaponBootstrap? bootstrap)
    {
        bootstrap = null;
        if (bytes.Length is < HeaderSize or > MaximumBytes || !bytes[..4].SequenceEqual("CWRB"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != Version
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != (ushort)ReplayCustomWeaponCapabilities.ExactPackages
            || !WorldBootstrapIdentity.TryRead(bytes.Slice(8, WorldBootstrapIdentity.Size), out var world)) return false;
        ulong configuration = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
        var layout = WeaponDigest.Read(bytes[48..80]);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(bytes[144..]);
        if (configuration == 0 || !layout.IsValid || bytes.Length != HeaderSize + size
            || !CustomWeaponPackageManifest.TryRead(bytes[HeaderSize..], out var packages)
            || packages!.Rows.Count != 1 || packages.Rows[0].Weapon.RuntimeId.Value != 10
            || packages.MechanicsDigest != WeaponDigest.Read(bytes[80..112])
            || packages.Digest != WeaponDigest.Read(bytes[112..144])) return false;
        bootstrap = new(world, configuration, layout, packages);
        return true;
    }

    // Worker-owned CPU preparation. Historical archives need no installed
    // marker, but must still match every recorded identity and retain exact OS
    // leases. Missing bytes never fall back to current definitions or download.
    internal WeaponMatchPackageBinding PrepareHistorical(WeaponPackageStore store,
        CancellationToken cancellationToken = default)
    {
        try { return WeaponMatchPackageBinding.PrepareHistorical(store, Packages, cancellationToken); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("The exact historical weapon package is unavailable or invalid. Restore its recorded archive revision to view this replay.", error);
        }
    }
}
