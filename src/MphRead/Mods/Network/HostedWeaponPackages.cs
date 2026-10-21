using System;
using System.IO;
using System.Threading;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

// Only a bounded preparation worker constructs this proof. Launch consumes an
// independently retained already-validated lease, without re-reading the store.
internal sealed class PreparedLocalWeaponPackage : IDisposable
{
    private WeaponPackageLease? _lease;
    internal LocalWeaponPackageSelection Selection { get; }
    private PreparedLocalWeaponPackage(LocalWeaponPackageSelection selection, WeaponPackageLease lease)
    { Selection = selection; _lease = lease; }

    internal static PreparedLocalWeaponPackage Prepare(string approvedStore, string archiveHash, CancellationToken cancellation)
    {
        var selection = new LocalWeaponPackageSelection(approvedStore, archiveHash);
        var lease = selection.Acquire(cancellation);
        try { cancellation.ThrowIfCancellationRequested(); return new(selection, lease); }
        catch { lease.Dispose(); throw; }
    }
    internal bool Matches(string? approvedStore, string? archiveHash)
        => approvedStore != null && Selection.ArchiveHash == archiveHash
            && (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Equals(Selection.StoreDirectory, approvedStore);
    internal WeaponPackageLease Retain()
    {
        var lease = _lease ?? throw new ObjectDisposedException(nameof(PreparedLocalWeaponPackage));
        if (lease.Package.PackageHash != Selection.ArchiveHash) throw new InvalidDataException("Invalid prepared archive proof.");
        return lease.Retain();
    }
    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
}

internal sealed class PreparedHostRequest : IDisposable
{
    internal HostedMapPreparation? Maps { get; }
    internal PreparedLocalWeaponPackage? Weapon { get; }
    private bool _mapsTransferred;
    internal PreparedHostRequest(HostedMapPreparation? maps, PreparedLocalWeaponPackage? weapon)
    { Maps = maps; Weapon = weapon; }
    internal void TransferMaps() => _mapsTransferred = true;
    public void Dispose()
    {
        Weapon?.Dispose();
        if (!_mapsTransferred) HostedMapRequests.TryDeleteDirectory(Maps?.LibraryPath);
    }
}
