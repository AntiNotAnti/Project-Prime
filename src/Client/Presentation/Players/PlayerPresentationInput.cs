using OpenTK.Windowing.GraphicsLibraryFramework;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private static void ApplyInputPreferences(PlayerControls controls)
        {
            controls.MouseSensitivity = Mods.InputSettings.MouseSensitivity;
            controls.InvertMouseX = Mods.InputSettings.InvertMouseX;
            controls.InvertMouseY = Mods.InputSettings.InvertMouseY;
        }

        private KeyboardState? _keyboardState;
        private MouseState? _mouseState;
        /// <summary>
        /// Whether a keybind is held, from a snapshot alone -- for the paths
        /// that read one control without running the pass that updates them
        /// all. Scroll binds have no state to read here and answer false.
        /// </summary>
        private static bool IsDown(Keybind control, KeyboardState keyboard, MouseState mouse)
        {
            if (control.Type == ButtonType.Key)
            {
                return control.Key != Keys.Unknown && keyboard.IsKeyDown(control.Key);
            }

            return control.Type == ButtonType.Mouse && mouse.IsButtonDown(control.MouseButton);
        }

        private static bool _isScrollingUp = false;
        private static bool _isScrollingDown = false;
        private static MorphBallMouseFlickDetector _mouseBoost = new();
        private static MorphBallStickFlickDetector _stickBoost;
        public static void ProcessInput(Scene scene, KeyboardState keyboardState, MouseState mouseState, bool noPlayerInput)
        {
            KeyboardState keyboardSnap = keyboardState.GetSnapshot();
            MouseState mouseSnap = mouseState.GetSnapshot();
#if ANDROID
            Mods.Input.StylusState desktopStylus = Mods.Input.StylusState.Empty;
#else
            Mods.Input.StylusState desktopStylus
                = Mods.Input.DesktopStylusInput.ConsumeState();
#endif
            if (noPlayerInput || Mods.SpectatorMode.IsSpectating
                || scene.LocalPlayer == null)
            {
                ResetMorphBallBoostDetectors();
            }
            if (Mods.SpectatorMode.IsSpectating)
            {
                // The one control somebody watching keeps, because a
                // scoreboard is the match's and not a player's. Read straight
                // off the snapshot and against the bindings themselves: the
                // pass below fills in each keybind's state from the player it
                // belongs to, and a spectator's own player is skipped there
                // while the player they are watching takes somebody else's
                // input entirely.
                Mods.SpectatorMode.NoteScoreboard(IsDown(Mods.InputSettings.Current.Pause, keyboardSnap, mouseSnap));
            }

            for (int i = 0; i < scene.Players.Count; i++)
            {
                PlayerEntity player = scene.Players[i];
                ApplyInputPreferences(player.Controls);
                if (player.IsBot)
                {
                    if (player.LoadFlags.TestFlag(LoadFlags.Active))
                    {
                        player.AiData.ProcessInput();
                    }

                    continue;
                }

                if (Mods.Network.NetHooks.TryApplyRemoteInput(player, i))
                {
                    if (i == Mods.Network.NetHooks.LocalSlot)
                        ResetMorphBallBoostDetectors();
                    continue;
                }

                if (noPlayerInput || i != Mods.Network.NetHooks.LocalSlot || Mods.SpectatorMode.IsSpectating) // todo: multiple input?
                {
                    if (i == Mods.Network.NetHooks.LocalSlot)
                        ResetMorphBallBoostDetectors();
                    continue;
                }

                player.Input.HasInput = false;
                KeyboardState? prevKeyboardSnap = player.GetPresentation()._keyboardState;
                MouseState? prevMouseSnap = player.GetPresentation()._mouseState;
                player.GetPresentation()._keyboardState = keyboardSnap;
                player.GetPresentation()._mouseState = mouseSnap;
                var rawLook = player.GetPresentation().Presentation.RenderLook;
                if (rawLook != null)
                {
                    // Look is extracted once through ISceneServices as a
                    // LocalLookFrame. Drain the compatibility accumulator so
                    // old callers cannot replay it, but do not apply it a
                    // second time through MouseDeltaX/Y.
                    rawLook.ConsumeForSimulation();
                    player.Input.MouseDeltaX = 0;
                    player.Input.MouseDeltaY = 0;
                }
                else
                {
                    // Platform adapters submit relative look to the neutral
                    // coordinator. Absolute pointer motion remains available
                    // for menus/radials but is never a second gameplay path.
                    player.Input.MouseDeltaX = 0;
                    player.Input.MouseDeltaY = 0;
                }
                ProcessMorphBallBoostInput(player, scene.Services.LocalLookFrame,
                    desktopStylus, scene.GlobalElapsedTime);
                _isScrollingUp = false;
                _isScrollingDown = false;
                // todo?: deal with overflow or whatever
                float curScrollY = mouseSnap.Scroll.Y;
                float prevScrollY = prevMouseSnap?.Scroll.Y ?? 0;
                if (curScrollY > prevScrollY)
                {
                    _isScrollingUp = true;
                }
                else if (curScrollY < prevScrollY)
                {
                    _isScrollingDown = true;
                }

                if (player.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    for (int j = 0; j < player.GetPresentation().Bindings.All.Length; j++)
                    {
                        Keybind control = player.GetPresentation().Bindings.All[j];
                        if (control.Type == ButtonType.Key)
                        {
                            if (control.Key != Keys.Unknown)
                            {
                                bool prevDown = prevKeyboardSnap?.IsKeyDown(control.Key) ?? false;
                                control.IsDown = keyboardSnap.IsKeyDown(control.Key);
                                control.IsPressed = control.IsDown && !prevDown;
                                control.IsReleased = !control.IsDown && prevDown;
                                if (control.IsDown || control.IsPressed || control.IsReleased)
                                {
                                    player.Input.HasInput = true;
                                }
                            }
                        }
                        else if (control.Type == ButtonType.Mouse)
                        {
                            bool down = mouseSnap.IsButtonDown(control.MouseButton);
                            bool prevDown = prevMouseSnap?.IsButtonDown(control.MouseButton) ?? false;
                            if (control.NeedsRepress)
                            {
                                if (!down || !prevDown)
                                {
                                    control.NeedsRepress = false;
                                }
                            }

                            if (!control.NeedsRepress)
                            {
                                control.IsDown = down;
                                control.IsPressed = control.IsDown && !prevDown;
                                control.IsReleased = !control.IsDown && prevDown;
                                if (control.IsDown || control.IsPressed || control.IsReleased)
                                {
                                    player.Input.HasInput = true;
                                }
                            }
                        }
                        else
                        {
                            control.IsDown = control.Type == ButtonType.ScrollUp && _isScrollingUp || control.Type == ButtonType.ScrollDown && _isScrollingDown;
                            control.IsPressed = control.IsDown;
                            control.IsReleased = false;
                            if (control.IsDown)
                            {
                                player.Input.HasInput = true;
                            }
                        }
                    }
                }

                if (mouseSnap.IsButtonDown(MouseButton.Left) && prevMouseSnap?.IsButtonDown(MouseButton.Left) != true)
                {
                    player.Input.ClickX = mouseSnap.X;
                    player.Input.ClickY = mouseSnap.Y;
                    player.Input.HasInput = true;
                }
                else
                {
                    player.Input.ClickX = -1;
                    player.Input.ClickY = -1;
                }
                if (!noPlayerInput) ApplyDesktopStylus(player, desktopStylus);
            // todo?: besides the code duplication, input processing like this should work even if
            // there's no player or the player is not active (will need to revisit this for menus)
            }
        }

        private static void ProcessMorphBallBoostInput(PlayerEntity player,
            in LocalLookFrame lookFrame, in Mods.Input.StylusState stylus,
            double timestamp)
        {
            bool eligible = MorphBallBoostEligibility.IsEligible(player);
            if (!eligible)
            {
                ResetMorphBallBoostDetectors();
                return;
            }

            bool queued = false;
            if (Mods.InputSettings.MorphBallMouseFlickBoost)
            {
                _mouseBoost.Observe(lookFrame.RawMouseDelta, timestamp);
                if (_mouseBoost.TakeDirection(out Vector2 mouseDirection))
                    queued = QueueMorphBallBoost(player, mouseDirection);
            }
            else
            {
                _mouseBoost.Reset();
            }

            if (Mods.InputSettings.MorphBallStickFlickBoost)
            {
                bool hasStickSample = (lookFrame.Contributors
                    & LookDeviceKind.GamepadStick) != 0;
                Vector2 rightStick = hasStickSample
                    ? lookFrame.RawDirection : Vector2.Zero;
                _stickBoost.ObserveFixedTick(rightStick, timestamp);
                if (!queued && _stickBoost.TakeDirection(out Vector2 stickDirection))
                    queued = QueueMorphBallBoost(player, stickDirection);
                else
                    _stickBoost.TakeDirection(out _);
            }
            else
            {
                _stickBoost.Reset();
            }

            if (!queued && Mods.InputSettings.StylusFlickBoost
                && stylus.FlickBoost)
            {
                queued = QueueMorphBallBoost(player,
                    new Vector2(stylus.FlickX, stylus.FlickY));
            }
            if (queued) player.ModNoteInput();
        }

        private static bool QueueMorphBallBoost(PlayerEntity player,
            Vector2 screenDirection)
        {
            return MorphBallFlickDirection.TryNormalize(screenDirection,
                out Vector2 normalized)
                && BoostIntent.TryCreateFlick(normalized, out BoostIntent intent)
                && player.Input.QueueBoostIntent(intent);
        }

        private static void ResetMorphBallBoostDetectors()
        {
            _mouseBoost.Reset();
            _stickBoost.Reset();
        }

        private static void ApplyDesktopStylus(PlayerEntity player,
            in Mods.Input.StylusState state)
        {
            Mods.Input.StylusBindings bindings = Mods.InputSettings.CurrentStylusBindings;
            bool fire = bindings.IsDown(state, Mods.Input.StylusAction.Fire)
                || state.PressureFireActive;
            bool firePressed = bindings.IsPressed(state, Mods.Input.StylusAction.Fire);
            bool zoom = bindings.IsDown(state, Mods.Input.StylusAction.Zoom);
            bool zoomPressed = bindings.IsPressed(state, Mods.Input.StylusAction.Zoom);
            ApplyStylusButton(player.GetPresentation().Bindings.Shoot, fire, firePressed);
            ApplyStylusButton(player.GetPresentation().Bindings.AltAttack, fire, firePressed);
            ApplyStylusButton(player.GetPresentation().Bindings.Zoom, zoom, zoomPressed);
            if (state.DoubleTapJump)
                ApplyStylusButton(player.GetPresentation().Bindings.Jump, true, true);
            if (fire || zoom || state.DoubleTapJump
                || state.FlickBoost && Mods.InputSettings.StylusFlickBoost
                    && MorphBallBoostEligibility.IsEligible(player))
                player.ModNoteInput();
        }

        private static void ApplyStylusButton(Keybind bind, bool down, bool pressed)
        {
            bind.IsDown |= down;
            bind.IsPressed |= pressed;
        }
    }
}
