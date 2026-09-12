using System;
using MphRead.Combat;
using MphRead.Hud;
using MphRead.Mods.Input;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private readonly WeaponSelectionIntent _weaponIntent = new();
        public WeaponRadialSelection WeaponRadial { get; } = new();
        private Keybind WeaponBind(byte weapon) => weapon switch
        {
            0 => Bindings.PowerBeam, 1 => Bindings.VoltDriver, 2 => Bindings.Missile,
            3 => Bindings.Battlehammer, 4 => Bindings.Imperialist, 5 => Bindings.Judicator,
            6 => Bindings.Magmaul, 7 => Bindings.ShockCoil, _ => Bindings.OmegaCannon
        };
        public void ApplyWeaponSelection(bool blocked)
        {
            if (Mods.Network.IntermissionVoteControls.Available)
            {
                if (!blocked)
                {
                    if (Bindings.NextWeapon.IsPressed) Mods.Network.IntermissionVoteControls.Move(1);
                    else if (Bindings.PrevWeapon.IsPressed) Mods.Network.IntermissionVoteControls.Move(-1);
                    if (Bindings.Shoot.IsPressed) Mods.Network.IntermissionVoteControls.Submit();
                }
                _weaponIntent.Cancel(); WeaponRadial.Reset(); return;
            }
            var identity = _player.ServerCombatIdentity;
            _weaponIntent.Observe((byte)_player.CurrentWeapon, _player.Health > 0, identity.ConnectionId, identity.Life);
            if (!blocked && !HasPostMatchResult && ApplyRecapNavigation())
            {
                _weaponIntent.Cancel(); WeaponRadial.Reset(); return;
            }
            if (HasPostMatchResult)
            {
                _weaponIntent.Cancel();
                WeaponRadial.Reset();
                return;
            }
            if (blocked || _player.Health <= 0)
            {
                _weaponIntent.Cancel();
                WeaponRadial.Reset();
                return;
            }
            if (Bindings.HudOverlay.IsPressed)
            {
                NativeBottomScreenController bottomScreen = Presentation.BottomScreen;
                bool wasVisible = bottomScreen.Visible;
                bool nowVisible = bottomScreen.TogglePopup();
                if (wasVisible && !nowVisible)
                {
                    // Closing is cancellation even if an Up was consumed
                    // earlier in this fixed step.
                    CancelBottomScreenInteraction();
                }
            }
            int available = 0;
            for (byte i = 0; i < 9; i++)
            {
                var info = Weapons.Current[i];
                int ammo = _player._ammo[info.AmmoType];
                if (_player._availableWeapons[(BeamType)i] && (i == 0 || ammo == -1 || ammo >= info.AmmoCost)) available |= 1 << i;
            }
            if (!GamepadInput.Active) WeaponRadial.Reset();
            GamepadButtons effectiveButtons = GamepadInput.EffectiveButtons;
            bool wheel = GamepadInput.Active
                && (effectiveButtons & PadBindings.Get(PadAction.WeaponWheel)) != 0;
            byte radial = WeaponRadial.Update(wheel,
                (effectiveButtons & GamepadButtons.B) != 0,
                GamepadInput.State.RightX, GamepadInput.State.RightY,
                Mods.InputSettings.GamepadLookDeadZone, available);
            if (radial <= 8) _weaponIntent.Request(radial, available);
            if (Bindings.WeaponMenu.IsReleased)
                _weaponIntent.Request((byte)_player.WeaponSelection, available);
            // Precedence is explicit: direct bind > affinity > quick swap >
            // cycle > bottom screen/native menu > controller radial. Requests
            // later in this method win; the intent remains the only path that
            // can reach gameplay authority.
            byte panel = TakeBottomScreenPending();
            if (panel <= 8)
            {
                BottomScreenWeaponAvailability snapshot
                    = CaptureBottomScreenAvailability();
                if (snapshot.Contains(panel))
                    _weaponIntent.Request(panel, snapshot.Mask);
            }
            if (Bindings.NextWeapon.IsPressed || Bindings.PrevWeapon.IsPressed)
            {
                if (_player.Controls.ScrollAllWeapons || _player.CurrentWeapon is not (BeamType.PowerBeam or BeamType.Missile))
                {
                    int current = 0;
                    for (int i = 0; i < 9; i++) if (WeaponRadialSelection.WeaponAt(i) == (byte)_player.CurrentWeapon) current = i;
                    for (int step = 1; step <= 9; step++)
                    {
                        int index = (current + (Bindings.NextWeapon.IsPressed ? step : -step) + 18) % 9;
                        if (!_player.Controls.ScrollAllWeapons && (index < 2 || index > 7)) continue;
                        byte weapon = WeaponRadialSelection.WeaponAt(index);
                        if ((available & (1 << weapon)) != 0) { _weaponIntent.Request(weapon, available); break; }
                    }
                }
            }
            if (Bindings.QuickSwap.IsPressed) _weaponIntent.Request(_weaponIntent.Previous, available);
            if (Bindings.AffinitySlot.IsPressed && _player.AffinitySlotWeapon != BeamType.None)
                _weaponIntent.Request((byte)_player.AffinitySlotWeapon, available);
            for (int i = 0; i < 9; i++)
            {
                byte weapon = WeaponRadialSelection.WeaponAt(i);
                if (WeaponBind(weapon).IsPressed) { _weaponIntent.Request(weapon, available); break; }
            }
            bool legal = !_player.IsAltForm && !_player.IsMorphing && !_player.IsUnmorphing
                && _player.GunAnimation != GunAnimation.UpDown && !Bindings.WeaponMenu.IsDown && !wheel;
            byte requested = _weaponIntent.Pending(legal, available);
            if (requested <= 8) WeaponBind(requested).IsPressed = true;
        }
        private void DrawWeaponRadial()
        {
            if (!WeaponRadial.Open) return;
            for (int i = 0; i < 9; i++)
                Presentation.DrawHudRadialSector(i, WeaponRadialSelection.WeaponAt(i) == WeaponRadial.Preview
                    ? new OpenTK.Mathematics.Vector4(.65f, .5f, .08f, .7f)
                    : new OpenTK.Mathematics.Vector4(0, 0, 0, .5f));
            for (int i = 0; i < 9; i++)
            {
                byte weapon = WeaponRadialSelection.WeaponAt(i);
                float angle = i * MathF.PI * 2 / 9;
                ColorRgba color = weapon == WeaponRadial.Preview ? new ColorRgba(255, 255, 64, 255) : new ColorRgba(180, 180, 180, 255);
                DrawText2D(128 + MathF.Sin(angle) * 62, 92 - MathF.Cos(angle) * 42, Align.Center, 0,
                    CombatFeedback.WeaponName(weapon), color, scale: .55f);
            }
            DrawText2D(128, 142, Align.Center, 0, "Release to equip / B to cancel", scale: .6f);
        }
    }
}
