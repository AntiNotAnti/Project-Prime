using System;
using System.IO;
using System.Threading;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

/// <summary>Explicit local host configuration, never a path supplied by a network peer.</summary>
public sealed class LocalWeaponPackageSelection
{
    public string StoreDirectory { get; }
    public string ArchiveHash { get; }

    public LocalWeaponPackageSelection(string storeDirectory, string archiveHash)
    {
        if (string.IsNullOrWhiteSpace(storeDirectory) || storeDirectory.Length > 4096
            || System.Linq.Enumerable.Any(storeDirectory, char.IsControl))
            throw new ArgumentException("Expected a bounded local weapon store directory.", nameof(storeDirectory));
        if (!WeaponIdentity.IsDigest(archiveHash) || archiveHash.AsSpan().IndexOfAnyExcept('0') < 0)
            throw new ArgumentException("Expected an exact lowercase archive SHA-256.", nameof(archiveHash));
        StoreDirectory = Path.GetFullPath(storeDirectory);
        _ = new WeaponPackageStore(StoreDirectory); // Validate without creating store state.
        ArchiveHash = archiveHash;
    }

    internal WeaponPackageLease Acquire(CancellationToken cancellation)
        => new WeaponPackageStore(StoreDirectory).AcquireInstalled(ArchiveHash, cancellation);

    internal void AppendArguments(System.Diagnostics.ProcessStartInfo start)
    {
        start.ArgumentList.Add("-weaponstore"); start.ArgumentList.Add(StoreDirectory);
        start.ArgumentList.Add("-weaponarchive"); start.ArgumentList.Add(ArchiveHash);
    }
}
