using System;
using System.Linq;
using MphRead.Droid;
using MphRead.Mods.Input;

internal static class ZoomTouchChecks
{
    public static void Run()
    {
        var oldMode = TouchSettings.Mode;
        bool oldButtons = TouchSettings.ButtonsVisible;
        bool oldZoom = TouchSettings.IsEnabled(TouchControl.Zoom);
        var oldLayout = TouchSettings.CopyLayout();
        float oldScale = TouchSettings.ButtonScale;
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new InvalidOperationException("Zoom touch: " + message);
        }
        try
        {
            TouchSettings.Mode = TouchMode.Touch;
            TouchSettings.ButtonsVisible = true;
            TouchSettings.SetEnabled(TouchControl.Zoom, true);
            TouchSettings.ResetLayout(TouchControl.Zoom);
            TouchSettings.ButtonScale = 1;
            var touch = new TouchControls();
            touch.Layout(1920, 1080, 2);
            var zoom = touch.Buttons.Single(b => b.Action == TouchAction.Zoom && b.Instance is null);
            void Down(int pointer) => touch.PointerDown(pointer, zoom.CentreX, zoom.CentreY);
            Check(zoom.Visible, "enabled Zoom has a visible hit target");
            Down(1);
            Check(touch.IsHeld(TouchAction.Zoom) && !touch.IsHeld(TouchAction.Shoot),
                "the Zoom target holds only its own action");
            for (int sample = 0; sample < 4; sample++)
                Check(touch.IsHeld(TouchAction.Zoom), "held input survives repeated samples");
            touch.PointerUp(1);
            Check(!touch.IsHeld(TouchAction.Zoom), "pointer release clears Zoom");

            Down(2);
            Down(3);
            touch.PointerUp(2);
            Check(touch.IsHeld(TouchAction.Zoom), "one release cannot clear another Zoom contact");
            touch.PointerUp(3);
            Check(!touch.IsHeld(TouchAction.Zoom), "the last Zoom contact releases the action");

            Down(4);
            TouchSettings.SetEnabled(TouchControl.Zoom, false);
            touch.ReloadSettings();
            Check(!zoom.Visible && !touch.IsHeld(TouchAction.Zoom),
                "intentionally hiding Zoom clears its held contact");
            Down(5);
            Check(!touch.IsHeld(TouchAction.Zoom), "a hidden Zoom target accepts no Zoom press");
            touch.PointerUp(4);
            touch.PointerUp(5);
            TouchSettings.SetEnabled(TouchControl.Zoom, true);
            touch.ReloadSettings();
            Check(zoom.Visible && !touch.IsHeld(TouchAction.Zoom),
                "restoring visibility does not revive an old contact");

            Down(13);
            TouchSettings.ButtonsVisible = false;
            touch.ReloadSettings();
            Down(14);
            Check(!zoom.Visible && !touch.IsHeld(TouchAction.Zoom),
                "global button hiding cancels Zoom and refuses new presses");
            TouchSettings.ButtonsVisible = true;
            touch.ReloadSettings();
            Check(zoom.Visible && !touch.IsHeld(TouchAction.Zoom),
                "global button restoration requires a fresh contact");

            Down(6);
            touch.ReleaseEverything();
            Check(!touch.IsHeld(TouchAction.Zoom), "lifecycle cancellation releases Zoom");
            touch.PointerUp(6);
            Check(!touch.IsHeld(TouchAction.Zoom), "a retired pointer cannot restore Zoom");
            Down(7);
            touch.Layout(1280, 720, 2);
            Check(!touch.IsHeld(TouchAction.Zoom), "layout replacement retires held Zoom");

            foreach (TouchMode mode in new[] { TouchMode.AimOnly, TouchMode.Off })
            {
                TouchSettings.Mode = TouchMode.Touch;
                touch.ReloadSettings();
                Down(8);
                Check(touch.IsHeld(TouchAction.Zoom), "fresh contact works before mode transition");
                TouchSettings.Mode = mode;
                touch.ReloadSettings();
                Check(!touch.IsHeld(TouchAction.Zoom), "mode transition releases held Zoom");
                Down(9);
                Check(!touch.IsHeld(TouchAction.Zoom), "mode without buttons cannot press Zoom");
                touch.ReleaseEverything();
            }

            TouchSettings.Mode = TouchMode.Auto;
            touch.ReloadSettings();
            Down(10);
            Check(touch.IsHeld(TouchAction.Zoom), "Auto permits touch before controller ownership");
            touch.UpdateController(new GamepadState { Connected = true, LeftX = .8f });
            Check(touch.PadDriving && !touch.IsHeld(TouchAction.Zoom),
                "controller ownership cancels held Zoom");
            Down(11);
            Check(!touch.IsHeld(TouchAction.Zoom), "controller-owned hidden controls accept no Zoom");
            touch.UpdateController(default);
            Check(!touch.PadDriving && !touch.IsHeld(TouchAction.Zoom),
                "disconnect restores touch without restoring the old Zoom contact");
            Down(12);
            Check(touch.IsHeld(TouchAction.Zoom), "a fresh Zoom contact works after disconnect");
            touch.PointerUp(12);
            Check(!touch.IsHeld(TouchAction.Zoom), "fresh contact releases normally");
            Console.WriteLine($"TOUCHCHECK PASS: {checks} Zoom target/hold/release, hidden setting and input lifecycle checks; no GameView or gameplay delivery claim");
        }
        finally
        {
            TouchSettings.Mode = oldMode;
            TouchSettings.ButtonsVisible = oldButtons;
            TouchSettings.SetEnabled(TouchControl.Zoom, oldZoom);
            TouchSettings.ReplaceLayout(oldLayout);
            TouchSettings.ButtonScale = oldScale;
        }
    }
}
