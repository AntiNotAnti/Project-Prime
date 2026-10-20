using System;
using MphRead.Droid;
using MphRead.Mods.Input;

internal static class InputOwnershipChecks
{
    public static void Run()
    {
        var oldMode = TouchSettings.Mode;
        try
        {
            TouchSettings.Mode = TouchMode.Auto;
            var touch = new TouchControls();
            touch.Layout(1920, 1080, 2);
            touch.UpdateController(new GamepadState { Connected = true, LeftX = .03f });
            if (touch.PadDriving) throw new Exception("idle controller hides touch");
            var fire = TouchSettings.Geometry(TouchControl.Shoot, 1920, 1080);
            touch.PointerDown(1, fire.X, fire.Y);
            if (!touch.IsHeld(TouchAction.Shoot)) throw new Exception("fire not held");
            touch.UpdateController(new GamepadState { Connected = true, LeftX = .7f });
            if (!touch.PadDriving || touch.IsHeld(TouchAction.Shoot)) throw new Exception("controller handoff retains fire");
            touch.PointerDown(2, fire.X, fire.Y);
            if (touch.IsHeld(TouchAction.Shoot) || !touch.PadDriving) throw new Exception("hidden control accepts fire");
            touch.UpdateController(new GamepadState { Connected = true });
            if (!touch.PadDriving) throw new Exception("idle sample flickers controls");
            touch.UpdateController(default);
            if (touch.PadDriving) throw new Exception("disconnect does not restore controls");
            touch.PointerMove(1, 100, 100, 0, 0);
            if (touch.TakeAimDelta() != (0f, 0f)) throw new Exception("old pointer survived handoff");

            touch.UpdateControllerSnapshot(new GamepadSnapshot("active-pad", new GamepadState { Connected = true, LeftX = .8f }, 1));
            if (!touch.PadDriving) throw new Exception("active pad not latched");
            touch.UpdateControllerSnapshot(new GamepadSnapshot("idle-second-pad", new GamepadState { Connected = true }, 2));
            if (touch.PadDriving) throw new Exception("disconnected active pad leaves idle replacement hiding touch");

            TouchSettings.Mode = TouchMode.AimOnly;
            touch.ReloadSettings();
            touch.PointerDown(3, 100, 500); // left half is aim too
            touch.PointerDown(4, fire.X, fire.Y);
            touch.PointerMove(4, fire.X + 60, fire.Y, 0, 0);
            touch.PointerMove(3, 107, 496, 0, 0);
            if (touch.Direction != TouchControls.Dir.None || touch.IsHeld(TouchAction.Shoot)
                || touch.TakeAimDelta() != (7f, -4f)) throw new Exception("AimOnly steals buttons or second pointer");
            if (touch.AltMoveDrive.Engaged || touch.SamusAltMoveDrive.Engaged || touch.NativeMorphBallAim().Down)
                throw new Exception("AimOnly controls morph movement");
            touch.PointerUp(3);
            touch.PointerDown(5, 100, 500);
            touch.PointerUp(5);
            if (touch.TakeDoubleTapJump()) throw new Exception("AimOnly synthesizes jump");
            touch.ReleaseEverything();
            if (touch.PeekAimDelta() != (0f, 0f)) throw new Exception("cancel retains aim");

            TouchSettings.Mode = TouchMode.Off;
            touch.ReloadSettings();
            touch.PointerDown(6, 1400, 500);
            touch.PointerMove(6, 1500, 500, 0, 0);
            if (touch.TakeAimDelta() != (0f, 0f)) throw new Exception("Off accepts aim");
            touch.ForceVisible = true; // native dialog retains its positional input
            touch.PointerIsAbsolute = true;
            touch.PointerDown(7, 1400, 500);
            if (!touch.AimPosition().Down) throw new Exception("Off blocks dialog input");
            touch.ForceVisible = false;
            if (touch.AimPosition().Down) throw new Exception("dialog handoff retains pointer");
            TouchSettings.ReadSetting("touch_mode", "garbage");
            if (TouchSettings.Mode != TouchMode.Touch) throw new Exception("invalid mode fallback");
            TouchSettings.ReadSetting("touch_mode", "99");
            if (TouchSettings.Mode != TouchMode.Touch) throw new Exception("numeric mode accepted");
            Console.WriteLine("TOUCHCHECK PASS: input modes, ownership, controller noise/disconnect, AimOnly, dialog cancellation");
        }
        finally { TouchSettings.Mode = oldMode; }
    }
}
