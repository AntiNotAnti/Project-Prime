using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Replay
{
    public static class ReplayInput
    {
        internal static bool Scrubbing { get; private set; }
        internal static void CancelScrub() => Scrubbing = false;
        internal static bool OverTimeline(float x, float y)
            => x >= 51f / 256 && x <= 205f / 256 && y >= 169f / 192 && y <= 179f / 192;
        internal static uint FrameAt(float x, uint duration)
            => (uint)System.Math.Round(System.Math.Clamp((x * 256d - 51) / 154, 0, 1) * duration);
        private static void Scrub(float x)
        {
            uint frame = FrameAt(x, ReplayController.DurationFrames);
            if (frame != ReplayController.TimelineFrame || (!ReplayController.IsPaused && !ReplayController.IsSeeking))
                ReplayController.Seek(frame, resume: false);
        }
        internal static bool PointerDown(float x, float y)
        {
            if (!DemoPlayback.IsActive || PauseMenu.Open || ReplayVideoExporter.Rendering || !OverTimeline(x, y)) return false;
            Scrubbing = true;
            Scrub(x);
            return true;
        }
        internal static bool PointerMove(float x)
        {
            if (!Scrubbing) return false;
            if (!DemoPlayback.IsActive || PauseMenu.Open) { CancelScrub(); return false; }
            Scrub(x);
            return true;
        }
        internal static bool PointerUp(float x)
        {
            if (!Scrubbing) return false;
            PointerMove(x);
            CancelScrub();
            return true;
        }
        internal static bool PointerWheel(float x, float y, float delta)
        {
            if (!DemoPlayback.IsActive || PauseMenu.Open || ReplayVideoExporter.Rendering || !OverTimeline(x, y)) return false;
            ReplayController.Seek((uint)System.Math.Clamp(ReplayController.TimelineFrame + delta * 60d,
                0, ReplayController.DurationFrames), resume: false);
            return true;
        }

        public static bool HandleKey(Keys key, bool editor = false)
        {
            if (!DemoPlayback.IsActive || (PauseMenu.Open && !editor)) return false;
            bool handled = true;
            if (Hit(key, InputSettings.ReplayPlayPauseKey)) ReplayController.TogglePause();
            else if (Hit(key, InputSettings.ReplayStepForwardKey)) ReplayController.StepForward();
            else if (Hit(key, InputSettings.ReplayStepBackKey))
                ReplayController.Seek(ReplayController.CurrentFrame > 0
                    ? ReplayController.CurrentFrame - 1 : 0, false);
            else if (Hit(key, InputSettings.ReplaySlowerKey)) ReplayController.ChangeRate(-1);
            else if (Hit(key, InputSettings.ReplayFasterKey)) ReplayController.ChangeRate(1);
            else if (Hit(key, InputSettings.ReplayRestartKey)) ReplayController.Restart();
            else if (Hit(key, InputSettings.ReplaySeekBackKey))
                ReplayController.Seek(ReplayController.CurrentFrame > 300
                    ? ReplayController.CurrentFrame - 300 : 0);
            else if (Hit(key, InputSettings.ReplaySeekForwardKey))
                ReplayController.Seek((uint)System.Math.Min(
                    (ulong)ReplayController.CurrentFrame + 300,
                    ReplayController.DurationFrames));
            else if (Hit(key, InputSettings.ReplayCameraTrackKey)) ReplayCamera.ToggleTrackPlayback();
            else if (Hit(key, InputSettings.ReplayConstantSpeedKey)) ReplayCamera.ToggleConstantSpeed();
            else if (Hit(key, InputSettings.ReplayInterpolationKey)) ReplayCamera.CycleInterpolation();
            else if (Hit(key, InputSettings.ReplayEasingKey)) ReplayCamera.CycleEase();
            else
            {
                switch (key)
                {
                    case Keys.Delete: ReplayCamera.RemoveKeyframe(); break;
                    case Keys.Minus: ReplayCamera.AdjustLens(-5, 0); break;
                    case Keys.Equal: ReplayCamera.AdjustLens(5, 0); break;
                    case Keys.Semicolon: ReplayCamera.AdjustLens(0, -5); break;
                    case Keys.Apostrophe: ReplayCamera.AdjustLens(0, 5); break;
                    case Keys.F: ReplayCamera.ToggleFree(); break;
                    case Keys.C: ReplayCamera.SetMode(ReplayCamera.Mode == ReplayCameraMode.Chase
                        ? ReplayCameraMode.FirstPerson : ReplayCameraMode.Chase); break;
                    case Keys.O: ReplayCamera.SetMode(ReplayCameraMode.Orbit); break;
                    case Keys.B: ReplayCamera.Bookmark(); break;
                    case Keys.N: ReplayCamera.RestoreBookmark(); break;
                    case >= Keys.D1 and <= Keys.D8: SpectatorMode.Watch(key - Keys.D1); break;
                    default: handled = false; break;
                }
            }
            if (!handled) return false;
            ReplayController.NoteInput();
            return true;
        }

        private static bool Hit(Keys key, Keys binding)
            => binding != Keys.Unknown && key == binding;

        public static void PollGamepad()
        {
            if (!DemoPlayback.IsActive || PauseMenu.Open) return;
            if (GamepadInput.TakeActionPress(PadAction.ReplayPlayPause))
                ReplayController.TogglePause();
            if (GamepadInput.TakeActionPress(PadAction.ReplayStep))
                ReplayController.StepForward();
            if (GamepadInput.TakeActionPress(PadAction.ReplayCameraMode))
                ReplayCamera.ToggleFree();
            if (GamepadInput.TakeActionPress(PadAction.ReplaySeekBack))
                ReplayController.Seek(ReplayController.CurrentFrame > 300
                    ? ReplayController.CurrentFrame - 300 : 0);
            if (GamepadInput.TakeActionPress(PadAction.ReplaySeekForward))
                ReplayController.Seek((uint)System.Math.Min(
                    (ulong)ReplayController.CurrentFrame + 300,
                    ReplayController.DurationFrames));
            if (GamepadInput.TakeActionPress(PadAction.ReplaySlower))
                ReplayController.ChangeRate(-1);
            if (GamepadInput.TakeActionPress(PadAction.ReplayFaster))
                ReplayController.ChangeRate(1);
            if (GamepadInput.TakeActionPress(PadAction.ReplayNextPlayer))
                SpectatorMode.CycleNext();
            if (GamepadInput.TakeActionPress(PadAction.ReplayPrevPlayer))
                SpectatorMode.CyclePrevious();
        }
    }
}
