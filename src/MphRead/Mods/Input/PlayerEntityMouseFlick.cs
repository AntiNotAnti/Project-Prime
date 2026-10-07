using MphRead.Formats;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private void ModClearAltFlick()
        {
            SwipeBoostRequested = false;
            SwipeBoostX = 0;
            SwipeBoostY = 0;
        }

        /// <summary>
        /// Read this frame's desktop mouse/pen movement as a possible alt-form
        /// flick. Android performs its own per-finger recognition and queues the
        /// same legacy request fields before this input pass.
        /// </summary>
        private void ModCheckMouseFlick(bool dedicatedBoostDown)
        {
            if (!IsMainPlayer || IsBot)
            {
                return;
            }
            bool movementBoostEnabled = Mods.Input.PointerDevice.Active
                ? Mods.InputSettings.StylusMovementBoost
                : Mods.InputSettings.MouseMovementBoost;
            if (!movementBoostEnabled || !Controls.MouseAim || dedicatedBoostDown
                || Flags1.TestFlag(PlayerFlags1.NoAimInput)
                || Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)
                || Mods.SpectatorMode.IsSpectating
                || _scene.FrameAdvance || _scene.FrameAdvanceLastFrame
                || CameraSequence.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) == true)
            {
                Mods.Input.MouseFlick.Reset();
                return;
            }
            if (Mods.Input.MouseFlick.Check(Input.MouseDeltaX, Input.MouseDeltaY,
                _scene.FrameCount, out float dirX, out float dirY))
            {
                SwipeBoostRequested = true;
                SwipeBoostX = dirX;
                SwipeBoostY = dirY;
            }
        }

        internal void ModSetAltSwipeDrive(bool engaged, float x, float y)
        {
            if (!global::System.Single.IsFinite(x) || !global::System.Single.IsFinite(y))
            {
                engaged = false;
                x = y = 0;
            }
            bool wasEngaged = Input.AltSwipeEngaged;
            if (!engaged && wasEngaged)
            {
                Input.AltSwipeStopRequested = true;
            }
            Input.AltSwipeEngaged = engaged;
            Input.AltSwipeX = engaged ? x : 0;
            Input.AltSwipeY = engaged ? y : 0;
        }

        private void ModResetAltSwipeDrive()
        {
            Input.ResetAltSwipe();
        }

        private void ModMarkAltSwipeInput()
        {
            if (Input.AltSwipeEngaged
                && (Input.AltSwipeX != 0 || Input.AltSwipeY != 0))
            {
                Input.HasInput = true;
            }
        }

        /// <summary>
        /// Feed rolling alt forms from the active desktop pointer source.
        ///
        /// Samus consumes only motion from the current simulation step, like
        /// native Morph Ball steering. Kanden/Spire/Noxus keep the anchored
        /// precision virtual stick. The opt-in relative-mouse mode follows the
        /// same hunter-specific split. Trace, Sylux and Weavel retain aim.
        /// </summary>
        private void ModApplyPointerAltMove()
        {
            // Android publishes Samus' aim finger before this shared
            // hardware-input pass. Advance the same 30 Hz producer here; the
            // other transformed hunters retain their existing Android drive.
            if (global::System.OperatingSystem.IsAndroid())
            {
                if (Hunter == Hunter.Samus)
                {
                    bool enabled = IsMainPlayer && !IsBot && IsAltForm
                        && !IsMorphing && !IsUnmorphing
                        && !Flags1.TestFlag(PlayerFlags1.NoAimInput)
                        && !Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)
                        && !Mods.SpectatorMode.IsSpectating;
                    Input.MorphTouch.StepLocal(_scene.FrameCount, enabled,
                        Mods.Input.MorphTouchHost.Down, 0, 0);
                    ModSetAltSwipeDrive(false, 0, 0);
                }
                return;
            }

            bool valid = IsMainPlayer && !IsBot && IsAltForm
                && !IsMorphing && !IsUnmorphing
                && Mods.Input.AltFormGesture.UsesRollMovement(Hunter)
                && !Flags1.TestFlag(PlayerFlags1.NoAimInput)
                && !Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen)
                && !Mods.SpectatorMode.IsSpectating
                && !_scene.FrameAdvance && !_scene.FrameAdvanceLastFrame
                && CameraSequence.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) != true;
            if (!valid)
            {
                Input.StylusAltTracking = false;
                if (Hunter == Hunter.Samus) Input.MorphTouch.Suspend();
                ModSetAltSwipeDrive(false, 0, 0);
                return;
            }

            bool absolutePointer = Mods.Input.PointerDevice.Active
                && Mods.Input.PointerDevice.Current.Device != Mods.Input.PointerDeviceType.Mouse;

            if (Hunter == Hunter.Samus)
            {
                bool enabled;
                bool contact = false;
                if (absolutePointer)
                {
                    Mods.Input.PointerSample sample = Mods.Input.PointerDevice.Current;
                    enabled = sample.InContact
                        && (!Mods.Input.StylusZone.Enabled || Mods.Input.StylusZone.Aiming);
                    contact = enabled;
                    Input.StylusAltTracking = enabled;
                }
                else
                {
                    Input.StylusAltTracking = false;
                    enabled = Mods.InputSettings.MouseAltFormMovement && Controls.MouseAim;
                }

                Input.MorphTouch.StepLocal(_scene.FrameCount, enabled, contact,
                    Input.MouseDeltaX, Input.MouseDeltaY);
                // Samus no longer uses the heuristic virtual-stick/flick path.
                // The native touch producer owns both roll and touch boost.
                ModSetAltSwipeDrive(false, 0, 0);
                return;
            }

            if (absolutePointer)
            {
                Mods.Input.PointerSample sample = Mods.Input.PointerDevice.Current;
                bool stylusValid = sample.InContact
                    && (!Mods.Input.StylusZone.Enabled || Mods.Input.StylusZone.Aiming);
                if (!stylusValid)
                {
                    Input.StylusAltTracking = false;
                    ModSetAltSwipeDrive(false, 0, 0);
                    return;
                }

                if (!Input.StylusAltTracking)
                {
                    Input.StylusAltTracking = true;
                    Input.StylusAltOriginX = sample.X;
                    Input.StylusAltOriginY = sample.Y;
                    ModSetAltSwipeDrive(true, 0, 0);
                    return;
                }

                (float X, float Y) stylusDrive = Mods.Input.AltFormGesture.Drive(
                    sample.X - Input.StylusAltOriginX,
                    sample.Y - Input.StylusAltOriginY,
                    deadZone: 6f, fullScale: 96f,
                    sensitivity: Mods.InputSettings.AltSwipeSensitivity);
                ModSetAltSwipeDrive(true, stylusDrive.X, stylusDrive.Y);
                return;
            }

            Input.StylusAltTracking = false;
            if (!Mods.InputSettings.MouseAltFormMovement || !Controls.MouseAim)
            {
                ModSetAltSwipeDrive(false, 0, 0);
                return;
            }

            (float X, float Y) mouseDrive = Mods.Input.AltFormGesture.MouseDrive(
                Input.MouseDeltaX, Input.MouseDeltaY,
                Mods.InputSettings.AltSwipeSensitivity);
            bool engaged = mouseDrive.X != 0 || mouseDrive.Y != 0;
            ModSetAltSwipeDrive(engaged, mouseDrive.X, mouseDrive.Y);
        }

        /// <summary>
        /// Resolve the one-shot alt-form gesture before network press history is
        /// captured. Samus leaves it for the native boost simulation; Spire leaves
        /// it for the rolling movement step, which turns the swipe into momentum.
        /// Unsupported/invalid states clear it.
        /// </summary>
        private void ModPrepareAltFlick()
        {
            Mods.Input.AltFlickAction action = Mods.Input.AltFormGesture.FlickAction(Hunter);
            bool acceptsFlick = IsAltForm && !IsMorphing && !IsUnmorphing
                && _health > 0 && _frozenTimer == 0
                && action != Mods.Input.AltFlickAction.None;
            if (!acceptsFlick)
            {
                ModClearAltFlick();
                Mods.Input.MouseFlick.Reset();
                return;
            }

            // Samus Morph Ball touch is handled by NativeTouchSample. The
            // remaining one-shot gesture is Spire's mobility nudge.
            if (action == Mods.Input.AltFlickAction.SpireMomentum
                && !global::System.OperatingSystem.IsAndroid())
            {
                ModCheckMouseFlick(dedicatedBoostDown: false);
            }

            if (action == Mods.Input.AltFlickAction.SpireMomentum && SwipeBoostRequested)
            {
                // Do not synthesize AltAttack. ProcessAlt consumes the same
                // direction fields as a one-shot horizontal momentum impulse.
                Input.HasInput = true;
            }
        }
    }
}
