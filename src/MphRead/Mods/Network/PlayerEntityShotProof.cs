using MphRead.Mods.Network;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    // Called only after the engine rebuilds the spawn loadout. Never sample these
    // fields after ApplyIntent, which may contain client-reported inventory.
    private void ModSeedShotProof()
    {
        ushort weapons=0,charges=0;
        for(int i=0;i<9;i++)
        {
            if(_availableWeapons[(BeamType)i])weapons|=(ushort)(1<<i);
            if(_availableCharges[(BeamType)i])charges|=(ushort)(1<<i);
        }
        NetShotProof.Seed(this,new ShotProofSource(weapons,charges,_ammo[0],_ammo[1],
            EquipInfo.InfiniteAmmo,Hunter,IsPrimeHunter,DoubleDamage,
            !_scene.GameState.BalancedMode && !_scene.GameState.OneInTheChamber
                && _scene.GameState.Mode!=GameMode.GunGame && !Mods.EnhancedHunters.EnhancedHunters.Enabled(this)));
    }
}
