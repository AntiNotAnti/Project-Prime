using MphRead.Mods.HeroModes;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        // Submit the same context-sensitive Zoom input as a human. Never give AI
        // an unchecked pickup, teleport, flag ownership change, or client authority.
        private void ApplyCtfBotInteraction()
        {
            if (!CtfRules.Active(_scene.GameState.Mode)) return;
            if (_player.ModCarryingCtfFlag)
            {
                // Native Imperialist AI may toggle zoom in combat. In CTF that
                // would throw the objective, so carrier AI must suppress it.
                _buttons.Select.IsDown = false;
                _buttons.R.IsDown = false;
                _touchButtons.Morph.IsDown = false;
                return;
            }
            if (_player.Health <= 0 || !_player.ModInPlay) return;
            foreach (var flag in _scene.GetOctolithFlagEntities())
            {
                if (flag.Data.TeamId == _player.TeamIndex || flag.Carrier != null) continue;
                if ((_player.Position + Vector3.UnitY * .65f - flag.Position).LengthSquared
                    > CtfRules.PickupRadius * CtfRules.PickupRadius) continue;
                if (_player.IsAltForm)
                {
                    _touchButtons.Morph.IsDown = false;
                    _touchButtons.Unmorph.IsDown = true;
                    _buttons.Select.IsDown = false;
                }
                else if (flag.CanPickupCtf(_player))
                {
                    // Release between attempts; one pressed edge never picks up
                    // and throws in the same tick. LOS/life/form checks stay in flag code.
                    _buttons.Select.IsDown = !_player.Controls.Zoom.IsDown;
                    _touchButtons.Morph.IsDown = false;
                }
                return;
            }
        }

        private bool BotHasWeaponAmmo(BeamType beam, WeaponInfo info)
            => _player._availableWeapons[beam]
                && _player.ModAmmoForWeapon(beam, info) >= _player.ModAmmoCostForWeapon(beam, info);
    }
}
