using System;
using MphRead.Entities;
using MphRead.Hud;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Network
{
    /// <summary>Shared replay controls. Touch hosts may forward logical 256x192 pointer coordinates.</summary>
    public static class ReplayControls
    {
        private static int _held;
        private static readonly object PointerLock = new();
        private static (object Session, Vector2 Position)? _queuedPointer;
        public static bool QueuePointerDown(float x, float y)
        {
            if (!ReplayPlayback.IsActive || ClientInputState.PauseOpen || Chat.ChatBox.Composing
                || !float.IsFinite(x) || !float.IsFinite(y) || x < 8 || x > 248 || y < 161 || y > 192) return false;
            lock (PointerLock)
            {
                if (ReplayPlayback.SessionIdentity is not object session) return false;
                _queuedPointer = (session, new Vector2(x, y));
            }
            return true;
        }
        public static void Poll(KeyboardState keyboard, MouseState mouse, Vector2i size)
        {
            if (!ReplayPlayback.IsActive) { _held = 0; return; }

            int keys = (keyboard.IsKeyDown(Keys.F6) ? 1 : 0) | (keyboard.IsKeyDown(Keys.F7) ? 2 : 0)
                | (keyboard.IsKeyDown(Keys.F8) ? 4 : 0) | (keyboard.IsKeyDown(Keys.F9) ? 8 : 0)
                | (keyboard.IsKeyDown(Keys.F10) ? 16 : 0) | (mouse.IsButtonDown(MouseButton.Left) ? 32 : 0);
            int pressed = keys & ~_held; _held = keys;
            ConsumeQueuedPointer();
            if (ClientInputState.PauseOpen || Chat.ChatBox.Composing) return;
            if ((pressed & 1) != 0) TogglePause();
            if ((pressed & 2) != 0) ReplayPlayback.Transport.Step();
            if ((pressed & 4) != 0) CycleRate();
            if ((pressed & 8) != 0) ReplayPlayback.SeekEvent(false);
            if ((pressed & 16) != 0) ReplayPlayback.SeekEvent(true);
            if ((pressed & 32) != 0 && size.X > 0 && size.Y > 0)
                PointerDown(mouse.Position.X * 256 / size.X, mouse.Position.Y * 192 / size.Y);
        }
        internal static bool ConsumeQueuedPointer()
        {
            (object Session, Vector2 Position)? pointer;
            lock (PointerLock) { pointer = _queuedPointer; _queuedPointer = null; }
            return pointer is { } tap && ReferenceEquals(tap.Session, ReplayPlayback.SessionIdentity)
                && PointerDown(tap.Position.X, tap.Position.Y);
        }
        public static void TogglePause() => ReplayPlayback.Transport.Paused = !ReplayPlayback.Transport.Paused;
        public static void CycleRate() => ReplayPlayback.Transport.Rate = ReplayPlayback.Transport.Rate switch
        { .25 => .5, .5 => 1, 1 => 2, 2 => 4, _ => .25 };
        public static bool PointerDown(float x, float y)
        {
            if (!ReplayPlayback.IsActive || ClientInputState.PauseOpen || Chat.ChatBox.Composing
                || !float.IsFinite(x) || !float.IsFinite(y) || x < 8 || x > 248 || y < 161 || y > 192) return false;
            if (y >= 180)
            {
                if (ReplayPlayback.CanSeek) ReplayPlayback.Seek((uint)(Math.Clamp((x - 12) / 232, 0, 1) * ReplayPlayback.DurationFrames));
            }
            else if (x < 60) TogglePause();
            else if (x < 108) ReplayPlayback.Transport.Step();
            else if (x < 157) CycleRate();
            else ReplayPlayback.SeekEvent(x >= 203);
            return true;
        }
        internal static void Draw(ScenePresentation presentation)
        {
            if (!ReplayPlayback.IsActive) return;
            presentation.DrawHudFlatBox(8, 161, 248, 191, new Vector4(0, 0, 0, .8f));
            var player = presentation.World.LocalPlayer!.GetPresentation();
            player.DrawText2D(13, 164, Align.Left, 0,
                $"{(ReplayPlayback.Transport.Paused ? "PLAY" : "PAUSE")}   STEP   {ReplayPlayback.Transport.Rate:0.##}x   PREV   NEXT", scale: .6f);
            player.DrawText2D(128, 173, Align.Center, 0,
                ReplayPlayback.LastError ?? $"{ReplayPlayback.CurrentFrame / 60.0:0.0}s / {ReplayPlayback.DurationFrames / 60.0:0.0}s  F6-F10", maxLength: 52, scale: .5f);
            presentation.DrawHudFlatBox(12, 183, 244, 188, new Vector4(.25f, .25f, .25f, 1));
            if (ReplayPlayback.DurationFrames > 0)
                presentation.DrawHudFlatBox(12, 183, 12 + 232 * Math.Clamp((float)ReplayPlayback.CurrentFrame / ReplayPlayback.DurationFrames, 0, 1), 188,
                    new Vector4(.2f, .8f, 1, 1));
        }
    }
}
