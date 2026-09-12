using System;
using MphRead.Combat;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods.Hud;
using MphRead.Mods.Input;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    internal readonly record struct BottomScreenWeaponAvailability(int Mask,
        byte Equipped)
    {
        public bool Contains(byte weapon)
            => weapon <= 8 && (Mask & (1 << weapon)) != 0;
    }

    public partial class PlayerPresentation
    {
        private long _bottomScreenGeneration;
        private byte _bottomScreenPreview = WeaponSelectionIntent.None;
        private byte _bottomScreenPending = WeaponSelectionIntent.None;
        private BottomScreenWeaponAvailability _bottomScreenAvailability;
        private bool _bottomScreenInteractionActive;
        private readonly HudObjectInstance[] _bottomScreenWeapons = new HudObjectInstance[6];
        private readonly HudObjectInstance[] _bottomScreenBoxes = new HudObjectInstance[6];
        private bool _bottomScreenAssetsReady;

        /// <summary>
        /// Starts a fresh scene epoch and clones the native HUD assets. The
        /// clones are the only instances the panel mutates while drawing.
        /// </summary>
        private void InitializeBottomScreenPresentation()
        {
            if (!_player.IsMainPlayer) return;
            _bottomScreenGeneration = Presentation.BottomScreen.BeginPresentation();
            _bottomScreenAvailability = CaptureBottomScreenAvailability();
            for (int i = 0; i < 6; i++)
            {
                _bottomScreenWeapons[i] = CloneHudInstance(_weaponSelectInsts[i]);
                _bottomScreenBoxes[i] = CloneHudInstance(_selectBoxInsts[i]);
            }
            _bottomScreenAssetsReady = true;
            _bottomScreenPreview = WeaponSelectionIntent.None;
            _bottomScreenPending = WeaponSelectionIntent.None;
            _bottomScreenInteractionActive = false;
        }

        private HudObjectInstance CloneHudInstance(HudObjectInstance source)
        {
            var clone = new HudObjectInstance(source.Width, source.Height)
            {
                Enabled = source.Enabled,
                Center = source.Center,
                PositionX = source.PositionX,
                PositionY = source.PositionY,
                FlipHorizontal = source.FlipHorizontal,
                FlipVertical = source.FlipVertical,
                UseMask = source.UseMask,
                Alpha = source.Alpha
            };
            if (source.CharacterData is { } characterData
                && source.PaletteData is { } paletteData)
            {
                clone.SetData(characterData, source.CurrentFrame, paletteData,
                    source.PaletteIndex, _player._scene);
            }
            return clone;
        }

        private BottomScreenWeaponAvailability CaptureBottomScreenAvailability()
        {
            int mask = 0;
            for (byte i = 0; i < 9; i++)
            {
                var info = Weapons.Current[i];
                int ammo = _player._ammo[info.AmmoType];
                if (_player._availableWeapons[(BeamType)i]
                    && (i == 0 || ammo == -1 || ammo >= info.AmmoCost))
                {
                    mask |= 1 << i;
                }
            }
            return new BottomScreenWeaponAvailability(mask,
                (byte)_player.CurrentWeapon);
        }

        /// <summary>
        /// Consume neutral panel events on the simulation thread. No player or
        /// renderer API is called while the controller owns its synchronization
        /// lock; this method only converts the immutable event batch to a
        /// preview and a one-shot pending request.
        /// </summary>
        private void ProcessBottomScreenInput(bool blocked)
        {
            if (!_player.IsMainPlayer || _bottomScreenGeneration == 0) return;
            NativeBottomScreenController controller = Presentation.BottomScreen;
            if (blocked || _player.Health <= 0
                || controller.Mode == NativeBottomScreenMode.Off)
            {
                _bottomScreenPreview = WeaponSelectionIntent.None;
                _bottomScreenPending = WeaponSelectionIntent.None;
                _bottomScreenInteractionActive = false;
                controller.Cancel();
                return;
            }

            BottomScreenWeaponAvailability currentAvailability
                = CaptureBottomScreenAvailability();
            NativeBottomScreenLayout layout = controller.Layout;
            foreach (NativeBottomScreenPointerEvent item
                in controller.Consume(_bottomScreenGeneration))
            {
                switch (item.Phase)
                {
                    case NativeBottomScreenPointerPhase.Down:
                        // The legal set is immutable for this contact. The
                        // commit path revalidates against live ammo/ownership.
                        _bottomScreenAvailability = currentAvailability;
                        _bottomScreenInteractionActive = true;
                        goto case NativeBottomScreenPointerPhase.Move;
                    case NativeBottomScreenPointerPhase.Move:
                        if (!_bottomScreenInteractionActive) break;
                        Vector2 ds = layout.LogicalToDs(item.Sample.X, item.Sample.Y);
                        _bottomScreenPreview = NativeWeaponSelector.TrySelect(
                            ds, _bottomScreenAvailability.Mask, out byte preview)
                            ? preview : WeaponSelectionIntent.None;
                        break;
                    case NativeBottomScreenPointerPhase.Up:
                        if (_bottomScreenInteractionActive
                            && _bottomScreenAvailability.Contains(_bottomScreenPreview))
                        {
                            _bottomScreenPending = _bottomScreenPreview;
                            controller.DismissPopupAfterSelection();
                        }
                        _bottomScreenInteractionActive = false;
                        _bottomScreenPreview = WeaponSelectionIntent.None;
                        break;
                    case NativeBottomScreenPointerPhase.Cancel:
                        _bottomScreenInteractionActive = false;
                        _bottomScreenPreview = WeaponSelectionIntent.None;
                        _bottomScreenPending = WeaponSelectionIntent.None;
                        break;
                }
            }
        }

        private byte TakeBottomScreenPending()
        {
            byte pending = _bottomScreenPending;
            _bottomScreenPending = WeaponSelectionIntent.None;
            return pending;
        }

        private void CancelBottomScreenInteraction()
        {
            _bottomScreenPreview = WeaponSelectionIntent.None;
            _bottomScreenPending = WeaponSelectionIntent.None;
            _bottomScreenInteractionActive = false;
            if (_bottomScreenGeneration != 0)
            {
                Presentation.BottomScreen.Cancel();
            }
        }

        private static float ToHudX(float framebufferX, Vector2i size)
            => framebufferX / Math.Max(1, size.X) * 256f;

        private static float ToHudY(float framebufferY, Vector2i size)
            => framebufferY / Math.Max(1, size.Y) * 192f;

        private void DrawBottomScreenRect(BottomScreenRect rect, Vector4 color)
        {
            Vector2i size = Presentation.Size;
            Presentation.DrawHudFlatBox(ToHudX(rect.Left, size), ToHudY(rect.Top, size),
                ToHudX(rect.Right, size), ToHudY(rect.Bottom, size), color);
        }

        /// <summary>
        /// Draw after the ordinary HUD using dedicated copies of the native
        /// weapon/select-box assets. The main HUD objects retain their native
        /// positions, animation, selection and availability state.
        /// </summary>
        private void DrawBottomScreenOverlay()
        {
            if (!_player.IsMainPlayer || !_bottomScreenAssetsReady
                || Mods.ThumbnailMode.Active) return;
            NativeBottomScreenController controller = Presentation.BottomScreen;
            NativeBottomScreenMode mode = controller.Mode;
            if (mode == NativeBottomScreenMode.Off) return;
            NativeBottomScreenLayout layout = controller.Layout;
            if (!layout.IsValid || Presentation.Size.X <= 0 || Presentation.Size.Y <= 0)
                return;

            if (!controller.Visible)
            {
                DrawBottomScreenRect(layout.TabFramebuffer,
                    new Vector4(.02f, .06f, .08f, .92f * Features.HudOpacity));
                DrawBottomScreenRect(new BottomScreenRect(
                    layout.TabFramebuffer.Left, layout.TabFramebuffer.Top,
                    layout.TabFramebuffer.Right, layout.TabFramebuffer.Top + 2),
                    new Vector4(.85f, .78f, .12f, .95f * Features.HudOpacity));
                Vector2 tabCenter = new(
                    (layout.TabFramebuffer.Left + layout.TabFramebuffer.Right) * .5f,
                    (layout.TabFramebuffer.Top + layout.TabFramebuffer.Bottom) * .5f);
                DrawText2D(ToHudX(tabCenter.X, Presentation.Size),
                    ToHudY(tabCenter.Y + 5, Presentation.Size), Align.Center, 0,
                    "WEAPON SELECT", new ColorRgba(0x3FEF), scale: .42f);
                return;
            }

            BottomScreenRect panel = layout.PanelFramebuffer;
            DrawBottomScreenRect(panel,
                new Vector4(.01f, .025f, .04f, .93f * Features.HudOpacity));
            const float border = 3;
            DrawBottomScreenRect(new BottomScreenRect(panel.Left, panel.Top,
                panel.Right, panel.Top + border),
                new Vector4(.85f, .78f, .12f, .98f * Features.HudOpacity));
            DrawBottomScreenRect(new BottomScreenRect(panel.Left, panel.Bottom - border,
                panel.Right, panel.Bottom),
                new Vector4(.85f, .78f, .12f, .98f * Features.HudOpacity));
            DrawBottomScreenRect(new BottomScreenRect(panel.Left, panel.Top,
                panel.Left + border, panel.Bottom),
                new Vector4(.85f, .78f, .12f, .98f * Features.HudOpacity));
            DrawBottomScreenRect(new BottomScreenRect(panel.Right - border, panel.Top,
                panel.Right, panel.Bottom),
                new Vector4(.85f, .78f, .12f, .98f * Features.HudOpacity));

            Vector2 title = new((panel.Left + panel.Right) * .5f,
                panel.Top + panel.Height * .09f);
            DrawText2D(ToHudX(title.X, Presentation.Size),
                ToHudY(title.Y, Presentation.Size), Align.Center, 0,
                "WEAPON SELECT", new ColorRgba(0x3FEF), scale: .5f);

            BottomScreenWeaponAvailability availability = CaptureBottomScreenAvailability();
            float iconScale = panel.Width / Math.Max(1, Presentation.Size.X);
            for (int i = 0; i < 6; i++)
            {
                byte weapon = NativeWeaponSelector.WeaponAtSlot(i);
                bool available = availability.Contains(weapon);
                HudObjectInstance icon = _bottomScreenWeapons[i];
                HudObjectInstance box = _bottomScreenBoxes[i];
                icon.Enabled = available;
                icon.Alpha = .82f * Features.HudOpacity;
                box.Enabled = true;
                box.Alpha = Features.HudOpacity;
                box.SetIndex(available && weapon == _bottomScreenPreview ? 2 : 1,
                    _player._scene);
                Vector2 position = NativeWeaponSelector.SlotPosition(i);
                Vector2 framebuffer = layout.DsToFramebuffer(position.X, position.Y);
                float x = framebuffer.X / Presentation.Size.X;
                float y = framebuffer.Y / Presentation.Size.Y;
                icon.PositionX = box.PositionX = x;
                icon.PositionY = box.PositionY = y;
                // Mode 0 preserves each sprite's native aspect ratio. Scaling
                // it by panelWidth/viewWidth maps both axes into the panel's
                // 256x192 canvas without widescreen stretching.
                Presentation.DrawHudObject(box, mode: 0, scale: iconScale);
                Presentation.DrawHudObject(icon, mode: 0, scale: iconScale);
            }

            Vector2 hint = new((panel.Left + panel.Right) * .5f,
                panel.Bottom - panel.Height * .08f);
            DrawText2D(ToHudX(hint.X, Presentation.Size),
                ToHudY(hint.Y, Presentation.Size), Align.Center, 0,
                "Tap a weapon", new ColorRgba(0x7FFF), scale: .42f);
        }
    }
}
