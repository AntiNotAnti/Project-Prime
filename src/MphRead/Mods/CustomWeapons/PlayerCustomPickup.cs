using MphRead.Sound;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Selection priority only; the custom weapon never adopts a native ID or
    // accesses native ammo/availability storage through its runtime identity.
    internal bool ModCustomPickupOutranksCurrent()
        => _scene.WeaponRules[(int)BeamType.Imperialist].Priority>EquipInfo.Weapon.Priority;
    internal void ModPlayCustomPickupCue(bool first)
    {
        if (IsMainPlayer&&Sfx.TimedSfxMute==0)
            _soundSource.PlayFreeSfx(first?SfxId.WEAPON_POWER_UP:SfxId.AMMO_POWER_UP1);
    }
}
