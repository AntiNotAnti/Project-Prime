using System;

namespace MphRead.Entities
{
    /// <summary>
    /// Per-player resources owned by the Balanced Mode ruleset.
    ///
    /// Imperialist normally spends the shared Universal Ammo pool. Balanced
    /// Mode gives it a private five-shot reserve instead, so UA pickups can
    /// still feed the other beams without also turning into sniper ammo.
    /// </summary>
    public partial class PlayerEntity
    {
        internal const int BalancedImperialistShotCap = 5;
        private int _balancedImperialistAmmo;

        private bool UsesBalancedImperialistAmmo(BeamType beam)
            => _scene.GameState.BalancedMode
                && beam == BeamType.Imperialist
                && !_scene.GameState.InstaGib
                && !_scene.GameState.OneInTheChamber
                && !_scene.GameState.Fiesta
                && _scene.GameState.Mode != GameMode.GunGame;

        private WeaponInfo BalancedImperialistWeaponInfo()
        {
            int index = (int)BeamType.Imperialist;
            if (Weapons.GetAffinityBeam(Hunter) == BeamType.Imperialist)
            {
                index += 9;
            }
            return _scene.WeaponRules[index];
        }

        private int BalancedImperialistAmmoCap
            => Math.Max(1, BalancedImperialistWeaponInfo().AmmoCost) * BalancedImperialistShotCap;

        private int ModAmmoForWeapon(BeamType beam, WeaponInfo info)
            => UsesBalancedImperialistAmmo(beam) ? _balancedImperialistAmmo : _ammo[info.AmmoType];

        private int ModAmmoCapForWeapon(BeamType beam, WeaponInfo info)
            => UsesBalancedImperialistAmmo(beam) ? BalancedImperialistAmmoCap : _ammoMax[info.AmmoType];

        private int ModAmmoCostForWeapon(BeamType beam, WeaponInfo info)
            => UsesBalancedImperialistAmmo(beam) ? Math.Max(1, BalancedImperialistWeaponInfo().AmmoCost) : info.AmmoCost;

        private WeaponInfo ModDisplayWeaponInfo(BeamType beam, WeaponInfo info)
            => UsesBalancedImperialistAmmo(beam) && Weapons.GetAffinityBeam(Hunter) == beam
                ? BalancedImperialistWeaponInfo()
                : info;

        private void ModBindWeaponAmmo(BeamType beam, byte ammoType)
        {
            if (UsesBalancedImperialistAmmo(beam))
            {
                EquipInfo.GetAmmo = () => _balancedImperialistAmmo;
                EquipInfo.SetAmmo = newAmmo =>
                    _balancedImperialistAmmo = Math.Clamp(newAmmo, 0, BalancedImperialistAmmoCap);
                return;
            }

            EquipInfo.GetAmmo = () => _ammo[ammoType];
            EquipInfo.SetAmmo = newAmmo => _ammo[ammoType] = newAmmo;
        }

        private void ModRefillWeaponPickupAmmo(BeamType beam, WeaponInfo info)
        {
            if (UsesBalancedImperialistAmmo(beam))
            {
                // Picking up the Imperialist is the sole refill source in
                // Balanced Mode. A re-pick always tops the private reserve up
                // to five shots and can never overflow it.
                int beforeShots = _balancedImperialistAmmo
                    / Math.Max(1, BalancedImperialistWeaponInfo().AmmoCost);
                _balancedImperialistAmmo = BalancedImperialistAmmoCap;
                BalancedModeTelemetry.NoteImperialistAcquired(this,
                    Math.Max(0, BalancedImperialistShotCap - beforeShots));
                return;
            }

            if (_ammo[info.AmmoType] < 60)
            {
                _ammo[info.AmmoType] = Math.Min(_ammo[info.AmmoType] + 60, 60);
            }
        }

        internal int ModBalancedImperialistShots
            => UsesBalancedImperialistAmmo(BeamType.Imperialist)
                ? _balancedImperialistAmmo / Math.Max(1, BalancedImperialistWeaponInfo().AmmoCost)
                : -1;
    }
}
