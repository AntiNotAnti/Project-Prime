using System;
using MphRead.Mods.CustomWeapons.Packages;
using MphRead.Mods.Network;

namespace MphRead;

public partial class Scene
{
    private WeaponMatchPackageBinding? _replayWeaponPackage;
    private ReplayCustomWeaponDescriptor? _replayWeaponDescriptor;
    internal bool HasReplayWeaponPackage(ReplayCustomWeaponDescriptor descriptor)
        => !_exiting && _replayWeaponPackage != null && _weaponPackageResources != null && _replayWeaponDescriptor != null
            && (ReferenceEquals(_replayWeaponDescriptor, descriptor)
                || _replayWeaponDescriptor.CopyBytes().AsSpan().SequenceEqual(descriptor.CopyBytes()));
    internal void BindReplayWeaponPackage(PreparedReplayWeaponPackage prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (_exiting || FrameCount != 0 || Room != null || Services is not ReplaySceneServices services
            || services.State.CustomWeapons is not { CompleteForRestore: true } state || services.State.CustomWeaponDescriptor == null
            || !services.State.CustomWeaponDescriptor.CopyBytes().AsSpan().SequenceEqual(prepared.Descriptor.CopyBytes())
            || state.Context.Digest != prepared.Context.Digest || _replayWeaponPackage != null || _onlineWeaponPackage != null
            || _localWeaponLease != null || OfflineWeapons != null || CustomCombat != null || PirateBlaster != null || CustomWeapons != null)
            throw new InvalidOperationException("Historical resources require a fresh isolated replay Scene with its exact custom checkpoint.");
        var owned = prepared.Retain(); // Atomic independent OS leases; no file access or decoding here.
        try
        {
            var resources = owned.Resources(0);
            ApplyCustomRiflePackageResources(resources);
            _weaponPackageResources = resources;
            _replayWeaponDescriptor = services.State.CustomWeaponDescriptor;
            _replayWeaponPackage = owned;
            // Scene ownership outlives a stopped decoder; keep native capture closed for this Scene's lifetime.
            CustomReplayUnsupported = true;
        }
        catch { owned.Dispose(); throw; }
    }
    private void ReleaseReplayWeaponPackage()
    {
        _replayWeaponPackage?.Dispose(); _replayWeaponPackage = null; _replayWeaponDescriptor = null;
    }
}
