using System;
using System.Collections.Generic;
using MphRead.Mods.CustomWeapons;

namespace MphRead;

public partial class Scene
{
    // A client scene remains unsupported by native replay after readiness loss,
    // disconnect or runtime retirement. Only a new Scene starts with this clear.
    internal bool CustomReplayUnsupported { get; set; }
    // No native-only allocation. No input, UI, save, replay or wire entry point
    // activates this foundation; gameplay admission is a later explicit gate.
    internal OfflineCustomWeaponRuntime? OfflineWeapons { get; private set; }
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
    internal CustomAuthorityCombatAdapter EnableDeveloperEngineCustomCombat(bool optIn,ICustomSourceReadiness readiness,ICustomCombatDelivery? delivery=null,bool pickupOnly=false)
    {
        if (OfflineWeapons != null || CustomCombat != null || PirateBlaster != null) throw new InvalidOperationException("Custom combat already enabled.");
        if (optIn) Mods.Network.CustomReplayBoundary.RequireActivationAllowed();
        return CustomCombat = new(this,optIn,readiness,delivery,pickupOnly);
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
    }
}
