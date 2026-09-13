using System;
using MphRead.Combat;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Mods.Hud;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Entities
{
    internal readonly record struct BottomScreenWeaponAvailability(int Mask,
        byte Equipped)
    {
        public bool Contains(byte weapon)
            => weapon <= 8 && (Mask & (1 << weapon)) != 0;
    }

    [Flags]
    internal enum BottomScreenAction
    {
        None = 0,
        NextWeapon = 1 << 0,
        Morph = 1 << 1
    }

    public partial class PlayerPresentation
    {
        private long _bottomScreenGeneration;
        private byte _bottomScreenPreview = WeaponSelectionIntent.None;
        private byte _bottomScreenPending = WeaponSelectionIntent.None;
        private NativeBottomScreenRegion _bottomScreenRegionPreview;
        private BottomScreenAction _bottomScreenPendingAction;
        private BottomScreenWeaponAvailability _bottomScreenAvailability;
        private bool _bottomScreenInteractionActive;
        private bool _bottomScreenSuppressesMouseThisStep;
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
            _bottomScreenRegionPreview = NativeBottomScreenRegion.None;
            _bottomScreenPendingAction = BottomScreenAction.None;
            _bottomScreenInteractionActive = false;
            _bottomScreenSuppressesMouseThisStep = false;
        }

        internal bool BottomScreenSuppressesMouseThisStep
            => _bottomScreenSuppressesMouseThisStep;

        /// <summary>
        /// Resolve the configurable binding before the fixed-step look frame
        /// is consumed. Returning true quarantines both the opening and closing
        /// step so neither can leak mouse aim or a morph-ball flick.
        /// </summary>
        internal bool PrepareBottomScreenActivation(KeyboardState keyboard,
            MouseState mouse, bool blocked)
        {
            NativeBottomScreenController controller = Presentation.BottomScreen;
            bool wasActive = controller.DesktopSessionActive;
            controller.UpdateDesktopCursorPreferences(
                Mods.InputSettings.BottomScreenCursorSensitivity,
                Mods.InputSettings.BottomScreenCursorStartX,
                Mods.InputSettings.BottomScreenCursorStartY);
            if (blocked || _player.Health <= 0
                || Mods.InputSettings.BottomScreenMode == NativeBottomScreenMode.Off)
            {
                if (wasActive) controller.EndDesktopSession();
                _bottomScreenSuppressesMouseThisStep = wasActive;
                return wasActive;
            }

            Keybind bind = Bindings.HudOverlay;
            bool down = IsDown(bind, keyboard, mouse);
            bool previousDown = _keyboardState != null && _mouseState != null
                && IsDown(bind, _keyboardState, _mouseState);
            bool pressed = down && !previousDown;
            bool released = !down && previousDown;
            NativeBottomScreenActivationMode activation
                = Mods.InputSettings.BottomScreenActivation;
            if (pressed)
            {
                controller.BeginDesktopSession(activation);
            }
            if (activation == NativeBottomScreenActivationMode.Hold
                && released)
            {
                // Hold owns a synthetic contact. Leave the session alive
                // until the fixed-step consumer has processed its Up sample;
                // that sample is the authoritative release coordinate.
                controller.TryDesktopPointerUp();
            }

            bool active = controller.DesktopSessionActive;
            _bottomScreenSuppressesMouseThisStep = wasActive || active;
            if (_bottomScreenSuppressesMouseThisStep)
                Presentation.ResetRenderLook();
            return _bottomScreenSuppressesMouseThisStep;
        }

        /// <summary>
        /// The cursor owns ordinary mouse actions for this step. Requiring a
        /// repress prevents the selecting click from becoming a shot when the
        /// overlay closes while the physical button is still down.
        /// </summary>
        internal void SuppressBottomScreenGameplayMouse()
        {
            foreach (Keybind bind in Bindings.All)
            {
                if (ReferenceEquals(bind, Bindings.HudOverlay)
                    || bind.Type != ButtonType.Mouse)
                {
                    continue;
                }
                bind.IsDown = false;
                bind.IsPressed = false;
                bind.IsReleased = false;
                bind.NeedsRepress = true;
            }
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
            controller.UpdatePreferences(Mods.InputSettings.BottomScreenMode,
                Mods.InputSettings.BottomScreenStyle,
                new NativeBottomScreenLayoutOptions(
                    Mods.InputSettings.BottomScreenScale,
                    Mods.InputSettings.BottomScreenCenterX,
                    Mods.InputSettings.BottomScreenCenterY));
            controller.UpdateDesktopCursorPreferences(
                Mods.InputSettings.BottomScreenCursorSensitivity,
                Mods.InputSettings.BottomScreenCursorStartX,
                Mods.InputSettings.BottomScreenCursorStartY);
            if (blocked || _player.Health <= 0
                || controller.Mode == NativeBottomScreenMode.Off)
            {
                _bottomScreenPreview = WeaponSelectionIntent.None;
                _bottomScreenRegionPreview = NativeBottomScreenRegion.None;
                _bottomScreenPending = WeaponSelectionIntent.None;
                _bottomScreenPendingAction = BottomScreenAction.None;
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
                bool desktopSample
                    = item.Sample.Id == NativeBottomScreenController.DesktopPointerId;
                switch (item.Phase)
                {
                    case NativeBottomScreenPointerPhase.Down:
                        // The legal set is immutable for this contact. The
                        // commit path revalidates against live ammo/ownership.
                        _bottomScreenAvailability = currentAvailability;
                        _bottomScreenInteractionActive = true;
                        goto case NativeBottomScreenPointerPhase.Move;
                    case NativeBottomScreenPointerPhase.Move:
                        if (!_bottomScreenInteractionActive
                            && !(desktopSample && controller.DesktopSessionActive))
                        {
                            break;
                        }
                        Vector2 ds = layout.LogicalToDs(item.Sample.X, item.Sample.Y);
                        if (controller.Style == NativeBottomScreenStyle.ClassicDs
                            && !controller.SelectorOpen)
                        {
                            _bottomScreenRegionPreview
                                = NativeBottomScreenClassicLayout.RegionAt(ds);
                            _bottomScreenPreview = WeaponSelectionIntent.None;
                            if (desktopSample
                                && controller.DesktopActivationMode
                                    == NativeBottomScreenActivationMode.Hold
                                && _bottomScreenRegionPreview
                                    == NativeBottomScreenRegion.WeaponSelect)
                            {
                                controller.OpenSelectorForDesktopDrag();
                            }
                        }
                        else
                        {
                            _bottomScreenRegionPreview = NativeBottomScreenRegion.None;
                            _bottomScreenPreview = NativeWeaponSelector.TrySelect(
                                ds, _bottomScreenAvailability.Mask, out byte preview)
                                ? preview : WeaponSelectionIntent.None;
                        }
                        break;
                    case NativeBottomScreenPointerPhase.Up:
                        if (!_bottomScreenInteractionActive) break;
                        // Always derive the release action from the Up sample.
                        // A final relative-mouse delta may have arrived in the
                        // same frame as the binding release.
                        Vector2 releaseDs = layout.LogicalToDs(
                            item.Sample.X, item.Sample.Y);
                        if (controller.Style == NativeBottomScreenStyle.ClassicDs
                            && !controller.SelectorOpen)
                        {
                            _bottomScreenRegionPreview
                                = NativeBottomScreenClassicLayout.RegionAt(releaseDs);
                            _bottomScreenPreview = WeaponSelectionIntent.None;
                        }
                        else
                        {
                            _bottomScreenRegionPreview = NativeBottomScreenRegion.None;
                            _bottomScreenPreview = NativeWeaponSelector.TrySelect(
                                releaseDs, _bottomScreenAvailability.Mask,
                                out byte releasePreview)
                                ? releasePreview : WeaponSelectionIntent.None;
                        }
                        if (controller.Style == NativeBottomScreenStyle.ClassicDs
                            && !controller.SelectorOpen)
                        {
                            bool completed = CommitClassicBottomScreenRegion(
                                _bottomScreenRegionPreview, _bottomScreenAvailability);
                            if (_bottomScreenRegionPreview
                                == NativeBottomScreenRegion.WeaponSelect)
                            {
                                controller.SetSelectorOpen(true);
                            }
                            else if (completed)
                            {
                                CompleteBottomScreenSelection(controller);
                            }
                        }
                        else
                        {
                            if (_bottomScreenAvailability.Contains(_bottomScreenPreview))
                            {
                                _bottomScreenPending = _bottomScreenPreview;
                                CompleteBottomScreenSelection(controller);
                            }
                            else
                            {
                                controller.SetSelectorOpen(false);
                            }
                        }
                        _bottomScreenInteractionActive = false;
                        _bottomScreenPreview = WeaponSelectionIntent.None;
                        _bottomScreenRegionPreview = NativeBottomScreenRegion.None;
                        if (desktopSample
                            && controller.DesktopActivationMode
                                == NativeBottomScreenActivationMode.Hold
                            && controller.DesktopSessionActive)
                        {
                            // Hold closes only after this fixed-step Up has
                            // been consumed and any legal intent queued.
                            controller.EndDesktopSession();
                        }
                        break;
                    case NativeBottomScreenPointerPhase.Cancel:
                        _bottomScreenInteractionActive = false;
                        _bottomScreenPreview = WeaponSelectionIntent.None;
                        _bottomScreenRegionPreview = NativeBottomScreenRegion.None;
                        _bottomScreenPending = WeaponSelectionIntent.None;
                        _bottomScreenPendingAction = BottomScreenAction.None;
                        if (desktopSample
                            && controller.DesktopActivationMode
                                == NativeBottomScreenActivationMode.Hold
                            && controller.DesktopSessionActive)
                        {
                            controller.EndDesktopSession();
                        }
                        break;
                }
            }
        }

        private static void CompleteBottomScreenSelection(
            NativeBottomScreenController controller)
        {
            if (controller.DesktopSessionActive)
                controller.CompleteDesktopSelection();
            else
                controller.DismissPopupAfterSelection();
        }

        private bool CommitClassicBottomScreenRegion(NativeBottomScreenRegion region,
            BottomScreenWeaponAvailability availability)
        {
            switch (region)
            {
                case NativeBottomScreenRegion.PowerBeam when availability.Contains(0):
                    _bottomScreenPending = 0;
                    return true;
                case NativeBottomScreenRegion.Missile when availability.Contains(1):
                    _bottomScreenPending = 1;
                    return true;
                case NativeBottomScreenRegion.NextWeapon:
                    _bottomScreenPendingAction |= BottomScreenAction.NextWeapon;
                    return true;
                case NativeBottomScreenRegion.AltForm:
                    _bottomScreenPendingAction |= BottomScreenAction.Morph;
                    return true;
                case NativeBottomScreenRegion.WeaponSelect:
                    return true;
                default:
                    return false;
            }
        }

        private byte TakeBottomScreenPending()
        {
            byte pending = _bottomScreenPending;
            _bottomScreenPending = WeaponSelectionIntent.None;
            return pending;
        }

        private BottomScreenAction TakeBottomScreenAction()
        {
            BottomScreenAction pending = _bottomScreenPendingAction;
            _bottomScreenPendingAction = BottomScreenAction.None;
            return pending;
        }

        private void CancelBottomScreenInteraction()
        {
            _bottomScreenPreview = WeaponSelectionIntent.None;
            _bottomScreenRegionPreview = NativeBottomScreenRegion.None;
            _bottomScreenPending = WeaponSelectionIntent.None;
            _bottomScreenPendingAction = BottomScreenAction.None;
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
            float opacity = Math.Clamp(Mods.InputSettings.BottomScreenOpacity
                * Features.HudOpacity, 0, 1);

            if (!controller.Visible) return;

            BottomScreenRect panel = layout.PanelFramebuffer;
            DrawBottomScreenRect(panel,
                controller.Style == NativeBottomScreenStyle.ClassicDs
                    ? new Vector4(.08f, .015f, .025f, opacity * .56f)
                    : new Vector4(.01f, .025f, .04f, opacity));
            float border = MathF.Max(1, panel.Height / 128f);
            Vector4 edge = controller.Style == NativeBottomScreenStyle.ClassicDs
                ? new Vector4(.88f, .22f, .16f, Math.Min(1, opacity * 1.8f))
                : new Vector4(.85f, .78f, .12f, opacity);
            DrawBottomScreenRect(new BottomScreenRect(panel.Left, panel.Top,
                panel.Right, panel.Top + border), edge);
            DrawBottomScreenRect(new BottomScreenRect(panel.Left, panel.Bottom - border,
                panel.Right, panel.Bottom), edge);
            DrawBottomScreenRect(new BottomScreenRect(panel.Left, panel.Top,
                panel.Left + border, panel.Bottom), edge);
            DrawBottomScreenRect(new BottomScreenRect(panel.Right - border, panel.Top,
                panel.Right, panel.Bottom), edge);

            if (controller.Style == NativeBottomScreenStyle.ClassicDs
                && !controller.SelectorOpen)
            {
                DrawClassicBottomScreen(layout, opacity);
            }
            else
            {
                DrawBottomScreenAffinitySelector(layout, opacity,
                    nested: controller.Style == NativeBottomScreenStyle.ClassicDs);
            }
            if (controller.DesktopSessionActive)
            {
                DrawBottomScreenCursor(layout, controller.DesktopCursorDs,
                    opacity);
            }
        }

        private void DrawBottomScreenCursor(NativeBottomScreenLayout layout,
            Vector2 cursorDs, float opacity)
        {
            Vector2 cursor = layout.DsToFramebuffer(cursorDs.X, cursorDs.Y);
            float radius = MathF.Max(4, layout.PanelFramebuffer.Height / 48f);
            Vector4 shadow = new(0, 0, 0, Math.Min(1, opacity * 2));
            Vector4 fill = new(1, .92f, .32f, Math.Min(1, opacity * 3));
            DrawBottomScreenRect(new BottomScreenRect(cursor.X - radius - 1,
                cursor.Y - 2, cursor.X + radius + 1, cursor.Y + 2), shadow);
            DrawBottomScreenRect(new BottomScreenRect(cursor.X - 2,
                cursor.Y - radius - 1, cursor.X + 2, cursor.Y + radius + 1), shadow);
            DrawBottomScreenRect(new BottomScreenRect(cursor.X - radius,
                cursor.Y - 1, cursor.X + radius, cursor.Y + 1), fill);
            DrawBottomScreenRect(new BottomScreenRect(cursor.X - 1,
                cursor.Y - radius, cursor.X + 1, cursor.Y + radius), fill);
        }

        private void DrawClassicBottomScreen(NativeBottomScreenLayout layout,
            float opacity)
        {
            BottomScreenRect panel = layout.PanelFramebuffer;
            Vector4 grid = new(.55f, .12f, .14f, opacity * .22f);
            for (int i = 1; i < 4; i++)
            {
                float x = panel.Left + panel.Width * i / 4f;
                float y = panel.Top + panel.Height * i / 4f;
                DrawBottomScreenRect(new BottomScreenRect(x, panel.Top, x + 1,
                    panel.Bottom), grid);
                DrawBottomScreenRect(new BottomScreenRect(panel.Left, y,
                    panel.Right, y + 1), grid);
            }

            float scaleX = panel.Width / NativeBottomScreenClassicLayout.DsWidth;
            float scaleY = panel.Height / NativeBottomScreenClassicLayout.DsHeight;
            foreach (NativeBottomScreenButton button
                in NativeBottomScreenClassicLayout.Buttons)
            {
                bool active = (_bottomScreenInteractionActive
                    || Presentation.BottomScreen.DesktopSessionActive)
                    && _bottomScreenRegionPreview == button.Region;
                bool equipped = button.Region == NativeBottomScreenRegion.PowerBeam
                        && _player.CurrentWeapon == BeamType.PowerBeam
                    || button.Region == NativeBottomScreenRegion.Missile
                        && _player.CurrentWeapon == BeamType.Missile;
                Vector4 fill = active
                    ? new Vector4(1f, .63f, .18f, Math.Min(1, opacity * 2.5f))
                    : equipped
                        ? new Vector4(.75f, .24f, .12f, Math.Min(1, opacity * 1.7f))
                        : new Vector4(.42f, .07f, .09f, opacity * .72f);
                Vector2 center = layout.DsToFramebuffer(button.Position.X,
                    button.Position.Y);
                DrawBottomScreenCircle(center.X, center.Y,
                    button.Radius * scaleX, button.Radius * scaleY, fill);
                if (Mods.InputSettings.BottomScreenLabels)
                {
                    DrawText2D(ToHudX(center.X, Presentation.Size),
                        ToHudY(center.Y + button.Radius * scaleY * .17f,
                            Presentation.Size), Align.Center, 0, button.Label,
                        new ColorRgba(0x7FFF), alpha: Math.Min(1, opacity * 2),
                        scale: Math.Clamp(panel.Width / Presentation.Size.X,
                            .26f, .58f));
                }
            }

            Vector2 aim = layout.DsToFramebuffer(128, 108);
            float crossX = 12 * scaleX;
            float crossY = 12 * scaleY;
            Vector4 reticle = new(.85f, .2f, .18f, opacity * .7f);
            DrawBottomScreenRect(new BottomScreenRect(aim.X - crossX, aim.Y,
                aim.X + crossX, aim.Y + 1), reticle);
            DrawBottomScreenRect(new BottomScreenRect(aim.X, aim.Y - crossY,
                aim.X + 1, aim.Y + crossY), reticle);
            if (Mods.InputSettings.BottomScreenLabels)
            {
                DrawText2D(ToHudX(aim.X, Presentation.Size),
                    ToHudY(aim.Y + 25 * scaleY, Presentation.Size), Align.Center, 0,
                    "AIM", new ColorRgba(0x3DEF), alpha: opacity,
                    scale: Math.Clamp(panel.Width / Presentation.Size.X,
                        .24f, .48f));
            }
        }

        private void DrawBottomScreenAffinitySelector(NativeBottomScreenLayout layout,
            float opacity, bool nested)
        {
            BottomScreenRect panel = layout.PanelFramebuffer;

            Vector2 title = new((panel.Left + panel.Right) * .5f,
                panel.Top + panel.Height * .09f);
            DrawText2D(ToHudX(title.X, Presentation.Size),
                ToHudY(title.Y, Presentation.Size), Align.Center, 0,
                nested ? "SELECT AFFINITY" : "WEAPON SELECT",
                new ColorRgba(0x3FEF), alpha: Math.Min(1, opacity * 1.6f),
                scale: .5f);

            BottomScreenWeaponAvailability availability = CaptureBottomScreenAvailability();
            float iconScale = panel.Width / Math.Max(1, Presentation.Size.X);
            for (int i = 0; i < 6; i++)
            {
                byte weapon = NativeWeaponSelector.WeaponAtSlot(i);
                bool available = availability.Contains(weapon);
                HudObjectInstance icon = _bottomScreenWeapons[i];
                HudObjectInstance box = _bottomScreenBoxes[i];
                icon.Enabled = available;
                icon.Alpha = .82f * opacity;
                box.Enabled = true;
                box.Alpha = opacity;
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
                nested ? "Tap a weapon / center to cancel" : "Tap a weapon",
                new ColorRgba(0x7FFF), alpha: Math.Min(1, opacity * 1.6f),
                scale: .42f);
        }

        private void DrawBottomScreenCircle(float centerX, float centerY,
            float radiusX, float radiusY, Vector4 color)
        {
            if (radiusX <= 0 || radiusY <= 0) return;
            int rows = Math.Clamp((int)MathF.Ceiling(radiusY * 2), 8, 48);
            float step = radiusY * 2 / rows;
            for (int i = 0; i < rows; i++)
            {
                float y = -radiusY + (i + .5f) * step;
                float ratio = y / radiusY;
                float half = radiusX * MathF.Sqrt(MathF.Max(0, 1 - ratio * ratio));
                if (half <= 0) continue;
                DrawBottomScreenRect(new BottomScreenRect(centerX - half,
                    centerY + y, centerX + half, centerY + y + step), color);
            }
        }
    }
}
