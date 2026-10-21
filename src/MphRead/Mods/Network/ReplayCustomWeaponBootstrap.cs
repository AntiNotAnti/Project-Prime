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

internal enum ReplayCustomWeaponOriginKind : byte { Recipient = 1, Authority = 2 }

// Detached file-only contract. Parsing/preparing this value cannot activate a
// match, admit a packet, grant inventory or enable unsupported replay recording.
// Actual archive/checkpoint/custom-fact integration is a subsequent contract.
internal sealed class ReplayCustomWeaponBootstrap
{
    internal const ushort Version = 1, AuthorityVersion = 2;
    internal const int HeaderSize = 146, MaximumBytes = 16 * 1024;
    private readonly byte[] _bytes;
    internal WorldBootstrapIdentity? RecipientBootstrap { get; }
    internal WorldBootstrapIdentity World => RecipientBootstrap
        ?? throw new InvalidOperationException("An authority replay origin is not a recipient bootstrap.");
    internal ReplayCustomWeaponOriginKind OriginKind => RecipientBootstrap.HasValue
        ? ReplayCustomWeaponOriginKind.Recipient : ReplayCustomWeaponOriginKind.Authority;
    internal MatchStartIdentity Start { get; }
    internal uint AuthorityFrame { get; }
    internal ulong PackageConfiguration { get; }
    internal WeaponDigest PickupLayout { get; }
    internal CustomWeaponPackageManifest Packages { get; }
    internal ReplayCustomWeaponCapabilities Capabilities => ReplayCustomWeaponCapabilities.ExactPackages;
    private static bool ValidStart(MatchStartIdentity start)
        => start.MatchId != 0 && start.AuthorityEpoch != 0 && start.StartGeneration != 0;

    internal ReplayCustomWeaponBootstrap(WorldBootstrapIdentity world, ulong packageConfiguration,
        WeaponDigest pickupLayout, CustomWeaponPackageManifest packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        Span<byte> identity = stackalloc byte[WorldBootstrapIdentity.Size];
        world.Write(identity);
        if (!WorldBootstrapIdentity.TryRead(identity, out var validated) || validated != world || !ValidStart(world.Start)
            || packageConfiguration == 0 || !pickupLayout.IsValid
            || packages.Rows.Count != 1 || packages.Rows[0].Weapon.RuntimeId.Value != 10
            || packages.Size > MaximumBytes - HeaderSize)
            throw new ArgumentException("A complete single-package historical weapon bootstrap is required.");
        RecipientBootstrap = world; Start = world.Start; AuthorityFrame = world.AuthorityFrame;
        PackageConfiguration = packageConfiguration;
        PickupLayout = pickupLayout; Packages = packages;
        _bytes = new byte[HeaderSize + packages.Size];
        "CWRB"u8.CopyTo(_bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(6), (ushort)Capabilities);
        identity.CopyTo(_bytes.AsSpan(8, WorldBootstrapIdentity.Size));
        WritePackageFields();
    }

    internal static ReplayCustomWeaponBootstrap ForAuthority(MatchStartIdentity start, uint authorityFrame,
        ulong packageConfiguration, WeaponDigest pickupLayout, CustomWeaponPackageManifest packages)
        => new(start, authorityFrame, packageConfiguration, pickupLayout, packages);

    private ReplayCustomWeaponBootstrap(MatchStartIdentity start, uint authorityFrame,
        ulong packageConfiguration, WeaponDigest pickupLayout, CustomWeaponPackageManifest packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (!ValidStart(start) || authorityFrame == 0 || packageConfiguration == 0 || !pickupLayout.IsValid
            || packages.Rows.Count != 1 || packages.Rows[0].Weapon.RuntimeId.Value != 10
            || packages.Size > MaximumBytes - HeaderSize)
            throw new ArgumentException("A complete authority-origin historical weapon bootstrap is required.");
        Start = start; AuthorityFrame = authorityFrame; PackageConfiguration = packageConfiguration;
        PickupLayout = pickupLayout; Packages = packages;
        _bytes = new byte[HeaderSize + packages.Size];
        "CWRB"u8.CopyTo(_bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(4), AuthorityVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(6), (ushort)Capabilities);
        _bytes[8] = (byte)ReplayCustomWeaponOriginKind.Authority;
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(12), start.MatchId);
        BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(14), start.AuthorityEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(22), start.StartGeneration);
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(26), authorityFrame);
        WritePackageFields();
    }

    private void WritePackageFields()
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(40), PackageConfiguration);
        PickupLayout.Write(_bytes.AsSpan(48, 32));
        Packages.MechanicsDigest.Write(_bytes.AsSpan(80, 32));
        Packages.Digest.Write(_bytes.AsSpan(112, 32));
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(144), checked((ushort)Packages.Size));
        Packages.Write(_bytes.AsSpan(HeaderSize));
    }

    internal byte[] CopyBytes() => (byte[])_bytes.Clone();
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ReplayCustomWeaponBootstrap? bootstrap)
    {
        bootstrap = null;
        if (bytes.Length is < HeaderSize or > MaximumBytes || !bytes[..4].SequenceEqual("CWRB"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) is not (Version or AuthorityVersion)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != (ushort)ReplayCustomWeaponCapabilities.ExactPackages) return false;
        bool authority = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) == AuthorityVersion;
        WorldBootstrapIdentity world = default; MatchStartIdentity start = default; uint frame = 0;
        if (authority)
        {
            if (bytes[8] != (byte)ReplayCustomWeaponOriginKind.Authority
                || bytes.Slice(9, 3).IndexOfAnyExcept((byte)0) >= 0
                || bytes.Slice(30, 10).IndexOfAnyExcept((byte)0) >= 0) return false;
            start = new(BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]),
                BinaryPrimitives.ReadUInt64LittleEndian(bytes[14..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[22..]));
            frame = BinaryPrimitives.ReadUInt32LittleEndian(bytes[26..]);
            if (!ValidStart(start) || frame == 0) return false;
        }
        else if (!WorldBootstrapIdentity.TryRead(bytes.Slice(8, WorldBootstrapIdentity.Size), out world)
            || !ValidStart(world.Start)) return false;
        ulong configuration = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
        var layout = WeaponDigest.Read(bytes[48..80]);
        int size = BinaryPrimitives.ReadUInt16LittleEndian(bytes[144..]);
        if (configuration == 0 || !layout.IsValid || bytes.Length != HeaderSize + size
            || !CustomWeaponPackageManifest.TryRead(bytes[HeaderSize..], out var packages)
            || packages!.Rows.Count != 1 || packages.Rows[0].Weapon.RuntimeId.Value != 10
            || packages.MechanicsDigest != WeaponDigest.Read(bytes[80..112])
            || packages.Digest != WeaponDigest.Read(bytes[112..144])) return false;
        bootstrap = authority ? ForAuthority(start, frame, configuration, layout, packages)
            : new(world, configuration, layout, packages);
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
