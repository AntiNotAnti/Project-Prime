using System;
using System.Collections.Generic;
using MphRead.Mods.CustomWeapons;

namespace MphRead;

public partial class Scene
{
    // No native-only allocation. No input, UI, save, replay or wire entry point
    // activates this foundation; gameplay admission is a later explicit gate.
    internal OfflineCustomWeaponRuntime? OfflineWeapons { get; private set; }
    internal OfflineCustomWeaponRuntime EnableDeveloperRifle(bool optIn)
    {
        if (OfflineWeapons != null) throw new InvalidOperationException("Developer rifle already enabled.");
        return OfflineWeapons = new(this,optIn);
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
        OfflineWeapons?.Dispose(); OfflineWeapons = null;
        CustomWeapons?.Dispose();
        CustomWeapons = null;
    }
}
