using System;
using System.Collections.Generic;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead;

public partial class Scene
{
    // A client scene remains unsupported by native replay after readiness loss,
    // disconnect or runtime retirement. Only a new Scene starts with this clear.
    internal bool CustomReplayUnsupported { get; set; }
    // No native-only allocation. No input, UI, save, replay or wire entry point
    // activates this foundation; gameplay admission is a later explicit gate.
    internal OfflineCustomWeaponRuntime? OfflineWeapons { get; private set; }
    private WeaponPackageLease? _localWeaponLease;
    private WeaponMatchPackageBinding? _onlineWeaponPackage;
    private Mods.Network.MatchStartIdentity _onlineWeaponPackageStart;
    private WeaponPackageResources? _weaponPackageResources;
    internal WeaponPackage? LocalWeaponPackage => _localWeaponLease?.Package ?? _onlineWeaponPackage?.Package(0) ?? _replayWeaponPackage?.Package(0);
    internal bool HasOnlineWeaponPackage(Mods.Network.MatchStartIdentity start, WeaponDigest manifest)
        => !_exiting && _onlineWeaponPackageStart == start && _onlineWeaponPackage?.Manifest.Digest == manifest
            && _weaponPackageResources != null;
    internal void BindOnlineWeaponPackage(WeaponMatchPackageBinding prepared, Mods.Network.MatchStartIdentity start)
    {
        if (_exiting || !Mods.Network.NetSession.IsClient || Mods.Network.NetSession.IsAuthority
            || Services.IsReplica || Mods.Network.DemoPlayback.IsActive || IsReplayLab || AimTrainer != null
            || OfflineWeapons != null || CustomCombat != null || PirateBlaster != null || _localWeaponLease != null
            || prepared.Count != 1 || start.StartGeneration == 0 || start.MatchId != Mods.Network.NetSession.CurrentMatchId
            || start.AuthorityEpoch != Mods.Network.NetSession.AuthorityEpoch)
            throw new InvalidOperationException("A prepared exact package requires its ordinary client scene and current match.");
        if (_onlineWeaponPackageStart == start && _onlineWeaponPackage?.Manifest.Digest == prepared.Manifest.Digest) return;
        var resources = prepared.Resources(0);
        var owned = prepared.Retain(); // No I/O or decoding; scene independently keeps the OS lease.
        try
        {
            ReleaseCustomWeaponPresentation();
            ReleaseCustomWeaponInput();
        }
        catch { owned.Dispose(); throw; }
        _onlineWeaponPackage?.Dispose();
        _onlineWeaponPackage = owned; _onlineWeaponPackageStart = start;
        _weaponPackageResources = resources;
        ApplyCustomRiflePackageResources(resources);
        PrewarmCustomRifleAudio();
    }
    // Explicit local sandbox activation. The store operation alone never calls
    // this method. A scene must bind exact resources before its first fixed tick.
    internal OfflineCustomWeaponRuntime EnableOfflinePackageAvailability(WeaponPackageStore store, string packageHash, bool optIn)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!optIn || FrameCount != 0 || _exiting || OfflineWeapons != null || CustomCombat != null
            || PirateBlaster != null || CustomWeapons != null || _localWeaponLease != null
            || Mods.Network.NetSession.Active || Mods.Network.DemoPlayback.IsActive || Services.IsReplica || IsReplayLab || AimTrainer != null)
            throw new InvalidOperationException("Package playback requires explicit opt-in on a fresh offline scene.");
        Mods.Network.CustomReplayBoundary.RequireActivationAllowed();
        var lease = store.AcquireInstalled(packageHash);
        try
        {
            _localWeaponLease = lease;
            PrepareCustomRiflePackageResources();
            var runtime = new OfflineCustomWeaponRuntime(this, true, publicAvailability: true, definition: lease.Package.Definition);
            OfflineWeapons = runtime;
            PrewarmCustomRifleAudio();
            return runtime;
        }
        catch
        {
            ReleaseCustomWeaponRegistry();
            lease.Dispose();
            throw;
        }
    }
    internal TrooperPirateBlasterRuntime? PirateBlaster { get; private set; }
    internal void EnablePirateBlaster(Entities.PlayerEntity player)
    {
        if (PirateBlaster != null || OfflineWeapons != null || CustomCombat != null)
            throw new InvalidOperationException("Custom combat already enabled.");
        PirateBlaster = new(this, player);
    }
    internal OfflineCustomWeaponRuntime EnableDeveloperRifle(bool optIn)
    {
        if (OfflineWeapons != null || PirateBlaster != null) throw new InvalidOperationException("Developer rifle already enabled.");
        if (optIn) Mods.Network.CustomReplayBoundary.RequireActivationAllowed();
        return OfflineWeapons = new(this,optIn);
    }
    // The launch plan has already frozen and validated availability. Capture
    // this scene's human now; each life starts empty and contact funds ownership.
    internal OfflineCustomWeaponRuntime EnableOfflineRifleAvailability(bool optIn)
    {
        if (!optIn) throw new InvalidOperationException("Explicit Riftline M7 availability is required.");
        if (OfflineWeapons != null || CustomCombat != null || PirateBlaster != null) throw new InvalidOperationException("Custom combat already enabled.");
        Mods.Network.CustomReplayBoundary.RequireActivationAllowed();
        return OfflineWeapons = new(this,optIn,publicAvailability:true);
    }
    internal CustomAuthorityCombatAdapter? CustomCombat { get; private set; }
    internal CustomAuthorityCombatAdapter EnableDeveloperCustomCombat(bool optIn, ICustomSourceAdmission sourceAdmission)
    {
        if (OfflineWeapons != null || CustomCombat != null || PirateBlaster != null) throw new InvalidOperationException("Custom combat already enabled.");
        if (optIn) Mods.Network.CustomReplayBoundary.RequireActivationAllowed();
        return CustomCombat = new(this,optIn,sourceAdmission);
    }
    internal CustomAuthorityCombatAdapter EnableDeveloperEngineCustomCombat(bool optIn,ICustomSourceReadiness readiness,ICustomCombatDelivery? delivery=null,bool pickupOnly=false,WeaponDefinition? definition=null)
    {
        if (OfflineWeapons != null || CustomCombat != null || PirateBlaster != null) throw new InvalidOperationException("Custom combat already enabled.");
        if (optIn) Mods.Network.CustomReplayBoundary.RequireActivationAllowed();
        return CustomCombat = new(this,optIn,readiness,delivery,pickupOnly,definition);
    }
    internal WeaponRegistry? CustomWeapons { get; private set; }
    internal WeaponRegistry FreezeCustomWeaponRegistry(IEnumerable<WeaponDefinition> definitions)
    {
        if (_exiting) throw new ObjectDisposedException(nameof(Scene));
        if (CustomWeapons != null) throw new InvalidOperationException("Weapon registry is already frozen.");
        return CustomWeapons = new WeaponRegistry(GameState.Multiplayer, definitions);
    }
    private void ReleaseCustomWeaponRegistry()
    {
        ReleaseCustomWeaponPresentation();
        ReleaseCustomWeaponInput();
        PirateBlaster?.Dispose(); PirateBlaster = null;
        OfflineWeapons?.Dispose(); OfflineWeapons = null;
        CustomCombat?.Dispose(); CustomCombat = null;
        CustomWeapons?.Dispose();
        CustomWeapons = null;
        _localWeaponLease?.Dispose(); _localWeaponLease = null;
        _onlineWeaponPackage?.Dispose(); _onlineWeaponPackage = null; _onlineWeaponPackageStart = default;
        ReleaseReplayWeaponPackage();
        _weaponPackageResources = null;
    }
}
